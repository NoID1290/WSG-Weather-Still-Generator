using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// HTTP HLS relay — serves Tunarr's HLS stream through our own HTTP endpoint and
    /// injects EAS alert segments into manifest responses at 4-second segment boundaries.
    ///
    /// Tunarr's disk cache is READ-ONLY. We never write to any of Tunarr's files.
    ///
    /// Setup: point TvMate's M3U source at  http://{server}:{HlsRelayPort}/m3u
    ///
    /// Alert delivery flow:
    ///   1. TriggerAlertSplice() segments the alert .ts into HLS chunks via FFmpeg (software encoder)
    ///   2. Records the current segment count from Tunarr's manifest as the injection point
    ///   3. Every manifest response from that point on presents:
    ///        ... existing Tunarr segments up to injection point ...
    ///        #EXT-X-DISCONTINUITY
    ///        ... EAS chunks (served from our temp dir) ...
    ///        #EXT-X-DISCONTINUITY
    ///        ... new Tunarr segments after injection point ...
    ///   4. After alert duration + grace period, manifest returns to pure pass-through
    ///
    /// Route table:
    ///   GET /m3u                         — M3U playlist for TvMate / VLC
    ///   GET /ch/{uuid}/stream.m3u8       — per-channel manifest (modified)
    ///   GET /ch/{uuid}/data{N}.ts        — Tunarr segment from disk cache
    ///   GET /ch/{uuid}/eas_{N}.ts        — EAS segment from temp dir
    /// </summary>
    public class HlsRelayService
    {
        private readonly StreamProxySettings _settings;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        // Alert injection state — only mutated in TriggerAlertSpliceAsync (single writer)
        private volatile bool _alertActive;
        private DateTime _alertExpiry;
        private int _injectionAfterSegment;   // inject EAS after this many segments are seen
        private List<(string FilePath, double Duration)> _easSegments = new();
        private string _easTempDir = "";

        public bool IsRunning { get; private set; }

        public HlsRelayService(StreamProxySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────────

        public void Start()
        {
            if (IsRunning) return;

            if (string.IsNullOrWhiteSpace(_settings.TunarrStreamCachePath))
            {
                Logger.Log("[HlsRelay] Cannot start — TunarrStreamCachePath is not configured.", Logger.LogLevel.Warning);
                return;
            }

            if (!Directory.Exists(_settings.TunarrStreamCachePath))
            {
                Logger.Log($"[HlsRelay] Stream cache path does not exist: {_settings.TunarrStreamCachePath}", Logger.LogLevel.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();

            // Use + prefix for remote access, localhost for local-only
            string prefix = _settings.AllowRemoteAccess
                ? $"http://+:{_settings.HlsRelayPort}/"
                : $"http://localhost:{_settings.HlsRelayPort}/";

            try
            {
                _listener.Prefixes.Add(prefix);
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                Logger.Log($"[HlsRelay] Failed to start listener on {prefix}: {ex.Message}", Logger.LogLevel.Error);
                Logger.Log($"[HlsRelay] If access denied, run (as admin): netsh http add urlacl url=http://+:{_settings.HlsRelayPort}/ user=Everyone", Logger.LogLevel.Warning);
                _cts.Dispose();
                _cts = null;
                return;
            }

            IsRunning = true;
            _ = AcceptLoopAsync(_cts.Token);

            Logger.Log($"[HlsRelay] ✓ Started on port {_settings.HlsRelayPort}. " +
                $"Point TvMate M3U at: http://{{server}}:{_settings.HlsRelayPort}/m3u", Logger.LogLevel.Info);
        }

        public async Task StopAsync()
        {
            if (!IsRunning) return;
            IsRunning = false;
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            await Task.Delay(200).ConfigureAwait(false);
            CleanupEasTempDir();
            Logger.Log("[HlsRelay] Stopped.", Logger.LogLevel.Info);
        }

        // ────────────────────────────────────────────────────────────────────
        // Accept loop
        // ────────────────────────────────────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                try
                {
                    var ctx = await _listener!.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(ctx), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested || !IsRunning) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Logger.Log($"[HlsRelay] Accept error: {ex.Message}", Logger.LogLevel.Warning);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Request dispatch
        // ────────────────────────────────────────────────────────────────────

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            try
            {
                // GET /m3u  or  /m3u.m3u  — TvMate channel list
                if (path.Equals("/m3u", StringComparison.OrdinalIgnoreCase) ||
                    path.Equals("/m3u.m3u", StringComparison.OrdinalIgnoreCase))
                {
                    string host = ctx.Request.UserHostName ?? $"localhost:{_settings.HlsRelayPort}";
                    await SendTextAsync(ctx.Response, BuildM3uPlaylist(host), "application/x-mpegurl").ConfigureAwait(false);
                    return;
                }

                // /ch/{uuid}/stream.m3u8  or  /ch/{uuid}/{file}.ts
                if (path.StartsWith("/ch/", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = path.TrimStart('/').Split('/');
                    if (parts.Length >= 3)
                    {
                        string uuid = parts[1];
                        string file = parts[2];

                        if (file.Equals("stream.m3u8", StringComparison.OrdinalIgnoreCase))
                        {
                            await ServeManifestAsync(uuid, ctx).ConfigureAwait(false);
                            return;
                        }

                        if (file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                        {
                            await ServeSegmentAsync(uuid, file, ctx).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsRelay] {path} — {ex.Message}", Logger.LogLevel.Debug);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Manifest relay + EAS injection
        // ────────────────────────────────────────────────────────────────────

        private async Task ServeManifestAsync(string uuid, HttpListenerContext ctx)
        {
            string cacheDir = Path.Combine(_settings.TunarrStreamCachePath, $"stream_{uuid}");
            string m3u8Path = Path.Combine(cacheDir, "stream.m3u8");

            if (!File.Exists(m3u8Path))
            {
                // Tunarr hasn't started streaming this channel yet — client should retry
                ctx.Response.StatusCode = 503;
                ctx.Response.Headers["Retry-After"] = "2";
                ctx.Response.Close();
                return;
            }

            string? rawContent = ReadFileSafe(m3u8Path);
            if (rawContent == null)
            {
                ctx.Response.StatusCode = 503;
                ctx.Response.Headers["Retry-After"] = "1";
                ctx.Response.Close();
                return;
            }

            // Check if alert has expired
            if (_alertActive && DateTime.UtcNow >= _alertExpiry)
            {
                _alertActive = false;
                Logger.Log("[HlsRelay] Alert expired — returning to pass-through mode.", Logger.LogLevel.Info);
            }

            string modified = _alertActive
                ? BuildInjectedManifest(rawContent, uuid)
                : RewriteSegmentUrls(rawContent, uuid);

            await SendTextAsync(ctx.Response, modified, "application/x-mpegurl").ConfigureAwait(false);
        }

        /// <summary>
        /// Rewrites bare segment filenames in the manifest to relay URLs.
        /// e.g. "data000500.ts" → "/ch/{uuid}/data000500.ts"
        /// Lines that already start with "/" or "http" are left unchanged (Tunarr unlikely, but safe).
        /// </summary>
        private static string RewriteSegmentUrls(string content, string uuid)
        {
            var sb = new StringBuilder(content.Length + 512);
            foreach (var rawLine in content.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("#") &&
                    !line.StartsWith("/") &&
                    !line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append($"/ch/{uuid}/{Path.GetFileName(line)}");
                    sb.Append('\n');
                }
                else
                {
                    sb.Append(line);
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds a manifest with EAS segments injected after segment #_injectionAfterSegment.
        /// </summary>
        private string BuildInjectedManifest(string content, string uuid)
        {
            var lines = content.Split('\n');
            var output = new StringBuilder(content.Length + 2048);

            int segmentsSeen = 0;
            bool injected = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                bool isSegmentUri = line.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) &&
                                    !line.StartsWith("#") &&
                                    !line.StartsWith("http", StringComparison.OrdinalIgnoreCase);

                if (isSegmentUri)
                {
                    segmentsSeen++;
                    string filename = Path.GetFileName(line);

                    // Rewrite URL to our relay
                    string relayUrl = line.StartsWith("/") ? line : $"/ch/{uuid}/{filename}";
                    output.Append(relayUrl);
                    output.Append('\n');

                    // Inject EAS block immediately after the recorded injection point
                    if (!injected && segmentsSeen == _injectionAfterSegment)
                    {
                        injected = true;
                        AppendEasBlock(output, uuid);
                    }
                }
                else
                {
                    output.Append(line);
                    output.Append('\n');
                }
            }

            // Fallback: injection point was beyond all segments (or none set) — append before ENDLIST
            if (!injected && _easSegments.Count > 0)
            {
                string result = output.ToString();
                int endListPos = result.IndexOf("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase);
                if (endListPos >= 0)
                {
                    var sb2 = new StringBuilder();
                    sb2.Append(result, 0, endListPos);
                    var easBlock = new StringBuilder();
                    AppendEasBlock(easBlock, uuid);
                    sb2.Append(easBlock);
                    sb2.Append(result, endListPos, result.Length - endListPos);
                    return sb2.ToString();
                }
                else
                {
                    var easBlock = new StringBuilder();
                    AppendEasBlock(easBlock, uuid);
                    output.Append(easBlock);
                }
            }

            return output.ToString();
        }

        private void AppendEasBlock(StringBuilder sb, string uuid)
        {
            sb.AppendLine("#EXT-X-DISCONTINUITY");
            int idx = 0;
            foreach (var (_, duration) in _easSegments)
            {
                sb.Append($"#EXTINF:{duration.ToString("F6", CultureInfo.InvariantCulture)},");
                sb.Append('\n');
                sb.Append($"/ch/{uuid}/eas_{idx:D6}.ts");
                sb.Append('\n');
                idx++;
            }
            sb.AppendLine("#EXT-X-DISCONTINUITY");
        }

        // ────────────────────────────────────────────────────────────────────
        // Segment delivery
        // ────────────────────────────────────────────────────────────────────

        private async Task ServeSegmentAsync(string uuid, string filename, HttpListenerContext ctx)
        {
            string filePath;

            if (filename.StartsWith("eas_", StringComparison.OrdinalIgnoreCase))
            {
                // EAS chunk from our temp directory
                if (string.IsNullOrEmpty(_easTempDir))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
                filePath = Path.Combine(_easTempDir, filename);
            }
            else
            {
                // Tunarr segment — read-only from its stream cache
                filePath = Path.Combine(_settings.TunarrStreamCachePath, $"stream_{uuid}", filename);
            }

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            try
            {
                byte[] data = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "video/mp2t";
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsRelay] Segment serve error ({filename}): {ex.Message}", Logger.LogLevel.Debug);
                try { ctx.Response.StatusCode = 503; ctx.Response.Close(); } catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert injection
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Segments the alert .ts into HLS chunks, then arms EAS injection for all clients.
        /// Falls back gracefully if FFmpeg fails or no stream cache is available.
        /// </summary>
        public void TriggerAlertSplice(string alertTsPath, double durationSeconds)
        {
            if (!IsRunning) return;

            if (!File.Exists(alertTsPath))
            {
                Logger.Log($"[HlsRelay] Alert .ts not found: {alertTsPath}", Logger.LogLevel.Warning);
                return;
            }

            _ = Task.Run(() => TriggerAlertSpliceAsync(alertTsPath, durationSeconds));
        }

        private async Task TriggerAlertSpliceAsync(string alertTsPath, double durationSeconds)
        {
            try
            {
                CleanupEasTempDir();

                string tempDir = Path.Combine(Path.GetTempPath(), $"eas_relay_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                int segDur = Math.Max(1, _settings.HlsSegmentDurationSeconds);
                string dummyPlaylist = Path.Combine(tempDir, "eas_playlist.m3u8");
                string segPattern = Path.Combine(tempDir, "eas_%06d.ts");

                // Software encode (libx264 copy-stream) — avoids NVENC contention with Tunarr
                string ffmpegArgs =
                    $"-y -i \"{alertTsPath}\" " +
                    $"-c copy " +
                    $"-f hls " +
                    $"-hls_time {segDur} " +
                    $"-hls_list_size 0 " +
                    $"-hls_flags independent_segments " +
                    $"-hls_segment_filename \"{segPattern}\" " +
                    $"\"{dummyPlaylist}\"";

                var psi = new ProcessStartInfo("ffmpeg", ffmpegArgs)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                Logger.Log("[HlsRelay] Segmenting alert .ts for HLS relay injection...", Logger.LogLevel.Info);

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Logger.Log("[HlsRelay] Failed to launch FFmpeg for alert segmentation.", Logger.LogLevel.Error);
                    return;
                }

                // Drain stderr to avoid deadlocks on large stderr output
                string stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await proc.WaitForExitAsync().ConfigureAwait(false);

                if (proc.ExitCode != 0)
                {
                    Logger.Log($"[HlsRelay] FFmpeg segmentation failed (exit {proc.ExitCode}): {stderr.TrimEnd()}", Logger.LogLevel.Error);
                    return;
                }

                var easSegments = ParseHlsSegmentList(dummyPlaylist, tempDir);
                if (easSegments.Count == 0)
                {
                    Logger.Log("[HlsRelay] No EAS segments produced — aborting alert injection.", Logger.LogLevel.Warning);
                    return;
                }

                Logger.Log($"[HlsRelay] Alert segmented into {easSegments.Count} chunk(s) ({segDur}s each).", Logger.LogLevel.Info);

                // Record injection point from the first alert-enabled channel's manifest
                int injectionPoint = GetCurrentSegmentCount();

                // Arm injection
                _easTempDir = tempDir;
                _easSegments = easSegments;
                _injectionAfterSegment = injectionPoint;
                // Grace period: alert duration + 30s so clients past the edge still see EAS
                _alertExpiry = DateTime.UtcNow.AddSeconds(durationSeconds + 30);
                _alertActive = true;

                Logger.Log($"[HlsRelay] ✓ Alert armed — injecting after segment #{injectionPoint} " +
                    $"for ~{durationSeconds:F0}s.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsRelay] TriggerAlertSplice error: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        /// <summary>
        /// Returns the current MPEG-TS segment count from the first available channel manifest.
        /// This becomes the injection-after index — EAS follows the last segment present at trigger time.
        /// </summary>
        private int GetCurrentSegmentCount()
        {
            foreach (var ch in _settings.Channels)
            {
                if (!ch.AlertInterruptEnabled) continue;

                string m3u8 = Path.Combine(_settings.TunarrStreamCachePath,
                    $"stream_{ch.TunarrChannelId}", "stream.m3u8");

                if (!File.Exists(m3u8)) continue;

                string? content = ReadFileSafe(m3u8);
                if (content == null) continue;

                int count = 0;
                foreach (var line in content.Split('\n'))
                {
                    string t = line.Trim();
                    if (t.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("#"))
                        count++;
                }

                if (count > 0)
                {
                    Logger.Log($"[HlsRelay] Injection point: after segment #{count} " +
                        $"(ch {ch.DisplayName}).", Logger.LogLevel.Debug);
                    return count;
                }
            }

            // No manifest available — inject at position 0 (start of playlist on next poll)
            return 0;
        }

        /// <summary>
        /// Parses an FFmpeg-generated HLS playlist to extract segment filenames + durations.
        /// </summary>
        private static List<(string FilePath, double Duration)> ParseHlsSegmentList(
            string playlistPath, string segDir)
        {
            var result = new List<(string, double)>();
            if (!File.Exists(playlistPath)) return result;

            string content = File.ReadAllText(playlistPath);
            double? pendingDuration = null;

            foreach (var rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();

                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    string durStr = line.Substring(8).TrimEnd(',').Trim();
                    if (double.TryParse(durStr, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double d))
                        pendingDuration = d;
                }
                else if (line.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) &&
                         !line.StartsWith("#"))
                {
                    string fullPath = Path.Combine(segDir, Path.GetFileName(line));
                    double dur = pendingDuration ?? 4.0;
                    pendingDuration = null;
                    if (File.Exists(fullPath))
                        result.Add((fullPath, dur));
                }
            }

            return result;
        }

        // ────────────────────────────────────────────────────────────────────
        // M3U playlist builder
        // ────────────────────────────────────────────────────────────────────

        public string BuildM3uPlaylist(string hostHeader)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");

            foreach (var ch in _settings.Channels)
            {
                string name = string.IsNullOrWhiteSpace(ch.DisplayName)
                    ? $"Channel {ch.ProxyChannelNumber}"
                    : ch.DisplayName;

                sb.Append($"#EXTINF:-1 tvg-id=\"{ch.TunarrChannelId}\" " +
                           $"tvg-name=\"{name}\" " +
                           $"tvg-chno=\"{ch.ProxyChannelNumber}\"," +
                           $"{name}");
                sb.Append('\n');

                sb.Append($"http://{hostHeader}/ch/{ch.TunarrChannelId}/stream.m3u8");
                sb.Append('\n');
            }

            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private static string? ReadFileSafe(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                string content = sr.ReadToEnd();
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch { return null; }
        }

        private static async Task SendTextAsync(
            HttpListenerResponse response, string text, string contentType)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            response.StatusCode = 200;
            response.ContentType = $"{contentType}; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["Access-Control-Allow-Origin"] = "*";
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.Close();
        }

        private void CleanupEasTempDir()
        {
            string dir = _easTempDir;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
            _easTempDir = "";
            _easSegments = new List<(string, double)>();
        }
    }
}
