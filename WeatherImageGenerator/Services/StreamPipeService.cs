using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Lightweight MPEG-TS byte pipe that reads from Tunarr's HTTP stream and forwards
    /// raw 188-byte TS packets directly over TCP to connected clients.
    /// When an EAS alert is triggered, the pipe seamlessly splices a pre-encoded
    /// alert .ts segment into every active client stream, then resumes the Tunarr feed —
    /// maintaining continuity counters and monotonic PTS/DTS/PCR timestamps throughout.
    ///
    /// Unlike the full StreamProxyService (HTTP proxy + HDHR emulation), this service
    /// has zero HTTP overhead — it's a raw TCP byte pipe with per-packet processing.
    /// Clients connect with any MPEG-TS-capable player (VLC, ffplay, Plex, etc.)
    /// using a direct TCP or HTTP URL like http://host:port/.
    /// </summary>
    public class StreamPipeService
    {
        // ── Configuration ──────────────────────────────────────────────────
        private readonly StreamProxySettings _settings;
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }

        // ── Per-channel listeners ──────────────────────────────────────────
        private readonly ConcurrentDictionary<int, ChannelPipeState> _channels = new();

        // ── Alert splice state (global — one alert at a time) ──────────────
        private volatile string? _activeAlertTsPath;
        private double _activeAlertDuration;
        private readonly object _alertDurationLock = new();
        private readonly ManualResetEventSlim _alertSignal = new(false);
        private int _alertGeneration; // incremented per alert to prevent stale-signal races

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────

        public StreamPipeService(StreamProxySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────────

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                _cts = new CancellationTokenSource();

                // Start a TCP listener for the configured listen port.
                // All alert-enabled channels share one port; the pipe reads from
                // the first alert-enabled channel's Tunarr stream.
                var alertChannel = _settings.Channels.FirstOrDefault(c => c.AlertInterruptEnabled);
                if (alertChannel == null && _settings.Channels.Count > 0)
                    alertChannel = _settings.Channels[0];

                if (alertChannel == null)
                {
                    Logger.Log("[StreamPipe] No channels configured — nothing to pipe.", Logger.LogLevel.Warning);
                    return;
                }

                // Start one listener per channel on sequential ports
                foreach (var ch in _settings.Channels)
                {
                    int listenPort = ch.ProxyChannelNumber == _settings.Channels[0].ProxyChannelNumber
                        ? _settings.ListenPort
                        : _settings.ListenPort + (ch.ProxyChannelNumber - _settings.Channels[0].ProxyChannelNumber);

                    var state = new ChannelPipeState(ch, listenPort);
                    _channels[ch.ProxyChannelNumber] = state;

                    _ = StartChannelListener(state, _cts.Token);
                }

                IsRunning = true;

                string localIp = NetworkHelper.GetLocalIPAddress();
                Logger.Log($"[StreamPipe] ✓ MPEG-TS byte pipe started", Logger.LogLevel.Info);
                Logger.Log($"[StreamPipe]   Tunarr upstream: {_settings.TunarrBaseUrl}", Logger.LogLevel.Info);
                foreach (var kv in _channels)
                {
                    var s = kv.Value;
                    Logger.Log($"[StreamPipe]   Channel {s.Config.ProxyChannelNumber} ({s.Config.DisplayName}): tcp://{localIp}:{s.ListenPort}  alert={s.Config.AlertInterruptEnabled}", Logger.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Logger.Log($"[StreamPipe] Failed to start: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            try
            {
                _cts?.Cancel();

                foreach (var kv in _channels)
                    kv.Value.Dispose();
                _channels.Clear();

                IsRunning = false;
                Logger.Log("[StreamPipe] Pipe stopped.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamPipe] Error stopping: {ex.Message}", Logger.LogLevel.Warning);
            }

            await Task.CompletedTask;
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert splice trigger (called from MainForm)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers an EAS alert splice on all alert-enabled channels.
        /// </summary>
        public void TriggerAlertSplice(string alertTsPath, double durationSeconds)
        {
            if (!IsRunning)
            {
                Logger.Log("[StreamPipe] Cannot trigger alert splice — pipe is not running.", Logger.LogLevel.Warning);
                return;
            }
            if (string.IsNullOrEmpty(alertTsPath) || !File.Exists(alertTsPath))
            {
                Logger.Log($"[StreamPipe] Cannot trigger alert splice — file missing: '{alertTsPath}'", Logger.LogLevel.Warning);
                return;
            }

            int totalClients = _channels.Values.Sum(c => c.ClientCount);
            Logger.Log($"[StreamPipe] 🚨 EAS ALERT SPLICE triggered — {Path.GetFileName(alertTsPath)} ({durationSeconds:F1}s), {totalClients} client(s) connected", Logger.LogLevel.Info);

            if (totalClients == 0)
            {
                Logger.Log("[StreamPipe] ⚠ WARNING: No clients are currently connected. The splice will have no visible effect.", Logger.LogLevel.Warning);
            }

            _activeAlertTsPath = alertTsPath;
            lock (_alertDurationLock) { _activeAlertDuration = durationSeconds; }
            Interlocked.Increment(ref _alertGeneration);
            _alertSignal.Set();
        }

        // ────────────────────────────────────────────────────────────────────
        // Per-channel TCP listener
        // ────────────────────────────────────────────────────────────────────

        private async Task StartChannelListener(ChannelPipeState state, CancellationToken ct)
        {
            var listener = new TcpListener(
                _settings.AllowRemoteAccess ? IPAddress.Any : IPAddress.Loopback,
                state.ListenPort);

            state.Listener = listener;

            try
            {
                listener.Start();
                Logger.Log($"[StreamPipe] Channel {state.Config.ProxyChannelNumber}: listening on port {state.ListenPort}", Logger.LogLevel.Debug);

                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    // Handle each client in its own task (tracked for clean shutdown)
                    var clientTask = HandleClient(state, client, ct);
                    state.TrackClientTask(clientTask);
                }
            }
            catch (SocketException ex)
            {
                Logger.Log($"[StreamPipe] Channel {state.Config.ProxyChannelNumber}: listener failed on port {state.ListenPort}: {ex.Message}", Logger.LogLevel.Error);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.Log($"[StreamPipe] Channel {state.Config.ProxyChannelNumber}: listener error: {ex.Message}", Logger.LogLevel.Error);
            }
            finally
            {
                try { listener.Stop(); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Per-client pipe loop
        // ────────────────────────────────────────────────────────────────────

        private async Task HandleClient(ChannelPipeState channelState, TcpClient tcpClient, CancellationToken ct)
        {
            var clientId = Guid.NewGuid().ToString("N")[..8];
            channelState.IncrementClients();
            Logger.Log($"[StreamPipe] Client {clientId} connected to channel {channelState.Config.ProxyChannelNumber} ({channelState.Config.DisplayName})", Logger.LogLevel.Info);

            try
            {
                tcpClient.NoDelay = true;
                tcpClient.SendBufferSize = 65536;
                tcpClient.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, true);

                using var clientStream = tcpClient.GetStream();

                // If the client sends an HTTP GET request, consume it and send a minimal
                // HTTP 200 response so HTTP-based players (VLC http://, Plex, etc.) work.
                await HandleHttpHandshakeIfNeeded(clientStream, ct);

                await PipeStreamLoop(channelState.Config, channelState, clientStream, clientId, ct);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                // Client disconnected — normal
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Log($"[StreamPipe] Client {clientId} error: {ex.Message}", Logger.LogLevel.Debug);
            }
            finally
            {
                Logger.Log($"[StreamPipe] Client {clientId} disconnected from channel {channelState.Config.ProxyChannelNumber}", Logger.LogLevel.Info);
                channelState.DecrementClients();
                try { tcpClient.Close(); } catch { }
            }
        }

        /// <summary>
        /// Peek at incoming bytes. If the client sent "GET " (HTTP request), consume the
        /// request headers and respond with a minimal HTTP 200 + MPEG-TS content type.
        /// This lets HTTP-capable players connect via http://host:port/ without needing
        /// a full HTTP server.
        /// </summary>
        private static async Task HandleHttpHandshakeIfNeeded(NetworkStream stream, CancellationToken ct)
        {
            // Set a short read timeout to peek for HTTP headers
            stream.ReadTimeout = 500; // ms

            try
            {
                if (!stream.DataAvailable)
                    await Task.Delay(200, ct); // give client time to send headers

                if (stream.DataAvailable)
                {
                    var peekBuf = new byte[4096];
                    int bytesRead = await stream.ReadAsync(peekBuf, 0, peekBuf.Length, ct);

                    if (bytesRead >= 4)
                    {
                        string header = System.Text.Encoding.ASCII.GetString(peekBuf, 0, Math.Min(bytesRead, 512));
                        if (header.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
                        {
                            // It's an HTTP request — send HTTP response headers.
                            // NO Transfer-Encoding or Content-Length — the stream is
                            // indeterminate-length; the client reads until we close.
                            string httpResponse =
                                "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: video/mp2t\r\n" +
                                "Connection: keep-alive\r\n" +
                                "Cache-Control: no-cache, no-store\r\n" +
                                "Access-Control-Allow-Origin: *\r\n" +
                                "\r\n";
                            var respBytes = System.Text.Encoding.ASCII.GetBytes(httpResponse);
                            await stream.WriteAsync(respBytes, 0, respBytes.Length, ct);
                            await stream.FlushAsync(ct);
                        }
                        // If it wasn't HTTP, these bytes are lost — but raw TCP MPEG-TS
                        // clients don't send anything before receiving.
                    }
                }
            }
            catch (IOException) { } // timeout — no data sent, client is raw TCP
            catch (OperationCanceledException) { }
            finally
            {
                stream.ReadTimeout = Timeout.Infinite; // restore for streaming
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Main pipe loop — reads from Tunarr, writes to client, splices alerts
        // ────────────────────────────────────────────────────────────────────

        private async Task PipeStreamLoop(
            ProxyChannelConfig chConfig,
            ChannelPipeState channelState,
            Stream clientStream,
            string clientId,
            CancellationToken ct)
        {
            string tunarrUrl = $"{_settings.TunarrBaseUrl.TrimEnd('/')}/stream/channels/{chConfig.TunarrChannelId}.ts";

            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            // ── Per-client stream state ────────────────────────────────
            var ccTracker = new ConcurrentDictionary<int, int>(); // PID → last CC sent
            long timestampOffset = 0; // 90kHz tick offset for splice transitions
            long lastPcrSeen = 0;
            long lastPtsSeen = 0;

            int consecutiveFailures = 0;
            int maxRetries = Math.Max(_settings.MaxReconnectRetries, 1);
            int reconnectBaseMs = Math.Max(_settings.ReconnectBaseMs, 500);

            while (!ct.IsCancellationRequested)
            {
                // ── Phase 1: Connect to Tunarr upstream ────────────────
                try
                {
                    Logger.Log($"[StreamPipe] {clientId}: Connecting to Tunarr ({tunarrUrl})", Logger.LogLevel.Debug);

                    using var response = await httpClient.GetAsync(tunarrUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var upstream = await response.Content.ReadAsStreamAsync(ct);

                    consecutiveFailures = 0; // connected successfully

                    var buffer = new byte[MpegTsHelper.PacketSize * 49]; // ~9KB per read
                    var leftover = new byte[MpegTsHelper.PacketSize];
                    int leftoverLen = 0;
                    var preallocWorkBuf = new byte[leftover.Length + buffer.Length]; // reusable work buffer

                    // Track which alert generation this client has already spliced
                    int lastSplicedGeneration = _alertGeneration;

                    // ── Phase 2: Forward Tunarr stream ─────────────────
                    while (!ct.IsCancellationRequested)
                    {
                        // ── Check for alert trigger ────────────────────
                        if (chConfig.AlertInterruptEnabled && _alertSignal.IsSet && _activeAlertTsPath != null)
                        {
                            int currentGen = _alertGeneration;
                            if (currentGen != lastSplicedGeneration)
                            {
                                string alertPath = _activeAlertTsPath;
                                double alertDur;
                                lock (_alertDurationLock) { alertDur = _activeAlertDuration; }

                                if (alertPath != null && File.Exists(alertPath))
                                {
                                    Logger.Log($"[StreamPipe] {clientId}: 🚨 Alert detected — splicing {Path.GetFileName(alertPath)} ({alertDur:F1}s)", Logger.LogLevel.Info);

                                    // ── Splice alert .ts ───────────────
                                    long preAlertPts = lastPtsSeen;
                                    bool needTimestampRecalc = false;

                                    try
                                    {
                                        var spliceResult = await SpliceAlertTs(alertPath, clientStream, ccTracker, preAlertPts, ct);

                                        // Mark that we need to recalculate timestamp offset
                                        // on the next PCR from Tunarr, so timestamps remain monotonic
                                        needTimestampRecalc = true;
                                        lastPtsSeen = spliceResult.EndPts;
                                        lastPcrSeen = spliceResult.EndPcr;

                                        Logger.Log($"[StreamPipe] {clientId}: ✓ Alert splice complete — resuming Tunarr (endPts={spliceResult.EndPts / 90000.0:F2}s)", Logger.LogLevel.Info);
                                    }
                                    catch (Exception spliceEx) when (!ct.IsCancellationRequested)
                                    {
                                        Logger.Log($"[StreamPipe] {clientId}: Alert splice failed: {spliceEx.Message}", Logger.LogLevel.Error);
                                    }

                                    lastSplicedGeneration = currentGen;

                                    // Continue reading from the SAME upstream — no reconnect needed.
                                    // The upstream kept sending data while we spliced; we just need to
                                    // drain/skip to the next keyframe and adjust timestamps.
                                    // The timestamp recalc flag will be handled below on next PCR read.
                                    if (needTimestampRecalc)
                                    {
                                        // We'll compute the new offset on the first PCR packet we read
                                        // from Tunarr after the splice. Set a sentinel value.
                                        timestampOffset = long.MinValue; // sentinel: "recalculate on next PCR"
                                    }

                                    continue; // resume inner loop — read from upstream
                                }
                            }
                        }

                        int bytesRead = await upstream.ReadAsync(buffer, 0, buffer.Length, ct);

                        if (bytesRead == 0)
                        {
                            // Upstream closed gracefully — send null packets to keep client alive
                            Logger.Log($"[StreamPipe] {clientId}: Upstream closed gracefully. Reconnecting in 2s...", Logger.LogLevel.Warning);
                            try
                            {
                                var nullPkt = MpegTsHelper.CreateNullPacket();
                                for (int i = 0; i < 50; i++)
                                    await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                                await clientStream.FlushAsync(ct);
                            }
                            catch { break; } // client gone

                            await Task.Delay(2000, ct);
                            break; // break inner loop to reconnect upstream
                        }

                        // ── Packet-aligned processing (zero-alloc) ────
                        byte[] workBuf;
                        int workLen;
                        if (leftoverLen > 0)
                        {
                            Buffer.BlockCopy(leftover, 0, preallocWorkBuf, 0, leftoverLen);
                            Buffer.BlockCopy(buffer, 0, preallocWorkBuf, leftoverLen, bytesRead);
                            workBuf = preallocWorkBuf;
                            workLen = leftoverLen + bytesRead;
                            leftoverLen = 0;
                        }
                        else
                        {
                            workBuf = buffer;
                            workLen = bytesRead;
                        }

                        int syncOff = MpegTsHelper.FindSyncOffset(workBuf, 0, workLen);
                        if (syncOff < 0)
                            continue; // no valid sync — discard and read more

                        int pos = syncOff;
                        while (pos + MpegTsHelper.PacketSize <= workLen)
                        {
                            if (workBuf[pos] != MpegTsHelper.SyncByte)
                            {
                                int nextSync = MpegTsHelper.FindSyncOffset(workBuf, pos, workLen - pos);
                                if (nextSync < 0) break;
                                pos = nextSync;
                                continue;
                            }

                            // ── Dynamic timestamp recalculation after splice ──
                            if (timestampOffset == long.MinValue && MpegTsHelper.HasPcr(workBuf, pos))
                            {
                                long tunarrPcr = MpegTsHelper.GetPcrBase(workBuf, pos);
                                if (tunarrPcr >= 0)
                                {
                                    // Tunarr's clock kept advancing during the splice.
                                    // We need: outgoing_time = lastPtsSeen (end of alert) + small_gap
                                    // So: offset = lastPtsSeen + gap - tunarrPcr
                                    long gap = 90000 / 4; // ~250ms gap for clean transition
                                    timestampOffset = (lastPtsSeen + gap) - tunarrPcr;
                                    Logger.Log($"[StreamPipe] {clientId}: Timestamp recalculated: offset={timestampOffset / 90000.0:F2}s (Tunarr PCR={tunarrPcr / 90000.0:F2}s, lastPts={lastPtsSeen / 90000.0:F2}s)", Logger.LogLevel.Debug);
                                }
                            }

                            // Track timestamps
                            if (MpegTsHelper.HasPcr(workBuf, pos))
                            {
                                long pcr = MpegTsHelper.GetPcrBase(workBuf, pos);
                                if (pcr >= 0) lastPcrSeen = pcr;
                            }

                            long pts = MpegTsHelper.GetPts(workBuf, pos);
                            if (pts >= 0)
                            {
                                if (timestampOffset != 0 && timestampOffset != long.MinValue)
                                    lastPtsSeen = pts + timestampOffset;
                                else
                                    lastPtsSeen = pts;
                            }

                            // Apply timestamp offset and CC rewriting
                            if (timestampOffset != 0 && timestampOffset != long.MinValue)
                            {
                                RewritePacket(workBuf, pos, ccTracker, timestampOffset);
                            }
                            else
                            {
                                // Track CC even during pass-through
                                int pid = MpegTsHelper.GetPid(workBuf, pos);
                                if (MpegTsHelper.HasPayload(workBuf, pos))
                                {
                                    ccTracker[pid] = MpegTsHelper.GetContinuityCounter(workBuf, pos);
                                }
                            }

                            pos += MpegTsHelper.PacketSize;
                        }

                        // Save leftover bytes
                        if (pos < workLen)
                        {
                            leftoverLen = workLen - pos;
                            Buffer.BlockCopy(workBuf, pos, leftover, 0, leftoverLen);
                        }

                        // Write processed packets to client (NoDelay=true pushes immediately)
                        int writeStart = syncOff;
                        int writeLen = pos - syncOff;
                        if (writeLen > 0)
                        {
                            await clientStream.WriteAsync(workBuf, writeStart, writeLen, ct);
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    consecutiveFailures++;
                    int backoffMs = Math.Min(reconnectBaseMs * (1 << Math.Min(consecutiveFailures, 4)), 15000);

                    Logger.Log($"[StreamPipe] {clientId}: Upstream connection lost ({ex.Message}). Reconnecting in {backoffMs / 1000.0:F0}s... (attempt {consecutiveFailures}/{maxRetries})", Logger.LogLevel.Warning);

                    // Send null packets to keep client decoder alive
                    try
                    {
                        var nullPkt = MpegTsHelper.CreateNullPacket();
                        for (int i = 0; i < 50; i++)
                            await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                        await clientStream.FlushAsync(ct);
                    }
                    catch { break; } // client gone too

                    if (consecutiveFailures >= maxRetries)
                    {
                        Logger.Log($"[StreamPipe] {clientId}: Too many consecutive upstream failures ({maxRetries}). Dropping client.", Logger.LogLevel.Error);
                        break;
                    }

                    await Task.Delay(backoffMs, ct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert .ts splice
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the alert .ts file and writes it to the client stream, rewriting
        /// continuity counters and offsetting timestamps for seamless splice.
        /// </summary>
        private static async Task<(long EndPts, long EndPcr)> SpliceAlertTs(
            string alertTsPath,
            Stream clientStream,
            ConcurrentDictionary<int, int> ccTracker,
            long baseTimestamp,
            CancellationToken ct)
        {
            long endPts = baseTimestamp;
            long endPcr = baseTimestamp;

            using var fs = new FileStream(alertTsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            var buffer = new byte[MpegTsHelper.PacketSize * 49];
            long alertTickOffset = baseTimestamp;

            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                int syncOff = MpegTsHelper.FindSyncOffset(buffer, 0, bytesRead);
                if (syncOff < 0) continue;

                int pos = syncOff;
                while (pos + MpegTsHelper.PacketSize <= bytesRead)
                {
                    if (buffer[pos] != MpegTsHelper.SyncByte)
                    {
                        pos++;
                        continue;
                    }

                    RewritePacket(buffer, pos, ccTracker, alertTickOffset);

                    if (MpegTsHelper.HasPcr(buffer, pos))
                    {
                        long pcr = MpegTsHelper.GetPcrBase(buffer, pos);
                        if (pcr > endPcr) endPcr = pcr;
                    }
                    long pts = MpegTsHelper.GetPts(buffer, pos);
                    if (pts > endPts) endPts = pts;

                    pos += MpegTsHelper.PacketSize;
                }

                int writeLen = pos - syncOff;
                if (writeLen > 0)
                {
                    await clientStream.WriteAsync(buffer, syncOff, writeLen, ct);
                    await clientStream.FlushAsync(ct);
                }
            }

            return (endPts, endPcr);
        }

        /// <summary>
        /// Rewrites a single TS packet in-place: updates continuity counter for its PID
        /// and offsets any PTS/DTS/PCR timestamps.
        /// </summary>
        private static void RewritePacket(
            byte[] buffer,
            int offset,
            ConcurrentDictionary<int, int> ccTracker,
            long tickOffset)
        {
            int pid = MpegTsHelper.GetPid(buffer, offset);
            if (pid == MpegTsHelper.PidNull) return;

            // ── Continuity counter ──────────────────────────────────────
            if (MpegTsHelper.HasPayload(buffer, offset))
            {
                int nextCc = ccTracker.AddOrUpdate(pid,
                    _ => 0,
                    (_, prev) => (prev + 1) & 0x0F);
                MpegTsHelper.SetContinuityCounter(buffer, offset, nextCc);
            }

            // ── PCR offset ──────────────────────────────────────────────
            if (tickOffset != 0 && MpegTsHelper.HasPcr(buffer, offset))
            {
                long pcr = MpegTsHelper.GetPcrBase(buffer, offset);
                if (pcr >= 0)
                    MpegTsHelper.SetPcrBase(buffer, offset, pcr + tickOffset);
            }

            // ── PTS/DTS offset ──────────────────────────────────────────
            if (tickOffset != 0)
            {
                MpegTsHelper.OffsetTimestamps(buffer, offset, tickOffset);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Per-channel state
        // ────────────────────────────────────────────────────────────────────

        private class ChannelPipeState : IDisposable
        {
            public ProxyChannelConfig Config { get; }
            public int ListenPort { get; }
            public TcpListener? Listener { get; set; }
            private int _clientCount;
            public int ClientCount => _clientCount;
            private readonly ConcurrentBag<Task> _clientTasks = new();

            public ChannelPipeState(ProxyChannelConfig config, int listenPort)
            {
                Config = config;
                ListenPort = listenPort;
            }

            public void IncrementClients() => Interlocked.Increment(ref _clientCount);
            public void DecrementClients() => Interlocked.Decrement(ref _clientCount);
            public void TrackClientTask(Task task) => _clientTasks.Add(task);

            public void Dispose()
            {
                try { Listener?.Stop(); } catch { }

                // Wait briefly for active client handlers to drain
                var activeTasks = _clientTasks.Where(t => !t.IsCompleted).ToArray();
                if (activeTasks.Length > 0)
                {
                    try { Task.WaitAll(activeTasks, TimeSpan.FromSeconds(3)); } catch { }
                }
            }
        }
    }
}
