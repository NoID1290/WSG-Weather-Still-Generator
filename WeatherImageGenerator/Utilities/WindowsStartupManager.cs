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

                    // Environment.ProcessPath is the most reliable way to get the .exe path
                    // in .NET 5+ (works correctly with single-file publish, unlike Assembly.Location)
                    string? executablePath = Environment.ProcessPath;

                    if (string.IsNullOrEmpty(executablePath))
                    {
                        // Fallback for edge cases
                        executablePath = Assembly.GetExecutingAssembly().Location;
                        if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            executablePath = Path.ChangeExtension(executablePath, ".exe");
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
