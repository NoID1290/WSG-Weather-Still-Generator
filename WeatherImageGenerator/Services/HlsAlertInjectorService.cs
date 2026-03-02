using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Injects EAS alert .ts segments directly into Tunarr's HLS stream cache on disk.
    /// When an alert fires, this service:
    ///   1. Segments the alert .ts into HLS-sized chunks using ffmpeg
    ///   2. Copies the segments into each alert-enabled channel's stream cache directory
    ///   3. Continuously re-injects #EXT-X-DISCONTINUITY + segment entries into stream.m3u8
    ///      every time Tunarr's ffmpeg rewrites the playlist (every ~4s)
    ///   4. Cleans up alert segments after the alert duration + buffer drain grace period
    ///
    /// Tunarr's ffmpeg completely rewrites stream.m3u8 on every segment boundary, so a
    /// single-shot append is erased within seconds. This service uses a FileSystemWatcher
    /// plus polling fallback to detect each rewrite and immediately re-append alert segments
    /// for the full alert duration.
    ///
    /// This is complementary to StreamPipeService — it covers clients that consume
    /// Tunarr's HLS endpoint directly (Plex, Jellyfin native) without going through
    /// the TCP byte pipe. Alert latency is HLS-buffer-dependent (typically 12-40s).
    /// </summary>
    public class HlsAlertInjectorService
    {
        private const string AlertSegmentPrefix = "eas_";
        private const string AlertPlaylistName = "eas_playlist.m3u8";
        private const int CleanupGraceSeconds = 90;  // extra time after alert duration before deleting segments
        private const int PollIntervalMs = 800;       // fallback polling interval for re-injection (FSW may miss events on network drives)
        private const int ReInjectRetryDelayMs = 150;  // delay between retries on IOException during re-inject
        private const int WarmUpTimeoutSeconds = 8;        // max time to wait for Tunarr to create a channel's HLS cache dir
        private const int DefaultPlaylistWindow = 3;       // default HLS sliding window size if we can't determine from playlist

        private readonly StreamProxySettings _settings;
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }

        public HlsAlertInjectorService(StreamProxySettings settings)
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
                Logger.Log("[HlsInjector] Cannot start — TunarrStreamCachePath is not configured.", Logger.LogLevel.Warning);
                return;
            }

            if (!Directory.Exists(_settings.TunarrStreamCachePath))
            {
                Logger.Log($"[HlsInjector] Stream cache path does not exist: {_settings.TunarrStreamCachePath}", Logger.LogLevel.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;

            int alertChannels = _settings.Channels.Count(c => c.AlertInterruptEnabled);
            Logger.Log($"[HlsInjector] ✓ HLS alert injection ready — cache: {_settings.TunarrStreamCachePath}, " +
                $"{alertChannels} alert-enabled channel(s) (may increase after auto-detect), " +
                $"segment duration: {_settings.HlsSegmentDurationSeconds}s", Logger.LogLevel.Info);

            // ── Compatibility warning ──
            // On-disk HLS injection modifies stream.m3u8 files in Tunarr's cache directory.
            // Tunarr v0.20.x may serve playlists via its internal HTTP handler rather than
            // reading the raw files from disk, which makes on-disk modifications invisible
            // to HLS clients. If the StreamPipe proxy is also enabled, its built-in HLS
            // proxy with HTTP-level injection is the recommended approach.
            if (_settings.Enabled)
            {
                Logger.Log("[HlsInjector] ⚠ NOTE: StreamPipe is also enabled. StreamPipe includes built-in HTTP-level HLS " +
                    "alert injection which is more reliable than on-disk cache manipulation. On-disk HLS injection may " +
                    "be redundant or ineffective with Tunarr v0.20+. Consider disabling HlsInjection if StreamPipe " +
                    "covers all your clients.", Logger.LogLevel.Warning);
            }

            // Validate that Tunarr actually uses the stream cache path by checking for
            // stream_* subdirectories
            bool foundAnyStreamDir = false;
            try
            {
                var streamDirs = Directory.GetDirectories(_settings.TunarrStreamCachePath, "stream_*");
                foundAnyStreamDir = streamDirs.Length > 0;
                if (!foundAnyStreamDir)
                {
                    Logger.Log($"[HlsInjector] ⚠ No stream_* directories found in {_settings.TunarrStreamCachePath}. " +
                        "On-disk injection will have no effect until Tunarr starts streaming channels.", Logger.LogLevel.Warning);
                }
                else
                {
                    Logger.Log($"[HlsInjector] Found {streamDirs.Length} stream_* directory(ies) in cache path.", Logger.LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsInjector] Could not enumerate stream cache: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        public Task StopAsync()
        {
            if (!IsRunning) return Task.CompletedTask;

            try
            {
                _cts?.Cancel();
                IsRunning = false;
                Logger.Log("[HlsInjector] HLS alert injection stopped.", Logger.LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsInjector] Error stopping: {ex.Message}", Logger.LogLevel.Warning);
            }

            return Task.CompletedTask;
        }

        // ────────────────────────────────────────────────────────────────────
        // Alert trigger
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers HLS alert injection for all alert-enabled channels.
        /// Runs asynchronously — returns immediately after launching the injection task.
        /// </summary>
        public void TriggerAlertSplice(string alertTsPath, double durationSeconds)
        {
            if (!IsRunning)
            {
                Logger.Log("[HlsInjector] Cannot trigger — service is not running.", Logger.LogLevel.Warning);
                return;
            }

            if (string.IsNullOrEmpty(alertTsPath) || !File.Exists(alertTsPath))
            {
                Logger.Log($"[HlsInjector] Cannot trigger — alert .ts file missing: '{alertTsPath}'", Logger.LogLevel.Warning);
                return;
            }

            var alertChannels = _settings.Channels.Where(c => c.AlertInterruptEnabled).ToList();
            if (alertChannels.Count == 0)
            {
                Logger.Log("[HlsInjector] No alert-enabled channels configured.", Logger.LogLevel.Warning);
                return;
            }

            Logger.Log($"[HlsInjector] 🚨 EAS ALERT — injecting into {alertChannels.Count} channel(s) HLS cache", Logger.LogLevel.Info);

            var ct = _cts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    await InjectAlertAsync(alertTsPath, durationSeconds, alertChannels, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Log($"[HlsInjector] Alert injection failed: {ex.Message}", Logger.LogLevel.Error);
                }
            }, ct);
        }

        // ────────────────────────────────────────────────────────────────────
        // Core injection pipeline
        // ────────────────────────────────────────────────────────────────────

        private async Task InjectAlertAsync(
            string alertTsPath,
            double durationSeconds,
            List<ProxyChannelConfig> channels,
            CancellationToken ct)
        {
            // Step 1: Segment the alert .ts into HLS chunks
            string tempDir = Path.Combine(Path.GetTempPath(), $"wsg_hls_alert_{DateTime.UtcNow:yyyyMMddHHmmss}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var segments = await SegmentAlertTsAsync(alertTsPath, tempDir, _settings.HlsSegmentDurationSeconds, ct);
                if (segments.Count == 0)
                {
                    Logger.Log("[HlsInjector] ffmpeg produced no HLS segments — aborting injection.", Logger.LogLevel.Error);
                    return;
                }

                Logger.Log($"[HlsInjector] Alert segmented into {segments.Count} HLS chunk(s)", Logger.LogLevel.Debug);

                // Step 1.5: Categorize channels by cache readiness & warm up inactive ones
                var channelsReady = new List<(ProxyChannelConfig Ch, string CacheDir)>();
                var channelsNeedingWarmup = new List<(ProxyChannelConfig Ch, string CacheDir)>();

                foreach (var ch in channels)
                {
                    if (ct.IsCancellationRequested) break;

                    string channelCacheDir = Path.Combine(
                        _settings.TunarrStreamCachePath,
                        $"stream_{ch.TunarrChannelId}");

                    if (Directory.Exists(channelCacheDir) && File.Exists(Path.Combine(channelCacheDir, "stream.m3u8")))
                    {
                        channelsReady.Add((ch, channelCacheDir));
                    }
                    else
                    {
                        channelsNeedingWarmup.Add((ch, channelCacheDir));
                    }
                }

                // Warm up channels that don't have cache directories (in parallel)
                if (channelsNeedingWarmup.Count > 0 && !ct.IsCancellationRequested)
                {
                    Logger.Log($"[HlsInjector] {channelsNeedingWarmup.Count} channel(s) have no HLS cache — requesting streams from Tunarr to warm up...", Logger.LogLevel.Info);

                    var warmUpTasks = channelsNeedingWarmup.Select(async x =>
                    {
                        bool ok = await TryWarmUpChannelAsync(x.Ch, x.CacheDir, ct);
                        return (x.Ch, x.CacheDir, ok);
                    }).ToList();

                    try
                    {
                        var results = await Task.WhenAll(warmUpTasks);
                        int warmed = 0;
                        foreach (var (ch, cacheDir, ok) in results)
                        {
                            if (ok)
                            {
                                channelsReady.Add((ch, cacheDir));
                                warmed++;
                            }
                            else
                            {
                                Logger.Log($"[HlsInjector] Channel {ch.ProxyChannelNumber} ({ch.DisplayName}): " +
                                    $"cache dir not found at {cacheDir} — channel may not be available in Tunarr. Skipping.",
                                    Logger.LogLevel.Warning);
                            }
                        }
                        if (warmed > 0)
                            Logger.Log($"[HlsInjector] ✓ Warm-up created cache for {warmed} channel(s)", Logger.LogLevel.Info);
                    }
                    catch (OperationCanceledException) { }
                }

                if (channelsReady.Count == 0)
                {
                    Logger.Log("[HlsInjector] No channels with active HLS cache — injection aborted.", Logger.LogLevel.Warning);
                    return;
                }

                // Step 2: For each ready channel, copy segments and start re-injection loop
                var injectedPaths = new List<string>();  // track for cleanup
                var loopTasks = new List<Task>();         // one re-injection loop per channel

                // Combined CTS: expires after alert duration OR when service stops
                using var alertCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                alertCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));

                foreach (var (ch, channelCacheDir) in channelsReady)
                {
                    if (ct.IsCancellationRequested) break;

                    // Copy alert segments into the channel's cache directory
                    var copiedSegments = new List<(string FileName, double Duration)>();
                    foreach (var (segFile, dur) in segments)
                    {
                        string destPath = Path.Combine(channelCacheDir, Path.GetFileName(segFile));
                        File.Copy(segFile, destPath, overwrite: true);
                        copiedSegments.Add((Path.GetFileName(segFile), dur));
                        injectedPaths.Add(destPath);
                    }

                    Logger.Log($"[HlsInjector] ✓ Channel {ch.ProxyChannelNumber} ({ch.DisplayName}): {copiedSegments.Count} alert segment(s) copied — starting re-injection loop for {durationSeconds:F0}s", Logger.LogLevel.Info);

                    // Launch persistent re-injection loop for this channel.
                    // Must use Task.Run to ensure each loop runs on its own thread pool thread —
                    // RunReInjectionLoopAsync uses SemaphoreSlim.WaitAsync with a
                    // FileSystemWatcher which would otherwise block the foreach
                    // and cause channels to be processed sequentially.
                    int chNum = ch.ProxyChannelNumber;
                    string chName = ch.DisplayName;
                    var segmentsCopy = segments.ToList();  // capture for closure safety
                    loopTasks.Add(Task.Run(() => RunReInjectionLoopAsync(
                        Path.Combine(channelCacheDir, "stream.m3u8"),
                        segmentsCopy,
                        chNum,
                        chName,
                        alertCts.Token), ct));
                }

                // Wait for all re-injection loops to finish (they run for the alert duration)
                if (loopTasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(loopTasks);
                    }
                    catch (OperationCanceledException) { }

                    Logger.Log($"[HlsInjector] Alert re-injection loops ended for {loopTasks.Count} channel(s)", Logger.LogLevel.Debug);
                }

                // Step 3: Schedule cleanup after grace period (alert duration already elapsed)
                Logger.Log($"[HlsInjector] Alert segments will be cleaned up in {CleanupGraceSeconds}s", Logger.LogLevel.Debug);

                try
                {
                    await Task.Delay(CleanupGraceSeconds * 1000, ct);
                }
                catch (OperationCanceledException) { }

                // Cleanup injected segment files
                int cleaned = 0;
                foreach (string path in injectedPaths)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            cleaned++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[HlsInjector] Cleanup failed for {path}: {ex.Message}", Logger.LogLevel.Debug);
                    }
                }

                if (cleaned > 0)
                    Logger.Log($"[HlsInjector] Cleaned up {cleaned} alert segment file(s)", Logger.LogLevel.Debug);
            }
            finally
            {
                // Always clean up temp directory
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Persistent re-injection loop
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Continuously re-injects alert segments into stream.m3u8 until cancelled.
        /// Uses a rolling window that advances with the HLS media sequence to maintain
        /// proper segment numbering continuity for HLS clients.
        ///
        /// The key insight: Tunarr's ffmpeg advances #EXT-X-MEDIA-SEQUENCE by 1 each cycle (~4s).
        /// If we always appended ALL alert segments, the same media-sequence position would map
        /// to different content on successive playlists (e.g., position N = eas_000000 in one
        /// playlist, then position N = streamN in the next), violating the HLS spec and causing
        /// clients to loop or skip. Instead, we append a sliding window of alert segments that
        /// advances by 1 each cycle, so each position maps to the same segment across playlists.
        /// </summary>
        private async Task RunReInjectionLoopAsync(
            string m3u8Path,
            List<(string FilePath, double Duration)> alertSegments,
            int channelNumber,
            string displayName,
            CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(m3u8Path)!;
            string fileName = Path.GetFileName(m3u8Path);
            int injectCount = 0;
            DateTime lastInjectedWriteTime = DateTime.MinValue;
            int? baseMediaSequence = null;  // recorded on first injection for sliding window calculation

            // Signal: set when FSW detects a change to stream.m3u8
            using var changeDetected = new SemaphoreSlim(0, 1);

            FileSystemWatcher? watcher = null;
            try
            {
                // Try to set up FileSystemWatcher (may fail on network drives)
                try
                {
                    watcher = new FileSystemWatcher(dir, fileName)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    watcher.Changed += (_, _) => { try { if (changeDetected.CurrentCount == 0) changeDetected.Release(); } catch { } };
                }
                catch (Exception ex)
                {
                    Logger.Log($"[HlsInjector] Ch {channelNumber}: FileSystemWatcher unavailable ({ex.Message}) — using polling only", Logger.LogLevel.Debug);
                    watcher = null;
                }

                // Perform immediate first injection
                {
                    string? content = ReadPlaylistSafe(m3u8Path);
                    if (content != null && !content.Contains(AlertSegmentPrefix))
                    {
                        int mediaSeq = ParseMediaSequence(content);
                        baseMediaSequence = mediaSeq;
                        int tunarrSegCount = CountContentSegments(content);
                        string block = BuildSlidingWindowBlock(alertSegments, 0, Math.Max(tunarrSegCount, DefaultPlaylistWindow));
                        if (WriteInjectedPlaylist(m3u8Path, content, block, ref lastInjectedWriteTime))
                            injectCount++;
                    }
                }

                // Loop until alert duration expires (ct is cancelled)
                while (!ct.IsCancellationRequested)
                {
                    // Wait for either: FSW event, poll timer, or cancellation
                    try
                    {
                        if (watcher != null)
                        {
                            // Wait up to PollIntervalMs for a FSW signal, then fall through to poll check anyway
                            await changeDetected.WaitAsync(PollIntervalMs, ct);
                        }
                        else
                        {
                            await Task.Delay(PollIntervalMs, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // Check if Tunarr has rewritten the file since our last injection
                    try
                    {
                        DateTime currentWriteTime = File.GetLastWriteTimeUtc(m3u8Path);

                        // Only re-inject if the file was modified since our last successful injection.
                        // This avoids needless rewrites when the file hasn't changed.
                        if (currentWriteTime <= lastInjectedWriteTime)
                            continue;

                        string? content = ReadPlaylistSafe(m3u8Path);
                        if (content == null || content.Contains(AlertSegmentPrefix))
                            continue;

                        int mediaSeq = ParseMediaSequence(content);
                        if (baseMediaSequence == null)
                            baseMediaSequence = mediaSeq;

                        int tunarrSegCount = CountContentSegments(content);
                        int windowSize = Math.Max(tunarrSegCount, DefaultPlaylistWindow);

                        // Calculate which alert segment to start from based on how far the
                        // media sequence has advanced since the first injection.
                        int alertStartIdx = 0;
                        if (baseMediaSequence.HasValue && mediaSeq > baseMediaSequence.Value)
                            alertStartIdx = mediaSeq - baseMediaSequence.Value;

                        // If all alert segments have already been presented via the sliding window,
                        // stop injecting — clients have already downloaded them, and Tunarr's clean
                        // playlist will naturally resume normal content.
                        if (alertStartIdx >= alertSegments.Count)
                            continue;

                        string block = BuildSlidingWindowBlock(alertSegments, alertStartIdx, windowSize);
                        if (WriteInjectedPlaylist(m3u8Path, content, block, ref lastInjectedWriteTime))
                            injectCount++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // File may be momentarily locked by Tunarr's ffmpeg — retry next cycle
                        Logger.Log($"[HlsInjector] Ch {channelNumber}: re-inject check failed: {ex.Message}", Logger.LogLevel.Debug);
                    }
                }
            }
            finally
            {
                watcher?.Dispose();
            }

            Logger.Log($"[HlsInjector] Ch {channelNumber} ({displayName}): re-injection loop ended — {injectCount} total injection(s)", Logger.LogLevel.Info);
        }

        // ────────────────────────────────────────────────────────────────────
        // M3U8 playlist manipulation
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the HLS injection block from segmented alert chunks.
        /// </summary>
        private static string BuildInjectionBlock(List<(string FilePath, double Duration)> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXT-X-DISCONTINUITY");

            foreach (var (filePath, duration) in segments)
            {
                sb.AppendLine($"#EXTINF:{duration.ToString("F6", CultureInfo.InvariantCulture)},");
                sb.AppendLine(Path.GetFileName(filePath));
            }

            sb.AppendLine("#EXT-X-DISCONTINUITY");
            return sb.ToString();
        }

        /// <summary>
        /// Reads playlist content from disk safely, handling concurrent access from Tunarr's ffmpeg.
        /// Returns null if the file can't be read or is empty.
        /// </summary>
        private static string? ReadPlaylistSafe(string m3u8Path)
        {
            try
            {
                using var fs = new FileStream(m3u8Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string content = reader.ReadToEnd();
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses the #EXT-X-MEDIA-SEQUENCE value from HLS playlist content.
        /// Returns 0 if the tag is not present or can't be parsed.
        /// </summary>
        private static int ParseMediaSequence(string content)
        {
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(22).Trim(), out int seq))
                        return seq;
                }
            }
            return 0;
        }

        /// <summary>
        /// Counts the number of #EXTINF segment entries (non-alert) in playlist content.
        /// </summary>
        private static int CountContentSegments(string content)
        {
            int count = 0;
            foreach (var line in content.Split('\n'))
            {
                if (line.TrimStart().StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Builds an HLS injection block using a sliding window over the alert segments.
        /// This maintains proper media sequence continuity — each injection cycle should
        /// advance startIndex by 1 (matching Tunarr's media sequence increment per segment
        /// boundary), so each playlist position maps to the same content across rewrites.
        /// </summary>
        private static string BuildSlidingWindowBlock(
            List<(string FilePath, double Duration)> allSegments,
            int startIndex,
            int windowSize)
        {
            int endIndex = Math.Min(startIndex + windowSize, allSegments.Count);
            if (endIndex <= startIndex || startIndex < 0)
                return BuildInjectionBlock(allSegments); // fallback: all segments

            var window = allSegments.GetRange(startIndex, endIndex - startIndex);
            return BuildInjectionBlock(window);
        }

        /// <summary>
        /// Writes the injection block into previously-read playlist content and saves to disk.
        /// Handles #EXT-X-ENDLIST placement and newline hygiene.
        /// Uses truncate-and-rewrite for atomicity.
        /// </summary>
        /// <returns>True if injection was performed, false if skipped or failed.</returns>
        private static bool WriteInjectedPlaylist(
            string m3u8Path,
            string content,
            string injectionBlock,
            ref DateTime lastInjectedWriteTime)
        {
            try
            {
                // Handle #EXT-X-ENDLIST: strip it so we can append after our injection
                bool hadEndList = false;
                string endListTag = "#EXT-X-ENDLIST";
                int endListIdx = content.LastIndexOf(endListTag, StringComparison.OrdinalIgnoreCase);
                if (endListIdx >= 0)
                {
                    hadEndList = true;
                    content = content.Substring(0, endListIdx).TrimEnd('\r', '\n');
                }

                // Ensure content ends with a newline to prevent corrupting the last segment line
                if (content.Length > 0 && content[content.Length - 1] != '\n')
                    content += "\n";

                // Build final playlist: original content + alert block + optional ENDLIST
                var sb = new StringBuilder(content.Length + injectionBlock.Length + 32);
                sb.Append(content);
                sb.Append(injectionBlock);

                if (hadEndList)
                {
                    sb.AppendLine(endListTag);
                }

                byte[] data = Encoding.UTF8.GetBytes(sb.ToString());

                // Truncate-and-rewrite (not seek-to-end) to avoid stale trailing bytes
                // when Tunarr writes a shorter playlist than our modified version
                using (var fs = new FileStream(
                    m3u8Path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    fs.SetLength(data.Length);
                    fs.Seek(0, SeekOrigin.Begin);
                    fs.Write(data, 0, data.Length);
                    fs.Flush();
                }

                // Record the write time so we can detect the next Tunarr rewrite
                lastInjectedWriteTime = File.GetLastWriteTimeUtc(m3u8Path);
                return true;
            }
            catch (IOException)
            {
                // File locked by Tunarr's ffmpeg mid-write — caller will retry next cycle
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[HlsInjector] Write failed for {Path.GetFileName(Path.GetDirectoryName(m3u8Path))}: {ex.Message}", Logger.LogLevel.Debug);
                return false;
            }
        }

        /// <summary>
        /// Attempts to warm up a channel by requesting its HLS stream from Tunarr,
        /// which triggers Tunarr to start ffmpeg and create the stream cache directory.
        /// Returns true if the cache directory and stream.m3u8 appear within the timeout.
        /// </summary>
        private async Task<bool> TryWarmUpChannelAsync(
            ProxyChannelConfig ch,
            string channelCacheDir,
            CancellationToken ct)
        {
            // Use TunarrBaseUrl (internal port) — that's where Tunarr listens
            string warmUpUrl = $"{_settings.TunarrBaseUrl.TrimEnd('/')}/stream/channels/{ch.TunarrChannelId}.m3u8";
            Logger.Log($"[HlsInjector] Ch {ch.ProxyChannelNumber} ({ch.DisplayName}): requesting stream to warm up cache...", Logger.LogLevel.Debug);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(WarmUpTimeoutSeconds) };
                // Request the HLS playlist — this triggers Tunarr to start transcoding
                using var response = await http.GetAsync(warmUpUrl, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Log($"[HlsInjector] Ch {ch.ProxyChannelNumber}: warm-up request returned HTTP {(int)response.StatusCode}", Logger.LogLevel.Debug);
                }

                // Wait for the cache directory + stream.m3u8 to appear
                for (int i = 0; i < WarmUpTimeoutSeconds; i++)
                {
                    await Task.Delay(1000, ct);
                    if (Directory.Exists(channelCacheDir))
                    {
                        string m3u8 = Path.Combine(channelCacheDir, "stream.m3u8");
                        if (File.Exists(m3u8))
                            return true;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Log($"[HlsInjector] Ch {ch.ProxyChannelNumber}: warm-up failed: {ex.Message}", Logger.LogLevel.Debug);
            }

            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // FFmpeg HLS segmentation
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Uses ffmpeg to split the monolithic alert .ts file into HLS-compatible segments.
        /// Returns a list of (segmentFilePath, durationSeconds) parsed from the generated playlist.
        /// </summary>
        private static async Task<List<(string FilePath, double Duration)>> SegmentAlertTsAsync(
            string tsPath,
            string outputDir,
            int segmentDuration,
            CancellationToken ct)
        {
            string playlistPath = Path.Combine(outputDir, AlertPlaylistName);
            string segmentPattern = Path.Combine(outputDir, $"{AlertSegmentPrefix}%06d.ts");

            string ffmpegPath = FFmpegLocator.GetFFmpegPath();
            if (!File.Exists(ffmpegPath))
                ffmpegPath = "ffmpeg";

            string args =
                $"-y -i \"{tsPath}\" -c copy " +
                $"-f hls -hls_time {segmentDuration} " +
                $"-hls_segment_filename \"{segmentPattern}\" " +
                $"-hls_flags independent_segments " +
                $"-hls_list_size 0 " +
                $"\"{playlistPath}\"";

            Logger.Log($"[HlsInjector] Segmenting alert: {Path.GetFileName(tsPath)} → {segmentDuration}s chunks", Logger.LogLevel.Debug);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Log("[HlsInjector] Failed to start ffmpeg for HLS segmentation.", Logger.LogLevel.Error);
                return new List<(string, double)>();
            }

            var stderrTask = process.StandardError.ReadToEndAsync();

            bool exited = await Task.Run(() => process.WaitForExit(120000), ct); // 2 min timeout
            if (!exited)
            {
                try { process.Kill(); } catch { }
                Logger.Log("[HlsInjector] ffmpeg segmentation timed out after 2 minutes.", Logger.LogLevel.Error);
                return new List<(string, double)>();
            }

            if (process.ExitCode != 0)
            {
                string stderr = await stderrTask;
                Logger.Log($"[HlsInjector] ffmpeg segmentation failed (exit {process.ExitCode}): {stderr.Substring(0, Math.Min(stderr.Length, 500))}", Logger.LogLevel.Error);
                return new List<(string, double)>();
            }

            // Parse the generated playlist to get segment filenames and durations
            return ParseHlsPlaylist(playlistPath, outputDir);
        }

        /// <summary>
        /// Parses a simple HLS playlist to extract segment filenames and durations.
        /// </summary>
        private static List<(string FilePath, double Duration)> ParseHlsPlaylist(string playlistPath, string baseDir)
        {
            var result = new List<(string, double)>();
            if (!File.Exists(playlistPath)) return result;

            string[] lines = File.ReadAllLines(playlistPath);
            double nextDuration = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    // #EXTINF:6.006000,
                    string durStr = line.Substring(8).TrimEnd(',');
                    double.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out nextDuration);
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line) && nextDuration > 0)
                {
                    string segPath = Path.Combine(baseDir, line);
                    if (File.Exists(segPath))
                    {
                        result.Add((segPath, nextDuration));
                    }
                    nextDuration = 0;
                }
            }

            return result;
        }
    }
}
