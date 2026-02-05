#nullable enable
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
    /// Usage: WSG.Updater.exe [pid]
    ///   pid - Optional process ID of the main application to wait for
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
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ========================================");
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Updater started");
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Arguments: {string.Join(" | ", args)}");
                    writer.Flush();
                }
            }
            catch { }

            try
            {
                // Determine the app directory from the updater's own location
                // The updater is located in the same directory as WSG.exe
                var appDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                appDirectory = Path.GetFullPath(appDirectory);
                
                var stagingDirectory = Path.Combine(Path.GetTempPath(), "WSG_Update_Staging");
                var exePath = Path.Combine(appDirectory, "WSG.exe");

                Log($"Updater Location: {Environment.ProcessPath}", logFile);
                Log($"App Directory: {appDirectory}", logFile);
                Log($"Staging Directory: {stagingDirectory}", logFile);
                Log($"Exe Path: {exePath}", logFile);

                // Verify app directory exists
                if (!Directory.Exists(appDirectory))
                {
                    Log($"ERROR: App directory not found: {appDirectory}", logFile);
                    Environment.Exit(1);
                }

                // Parse PID from arguments (first argument that's a valid integer)
                int? mainProcessPid = null;
                foreach (var arg in args)
                {
                    if (int.TryParse(arg.Trim(), out int parsedPid) && parsedPid > 0)
                    {
                        mainProcessPid = parsedPid;
                        break;
                    }
                }

                // If a PID was provided, wait for that process to exit (and kill it if necessary)
                if (mainProcessPid.HasValue)
                {
                    Log($"Main application PID: {mainProcessPid.Value}", logFile);
                    WaitForProcessToExit(mainProcessPid.Value, logFile);
                }
                else
                {
                    Log("No PID provided, waiting 3 seconds for main app to close...", logFile);
                    Thread.Sleep(3000);
                }
                
                // Kill any lingering WSG processes
                KillAllWSGProcesses(logFile);

                // Wait for the main EXE to be released
                Log("Waiting for application files to be unlocked...", logFile);
                if (!WaitForFilesUnlocked(appDirectory, 30000, logFile)) // 30 second timeout
                {
                    Log("WARNING: Timeout waiting for files to unlock, trying to kill processes...", logFile);
                    KillAllWSGProcesses(logFile);
                    Thread.Sleep(2000);
                    
                    // Try one more time
                    if (!WaitForFilesUnlocked(appDirectory, 10000, logFile))
                    {
                        Log("WARNING: Files still locked, attempting update anyway...", logFile);
                    }
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

                // Files that the updater cannot update (itself)
                var updaterFiles = new[] { "WSG.Updater.exe", "WSG.Updater.dll", "WSG.Updater.deps.json", "WSG.Updater.runtimeconfig.json" };
                
                // Filter out updater's own files - we can't update ourselves
                var filesToUpdate = stagingFiles
                    .Where(f => !updaterFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                
                var skippedUpdaterFiles = stagingFiles.Length - filesToUpdate.Length;
                if (skippedUpdaterFiles > 0)
                {
                    Log($"Skipping {skippedUpdaterFiles} updater files (cannot self-update)", logFile);
                }
                
                Log($"Applying {filesToUpdate.Length} files...", logFile);

                // Copy all files from staging to app directory with retry logic
                int copiedCount = 0;
                int failedCount = 0;
                var failedFiles = new System.Collections.Generic.List<(string source, string target, string relative)>();

                foreach (var file in filesToUpdate)
                {
                    var relativePath = Path.GetRelativePath(stagingDirectory, file);
                    var targetPath = Path.Combine(appDirectory, relativePath);

                    if (!TryCopyFile(file, targetPath, relativePath, logFile))
                    {
                        failedFiles.Add((file, targetPath, relativePath));
                    }
                    else
                    {
                        copiedCount++;
                    }
                }

                // Retry failed files after a short wait
                if (failedFiles.Count > 0)
                {
                    Log($"Retrying {failedFiles.Count} failed files after 2 second wait...", logFile);
                    Thread.Sleep(2000);
                    
                    // Try to kill any remaining processes that might be locking files
                    KillAllWSGProcesses(logFile);
                    Thread.Sleep(1000);
                    
                    foreach (var (source, target, relative) in failedFiles.ToArray())
                    {
                        if (TryCopyFile(source, target, relative, logFile))
                        {
                            copiedCount++;
                            failedFiles.Remove((source, target, relative));
                        }
                    }
                    
                    failedCount = failedFiles.Count;
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
                
                if (failedCount > 0)
                {
                    Log("WARNING: Some files could not be updated. The application may need manual update.", logFile);
                }

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

        /// <summary>
        /// Tries to copy a single file, returns true on success
        /// </summary>
        private static bool TryCopyFile(string sourceFile, string targetPath, string relativePath, string logFile)
        {
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
                        // Try to rename the file instead of deleting
                        try
                        {
                            var bakPath = targetPath + ".bak";
                            if (File.Exists(bakPath)) File.Delete(bakPath);
                            File.Move(targetPath, bakPath);
                        }
                        catch
                        {
                            Log($"WARN: Could not delete or rename {relativePath}, file may be in use", logFile);
                            return false;
                        }
                    }
                }

                // Copy the new file
                File.Copy(sourceFile, targetPath, overwrite: true);
                Log($"Updated: {relativePath}", logFile);
                return true;
            }
            catch (Exception ex)
            {
                Log($"ERROR copying {relativePath}: {ex.Message}", logFile);
                return false;
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
        /// Waits for a specific process to exit, killing it if necessary
        /// </summary>
        private static void WaitForProcessToExit(int pid, string logFile)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                Log($"Found process {pid} ({process.ProcessName}), waiting for it to exit...", logFile);
                
                // Wait up to 10 seconds for graceful exit
                if (!process.WaitForExit(10000))
                {
                    Log($"Process {pid} did not exit gracefully, attempting to kill...", logFile);
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                        Log($"Process {pid} killed", logFile);
                    }
                    catch (Exception killEx)
                    {
                        Log($"Failed to kill process {pid}: {killEx.Message}", logFile);
                    }
                }
                else
                {
                    Log($"Process {pid} exited gracefully", logFile);
                }
            }
            catch (ArgumentException)
            {
                // Process doesn't exist - that's fine, it means it already exited
                Log($"Process {pid} not found (already exited)", logFile);
            }
            catch (Exception ex)
            {
                Log($"Error waiting for process {pid}: {ex.Message}", logFile);
            }
            
            // Extra wait to ensure file handles are released
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Kills all WSG-related processes except the updater
        /// </summary>
        private static void KillAllWSGProcesses(string logFile)
        {
            var myPid = Environment.ProcessId;
            var processNamesToKill = new[] { "WSG", "dotnet" };
            
            foreach (var processName in processNamesToKill)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var proc in processes)
                    {
                        try
                        {
                            // Skip self
                            if (proc.Id == myPid)
                                continue;
                            
                            // For dotnet processes, check if it's related to WSG
                            if (processName == "dotnet")
                            {
                                var cmdLine = GetCommandLine(proc);
                                if (cmdLine == null || !cmdLine.Contains("WSG", StringComparison.OrdinalIgnoreCase))
                                    continue;
                            }
                            
                            Log($"Killing process: {proc.ProcessName} (PID: {proc.Id})", logFile);
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(3000);
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to kill {proc.ProcessName} ({proc.Id}): {ex.Message}", logFile);
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error enumerating {processName} processes: {ex.Message}", logFile);
                }
            }
        }

        /// <summary>
        /// Gets the command line of a process (returns null if unable to access)
        /// </summary>
        private static string? GetCommandLine(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Waits for all critical application files to be unlocked
        /// </summary>
        private static bool WaitForFilesUnlocked(string appDirectory, int timeoutMs, string logFile)
        {
            var sw = Stopwatch.StartNew();
            // Include all DLLs that need to be unlocked
            // Note: We do NOT include WSG.Updater files here because the updater itself locks them
            // and we skip updating them anyway (updater cannot self-update)
            var criticalFiles = new[] { 
                "WSG.exe", 
                "WSG.dll", 
                "ECCC.dll", 
                "OpenMeteo.dll",
                "EAS.dll",
                "WeatherShared.dll",
                "OpenMap.dll"
            };

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool allUnlocked = true;
                string? lockedFile = null;

                foreach (var fileName in criticalFiles)
                {
                    var filePath = Path.Combine(appDirectory, fileName);
                    if (File.Exists(filePath) && IsFileLocked(filePath))
                    {
                        allUnlocked = false;
                        lockedFile = fileName;
                        break;
                    }
                }

                if (allUnlocked)
                {
                    Log("All critical files unlocked", logFile);
                    return true;
                }

                if (sw.ElapsedMilliseconds % 5000 < 200) // Log every 5 seconds
                {
                    Log($"Still waiting for files to unlock (locked: {lockedFile})...", logFile);
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
