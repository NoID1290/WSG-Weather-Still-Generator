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
            try
            {
                // Usage: WeatherImageGenerator.Updater.exe <app_directory> [wait_for_pid]
                if (args.Length < 1)
                {
                    Console.WriteLine("Usage: WeatherImageGenerator.Updater.exe <app_directory> [wait_for_pid]");
                    Environment.Exit(1);
                }

                var appDirectory = args[0];
                var stagingDirectory = Path.Combine(Path.GetTempPath(), "WSG_Update_Staging");
                var exePath = Path.Combine(appDirectory, "WSG.exe");

                Console.WriteLine($"[Updater] App Directory: {appDirectory}");
                Console.WriteLine($"[Updater] Staging Directory: {stagingDirectory}");

                // If a PID was provided, wait for that process to exit
                if (args.Length > 1 && int.TryParse(args[1], out int pid))
                {
                    Console.WriteLine($"[Updater] Waiting for process {pid} to exit...");
                    try
                    {
                        var process = Process.GetProcessById(pid);
                        if (!process.HasExited)
                        {
                            process.WaitForExit(30000); // Wait up to 30 seconds
                        }
                    }
                    catch { /* Process already exited */ }
                    Console.WriteLine("[Updater] Process exited, proceeding with update...");
                }

                // Wait for the main EXE to be released
                Console.WriteLine("[Updater] Waiting for application files to be unlocked...");
                if (!WaitForFilesUnlocked(appDirectory, 10000)) // 10 second timeout
                {
                    Console.WriteLine("[Updater] WARNING: Timeout waiting for files to unlock, attempting anyway...");
                }

                // Check if staging directory exists and has files
                if (!Directory.Exists(stagingDirectory))
                {
                    Console.WriteLine("[Updater] ERROR: Staging directory not found!");
                    Environment.Exit(1);
                }

                var stagingFiles = Directory.GetFiles(stagingDirectory, "*.*", SearchOption.AllDirectories);
                if (stagingFiles.Length == 0)
                {
                    Console.WriteLine("[Updater] No files in staging directory, nothing to update.");
                    Directory.Delete(stagingDirectory, recursive: true);
                    LaunchApplication(exePath);
                    return;
                }

                Console.WriteLine($"[Updater] Found {stagingFiles.Length} files to apply...");

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
                            catch (IOException)
                            {
                                // File still locked, skip it
                                Console.WriteLine($"[Updater] WARN: Could not delete {relativePath}, file may be in use");
                                failedCount++;
                                continue;
                            }
                        }

                        // Copy the new file
                        File.Copy(file, targetPath, overwrite: true);
                        copiedCount++;
                        Console.WriteLine($"[Updater] Updated: {relativePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Updater] ERROR copying {relativePath}: {ex.Message}");
                        failedCount++;
                    }
                }

                // Clean up staging directory
                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                    Console.WriteLine("[Updater] Cleaned up staging directory");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Updater] WARNING: Could not clean staging directory: {ex.Message}");
                }

                Console.WriteLine($"[Updater] Update complete: {copiedCount} files applied, {failedCount} failed");

                // Launch the application
                LaunchApplication(exePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater] FATAL ERROR: {ex.Message}");
                Console.WriteLine($"[Updater] Stack trace: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Waits for all critical application files to be unlocked
        /// </summary>
        private static bool WaitForFilesUnlocked(string appDirectory, int timeoutMs)
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
                    return true;
                }

                Thread.Sleep(200);
            }

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
        private static void LaunchApplication(string exePath)
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    Console.WriteLine($"[Updater] ERROR: Application executable not found: {exePath}");
                    Environment.Exit(1);
                }

                Console.WriteLine($"[Updater] Launching application: {exePath}");
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                Console.WriteLine("[Updater] Application launched successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater] ERROR launching application: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
