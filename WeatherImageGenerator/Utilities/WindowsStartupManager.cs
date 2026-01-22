using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Manages Windows startup configuration for the application
    /// </summary>
    public static class WindowsStartupManager
    {
        private const string AppName = "WeatherStillGenerator";
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Enables the application to start when Windows starts
        /// </summary>
        public static void EnableStartup()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("Unable to open Windows startup registry key");
                    }

                    string executablePath = Assembly.GetExecutingAssembly().Location;
                    
                    // If running as a .dll (common for .NET Core/5+), get the .exe path
                    if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        executablePath = Path.ChangeExtension(executablePath, ".exe");
                        
                        // If .exe doesn't exist, try to find it in the same directory
                        if (!File.Exists(executablePath))
                        {
                            string? directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                            if (directory != null)
                            {
                                // Look for any .exe in the directory
                                var exeFiles = Directory.GetFiles(directory, "*.exe");
                                if (exeFiles.Length > 0)
                                {
                                    executablePath = exeFiles[0];
                                }
                                else
                                {
                                    // Fallback: use dotnet command with dll
                                    executablePath = $"dotnet \"{Assembly.GetExecutingAssembly().Location}\"";
                                }
                            }
                        }
                    }

                    // Wrap path in quotes to handle spaces
                    if (!executablePath.StartsWith("dotnet"))
                    {
                        executablePath = $"\"{executablePath}\"";
                    }

                    key.SetValue(AppName, executablePath);
                    Logger.Log($"Windows startup enabled: {executablePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to enable Windows startup: {ex.Message}", Logger.LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Disables the application from starting when Windows starts
        /// </summary>
        public static void DisableStartup()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("Unable to open Windows startup registry key");
                    }

                    // Check if the value exists before trying to delete it
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                        Logger.Log("Windows startup disabled");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to disable Windows startup: {ex.Message}", Logger.LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// Checks if the application is currently configured to start with Windows
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    return key.GetValue(AppName) != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to check Windows startup status: {ex.Message}", Logger.LogLevel.Error);
                return false;
            }
        }
    }
}
