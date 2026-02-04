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
        private static readonly string PendingUpdatesManifest = Path.Combine(AppContext.BaseDirectory, ".update_pending");
        
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
                if (subdirs.Length == 1 && !File.Exists(Path.Combine(extractPath, "WeatherImageGenerator.exe")))
                {
                    sourceDir = subdirs[0];
                    // Check one more level deep
                    var nestedSubdirs = Directory.GetDirectories(sourceDir);
                    if (nestedSubdirs.Length == 1 && !File.Exists(Path.Combine(sourceDir, "WeatherImageGenerator.exe")))
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
                
                // Step 4: Copy new files with deferred update on locked files
                progress?.Report((75, "Installing update..."));
                Logger.Log("Installing update...", Logger.LogLevel.Info);
                
                var newFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                var totalFiles = newFiles.Length;
                var processed = 0;
                var deferredFiles = new List<string>();
                var pendingUpdatesList = new List<string>(); // Track files for startup fallback
                
                foreach (var file in newFiles)
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var targetPath = Path.Combine(appDir, relativePath);
                    
                    // Skip appsettings.json to preserve user settings
                    if (relativePath.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        processed++;
                        continue;
                    }
                    
                    try
                    {
                        var targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        
                        // Try direct replacement first
                        try
                        {
                            File.Copy(file, targetPath, overwrite: true);
                            Logger.Log($"Updated file: {relativePath}", Logger.LogLevel.Debug);
                        }
                        catch (IOException ex) when (ex.Message.Contains("being used") || ex.Message.Contains("access"))
                        {
                            // File is locked - use fallback mechanism
                            Logger.Log($"File locked, scheduling for deferred update: {relativePath}", Logger.LogLevel.Warning);
                            
                            // Copy to temporary location with .new extension
                            var tempNewPath = targetPath + ".new";
                            try
                            {
                                File.Copy(file, tempNewPath, overwrite: true);
                                
                                // First try: Schedule the replacement on next reboot using Windows API
                                bool scheduledWithMoveFileEx = false;
                                try
                                {
                                    if (MoveFileEx(tempNewPath, targetPath, MOVEFILE_DELAY_UNTIL_REBOOT | MOVEFILE_REPLACE_EXISTING))
                                    {
                                        deferredFiles.Add(relativePath);
                                        Logger.Log($"Scheduled for deferred replacement on next reboot: {relativePath}", Logger.LogLevel.Info);
                                        scheduledWithMoveFileEx = true;
                                    }
                                    else
                                    {
                                        int error = Marshal.GetLastWin32Error();
                                        Logger.Log($"MoveFileEx failed (error {error}), using fallback startup mechanism", Logger.LogLevel.Warning);
                                    }
                                }
                                catch (Exception moveEx)
                                {
                                    Logger.Log($"MoveFileEx exception: {moveEx.Message}, using fallback", Logger.LogLevel.Warning);
                                }
                                
                                // Fallback: Store in manifest for startup application
                                if (!scheduledWithMoveFileEx)
                                {
                                    pendingUpdatesList.Add($"{tempNewPath}|{targetPath}");
                                    deferredFiles.Add(relativePath);
                                    Logger.Log($"Stored for startup application: {relativePath}", Logger.LogLevel.Info);
                                }
                            }
                            catch (Exception tempEx)
                            {
                                Logger.Log($"Failed to prepare deferred update for {relativePath}: {tempEx.Message}", Logger.LogLevel.Error);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error installing file {relativePath}: {ex.Message}", Logger.LogLevel.Error);
                    }
                    
                    processed++;
                    var percent = 75 + (int)((processed * 20) / totalFiles); // 75-95%
                    progress?.Report((percent, $"Installing... {processed}/{totalFiles} files"));
                }
                
                // Write pending updates manifest if there are any fallback files
                if (pendingUpdatesList.Count > 0)
                {
                    try
                    {
                        File.WriteAllLines(PendingUpdatesManifest, pendingUpdatesList);
                        Logger.Log($"Wrote {pendingUpdatesList.Count} pending updates to manifest for startup application", Logger.LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to write pending updates manifest: {ex.Message}", Logger.LogLevel.Error);
                    }
                }
                
                progress?.Report((95, "Cleaning up..."));
                
                // Report deferred files status
                string deferMessage = "";
                if (deferredFiles.Count > 0)
                {
                    deferMessage = $"\n\n⚠️  {deferredFiles.Count} file(s) are in use and will be updated on the next restart:\n" + 
                                   string.Join("\n", deferredFiles.Select(f => $"  • {f}"));
                    Logger.Log($"{deferredFiles.Count} files scheduled for deferred update on next reboot", Logger.LogLevel.Warning);
                }
                
                progress?.Report((95, "Cleaning up..."));
                
                // Step 5: Cleanup temp files
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch { /* Best effort cleanup */ }
                
                progress?.Report((100, "Update complete!"));
                Logger.Log("Update installed successfully!", Logger.LogLevel.Info);
                
                string successMessage = "Update installed successfully! Please restart the application to apply changes." + deferMessage;
                return (true, successMessage);
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
        /// Applies any pending updates left over from previous installation attempts
        /// Call this at application startup
        /// </summary>
        public static void ApplyPendingUpdates()
        {
            try
            {
                // Check if manifest exists
                if (!File.Exists(PendingUpdatesManifest))
                {
                    return; // No pending updates
                }
                
                Logger.Log("Found pending updates from previous installation. Attempting to apply...", Logger.LogLevel.Info);
                
                var pendingFiles = File.ReadAllLines(PendingUpdatesManifest)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                
                if (pendingFiles.Count == 0)
                {
                    File.Delete(PendingUpdatesManifest);
                    return;
                }
                
                int appliedCount = 0;
                int failedCount = 0;
                var stillLockedFiles = new List<string>();
                
                foreach (var entry in pendingFiles)
                {
                    var parts = entry.Split('|');
                    if (parts.Length != 2)
                        continue;
                    
                    var tempNewPath = parts[0];
                    var targetPath = parts[1];
                    
                    if (!File.Exists(tempNewPath))
                    {
                        Logger.Log($"Pending file not found: {tempNewPath}", Logger.LogLevel.Warning);
                        failedCount++;
                        continue;
                    }
                    
                    try
                    {
                        // Try to replace the target file with timeout
                        var sw = Stopwatch.StartNew();
                        var maxWaitTime = TimeSpan.FromSeconds(5);
                        bool replaced = false;
                        
                        while (sw.Elapsed < maxWaitTime && !replaced)
                        {
                            try
                            {
                                if (File.Exists(targetPath))
                                {
                                    File.Delete(targetPath);
                                }
                                File.Move(tempNewPath, targetPath, overwrite: true);
                                replaced = true;
                                appliedCount++;
                                Logger.Log($"Applied pending update: {Path.GetFileName(targetPath)}", Logger.LogLevel.Info);
                            }
                            catch (IOException)
                            {
                                if (sw.Elapsed < maxWaitTime)
                                {
                                    System.Threading.Thread.Sleep(200);
                                }
                            }
                        }
                        
                        if (!replaced)
                        {
                            Logger.Log($"File still locked after timeout: {targetPath}", Logger.LogLevel.Warning);
                            stillLockedFiles.Add(targetPath);
                            failedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Failed to apply pending update {targetPath}: {ex.Message}", Logger.LogLevel.Error);
                        failedCount++;
                    }
                }
                
                // Clean up manifest
                try
                {
                    File.Delete(PendingUpdatesManifest);
                }
                catch { }
                
                if (failedCount == 0)
                {
                    Logger.Log($"Successfully applied {appliedCount} pending updates.", Logger.LogLevel.Info);
                }
                else
                {
                    Logger.Log($"Applied {appliedCount} updates with {failedCount} failures.", Logger.LogLevel.Warning);
                    if (stillLockedFiles.Count > 0)
                    {
                        Logger.Log($"Files still in use: {string.Join(", ", stillLockedFiles.Select(Path.GetFileName))}", Logger.LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error applying pending updates: {ex.Message}", Logger.LogLevel.Error);
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
