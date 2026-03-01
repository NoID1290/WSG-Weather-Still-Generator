using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
    /// All channels share a single TCP listener port (the public Tunarr port, e.g. 8000).
    /// When a client connects with an HTTP GET, the request path determines which Tunarr
    /// channel to pipe (e.g. GET /stream/channels/{uuid}.ts). Raw TCP clients without an
    /// HTTP request are routed to the first configured channel.
    /// </summary>
    public class StreamPipeService
    {
        // ── Configuration ──────────────────────────────────────────────────
        private readonly StreamProxySettings _settings;
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }

        // ── Per-channel state (keyed by ProxyChannelNumber) ────────────────
        private readonly ConcurrentDictionary<int, ChannelPipeState> _channels = new();

        // ── Channel lookup by TunarrChannelId (for HTTP path routing) ──────
        private readonly ConcurrentDictionary<string, ChannelPipeState> _channelsByTunarrId = new();

        // ── Single shared TCP listener ─────────────────────────────────────
        private TcpListener? _sharedListener;

        // ── Alert splice state (global — one alert at a time) ──────────────
        private volatile string? _activeAlertTsPath;
        private double _activeAlertDuration;
        private readonly object _alertDurationLock = new();
        private readonly ManualResetEventSlim _alertSignal = new(false);
        private int _alertGeneration; // incremented per alert to prevent stale-signal races

        // ── Regex to extract channel ID from HTTP request path ─────────────
        // Matches: /stream/channels/{id}.ts  or  /stream/{N}.ts  or just /{id}.ts
        private static readonly Regex ChannelPathRegex = new(
            @"/(?:stream/(?:channels/)?)?([^/]+?)(?:\.ts)?(?:\?.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ── Sentinel returned by HandleHttpHandshake when the request was an API
        //    proxy (already fully handled — caller should just close the connection).
        private const string API_PROXY_HANDLED = "\x00__API_PROXIED__";

        // ── Shared HttpClient for short-lived API proxy requests ───────────
        private static readonly HttpClient _apiProxyClient = new() { Timeout = TimeSpan.FromSeconds(30) };

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

                if (_settings.Channels.Count == 0)
                {
                    Logger.Log("[StreamPipe] No channels configured — nothing to pipe.", Logger.LogLevel.Warning);
                    return;
                }

                // Register all channels (single shared listener — no per-channel ports)
                foreach (var ch in _settings.Channels)
                {
                    var state = new ChannelPipeState(ch, _settings.ListenPort);
                    _channels[ch.ProxyChannelNumber] = state;

                    // Index by TunarrChannelId for HTTP path-based routing
                    if (!string.IsNullOrEmpty(ch.TunarrChannelId))
                        _channelsByTunarrId[ch.TunarrChannelId] = state;
                }

                // Start single shared TCP listener on the public port
                _ = StartSharedListener(_cts.Token);

                IsRunning = true;

                string localIp = NetworkHelper.GetLocalIPAddress();
                Logger.Log($"[StreamPipe] ✓ MPEG-TS byte pipe started on port {_settings.ListenPort}", Logger.LogLevel.Info);
                Logger.Log($"[StreamPipe]   Tunarr upstream: {_settings.TunarrBaseUrl}", Logger.LogLevel.Info);
                foreach (var kv in _channels.OrderBy(k => k.Key))
                {
                    var s = kv.Value;
                    Logger.Log($"[StreamPipe]   Channel {s.Config.ProxyChannelNumber} ({s.Config.DisplayName}): http://{localIp}:{_settings.ListenPort}/stream/channels/{s.Config.TunarrChannelId}.ts  alert={s.Config.AlertInterruptEnabled}", Logger.LogLevel.Info);
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

                try { _sharedListener?.Stop(); } catch { }

                foreach (var kv in _channels)
                    kv.Value.Dispose();
                _channels.Clear();
                _channelsByTunarrId.Clear();

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
        // Single shared TCP listener
        // ────────────────────────────────────────────────────────────────────

        private async Task StartSharedListener(CancellationToken ct)
        {
            _sharedListener = new TcpListener(
                _settings.AllowRemoteAccess ? IPAddress.Any : IPAddress.Loopback,
                _settings.ListenPort);

            try
            {
                _sharedListener.Start();
                Logger.Log($"[StreamPipe] Listening on port {_settings.ListenPort} (all channels)", Logger.LogLevel.Debug);

                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _sharedListener.AcceptTcpClientAsync(ct);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    // Route and handle each client in its own task
                    _ = Task.Run(() => RouteAndHandleClient(client, ct), ct);
                }
            }
            catch (SocketException ex)
            {
                Logger.Log($"[StreamPipe] Listener failed on port {_settings.ListenPort}: {ex.Message}", Logger.LogLevel.Error);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.Log($"[StreamPipe] Listener error: {ex.Message}", Logger.LogLevel.Error);
            }
            finally
            {
                try { _sharedListener?.Stop(); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Client routing (parses HTTP path → channel)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Accepts a new TCP connection, reads the HTTP request (if any) to determine
        /// which channel the client wants, then routes to the correct channel pipe.
        /// </summary>
        private async Task RouteAndHandleClient(TcpClient tcpClient, CancellationToken ct)
        {
            var clientId = Guid.NewGuid().ToString("N")[..8];
            ChannelPipeState? channelState = null;

            try
            {
                tcpClient.NoDelay = true;
                tcpClient.SendBufferSize = 65536;
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                using var clientStream = tcpClient.GetStream();

                // Read HTTP request and extract channel ID from path.
                // Also sends the HTTP 200 response if it's an HTTP client.
                // Non-stream (API) requests are transparently proxied to Tunarr.
                string? resolvedChannelId = await HandleHttpHandshake(clientStream, ct);

                // If the request was already handled as an API proxy, we're done
                if (resolvedChannelId == API_PROXY_HANDLED)
                    return;

                // Resolve channel: try by TunarrChannelId first, then by channel number
                channelState = ResolveChannel(resolvedChannelId);

                if (channelState == null)
                {
                    Logger.Log($"[StreamPipe] Client {clientId}: could not resolve channel from request (path id='{resolvedChannelId ?? "null"}') — rejecting", Logger.LogLevel.Warning);
                    return;
                }

                channelState.IncrementClients();

                Logger.Log($"[StreamPipe] Client {clientId} connected to channel {channelState.Config.ProxyChannelNumber} ({channelState.Config.DisplayName})", Logger.LogLevel.Info);

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
                if (channelState != null)
                {
                    Logger.Log($"[StreamPipe] Client {clientId} disconnected from channel {channelState.Config.ProxyChannelNumber}", Logger.LogLevel.Info);
                    channelState.DecrementClients();
                }
                try { tcpClient.Close(); } catch { }
            }
        }

        /// <summary>
        /// Resolves a channel ID string to a ChannelPipeState.
        /// Tries: exact TunarrChannelId match → case-insensitive → channel number → fallback.
        /// </summary>
        private ChannelPipeState? ResolveChannel(string? channelId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                // No HTTP path or raw TCP client — fall back to first channel
                return _channels.Values.FirstOrDefault();
            }

            // Try exact TunarrChannelId match (UUID)
            if (_channelsByTunarrId.TryGetValue(channelId, out var byId))
                return byId;

            // Try case-insensitive TunarrChannelId match
            var caseInsensitive = _channelsByTunarrId
                .FirstOrDefault(kv => string.Equals(kv.Key, channelId, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitive.Value != null)
                return caseInsensitive.Value;

            // Try parsing as a channel number
            if (int.TryParse(channelId, out int chNum) && _channels.TryGetValue(chNum, out var byNum))
                return byNum;

            // No match — fall back to first channel
            Logger.Log($"[StreamPipe] Channel ID '{channelId}' not found — falling back to first channel", Logger.LogLevel.Warning);
            return _channels.Values.FirstOrDefault();
        }

        /// <summary>
        /// Returns true if the HTTP request path is a stream path that should be
        /// handled by the pipe. All other paths are proxied transparently to Tunarr.
        /// </summary>
        private static bool IsStreamPath(string path)
        {
            return path.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the HTTP request from the client (if present), sends an HTTP 200 response,
        /// and extracts the channel ID from the request path.
        /// Non-stream paths (API requests such as /api/xmltv.xml, /api/channels.m3u,
        /// HDHR discovery, etc.) are transparently proxied to Tunarr's internal port.
        /// Returns the extracted channel ID string, null if no HTTP request was sent,
        /// or API_PROXY_HANDLED if the request was already proxied to Tunarr.
        /// </summary>
        private async Task<string?> HandleHttpHandshake(NetworkStream stream, CancellationToken ct)
        {
            stream.ReadTimeout = 500; // ms
            string? channelId = null;

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
                            // Extract request path: "GET /stream/channels/{id}.ts HTTP/1.1\r\n..."
                            int pathStart = 4; // after "GET "
                            int pathEnd = header.IndexOf(' ', pathStart);
                            if (pathEnd < 0) pathEnd = header.IndexOf('\r', pathStart);
                            if (pathEnd < 0) pathEnd = header.Length;

                            string requestPath = header.Substring(pathStart, pathEnd - pathStart);

                            // ── Non-stream request → proxy to Tunarr's internal port ──
                            if (!IsStreamPath(requestPath))
                            {
                                stream.ReadTimeout = Timeout.Infinite;
                                await ProxyApiRequestToTunarr(stream, requestPath, ct);
                                return API_PROXY_HANDLED;
                            }

                            // ── Stream request → extract channel ID ──
                            var match = ChannelPathRegex.Match(requestPath);
                            if (match.Success)
                            {
                                channelId = match.Groups[1].Value;
                            }

                            Logger.Log($"[StreamPipe] HTTP request: GET {requestPath} → channel id='{channelId ?? "?"}'", Logger.LogLevel.Debug);

                            // Send HTTP 200 response for MPEG-TS stream
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

            return channelId;
        }

        /// <summary>
        /// Forwards a non-stream HTTP GET request to Tunarr's internal port and writes
        /// the complete HTTP response (status line + headers + body) back to the client.
        /// This allows API endpoints like /api/xmltv.xml, /api/channels.m3u, HDHR
        /// discovery (/discover.json, /lineup.json, etc.) to work transparently through
        /// the proxy.
        /// </summary>
        private async Task ProxyApiRequestToTunarr(NetworkStream clientStream, string requestPath, CancellationToken ct)
        {
            try
            {
                string tunarrUrl = $"{_settings.TunarrBaseUrl.TrimEnd('/')}{requestPath}";
                Logger.Log($"[StreamPipe] API proxy: GET {requestPath} → {tunarrUrl}", Logger.LogLevel.Debug);

                using var response = await _apiProxyClient.GetAsync(tunarrUrl, ct);

                // Build the raw HTTP response to send back to the client
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");

                // Forward response headers
                foreach (var h in response.Headers)
                    foreach (var v in h.Value)
                        sb.Append($"{h.Key}: {v}\r\n");
                foreach (var h in response.Content.Headers)
                    foreach (var v in h.Value)
                        sb.Append($"{h.Key}: {v}\r\n");

                sb.Append("Access-Control-Allow-Origin: *\r\n");
                sb.Append("Connection: close\r\n");
                sb.Append("\r\n");

                var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
                await clientStream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);

                // Forward the body
                var body = await response.Content.ReadAsByteArrayAsync(ct);
                if (body.Length > 0)
                {
                    await clientStream.WriteAsync(body, 0, body.Length, ct);
                }

                await clientStream.FlushAsync(ct);
                Logger.Log($"[StreamPipe] API proxy: {requestPath} → {(int)response.StatusCode} ({body.Length} bytes)", Logger.LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamPipe] API proxy error for {requestPath}: {ex.Message}", Logger.LogLevel.Warning);

                // Try to send a 502 Bad Gateway response
                try
                {
                    string errResponse = "HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
                    var errBytes = Encoding.ASCII.GetBytes(errResponse);
                    await clientStream.WriteAsync(errBytes, 0, errBytes.Length, ct);
                    await clientStream.FlushAsync(ct);
                }
                catch { }
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

            // ── Upstream PID map (learned from PAT/PMT) for splice PID remapping ──
            var upstreamPidMap = new UpstreamPidMap();

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
                                        var spliceResult = await SpliceAlertTs(alertPath, clientStream, ccTracker, preAlertPts, upstreamPidMap, clientId, ct);

                                        // Mark that we need to recalculate timestamp offset
                                        // on the next PCR from Tunarr, so timestamps remain monotonic
                                        needTimestampRecalc = true;
                                        lastPtsSeen = spliceResult.EndPts;
                                        lastPcrSeen = spliceResult.EndPcr;

                                        // Mark this generation as spliced so we don't re-splice
                                        // the same alert in a tight loop
                                        lastSplicedGeneration = currentGen;

                                        Logger.Log($"[StreamPipe] {clientId}: ✓ Alert splice complete — resuming Tunarr (endPts={spliceResult.EndPts / 90000.0:F2}s)", Logger.LogLevel.Info);
                                    }
                                    catch (Exception spliceEx) when (spliceEx is IOException || spliceEx is SocketException)
                                    {
                                        // Client disconnected during splice — propagate so outer handler exits cleanly
                                        Logger.Log($"[StreamPipe] {clientId}: Alert splice failed (client disconnected): {spliceEx.Message}", Logger.LogLevel.Warning);
                                        throw;
                                    }

                                    // We'll compute the new offset on the first PCR packet we read
                                    // from Tunarr after the splice. Set a sentinel value.
                                    timestampOffset = long.MinValue; // sentinel: "recalculate on next PCR"

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

                            // ── Learn upstream PID structure from PAT/PMT ──
                            upstreamPidMap.LearnFromPacket(workBuf, pos);

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
                catch (Exception ex) when (ex is IOException || ex is SocketException)
                {
                    // Distinguish client disconnect from upstream failure:
                    // If we can't write null packets to the client, the client is gone.
                    bool clientAlive = true;
                    try
                    {
                        var nullPkt = MpegTsHelper.CreateNullPacket();
                        await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                        await clientStream.FlushAsync(ct);
                    }
                    catch
                    {
                        clientAlive = false;
                    }

                    if (!clientAlive)
                        throw; // re-throw so RouteAndHandleClient catches it as a clean disconnect

                    // Client is alive — this was an upstream failure
                    consecutiveFailures++;
                    int backoffMs = Math.Min(reconnectBaseMs * (1 << Math.Min(consecutiveFailures, 4)), 15000);

                    Logger.Log($"[StreamPipe] {clientId}: Upstream connection lost ({ex.Message}). Reconnecting in {backoffMs / 1000.0:F0}s... (attempt {consecutiveFailures}/{maxRetries})", Logger.LogLevel.Warning);

                    // Send more null packets to keep client decoder alive
                    try
                    {
                        var nullPkt = MpegTsHelper.CreateNullPacket();
                        for (int i = 0; i < 49; i++)
                            await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                        await clientStream.FlushAsync(ct);
                    }
                    catch { break; }

                    if (consecutiveFailures >= maxRetries)
                    {
                        Logger.Log($"[StreamPipe] {clientId}: Too many consecutive upstream failures ({maxRetries}). Dropping client.", Logger.LogLevel.Error);
                        break;
                    }

                    await Task.Delay(backoffMs, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    consecutiveFailures++;
                    int backoffMs = Math.Min(reconnectBaseMs * (1 << Math.Min(consecutiveFailures, 4)), 15000);

                    Logger.Log($"[StreamPipe] {clientId}: Upstream error ({ex.Message}). Reconnecting in {backoffMs / 1000.0:F0}s... (attempt {consecutiveFailures}/{maxRetries})", Logger.LogLevel.Warning);

                    try
                    {
                        var nullPkt = MpegTsHelper.CreateNullPacket();
                        for (int i = 0; i < 50; i++)
                            await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                        await clientStream.FlushAsync(ct);
                    }
                    catch { break; }

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
        /// Splices an alert .ts into the client stream. The approach:
        ///   1. Send a burst of null packets to cleanly break the decoder's state
        ///   2. Send the complete alert .ts with its own PAT/PMT (like a channel change),
        ///      rewriting only CC counters and timestamp offsets
        ///   3. The file is written as fast as I/O allows — decoders buffer the data
        ///      and use PTS timestamps to render at the correct playback rate
        /// When the caller resumes the Tunarr stream, Tunarr's PAT/PMT will re-establish
        /// the original program. This "channel-change" approach is widely supported by
        /// all MPEG-TS clients including Plex, Jellyfin, VLC, and hardware STBs.
        /// </summary>
        private static async Task<(long EndPts, long EndPcr)> SpliceAlertTs(
            string alertTsPath,
            Stream clientStream,
            ConcurrentDictionary<int, int> ccTracker,
            long baseTimestamp,
            UpstreamPidMap upstreamPids,
            string clientId,
            CancellationToken ct)
        {
            long endPts = baseTimestamp;
            long endPcr = baseTimestamp;

            // ── Step 1: Send null packet burst to signal a clean break ──
            // ~500 null packets (~92KB) creates a gap the decoder can use to
            // drain buffers and reset. This is standard practice in low-cost
            // MPEG-TS ad-insertion systems.
            var nullPkt = MpegTsHelper.CreateNullPacket();
            var nullBurst = new byte[MpegTsHelper.PacketSize * 500];
            for (int i = 0; i < 500; i++)
                Buffer.BlockCopy(nullPkt, 0, nullBurst, i * MpegTsHelper.PacketSize, MpegTsHelper.PacketSize);

            await clientStream.WriteAsync(nullBurst, 0, nullBurst.Length, ct);
            await clientStream.FlushAsync(ct);

            Logger.Log($"[StreamPipe] {clientId}: Sent {nullBurst.Length / 1024}KB null packet gap before alert splice", Logger.LogLevel.Debug);

            // ── Step 2: Read and forward alert .ts with CC + timestamp rewrite ──
            using var fs = new FileStream(alertTsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            var buffer = new byte[MpegTsHelper.PacketSize * 49]; // ~9KB per read
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

                    // Rewrite CC counters and offset timestamps — nothing else.
                    // The alert's own PAT/PMT flow through untouched (except CC),
                    // so the client can lock onto the new program structure.
                    RewritePacket(buffer, pos, ccTracker, alertTickOffset);

                    // Track end timestamps for the caller
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
                }
            }

            // Flush after sending all alert data
            await clientStream.FlushAsync(ct);

            // Send another small null burst to signal end of alert before Tunarr resumes
            var smallNull = new byte[MpegTsHelper.PacketSize * 50];
            for (int i = 0; i < 50; i++)
                Buffer.BlockCopy(nullPkt, 0, smallNull, i * MpegTsHelper.PacketSize, MpegTsHelper.PacketSize);
            await clientStream.WriteAsync(smallNull, 0, smallNull.Length, ct);
            await clientStream.FlushAsync(ct);

            return (endPts, endPcr);
        }

        /// <summary>
        /// Builds a PID remap table: alert file PIDs → upstream (Tunarr) PIDs.
        /// Maps PMT PID, video PID(s), audio PID(s), and PCR PID.
        /// Currently unused but retained for future PID-remapping splice mode.
        /// </summary>
        private static Dictionary<int, int> BuildPidRemapTable(UpstreamPidMap alertPids, UpstreamPidMap upstreamPids)
        {
            var remap = new Dictionary<int, int>();

            // Don't remap if we haven't learned the upstream PIDs yet
            if (!upstreamPids.IsLearned) return remap;
            if (!alertPids.IsLearned) return remap;

            // Remap PMT PID
            if (alertPids.PmtPid > 0 && upstreamPids.PmtPid > 0 && alertPids.PmtPid != upstreamPids.PmtPid)
                remap[alertPids.PmtPid] = upstreamPids.PmtPid;

            // Remap video PID
            if (alertPids.VideoPid > 0 && upstreamPids.VideoPid > 0 && alertPids.VideoPid != upstreamPids.VideoPid)
                remap[alertPids.VideoPid] = upstreamPids.VideoPid;

            // Remap audio PID
            if (alertPids.AudioPid > 0 && upstreamPids.AudioPid > 0 && alertPids.AudioPid != upstreamPids.AudioPid)
                remap[alertPids.AudioPid] = upstreamPids.AudioPid;

            // Remap PCR PID (often same as video, but could be separate)
            if (alertPids.PcrPid > 0 && upstreamPids.PcrPid > 0 && alertPids.PcrPid != upstreamPids.PcrPid)
            {
                if (!remap.ContainsKey(alertPids.PcrPid))
                    remap[alertPids.PcrPid] = upstreamPids.PcrPid;
            }

            return remap;
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
            private int _clientCount;
            public int ClientCount => _clientCount;

            public ChannelPipeState(ProxyChannelConfig config, int listenPort)
            {
                Config = config;
                ListenPort = listenPort;
            }

            public void IncrementClients() => Interlocked.Increment(ref _clientCount);
            public void DecrementClients() => Interlocked.Decrement(ref _clientCount);

            public void Dispose() { }
        }

        // ────────────────────────────────────────────────────────────────────
        // Upstream PID map — learned from PAT/PMT flowing through the pipe
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tracks the PID structure of the upstream MPEG-TS by parsing PAT and PMT
        /// packets as they flow through the pipe. Used to remap alert .ts PIDs so
        /// they match the upstream's program structure, enabling clean splicing.
        /// </summary>
        private class UpstreamPidMap
        {
            public int PmtPid { get; private set; } = -1;
            public int PcrPid { get; private set; } = -1;
            public int VideoPid { get; private set; } = -1;
            public int AudioPid { get; private set; } = -1;

            /// <summary>True once we've successfully parsed both PAT and PMT.</summary>
            public bool IsLearned => PmtPid > 0 && (VideoPid > 0 || AudioPid > 0);

            private readonly HashSet<int> _pmtPids = new();

            /// <summary>
            /// Inspects a TS packet. If it's a PAT, learns the PMT PID(s).
            /// If it's a PMT, learns video/audio/PCR PIDs.
            /// </summary>
            public void LearnFromPacket(byte[] buffer, int offset)
            {
                int pid = MpegTsHelper.GetPid(buffer, offset);

                if (pid == MpegTsHelper.PidPat && MpegTsHelper.HasPayloadUnitStart(buffer, offset))
                {
                    var pmtPids = MpegTsHelper.ParsePatForPmtPids(buffer, offset);
                    if (pmtPids.Count > 0)
                    {
                        PmtPid = pmtPids[0]; // use first program's PMT
                        _pmtPids.Clear();
                        foreach (var p in pmtPids) _pmtPids.Add(p);
                    }
                }
                else if (_pmtPids.Contains(pid) && MpegTsHelper.HasPayloadUnitStart(buffer, offset))
                {
                    var (pcrPid, streams) = MpegTsHelper.ParsePmt(buffer, offset);
                    if (pcrPid >= 0) PcrPid = pcrPid;

                    foreach (var (streamType, esPid) in streams)
                    {
                        if (MpegTsHelper.IsVideoStreamType(streamType) && VideoPid < 0)
                            VideoPid = esPid;
                        else if (MpegTsHelper.IsAudioStreamType(streamType) && AudioPid < 0)
                            AudioPid = esPid;
                    }
                }
            }
        }
    }
}
