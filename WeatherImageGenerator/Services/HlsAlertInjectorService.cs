using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    ///   3. Appends #EXT-X-DISCONTINUITY + segment entries to stream.m3u8
    ///   4. Cleans up alert segments after the alert duration + buffer drain grace period
    ///
    /// This is complementary to StreamPipeService — it covers clients that consume
    /// Tunarr's HLS endpoint directly (Plex, Jellyfin native) without going through
    /// the TCP byte pipe. Alert latency is HLS-buffer-dependent (typically 12-40s).
    /// </summary>
    public class HlsAlertInjectorService
    {
        private const string AlertSegmentPrefix = "eas_";
        private const string AlertPlaylistName = "eas_playlist.m3u8";
        private const int M3u8WriteRetries = 5;
        private const int M3u8RetryDelayMs = 300;
        private const int CleanupGraceSeconds = 90; // extra time after alert duration before deleting segments

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
            Logger.Log($"[HlsInjector] ✓ HLS alert injection ready — cache: {_settings.TunarrStreamCachePath}, {alertChannels} alert-enabled channel(s), segment duration: {_settings.HlsSegmentDurationSeconds}s", Logger.LogLevel.Info);
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

                // Step 2: For each alert-enabled channel, copy segments and modify m3u8
                var injectedPaths = new List<string>(); // track for cleanup

                foreach (var ch in channels)
                {
                    if (ct.IsCancellationRequested) break;

                    string channelCacheDir = Path.Combine(
                        _settings.TunarrStreamCachePath,
                        $"stream_{ch.TunarrChannelId}");

                    if (!Directory.Exists(channelCacheDir))
                    {
                        Logger.Log($"[HlsInjector] Channel {ch.ProxyChannelNumber} ({ch.DisplayName}): cache dir not found at {channelCacheDir} — channel may not be rendering. Skipping.", Logger.LogLevel.Warning);
                        continue;
                    }

                    string m3u8Path = Path.Combine(channelCacheDir, "stream.m3u8");
                    if (!File.Exists(m3u8Path))
                    {
                        Logger.Log($"[HlsInjector] Channel {ch.ProxyChannelNumber}: stream.m3u8 not found — channel may not be streaming. Skipping.", Logger.LogLevel.Warning);
                        continue;
                    }

                    // Copy alert segments into the channel's cache directory
                    var copiedSegments = new List<(string FileName, double Duration)>();
                    foreach (var (segFile, dur) in segments)
                    {
                        string destPath = Path.Combine(channelCacheDir, Path.GetFileName(segFile));
                        File.Copy(segFile, destPath, overwrite: true);
                        copiedSegments.Add((Path.GetFileName(segFile), dur));
                        injectedPaths.Add(destPath);
                    }

                    // Append to stream.m3u8
                    bool success = await AppendAlertToPlaylistAsync(m3u8Path, copiedSegments, ct);
                    if (success)
                    {
                        Logger.Log($"[HlsInjector] ✓ Channel {ch.ProxyChannelNumber} ({ch.DisplayName}): {copiedSegments.Count} alert segment(s) injected into HLS playlist", Logger.LogLevel.Info);
                    }
                    else
                    {
                        Logger.Log($"[HlsInjector] ✗ Channel {ch.ProxyChannelNumber}: failed to append to stream.m3u8 after {M3u8WriteRetries} retries", Logger.LogLevel.Error);
                    }
                }

                // Step 3: Schedule cleanup after alert + grace period
                int cleanupDelay = (int)(durationSeconds + CleanupGraceSeconds) * 1000;
                Logger.Log($"[HlsInjector] Alert segments will be cleaned up in {(durationSeconds + CleanupGraceSeconds):F0}s", Logger.LogLevel.Debug);

                try
                {
                    await Task.Delay(cleanupDelay, ct);
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

        // ────────────────────────────────────────────────────────────────────
        // M3U8 playlist manipulation
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Appends alert segments into an existing live HLS playlist.
        /// Waits for a quiet window (Tunarr's ffmpeg not actively writing) before appending.
        /// </summary>
        private static async Task<bool> AppendAlertToPlaylistAsync(
            string m3u8Path,
            List<(string FileName, double Duration)> segments,
            CancellationToken ct)
        {
            // Build the text block to inject
            var sb = new StringBuilder();
            sb.AppendLine("#EXT-X-DISCONTINUITY");

            foreach (var (fileName, duration) in segments)
            {
                sb.AppendLine($"#EXTINF:{duration.ToString("F6", CultureInfo.InvariantCulture)},");
                sb.AppendLine(fileName);
            }

            sb.AppendLine("#EXT-X-DISCONTINUITY");

            string injectionBlock = sb.ToString();

            // Retry loop with timing-aware writes
            for (int attempt = 0; attempt < M3u8WriteRetries; attempt++)
            {
                if (ct.IsCancellationRequested) return false;

                try
                {
                    // Wait for Tunarr's ffmpeg to finish its current write cycle.
                    // Tunarr appends to m3u8 once per segment (~4s). We detect a quiet
                    // window by checking if the file hasn't been modified in the last 500ms.
                    await WaitForQuietWindowAsync(m3u8Path, 500, 4000, ct);

                    // Open with FileShare.ReadWrite to coexist with Tunarr's ffmpeg
                    using var fs = new FileStream(
                        m3u8Path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite);

                    // Seek to end
                    fs.Seek(0, SeekOrigin.End);

                    byte[] data = Encoding.UTF8.GetBytes(injectionBlock);
                    await fs.WriteAsync(data, 0, data.Length, ct);
                    await fs.FlushAsync(ct);

                    return true;
                }
                catch (IOException ex) when (attempt < M3u8WriteRetries - 1)
                {
                    Logger.Log($"[HlsInjector] m3u8 write attempt {attempt + 1}/{M3u8WriteRetries} failed: {ex.Message} — retrying", Logger.LogLevel.Debug);
                    await Task.Delay(M3u8RetryDelayMs * (attempt + 1), ct);
                }
            }

            return false;
        }

        /// <summary>
        /// Waits until the given file has not been modified for at least <paramref name="quietMs"/>
        /// milliseconds, or until <paramref name="maxWaitMs"/> expires.
        /// </summary>
        private static async Task WaitForQuietWindowAsync(
            string filePath, int quietMs, int maxWaitMs, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(filePath);
                    double msSinceWrite = (DateTime.UtcNow - lastWrite).TotalMilliseconds;

                    if (msSinceWrite >= quietMs)
                        return; // quiet window found
                }
                catch { }

                await Task.Delay(100, ct);
            }

            // maxWait expired — proceed anyway (best effort)
        }
    }
}
