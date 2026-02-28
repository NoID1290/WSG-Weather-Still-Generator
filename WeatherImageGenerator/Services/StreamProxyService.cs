using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// [OBSOLETE] Full HTTP MPEG-TS proxy with HDHR emulation. Replaced by StreamPipeService
    /// which provides a lightweight TCP byte pipe without HTTP overhead.
    /// Kept in the codebase for reference / optional use.
    /// </summary>
    [Obsolete("Use StreamPipeService instead — lightweight TCP byte pipe without HTTP proxy overhead.")]
    public class StreamProxyService
    {
        // ── Configuration ──────────────────────────────────────────────────
        private readonly StreamProxySettings _settings;
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }

        // ── Per-channel upstream state ─────────────────────────────────────
        private readonly ConcurrentDictionary<int, ChannelProxyState> _channels = new();

        // ── Alert splice state (global — one alert at a time) ──────────────
        private volatile string? _activeAlertTsPath;
        private double _activeAlertDuration;
        private readonly object _alertDurationLock = new();
        private readonly ManualResetEventSlim _alertSignal = new(false);
        private readonly ManualResetEventSlim _alertEnded = new(true);

        // ── Events ─────────────────────────────────────────────────────────
        public event EventHandler? ProxyStarted;
        public event EventHandler? ProxyStopped;
        public event EventHandler<string>? ProxyError;

        // ────────────────────────────────────────────────────────────────────
        // Construction
        // ────────────────────────────────────────────────────────────────────

        public StreamProxyService(StreamProxySettings settings)
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
                _httpListener = new HttpListener();

                string prefix = _settings.AllowRemoteAccess
                    ? $"http://*:{_settings.ListenPort}/"
                    : $"http://localhost:{_settings.ListenPort}/";

                _httpListener.Prefixes.Add(prefix);

                try
                {
                    _httpListener.Start();
                }
                catch (HttpListenerException hlEx) when (hlEx.ErrorCode == 5 && _settings.AllowRemoteAccess)
                {
                    Logger.Log($"[StreamProxy] Access denied for {prefix}. Attempting URL ACL + Firewall setup...", Logger.LogLevel.Warning);
                    if (TryRegisterAccess(prefix, _settings.ListenPort))
                    {
                        _httpListener = new HttpListener();
                        _httpListener.Prefixes.Add(prefix);
                        try { _httpListener.Start(); }
                        catch
                        {
                            Logger.Log("[StreamProxy] Retry failed, falling back to localhost.", Logger.LogLevel.Warning);
                            FallbackToLocalhost();
                        }
                    }
                    else
                    {
                        FallbackToLocalhost();
                    }
                }

                IsRunning = true;
                ProxyStarted?.Invoke(this, EventArgs.Empty);

                string localIp = NetworkHelper.GetLocalIPAddress();
                Logger.Log($"[StreamProxy] \u2713 MPEG-TS proxy started \u2014 port takeover active", Logger.LogLevel.Info);
                Logger.Log($"[StreamProxy]   Public port (clients):  {_settings.TunarrPublicPort}  \u2192 proxy intercepts here", Logger.LogLevel.Info);
                Logger.Log($"[StreamProxy]   Internal port (Tunarr): {_settings.TunarrInternalPort} \u2192 actual Tunarr at {_settings.TunarrBaseUrl}", Logger.LogLevel.Info);
                Logger.Log($"[StreamProxy]   HDHR discover: http://{localIp}:{_settings.ListenPort}/discover.json", Logger.LogLevel.Info);
                Logger.Log($"[StreamProxy]   M3U playlist:  http://{localIp}:{_settings.ListenPort}/channels.m3u", Logger.LogLevel.Info);
                Logger.Log($"[StreamProxy]   Channels configured: {_settings.Channels.Count}", Logger.LogLevel.Info);

                _ = ListenLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Logger.Log($"[StreamProxy] Failed to start: {ex.Message}", Logger.LogLevel.Error);
                ProxyError?.Invoke(this, ex.Message);
            }
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;

            try
            {
                _cts?.Cancel();

                // Dispose all upstream connections
                foreach (var kv in _channels)
                {
                    kv.Value.Dispose();
                }
                _channels.Clear();

                _httpListener?.Stop();
                _httpListener?.Close();
                IsRunning = false;
                Logger.Log("[StreamProxy] Proxy stopped.", Logger.LogLevel.Info);
                ProxyStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamProxy] Error stopping: {ex.Message}", Logger.LogLevel.Warning);
            }

            await Task.CompletedTask;
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert splice trigger (called from outside — Program.cs / MainForm)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers an EAS alert splice on all alert-enabled channels.
        /// The proxy will insert the pre-encoded .ts file into every active stream.
        /// </summary>
        /// <param name="alertTsPath">Path to the pre-encoded alert .ts file.</param>
        /// <param name="durationSeconds">Duration the alert should play (determines when to resume).</param>
        public void TriggerAlertSplice(string alertTsPath, double durationSeconds)
        {
            if (!IsRunning)
            {
                Logger.Log("[StreamProxy] Cannot trigger alert splice — proxy is not running.", Logger.LogLevel.Warning);
                return;
            }
            if (string.IsNullOrEmpty(alertTsPath) || !File.Exists(alertTsPath))
            {
                Logger.Log($"[StreamProxy] Cannot trigger alert splice — file missing or empty: '{alertTsPath}'", Logger.LogLevel.Warning);
                return;
            }

            // Count connected clients for diagnostic
            int totalClients = 0;
            foreach (var kv in _channels)
                totalClients += kv.Value.ClientCount;

            Logger.Log($"[StreamProxy] 🚨 EAS ALERT SPLICE triggered — {Path.GetFileName(alertTsPath)} ({durationSeconds:F1}s), {_settings.Channels.Count} channel(s) configured, {totalClients} client(s) connected", Logger.LogLevel.Info);

            if (totalClients == 0)
            {
                Logger.Log("[StreamProxy] ⚠ WARNING: No clients are currently connected through the proxy. The splice will have no visible effect.", Logger.LogLevel.Warning);
                Logger.Log($"[StreamProxy]   Clients should connect via Tunarr's original port {_settings.TunarrPublicPort} (now intercepted by proxy)", Logger.LogLevel.Warning);
            }

            _activeAlertTsPath = alertTsPath;
            lock (_alertDurationLock) { _activeAlertDuration = durationSeconds; }
            _alertEnded.Reset();
            _alertSignal.Set();
        }

        /// <summary>
        /// Signals the end of the alert splice — resume normal Tunarr forwarding.
        /// Called automatically after the alert .ts file finishes playing on each channel.
        /// </summary>
        public void EndAlertSplice()
        {
            Logger.Log("[StreamProxy] Alert splice completed — resuming normal streams.", Logger.LogLevel.Info);
            _alertSignal.Reset();
            _activeAlertTsPath = null;
            _alertEnded.Set();
        }

        // ────────────────────────────────────────────────────────────────────
        // HTTP request router
        // ────────────────────────────────────────────────────────────────────

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _httpListener?.IsListening == true)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequest(context, ct), ct);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    Logger.Log($"[StreamProxy] Listener error: {ex.Message}", Logger.LogLevel.Warning);
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context, CancellationToken ct)
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";

            try
            {
                // ── HDHR discovery endpoints ─────────────────────────────
                if (path.Equals("/discover.json", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeDiscoverJson(context);
                    return;
                }
                if (path.Equals("/lineup_status.json", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeLineupStatus(context);
                    return;
                }
                if (path.Equals("/lineup.json", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeLineupJson(context);
                    return;
                }
                if (path.Equals("/device.xml", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeDeviceXml(context);
                    return;
                }

                // ── M3U playlist ─────────────────────────────────────────
                if (path.Equals("/channels.m3u", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeM3U(context);
                    return;
                }

                // ── Stream endpoint: /stream/{number}.ts ─────────────────
                if (path.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                {
                    string segment = path.Substring("/stream/".Length);
                    segment = segment.Substring(0, segment.Length - 3); // strip .ts
                    if (int.TryParse(segment, out int channelNum))
                    {
                        await HandleStreamRequest(context, channelNum, ct);
                        return;
                    }
                }

                // ── Status endpoint ──────────────────────────────────────
                if (path.Equals("/status", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeStatus(context);
                    return;
                }

                context.Response.StatusCode = 404;
                context.Response.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamProxy] Request error ({path}): {ex.Message}", Logger.LogLevel.Debug);
                try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // HDHR / discovery endpoints
        // ────────────────────────────────────────────────────────────────────

        private async Task ServeDiscoverJson(HttpListenerContext ctx)
        {
            string host = ctx.Request.Url?.Host ?? "localhost";
            int port = _settings.ListenPort;

            var discover = new
            {
                FriendlyName = _settings.DeviceFriendlyName,
                Manufacturer = "WSG",
                ModelNumber = "WSG-EAS-1",
                FirmwareName = "WSG-StreamProxy",
                FirmwareVersion = "1.0",
                DeviceID = _settings.DeviceId,
                DeviceAuth = "wsg",
                BaseURL = $"http://{host}:{port}",
                LineupURL = $"http://{host}:{port}/lineup.json",
                TunerCount = Math.Max(_settings.Channels.Count, 2)
            };

            await RespondJson(ctx, discover);
        }

        private async Task ServeLineupStatus(HttpListenerContext ctx)
        {
            var status = new
            {
                ScanInProgress = 0,
                ScanPossible = 1,
                Source = "Cable",
                SourceList = new[] { "Cable" }
            };
            await RespondJson(ctx, status);
        }

        private async Task ServeLineupJson(HttpListenerContext ctx)
        {
            string host = ctx.Request.Url?.Host ?? "localhost";
            int port = _settings.ListenPort;

            var lineup = _settings.Channels.Select(ch => new
            {
                GuideNumber = ch.ProxyChannelNumber.ToString(),
                GuideName = ch.DisplayName,
                URL = $"http://{host}:{port}/stream/{ch.ProxyChannelNumber}.ts"
            }).ToArray();

            await RespondJson(ctx, lineup);
        }

        private async Task ServeDeviceXml(HttpListenerContext ctx)
        {
            string host = ctx.Request.Url?.Host ?? "localhost";
            int port = _settings.ListenPort;

            string xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"">
  <URLBase>http://{host}:{port}</URLBase>
  <device>
    <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
    <friendlyName>{System.Net.WebUtility.HtmlEncode(_settings.DeviceFriendlyName)}</friendlyName>
    <manufacturer>WSG</manufacturer>
    <modelName>WSG-EAS-Proxy</modelName>
    <modelNumber>1</modelNumber>
    <serialNumber>{_settings.DeviceId}</serialNumber>
    <UDN>uuid:{_settings.DeviceId}</UDN>
  </device>
</root>";

            ctx.Response.ContentType = "application/xml";
            byte[] data = Encoding.UTF8.GetBytes(xml);
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.Close();
        }

        private async Task ServeM3U(HttpListenerContext ctx)
        {
            string host = ctx.Request.Url?.Host ?? "localhost";
            int port = _settings.ListenPort;

            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var ch in _settings.Channels)
            {
                sb.AppendLine($"#EXTINF:-1 tvg-chno=\"{ch.ProxyChannelNumber}\",{ch.DisplayName}");
                sb.AppendLine($"http://{host}:{port}/stream/{ch.ProxyChannelNumber}.ts");
            }

            ctx.Response.ContentType = "audio/x-mpegurl";
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.Close();
        }

        private async Task ServeStatus(HttpListenerContext ctx)
        {
            var status = new
            {
                Running = IsRunning,
                AlertActive = _alertSignal.IsSet,
                AlertFile = _activeAlertTsPath != null ? Path.GetFileName(_activeAlertTsPath) : null,
                Channels = _settings.Channels.Select(ch => new
                {
                    ch.ProxyChannelNumber,
                    ch.DisplayName,
                    ch.AlertInterruptEnabled,
                    ConnectedClients = _channels.TryGetValue(ch.ProxyChannelNumber, out var state) ? state.ClientCount : 0
                })
            };
            await RespondJson(ctx, status);
        }

        // ────────────────────────────────────────────────────────────────────
        // Core stream proxy
        // ────────────────────────────────────────────────────────────────────

        private async Task HandleStreamRequest(HttpListenerContext ctx, int channelNum, CancellationToken ct)
        {
            // Find channel config
            var chConfig = _settings.Channels.FirstOrDefault(c => c.ProxyChannelNumber == channelNum);
            if (chConfig == null)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            // Set response headers for MPEG-TS streaming
            ctx.Response.ContentType = "video/mp2t";
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Connection", "close");
            ctx.Response.Headers.Add("Cache-Control", "no-cache, no-store");
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            var clientStream = ctx.Response.OutputStream;
            var clientId = Guid.NewGuid().ToString("N")[..8];
            Logger.Log($"[StreamProxy] Client {clientId} connected to channel {channelNum} ({chConfig.DisplayName})", Logger.LogLevel.Info);

            // Get or create per-channel state
            var channelState = _channels.GetOrAdd(channelNum, _ => new ChannelProxyState(channelNum));

            try
            {
                await ProxyStreamLoop(chConfig, channelState, clientStream, clientId, ct);
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is IOException || ex is ObjectDisposedException)
            {
                // Client disconnected — normal
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamProxy] Client {clientId} stream error: {ex.Message}", Logger.LogLevel.Debug);
            }
            finally
            {
                Logger.Log($"[StreamProxy] Client {clientId} disconnected from channel {channelNum}", Logger.LogLevel.Info);
                channelState.DecrementClients();
                try { ctx.Response.Close(); } catch { }
            }
        }

        /// <summary>
        /// Main proxy loop for a single client connection.
        /// Reads from Tunarr upstream, forwards to client, and handles alert splicing.
        /// </summary>
        private async Task ProxyStreamLoop(
            ProxyChannelConfig chConfig,
            ChannelProxyState channelState,
            Stream clientStream,
            string clientId,
            CancellationToken ct)
        {
            channelState.IncrementClients();

            string tunarrUrl = $"{_settings.TunarrBaseUrl.TrimEnd('/')}/stream/channels/{chConfig.TunarrChannelId}.ts";

            using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            // ── Continuity counter tracking: per-PID CC values ─────────
            var ccTracker = new ConcurrentDictionary<int, int>(); // PID → last CC sent
            long timestampOffset = 0; // 90kHz tick offset for splice transitions
            long lastPcrSeen = 0;
            long lastPtsSeen = 0;

            while (!ct.IsCancellationRequested)
            {
                // ── Phase 1: Forward Tunarr stream ─────────────────────
                try
                {
                    Logger.Log($"[StreamProxy] {clientId}: Connecting to Tunarr ({tunarrUrl})", Logger.LogLevel.Debug);

                    using var response = await httpClient.GetAsync(tunarrUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using var upstream = await response.Content.ReadAsStreamAsync(ct);

                    var buffer = new byte[MpegTsHelper.PacketSize * 49]; // ~49 packets per read (~9KB)
                    var leftover = new byte[MpegTsHelper.PacketSize];
                    int leftoverLen = 0;

                    while (!ct.IsCancellationRequested)
                    {
                        // Check for alert trigger
                        if (chConfig.AlertInterruptEnabled && _alertSignal.IsSet && _activeAlertTsPath != null)
                        {
                            // Grab a local copy of the alert path and clear the signal BEFORE splicing
                            // so other clients can also pick it up, and this client won't re-splice on reconnect.
                            string alertPath = _activeAlertTsPath;
                            double alertDur;
                            lock (_alertDurationLock) { alertDur = _activeAlertDuration; }

                            Logger.Log($"[StreamProxy] {clientId}: 🚨 Alert detected — splicing {Path.GetFileName(alertPath)} ({alertDur:F1}s) into channel {chConfig.ProxyChannelNumber}", Logger.LogLevel.Info);

                            // ── Phase 2: Splice alert .ts ──────────────────
                            long preAlertPts = lastPtsSeen;

                            try
                            {
                                var spliceResult = await SpliceAlertTs(alertPath, clientStream, ccTracker, preAlertPts, ct);

                                // Update timestamp offset so Tunarr stream continues monotonically
                                // after the alert ends. The alert's timestamps started at ~0 and we
                                // offset them to continue from preAlertPts. After it finishes,
                                // Tunarr's own timestamps will still be around where they were before
                                // the alert, so we need to skip over the alert duration.
                                long alertDurationTicks = (long)(alertDur * 90000); // 90kHz
                                timestampOffset += alertDurationTicks;

                                Logger.Log($"[StreamProxy] {clientId}: ✓ Alert splice complete — resuming Tunarr stream (ts offset: {timestampOffset / 90000.0:F2}s)", Logger.LogLevel.Info);
                            }
                            catch (Exception spliceEx) when (!ct.IsCancellationRequested)
                            {
                                Logger.Log($"[StreamProxy] {clientId}: Alert splice failed: {spliceEx.Message}", Logger.LogLevel.Error);
                            }

                            // Auto-reset the alert signal after this client has spliced.
                            // Use Interlocked exchange pattern: only the first client to finish resets the global state.
                            Interlocked.CompareExchange(ref _activeAlertTsPath, null, alertPath);
                            _alertSignal.Reset();
                            _alertEnded.Set();

                            // Wait briefly for Tunarr to catch up, then fall through to reconnect upstream.
                            await Task.Delay(500, ct);
                            break; // break inner loop to reconnect upstream
                        }

                        int bytesRead = await upstream.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (bytesRead == 0) break; // upstream closed

                        // ── Packet-aligned processing ──────────────────────
                        // Prepend any leftover bytes from previous read
                        byte[] workBuf;
                        int workLen;
                        if (leftoverLen > 0)
                        {
                            workBuf = new byte[leftoverLen + bytesRead];
                            Buffer.BlockCopy(leftover, 0, workBuf, 0, leftoverLen);
                            Buffer.BlockCopy(buffer, 0, workBuf, leftoverLen, bytesRead);
                            workLen = leftoverLen + bytesRead;
                            leftoverLen = 0;
                        }
                        else
                        {
                            workBuf = buffer;
                            workLen = bytesRead;
                        }

                        // Find first sync byte
                        int syncOff = MpegTsHelper.FindSyncOffset(workBuf, 0, workLen);
                        if (syncOff < 0)
                        {
                            // No valid sync found — discard and read more
                            continue;
                        }

                        // Process complete packets
                        int pos = syncOff;
                        while (pos + MpegTsHelper.PacketSize <= workLen)
                        {
                            if (workBuf[pos] != MpegTsHelper.SyncByte)
                            {
                                // Lost sync — try to find next
                                int nextSync = MpegTsHelper.FindSyncOffset(workBuf, pos, workLen - pos);
                                if (nextSync < 0) break;
                                pos = nextSync;
                                continue;
                            }

                            // Track timestamps for splice offset calculation
                            if (MpegTsHelper.HasPcr(workBuf, pos))
                            {
                                lastPcrSeen = MpegTsHelper.GetPcrBase(workBuf, pos);
                            }

                            long pts = MpegTsHelper.GetPts(workBuf, pos);
                            if (pts >= 0) lastPtsSeen = pts;

                            // Apply timestamp offset (non-zero after an alert splice)
                            if (timestampOffset != 0)
                            {
                                RewritePacket(workBuf, pos, ccTracker, timestampOffset);
                            }
                            else
                            {
                                // Track CC even during pass-through (for future splice)
                                int pid = MpegTsHelper.GetPid(workBuf, pos);
                                if (MpegTsHelper.HasPayload(workBuf, pos))
                                {
                                    ccTracker[pid] = MpegTsHelper.GetContinuityCounter(workBuf, pos);
                                }
                            }

                            pos += MpegTsHelper.PacketSize;
                        }

                        // Save leftover bytes for next read
                        if (pos < workLen)
                        {
                            leftoverLen = workLen - pos;
                            Buffer.BlockCopy(workBuf, pos, leftover, 0, leftoverLen);
                        }

                        // Write processed packets to client
                        int writeStart = syncOff;
                        int writeLen = pos - syncOff;
                        if (writeLen > 0)
                        {
                            await clientStream.WriteAsync(workBuf, writeStart, writeLen, ct);
                            await clientStream.FlushAsync(ct);
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Logger.Log($"[StreamProxy] {clientId}: Upstream connection lost ({ex.Message}). Reconnecting in 2s...", Logger.LogLevel.Warning);

                    // Send null packets to keep client alive during reconnection
                    try
                    {
                        var nullPkt = MpegTsHelper.CreateNullPacket();
                        for (int i = 0; i < 50; i++) // ~50 null packets
                        {
                            await clientStream.WriteAsync(nullPkt, 0, nullPkt.Length, ct);
                        }
                        await clientStream.FlushAsync(ct);
                    }
                    catch { break; } // client gone too

                    await Task.Delay(2000, ct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert .ts splice
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the alert .ts file and writes it to the client stream, rewriting
        /// continuity counters and offsetting timestamps for seamless splice.
        /// Returns (EndPts, EndPcr) — the highest PTS and PCR written.
        /// </summary>
        private async Task<(long EndPts, long EndPcr)> SpliceAlertTs(
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
            long alertTickOffset = baseTimestamp; // Offset alert timestamps (which start near 0) to continue from where Tunarr left off

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

                    // Rewrite CC + timestamps
                    RewritePacket(buffer, pos, ccTracker, alertTickOffset);

                    // Track the highest PTS/PCR we write
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

            // Skip null packets — no state to track
            if (pid == MpegTsHelper.PidNull) return;

            // ── Continuity counter ──────────────────────────────────────
            if (MpegTsHelper.HasPayload(buffer, offset))
            {
                int nextCc = ccTracker.AddOrUpdate(pid,
                    _ => 0, // first packet for this PID
                    (_, prev) => (prev + 1) & 0x0F);
                MpegTsHelper.SetContinuityCounter(buffer, offset, nextCc);
            }

            // ── PCR offset ──────────────────────────────────────────────
            if (tickOffset != 0 && MpegTsHelper.HasPcr(buffer, offset))
            {
                long pcr = MpegTsHelper.GetPcrBase(buffer, offset);
                if (pcr >= 0)
                {
                    MpegTsHelper.SetPcrBase(buffer, offset, pcr + tickOffset);
                }
            }

            // ── PTS/DTS offset ──────────────────────────────────────────
            if (tickOffset != 0)
            {
                MpegTsHelper.OffsetTimestamps(buffer, offset, tickOffset);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private void FallbackToLocalhost()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_settings.ListenPort}/");
            _httpListener.Prefixes.Add($"http://127.0.0.1:{_settings.ListenPort}/");
            _httpListener.Start();
            Logger.Log($"[StreamProxy] Fallback: listening on localhost:{_settings.ListenPort} only", Logger.LogLevel.Warning);
        }

        private static bool TryRegisterAccess(string prefix, int port)
        {
            try
            {
                string firewallRuleName = $"WSG_StreamProxy_{port}";
                string commands = string.Join(" & ",
                    $"netsh http add urlacl url={prefix} user=Everyone",
                    $"netsh advfirewall firewall delete rule name=\"{firewallRuleName}\" >nul 2>&1",
                    $"netsh advfirewall firewall add rule name=\"{firewallRuleName}\" dir=in action=allow protocol=TCP localport={port}"
                );

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {commands}",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(15000);
                    Logger.Log($"[StreamProxy] URL ACL + Firewall configured for port {port}", Logger.LogLevel.Info);
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception w32ex) when (w32ex.NativeErrorCode == 1223)
            {
                Logger.Log("[StreamProxy] User cancelled UAC prompt.", Logger.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Logger.Log($"[StreamProxy] Access setup error: {ex.Message}", Logger.LogLevel.Warning);
            }
            return false;
        }

        private static async Task RespondJson(HttpListenerContext ctx, object data)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
            ctx.Response.ContentLength64 = json.Length;
            await ctx.Response.OutputStream.WriteAsync(json, 0, json.Length);
            ctx.Response.Close();
        }

        // ────────────────────────────────────────────────────────────────────
        // Per-channel state tracker
        // ────────────────────────────────────────────────────────────────────

        private class ChannelProxyState : IDisposable
        {
            public int ChannelNumber { get; }
            private int _clientCount;
            public int ClientCount => _clientCount;

            public ChannelProxyState(int channelNumber)
            {
                ChannelNumber = channelNumber;
            }

            public void IncrementClients() => Interlocked.Increment(ref _clientCount);
            public void DecrementClients() => Interlocked.Decrement(ref _clientCount);

            public void Dispose() { }
        }
    }
}
