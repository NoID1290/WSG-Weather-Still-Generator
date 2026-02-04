using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace WeatherImageGenerator.Updater
{
    /// <summary>
    /// Helper utility that applies pending updates after the main application has closed.
    /// Launched by WeatherImageGenerator during update installation.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // Write to console for debugging
            var logFile = Path.Combine(Path.GetTempPath(), "WSG_Updater.log");
            try
            {
                using (var writer = new StreamWriter(logFile, append: true))
                {
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Updater started");
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Arguments: {string.Join(" | ", args)}");
                    writer.Flush();
                }
            }
            catch { }

            try
            {
                // Usage: WeatherImageGenerator.Updater.exe <app_directory> [wait_for_pid]
                if (args.Length < 1)
                {
                    Log("Usage: WeatherImageGenerator.Updater.exe <app_directory> [wait_for_pid]", logFile);
                    Environment.Exit(1);
                }

                var appDirectory = args[0];
                var stagingDirectory = Path.Combine(Path.GetTempPath(), "WSG_Update_Staging");
                var exePath = Path.Combine(appDirectory, "WSG.exe");

                Log($"App Directory: {appDirectory}", logFile);
                Log($"Staging Directory: {stagingDirectory}", logFile);
                Log($"Exe Path: {exePath}", logFile);

                // Verify app directory exists
                if (!Directory.Exists(appDirectory))
                {
                    Log($"ERROR: App directory not found: {appDirectory}", logFile);
                    Environment.Exit(1);
                }

                // If a PID was provided, wait for that process to exit
                if (args.Length > 1 && int.TryParse(args[1], out int pid))
                {
                    Log($"Waiting for process {pid} to exit...", logFile);
                    try
                    {
                        var process = Process.GetProcessById(pid);
                        if (!process.HasExited)
                        {
                            Log($"Process {pid} is running, waiting...", logFile);
                            process.WaitForExit(30000); // Wait up to 30 seconds
                            Log($"Process {pid} exited", logFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Process check error: {ex.Message}", logFile);
                    }
                    Log("Proceeding with update...", logFile);
                }

                // Wait for the main EXE to be released
                Log("Waiting for application files to be unlocked...", logFile);
                if (!WaitForFilesUnlocked(appDirectory, 10000, logFile)) // 10 second timeout
                {
                    Log("WARNING: Timeout waiting for files to unlock, attempting anyway...", logFile);
                }

                // Check if staging directory exists and has files
                if (!Directory.Exists(stagingDirectory))
                {
                    Log($"ERROR: Staging directory not found: {stagingDirectory}", logFile);
                    Environment.Exit(1);
                }

                var stagingFiles = Directory.GetFiles(stagingDirectory, "*.*", SearchOption.AllDirectories);
                Log($"Found {stagingFiles.Length} files in staging directory", logFile);
                
                if (stagingFiles.Length == 0)
                {
                    Log("No files in staging directory, nothing to update.", logFile);
                    try { Directory.Delete(stagingDirectory, recursive: true); } catch { }
                    LaunchApplication(exePath, logFile);
                    return;
                }

                Log($"Applying {stagingFiles.Length} files...", logFile);

                // Copy all files from staging to app directory
                int copiedCount = 0;
                int failedCount = 0;

                foreach (var file in stagingFiles)
                {
                    var relativePath = Path.GetRelativePath(stagingDirectory, file);
                    var targetPath = Path.Combine(appDirectory, relativePath);

                    try
                    {
                        var targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // Delete target if it exists
                        if (File.Exists(targetPath))
                        {
                            try
                            {
                                File.Delete(targetPath);
                            }
                            catch (IOException ex)
                            {
                                // File still locked, skip it
                                Log($"WARN: Could not delete {relativePath}, file may be in use: {ex.Message}", logFile);
                                failedCount++;
                                continue;
                            }
                        }

                        // Copy the new file
                        File.Copy(file, targetPath, overwrite: true);
                        copiedCount++;
                        Log($"Updated: {relativePath}", logFile);
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR copying {relativePath}: {ex.Message}", logFile);
                        failedCount++;
                    }
                }

                // Clean up staging directory
                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                    Log("Cleaned up staging directory", logFile);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Could not clean staging directory: {ex.Message}", logFile);
                }

                Log($"Update complete: {copiedCount} files applied, {failedCount} failed", logFile);

                // Launch the application
                LaunchApplication(exePath, logFile);
            }
            catch (Exception ex)
            {
                Log($"FATAL ERROR: {ex.Message}", logFile);
                Log($"Stack trace: {ex.StackTrace}", logFile);
                Environment.Exit(1);
            }
        }

        private static void Log(string message, string logFile)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[{timestamp}] {message}");
            
            try
            {
                using (var writer = new StreamWriter(logFile, append: true))
                {
                    writer.WriteLine($"[{timestamp}] {message}");
                    writer.Flush();
                }
            }
            catch { }
        }

        /// <summary>
        /// Waits for all critical application files to be unlocked
        /// </summary>
        private static bool WaitForFilesUnlocked(string appDirectory, int timeoutMs, string logFile)
        {
            var sw = Stopwatch.StartNew();
            var criticalFiles = new[] { "WSG.exe", "WeatherImageGenerator.dll", "ECCC.dll", "OpenMeteo.dll" };

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool allUnlocked = true;

                foreach (var fileName in criticalFiles)
                {
                    var filePath = Path.Combine(appDirectory, fileName);
                    if (File.Exists(filePath) && IsFileLocked(filePath))
                    {
                        allUnlocked = false;
                        break;
                    }
                }

                if (allUnlocked)
                {
                    Log("All files unlocked", logFile);
                    return true;
                }

                Thread.Sleep(200);
            }

            Log("Timeout waiting for files to unlock", logFile);
            return false;
        }

        /// <summary>
        /// Checks if a file is locked by another process
        /// </summary>
        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
        }

        /// <summary>
        /// Launches the main application
        /// </summary>
        private static void LaunchApplication(string exePath, string logFile)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    Log($"ERROR: Application executable not found: {exePath}", logFile);
                    Environment.Exit(1);
                }

                Log($"Launching application: {exePath}", logFile);
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                Log("Application launched successfully", logFile);
            }
            catch (Exception ex)
            {
                Log($"ERROR launching application: {ex.Message}", logFile);
                Environment.Exit(1);
            }
        }
    }
}
