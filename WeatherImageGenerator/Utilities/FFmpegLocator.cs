#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.IO.Compression;
using System.Threading.Tasks;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Manages FFmpeg binary location and automatic download.
    /// Downloads FFmpeg binaries directly without using Xabe.FFmpeg's internal APIs to avoid memory corruption issues.
    /// </summary>
    public static class FFmpegLocator
    {
        private static bool _isInitialized = false;
        private static readonly object _initLock = new object();
        private static string? _ffmpegPath;
        private static FFmpegSource _currentSource = FFmpegSource.Bundled;
        private static string? _customPath;

        // FFmpeg download URLs (using BtbN builds which are reliable and up-to-date)
        private const string FFmpegDownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        private const string FFmpegDownloadUrlFallback = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";

        /// <summary>
        /// Gets the directory containing FFmpeg executables.
        /// </summary>
        public static string FFmpegDirectory => _ffmpegPath ?? GetDefaultFFmpegDirectory();

        /// <summary>
        /// Gets the full path to the ffmpeg executable.
        /// </summary>
        public static string FFmpegExecutable => Path.Combine(FFmpegDirectory, "ffmpeg.exe");

        /// <summary>
        /// Gets the full path to the ffprobe executable.
        /// </summary>
        public static string FFprobeExecutable => Path.Combine(FFmpegDirectory, "ffprobe.exe");

        /// <summary>
        /// Returns true if FFmpeg binaries are available.
        /// </summary>
        public static bool IsAvailable => _isInitialized && File.Exists(FFmpegExecutable);

        /// <summary>
        /// Gets the default directory for FFmpeg binaries (in the application's local data folder).
        /// </summary>
        private static string GetDefaultFFmpegDirectory()
        {
            // Store FFmpeg binaries in LocalApplicationData to persist across app updates
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, "WeatherImageGenerator", "FFmpeg");
        }

        /// <summary>
        /// Initialize FFmpeg by checking for existing binaries or downloading if needed.
        /// Call this once at application startup.
        /// </summary>
        /// <param name="progress">Optional progress callback (0-100 percentage)</param>
        /// <returns>True if FFmpeg is ready to use</returns>
        public static async Task<bool> InitializeAsync(IProgress<float>? progress = null)
        {
            if (_isInitialized)
                return true;

            lock (_initLock)
            {
                if (_isInitialized)
                    return true;
            }

            try
            {
                _ffmpegPath = GetDefaultFFmpegDirectory();

                // Ensure directory exists
                if (!Directory.Exists(_ffmpegPath))
                {
                    Directory.CreateDirectory(_ffmpegPath);
                }

                // Check if FFmpeg already exists
                if (File.Exists(FFmpegExecutable) && File.Exists(FFprobeExecutable))
                {
                    Logger.Log($"[FFmpeg] Found existing FFmpeg at: {_ffmpegPath}", ConsoleColor.Green);
                    _isInitialized = true;
                    progress?.Report(100f);
                    return true;
                }

                // Download FFmpeg binaries
                Logger.Log("[FFmpeg] Downloading FFmpeg binaries (this only happens once)...", ConsoleColor.Yellow);
                
                bool downloaded = await DownloadFFmpegAsync(_ffmpegPath, progress);

                // Verify download succeeded
                if (downloaded && File.Exists(FFmpegExecutable))
                {
                    Logger.Log($"[FFmpeg] Successfully downloaded FFmpeg to: {_ffmpegPath}", ConsoleColor.Green);
                    _isInitialized = true;
                    progress?.Report(100f);
                    return true;
                }
                else
                {
                    Logger.Log("[FFmpeg] ERROR: Download completed but FFmpeg executable not found!", ConsoleColor.Red);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FFmpeg] ERROR: Failed to initialize FFmpeg: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Downloads and extracts FFmpeg binaries.
        /// </summary>
        private static async Task<bool> DownloadFFmpegAsync(string destinationDir, IProgress<float>? progress)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid()}.zip");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"ffmpeg_extract_{Guid.NewGuid()}");

            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10);
                httpClient.DefaultRequestHeaders.Add("User-Agent", "WeatherImageGenerator/1.0");

                // Try primary URL, then fallback
                string[] urls = { FFmpegDownloadUrl, FFmpegDownloadUrlFallback };
                bool downloadSuccess = false;

                foreach (var url in urls)
                {
                    try
                    {
                        Logger.Log($"[FFmpeg] Downloading from: {url}", ConsoleColor.DarkGray);
                        
                        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var totalMb = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;

                        using var contentStream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                        var buffer = new byte[81920];
                        long downloadedBytes = 0;
                        int bytesRead;
                        DateTime lastLog = DateTime.MinValue;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percent = (float)downloadedBytes / totalBytes * 100f;
                                progress?.Report(percent * 0.8f); // 80% for download

                                // Log every 2 seconds
                                if ((DateTime.Now - lastLog).TotalSeconds >= 2)
                                {
                                    var mb = downloadedBytes / 1024.0 / 1024.0;
                                    Logger.Log($"[FFmpeg] Downloading: {mb:F1} / {totalMb:F1} MB ({percent:F0}%)", ConsoleColor.DarkGray);
                                    lastLog = DateTime.Now;
                                }
                            }
                        }

                        downloadSuccess = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[FFmpeg] Download failed from {url}: {ex.Message}", ConsoleColor.Yellow);
                        if (File.Exists(tempZipPath))
                            File.Delete(tempZipPath);
                    }
                }

                if (!downloadSuccess)
                {
                    Logger.Log("[FFmpeg] All download sources failed.", ConsoleColor.Red);
                    return false;
                }

                // Extract the zip
                Logger.Log("[FFmpeg] Extracting FFmpeg...", ConsoleColor.DarkGray);
                progress?.Report(85f);

                if (Directory.Exists(tempExtractDir))
                    Directory.Delete(tempExtractDir, true);
                
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir);
                progress?.Report(95f);

                // Find the bin folder in the extracted content (structure varies by source)
                string? binFolder = FindBinFolder(tempExtractDir);
                
                if (binFolder == null)
                {
                    Logger.Log("[FFmpeg] Could not find bin folder in extracted archive.", ConsoleColor.Red);
                    return false;
                }

                // Copy executables to destination
                foreach (var exeName in new[] { "ffmpeg.exe", "ffprobe.exe", "ffplay.exe" })
                {
                    var srcPath = Path.Combine(binFolder, exeName);
                    var dstPath = Path.Combine(destinationDir, exeName);
                    
                    if (File.Exists(srcPath))
                    {
                        if (File.Exists(dstPath))
                            File.Delete(dstPath);
                        File.Copy(srcPath, dstPath);
                        Logger.Log($"[FFmpeg] Installed: {exeName}", ConsoleColor.DarkGray);
                    }
                }

                progress?.Report(100f);
                return true;
            }
            finally
            {
                // Cleanup temp files
                try
                {
                    if (File.Exists(tempZipPath))
                        File.Delete(tempZipPath);
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        /// <summary>
        /// Finds the bin folder containing ffmpeg.exe in the extracted directory.
        /// </summary>
        private static string? FindBinFolder(string extractDir)
        {
            // Check if ffmpeg.exe is directly in the extract dir
            if (File.Exists(Path.Combine(extractDir, "ffmpeg.exe")))
                return extractDir;

            // Search for bin folder
            foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, "ffmpeg.exe")))
                    return dir;
            }

            // Check subdirectories named "bin"
            foreach (var dir in Directory.GetDirectories(extractDir, "bin", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, "ffmpeg.exe")))
                    return dir;
            }

            return null;
        }

        /// <summary>
        /// Synchronous initialization (blocks until complete).
        /// Prefer InitializeAsync when possible.
        /// </summary>
        public static bool Initialize(IProgress<float>? progress = null)
        {
            return InitializeAsync(progress).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Try to find FFmpeg in common locations or PATH.
        /// This is a fallback if bundled FFmpeg is not available.
        /// </summary>
        /// <returns>Path to ffmpeg executable, or null if not found</returns>
        public static string? TryFindSystemFFmpeg()
        {
            // Common installation locations on Windows
            string[] commonPaths = new[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffmpeg.exe"),
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    Logger.Log($"[FFmpeg] Found system FFmpeg at: {path}", ConsoleColor.DarkGray);
                    return path;
                }
            }

            // Try to find in PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    var ffmpegPath = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(ffmpegPath))
                    {
                        Logger.Log($"[FFmpeg] Found FFmpeg in PATH: {ffmpegPath}", ConsoleColor.DarkGray);
                        return ffmpegPath;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Configures the FFmpeg source from application settings.
        /// Call this at application startup after loading config.
        /// </summary>
        public static void ConfigureFromSettings()
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                var ffmpegSettings = cfg.FFmpeg;
                
                if (ffmpegSettings != null)
                {
                    _currentSource = ffmpegSettings.Source?.ToLowerInvariant() switch
                    {
                        "bundled" => FFmpegSource.Bundled,
                        "systempath" => FFmpegSource.SystemPath,
                        "custom" => FFmpegSource.Custom,
                        _ => FFmpegSource.Bundled
                    };
                    _customPath = ffmpegSettings.CustomPath;
                    
                    Logger.Log($"[FFmpeg] Configured source: {_currentSource}" + 
                        (_currentSource == FFmpegSource.Custom ? $" ({_customPath})" : ""), ConsoleColor.DarkGray);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FFmpeg] Failed to load settings, using defaults: {ex.Message}", ConsoleColor.Yellow);
                _currentSource = FFmpegSource.Bundled;
            }
        }

        /// <summary>
        /// Sets the FFmpeg source programmatically.
        /// </summary>
        public static void SetSource(FFmpegSource source, string? customPath = null)
        {
            _currentSource = source;
            _customPath = customPath;
            _isInitialized = false; // Force re-initialization
            Logger.Log($"[FFmpeg] Source changed to: {source}" + 
                (source == FFmpegSource.Custom ? $" ({customPath})" : ""), ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Gets the current FFmpeg source setting.
        /// </summary>
        public static FFmpegSource CurrentSource => _currentSource;

        /// <summary>
        /// Gets the custom FFmpeg path if set.
        /// </summary>
        public static string? CustomPath => _customPath;

        /// <summary>
        /// Gets the path to use for FFmpeg execution based on the configured source.
        /// </summary>
        public static string GetFFmpegPath()
        {
            switch (_currentSource)
            {
                case FFmpegSource.SystemPath:
                    // Only look in system PATH
                    var systemPath = TryFindSystemFFmpeg();
                    if (!string.IsNullOrEmpty(systemPath))
                    {
                        Logger.Log($"[FFmpeg] Using system PATH: {systemPath}", ConsoleColor.DarkGray);
                        return systemPath;
                    }
                    Logger.Log("[FFmpeg] WARNING: System PATH selected but FFmpeg not found in PATH!", ConsoleColor.Yellow);
                    return "ffmpeg"; // Return bare command, let the system try to find it
                    
                case FFmpegSource.Custom:
                    // Use custom user-specified path
                    if (!string.IsNullOrEmpty(_customPath))
                    {
                        var customExe = Path.Combine(_customPath, "ffmpeg.exe");
                        if (File.Exists(customExe))
                        {
                            Logger.Log($"[FFmpeg] Using custom path: {customExe}", ConsoleColor.DarkGray);
                            return customExe;
                        }
                        // Maybe they specified the exe directly?
                        if (File.Exists(_customPath) && _customPath.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Log($"[FFmpeg] Using custom executable: {_customPath}", ConsoleColor.DarkGray);
                            return _customPath;
                        }
                        Logger.Log($"[FFmpeg] WARNING: Custom path specified but ffmpeg.exe not found at: {_customPath}", ConsoleColor.Yellow);
                    }
                    else
                    {
                        Logger.Log("[FFmpeg] WARNING: Custom source selected but no path specified!", ConsoleColor.Yellow);
                    }
                    // Fall through to bundled as fallback
                    goto case FFmpegSource.Bundled;
                    
                case FFmpegSource.Bundled:
                default:
                    // First check if bundled FFmpeg exists
                    if (File.Exists(FFmpegExecutable))
                    {
                        _isInitialized = true;
                        return FFmpegExecutable;
                    }

                    // Try system FFmpeg as fallback
                    var fallbackPath = TryFindSystemFFmpeg();
                    if (!string.IsNullOrEmpty(fallbackPath))
                        return fallbackPath;

                    // Return the expected bundled path (caller should check if it exists)
                    return FFmpegExecutable;
            }
        }

        /// <summary>
        /// Validates the current FFmpeg configuration.
        /// </summary>
        /// <returns>True if FFmpeg is available with current settings, false otherwise.</returns>
        public static bool ValidateConfiguration(out string message)
        {
            var path = GetFFmpegPath();
            
            if (_currentSource == FFmpegSource.SystemPath)
            {
                // For system path, we need to check if it's actually in PATH
                var systemPath = TryFindSystemFFmpeg();
                if (string.IsNullOrEmpty(systemPath))
                {
                    message = "FFmpeg not found in system PATH. Please install FFmpeg and add it to your PATH.";
                    return false;
                }
                message = $"Using FFmpeg from system PATH: {systemPath}";
                return true;
            }
            else if (_currentSource == FFmpegSource.Custom)
            {
                if (string.IsNullOrEmpty(_customPath))
                {
                    message = "Custom path is not specified.";
                    return false;
                }
                
                var customExe = Path.Combine(_customPath, "ffmpeg.exe");
                if (!File.Exists(customExe) && !File.Exists(_customPath))
                {
                    message = $"FFmpeg not found at custom path: {_customPath}";
                    return false;
                }
                message = $"Using FFmpeg from custom path: {_customPath}";
                return true;
            }
            else // Bundled
            {
                if (File.Exists(FFmpegExecutable))
                {
                    message = $"Using bundled FFmpeg: {FFmpegExecutable}";
                    return true;
                }
                message = "Bundled FFmpeg not yet downloaded. It will be downloaded automatically when needed.";
                return true; // Return true because it will auto-download
            }
        }

        /// <summary>
        /// Clears the downloaded FFmpeg binaries.
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                var ffmpegDir = GetDefaultFFmpegDirectory();
                if (Directory.Exists(ffmpegDir))
                {
                    Directory.Delete(ffmpegDir, true);
                    Logger.Log("[FFmpeg] Cleared FFmpeg cache.", ConsoleColor.Yellow);
                }
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[FFmpeg] Failed to clear cache: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}
