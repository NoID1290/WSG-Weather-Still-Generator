using System;
namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Simple logger that writes to Console and raises an event for UI sinks (e.g. embedded console in a WinForms form).
    /// Enhanced with timestamps and message throttling to reduce UI noise.
    /// </summary>
    public static class Logger
    {
        public enum LogLevel { Debug, Info, Warning, Error }

        public static event Action<string>? MessageLogged;
        public static event Action<string, LogLevel>? MessageLoggedWithLevel;
        // Allow external callers to request that UI log widgets archive older logs to disk (subscribed by MainForm)
        public static event Action? ArchiveRequested;

        private static readonly object _sync = new object();

        // Throttling: track last message to suppress rapid duplicates
        private static string? _lastMessage;
        private static DateTime _lastMessageTime = DateTime.MinValue;
        private static int _repeatCount = 0;
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Whether to include timestamps in log messages (default: true for UI readability)
        /// </summary>
        public static bool IncludeTimestamp { get; set; } = true;

        /// <summary>
        /// Request subscribed UI components to archive their logs to disk (non-blocking).
        /// </summary>
        public static void RequestArchive()
        {
            try { ArchiveRequested?.Invoke(); } catch { }
        }

        // Format message with optional timestamp
        private static string FormatMessage(string message)
        {
            if (!IncludeTimestamp) return message;
            // Use compact time format for cleaner logs
            return $"[{DateTime.Now:HH:mm:ss}] {message}";
        }

        // Check if message should be throttled (returns true if should skip)
        private static bool ShouldThrottle(string message, out string? summaryMessage)
        {
            summaryMessage = null;
            var now = DateTime.Now;

            if (_lastMessage == message && (now - _lastMessageTime) < ThrottleWindow)
            {
                _repeatCount++;
                _lastMessageTime = now;
                return true; // Skip this message
            }

            // If we had repeats, emit a summary before the new message
            if (_repeatCount > 0 && _lastMessage != null)
            {
                summaryMessage = $"    ↳ (repeated {_repeatCount}x)";
            }

            _lastMessage = message;
            _lastMessageTime = now;
            _repeatCount = 0;
            return false;
        }

        public static void Log(string message, ConsoleColor? color = null)
        {
            lock (_sync)
            {
                if (ShouldThrottle(message, out var summary))
                    return; // Skip duplicate

                // Emit summary of previous repeats if any
                if (summary != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(summary);
                    Console.ResetColor();
                    MessageLoggedWithLevel?.Invoke(summary + Environment.NewLine, LogLevel.Debug);
                }

                var formattedMessage = FormatMessage(message);

                if (color.HasValue)
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(formattedMessage);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.WriteLine(formattedMessage);
                }

                var txt = formattedMessage + Environment.NewLine;
                MessageLogged?.Invoke(txt);
                MessageLoggedWithLevel?.Invoke(txt, LogLevel.Info);
            }
        }

        /// <summary>
        /// Log a message with an explicit severity level.
        /// </summary>
        public static void Log(string message, LogLevel level)
        {
            var color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => (ConsoleColor?)null
            };

            lock (_sync)
            {
                if (ShouldThrottle(message, out var summary))
                    return; // Skip duplicate

                // Emit summary of previous repeats if any
                if (summary != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(summary);
                    Console.ResetColor();
                    MessageLoggedWithLevel?.Invoke(summary + Environment.NewLine, LogLevel.Debug);
                }

                var formattedMessage = FormatMessage(message);

                if (color.HasValue)
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(formattedMessage);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.WriteLine(formattedMessage);
                }

                var txt = formattedMessage + Environment.NewLine;
                MessageLogged?.Invoke(txt);
                MessageLoggedWithLevel?.Invoke(txt, level);
            }
        }

        public static void LogPlain()
        {
            Console.WriteLine();
            MessageLogged?.Invoke(Environment.NewLine);
        }
    }
}
