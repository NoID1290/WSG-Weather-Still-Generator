#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Service to check for and download application updates from GitHub releases
    /// </summary>
    public static class UpdateService
    {
        // Windows API for deferred file operations
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
        
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;
        private const int MOVEFILE_REPLACE_EXISTING = 1;
        private const string GitHubApiUrl = "https://api.github.com/repos/NoID1290/WSG-Weather-Still-Generator/releases/latest";
        private const string GitHubReleasesUrl = "https://github.com/NoID1290/WSG-Weather-Still-Generator/releases";
        private static readonly string UpdateStagingDirectory = Path.Combine(Path.GetTempPath(), "WSG_Update_Staging");
        private static readonly string UpdaterExecutable = "WSG.Updater.exe";
        
        private static readonly HttpClient _httpClient = new HttpClient();
        
        static UpdateService()
        {
            // GitHub API requires a User-Agent header
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-WeatherStillGenerator/1.0");
        }
        
        /// <summary>
        /// Gets the current application version from assembly attributes
        /// </summary>
        public static string GetCurrentVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString() ?? "0.0.0";
        }
        
        /// <summary>
        /// Represents information about an available update
        /// </summary>
        public class UpdateInfo
        {
            public string CurrentVersion { get; set; } = string.Empty;
            public string LatestVersion { get; set; } = string.Empty;
            public bool IsUpdateAvailable { get; set; }
            public string? DownloadUrl { get; set; }
            public string? ReleaseNotes { get; set; }
            public string? ReleaseName { get; set; }
            public DateTime? PublishedAt { get; set; }
            public string? Error { get; set; }
        }
        
        /// <summary>
        /// Checks GitHub for the latest release version
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            var result = new UpdateInfo
            {
                CurrentVersion = GetCurrentVersion()
            };
            
            try
            {
                Logger.Log("Checking for updates...", Logger.LogLevel.Info);
                
                var response = await _httpClient.GetStringAsync(GitHubApiUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                // Get tag_name (version), usually like "v1.7.4" or "1.7.4"
                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                result.LatestVersion = tagName.TrimStart('v', 'V');
                result.ReleaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                result.ReleaseNotes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
                
                if (root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var pubDate))
                {
                    result.PublishedAt = pubDate;
                }
                
                // Find the Windows zip asset
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var assetName) ? assetName.GetString() : "";
                        if (name != null && (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && 
                            (name.Contains("win", StringComparison.OrdinalIgnoreCase) || 
                             name.Contains("WSG", StringComparison.OrdinalIgnoreCase))))
                        {
                            result.DownloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) 
                                ? urlProp.GetString() : null;
                            break;
                        }
                    }
                }
                
                // If no specific Windows asset found, try first zip asset
                if (string.IsNullOrEmpty(result.DownloadUrl) && root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var assetName) ? assetName.GetString() : "";
                        if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            result.DownloadUrl = asset.TryGetProperty("browser_download_url", out var urlProp) 
                                ? urlProp.GetString() : null;
                            break;
                        }
                    }
                }
                
                // Fallback to zipball if no assets
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    result.DownloadUrl = root.TryGetProperty("zipball_url", out var zipProp) 
                        ? zipProp.GetString() : null;
                }
                
                // Compare versions
                result.IsUpdateAvailable = CompareVersions(result.CurrentVersion, result.LatestVersion) < 0;
                
                Logger.Log($"Update check complete. Current: {result.CurrentVersion}, Latest: {result.LatestVersion}, Update available: {result.IsUpdateAvailable}", Logger.LogLevel.Info);
            }
            catch (HttpRequestException ex)
            {
                result.Error = $"Network error: {ex.Message}";
                Logger.Log($"Update check failed: {result.Error}", Logger.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                result.Error = $"Error checking for updates: {ex.Message}";
                Logger.Log($"Update check failed: {result.Error}", Logger.LogLevel.Error);
            }
            
            return result;
        }
        
        /// <summary>
        /// Downloads and extracts the update, then replaces the current files
        /// </summary>
        public static async Task<(bool Success, string Message)> DownloadAndInstallUpdateAsync(
            string downloadUrl, 
            IProgress<(int Percent, string Status)>? progress = null)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"WSG_Update_{Guid.NewGuid():N}");
            var zipPath = Path.Combine(tempDir, "update.zip");
            var extractPath = Path.Combine(tempDir, "extracted");
            var appDir = AppContext.BaseDirectory;
            var backupDir = Path.Combine(tempDir, "backup");
            
            try
            {
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(extractPath);
                Directory.CreateDirectory(backupDir);
                
                // Step 1: Download
                progress?.Report((5, "Downloading update..."));
                Logger.Log($"Downloading update from: {downloadUrl}", Logger.LogLevel.Info);
                
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    
                    await using var contentStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                    
                    var buffer = new byte[8192];
                    var totalRead = 0L;
                    int bytesRead;
                    
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        
                        if (totalBytes > 0)
                        {
                            var percent = (int)((totalRead * 50) / totalBytes); // 0-50%
                            progress?.Report((5 + percent, $"Downloading... {totalRead / 1024:N0} KB"));
                        }
                    }
                }
                
                Logger.Log("Download complete.", Logger.LogLevel.Info);
                
                // Step 2: Extract
                progress?.Report((55, "Extracting update..."));
                Logger.Log("Extracting update...", Logger.LogLevel.Info);
                
                ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
                
                // Find the actual content directory (might be nested)
                var sourceDir = extractPath;
                var subdirs = Directory.GetDirectories(extractPath);
                if (subdirs.Length == 1 && !File.Exists(Path.Combine(extractPath, "WSG.exe")))
                {
                    sourceDir = subdirs[0];
                    // Check one more level deep
                    var nestedSubdirs = Directory.GetDirectories(sourceDir);
                    if (nestedSubdirs.Length == 1 && !File.Exists(Path.Combine(sourceDir, "WSG.exe")))
                    {
                        sourceDir = nestedSubdirs[0];
                    }
                }
                
                // Step 3: Backup current files
                progress?.Report((65, "Backing up current files..."));
                Logger.Log("Backing up current files...", Logger.LogLevel.Info);
                
                var filesToReplace = new[] { "*.exe", "*.dll", "*.json", "*.deps.json", "*.runtimeconfig.json" };
                foreach (var pattern in filesToReplace)
                {
                    foreach (var file in Directory.GetFiles(appDir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var fileName = Path.GetFileName(file);
                        // Skip config file to preserve user settings
                        if (fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
                            continue;
                        
                        try
                        {
                            File.Copy(file, Path.Combine(backupDir, fileName), overwrite: true);
                        }
                        catch { /* Best effort backup */ }
                    }
                }
                
                // Step 4: Copy new files to staging directory
                progress?.Report((75, "Staging files..."));
                Logger.Log("Staging files for installation...", Logger.LogLevel.Info);
                Logger.Log($"Source directory: {sourceDir}", Logger.LogLevel.Debug);
                Logger.Log($"Source directory exists: {Directory.Exists(sourceDir)}", Logger.LogLevel.Debug);
                
                if (!Directory.Exists(sourceDir))
                {
                    Logger.Log($"ERROR: Source directory does not exist: {sourceDir}", Logger.LogLevel.Error);
                    return (false, $"Extracted files directory not found: {sourceDir}");
                }
                
                // Clean up old staging directory if it exists
                try
                {
                    if (Directory.Exists(UpdateStagingDirectory))
                    {
                        Directory.Delete(UpdateStagingDirectory, recursive: true);
                    }
                }
                catch { }
                
                Directory.CreateDirectory(UpdateStagingDirectory);
                Logger.Log($"Created staging directory: {UpdateStagingDirectory}", Logger.LogLevel.Debug);
                
                var newFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                var totalFiles = newFiles.Length;
                Logger.Log($"Found {totalFiles} files to stage from {sourceDir}", Logger.LogLevel.Info);
                var processed = 0;
                
                foreach (var file in newFiles)
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    
                    // Skip appsettings.json to preserve user settings
                    if (relativePath.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        processed++;
                        continue;
                    }
                    
                    try
                    {
                        var stagingPath = Path.Combine(UpdateStagingDirectory, relativePath);
                        var stagingDir = Path.GetDirectoryName(stagingPath);
                        
                        if (!string.IsNullOrEmpty(stagingDir) && !Directory.Exists(stagingDir))
                        {
                            Directory.CreateDirectory(stagingDir);
                        }
                        
                        File.Copy(file, stagingPath, overwrite: true);
                        Logger.Log($"Staged file: {relativePath}", Logger.LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error staging file {relativePath}: {ex.Message}", Logger.LogLevel.Error);
                    }
                    
                    processed++;
                    var percent = 75 + (int)((processed * 20) / totalFiles); // 75-95%
                    progress?.Report((percent, $"Staging... {processed}/{totalFiles} files"));
                }
                
                progress?.Report((95, "Launching updater..."));
                
                // Step 5: Cleanup temp download directory
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch { /* Best effort cleanup */ }
                
                // Launch the updater helper
                var updaterPath = Path.Combine(appDir, UpdaterExecutable);
                var currentProcess = Process.GetCurrentProcess();
                
                if (!File.Exists(updaterPath))
                {
                    Logger.Log($"ERROR: Updater executable not found: {updaterPath}", Logger.LogLevel.Error);
                    return (false, "Update staged but updater executable not found. Please restart the application manually.");
                }
                
                Logger.Log($"Launching updater: {updaterPath}", Logger.LogLevel.Info);
                Logger.Log($"Staging directory: {UpdateStagingDirectory}", Logger.LogLevel.Info);
                Logger.Log($"Current process ID: {currentProcess.Id}", Logger.LogLevel.Info);
                
                try
                {
                    // Launch updater with ONLY the current process ID - updater determines app directory from its own location
                    var updaterArgs = $"{currentProcess.Id}";
                    Logger.Log($"Updater arguments: {updaterArgs}", Logger.LogLevel.Info);
                    
                    Process.Start(new ProcessStartInfo(updaterPath, updaterArgs)
                    {
                        UseShellExecute = true,
                        CreateNoWindow = false
                    });
                    
                    // Exit the main application so updater can apply files
                    Logger.Log("Update staged successfully. Closing application for updater to apply changes...", Logger.LogLevel.Info);
                    progress?.Report((100, "Update ready! Restarting application..."));
                    
                    // Give updater a moment to start, then force exit
                    System.Threading.Thread.Sleep(500);
                    
                    // Force exit the entire application - Environment.Exit is more reliable than Application.Exit
                    // for ensuring all threads and resources are released
                    Logger.Log("Forcing application exit...", Logger.LogLevel.Info);
                    Environment.Exit(0);
                    
                    return (true, "Update staged and updater launched. Application will restart automatically.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to launch updater: {ex.Message}", Logger.LogLevel.Error);
                    return (false, $"Update staged but failed to launch updater: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Update installation failed: {ex.Message}", Logger.LogLevel.Error);
                
                // Try to restore from backup
                try
                {
                    if (Directory.Exists(backupDir))
                    {
                        foreach (var file in Directory.GetFiles(backupDir))
                        {
                            var targetPath = Path.Combine(appDir, Path.GetFileName(file));
                            File.Copy(file, targetPath, overwrite: true);
                        }
                        Logger.Log("Backup restored successfully.", Logger.LogLevel.Info);
                    }
                }
                catch { /* Restore failed */ }
                
                // Cleanup
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
                
                return (false, $"Update failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Opens the GitHub releases page in the default browser
        /// </summary>
        public static void OpenReleasesPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to open releases page: {ex.Message}", Logger.LogLevel.Error);
            }
        }
        
        /// <summary>
        /// Compares two version strings. Returns negative if v1 &lt; v2, zero if equal, positive if v1 &gt; v2
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            // Clean up version strings
            v1 = v1.TrimStart('v', 'V').Split('-')[0].Split('+')[0];
            v2 = v2.TrimStart('v', 'V').Split('-')[0].Split('+')[0];
            
            var parts1 = v1.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            var parts2 = v2.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            
            var maxLen = Math.Max(parts1.Length, parts2.Length);
            
            for (int i = 0; i < maxLen; i++)
            {
                var p1 = i < parts1.Length ? parts1[i] : 0;
                var p2 = i < parts2.Length ? parts2[i] : 0;
                
                if (p1 < p2) return -1;
                if (p1 > p2) return 1;
            }
            
            return 0;
        }
        
        
        /// <summary>
        /// Restarts the application
        /// </summary>
        public static void RestartApplication()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to restart: {ex.Message}", Logger.LogLevel.Error);
            }
        }
    }
}
