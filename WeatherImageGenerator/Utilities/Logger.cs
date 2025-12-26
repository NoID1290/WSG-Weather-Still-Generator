using System;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Simple logger that writes to Console and raises an event for UI sinks (e.g. embedded console in a WinForms form).
    /// </summary>
    public static class Logger
    {
        public enum LogLevel { Debug, Info, Warning, Error }

        public static event Action<string>? MessageLogged;
        public static event Action<string, LogLevel>? MessageLoggedWithLevel;

        private static readonly object _sync = new object();

        public static void Log(string message, ConsoleColor? color = null)
        {
            lock (_sync)
            {
                if (color.HasValue)
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(message);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.WriteLine(message);
                }

                var txt = message + Environment.NewLine;
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
                if (color.HasValue)
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color.Value;
                    Console.WriteLine(message);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.WriteLine(message);
                }

                var txt = message + Environment.NewLine;
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
