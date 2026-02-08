using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WeatherImageGenerator.Utilities
{
    /// <summary>
    /// Professional console logger with rich formatting, color-coded categories,
    /// structured grouping, and clean visual hierarchy.
    /// Writes to Console and raises events for UI sinks (e.g. embedded console in WinForms).
    /// </summary>
    public static class Logger
    {
        public enum LogLevel { Debug, Info, Warning, Error }

        public static event Action<string>? MessageLogged;
        public static event Action<string, LogLevel>? MessageLoggedWithLevel;
        public static event Action? ArchiveRequested;

        private static readonly object _sync = new object();

        // Throttling
        private static string? _lastMessage;
        private static DateTime _lastMessageTime = DateTime.MinValue;
        private static int _repeatCount = 0;
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMilliseconds(500);

        /// <summary>Whether to include timestamps in log messages.</summary>
        public static bool IncludeTimestamp { get; set; } = true;

        // ── Section / Group tracking ──────────────────────────────────────────
        private static string? _currentSection;
        private static int _sectionItemCount;
        private static readonly Stopwatch _sectionTimer = new Stopwatch();

        // ── Category styling ──────────────────────────────────────────────────
        // Maps [Tag] prefixes to (icon, color) pairs for consistent visual identity
        private static readonly Dictionary<string, (string Icon, ConsoleColor Color)> _categories = new(StringComparer.OrdinalIgnoreCase)
        {
            // Weather data sources
            { "OpenMeteo",       ("🌡", ConsoleColor.Cyan) },
            { "ECCC",            ("🍁", ConsoleColor.Cyan) },
            { "ECCC API",        ("🍁", ConsoleColor.Cyan) },
            { "ECCC Fallback",   ("🍁", ConsoleColor.DarkYellow) },
            { "ECCC+OpenMeteo",  ("🔗", ConsoleColor.Cyan) },
            { "Hybrid",          ("🔗", ConsoleColor.Cyan) },
            { "OpenMeteo Retry", ("🔄", ConsoleColor.DarkYellow) },

            // Alerts
            { "Alerts",          ("🔔", ConsoleColor.Yellow) },
            { "ECCC CAP",        ("📋", ConsoleColor.Yellow) },
            { "AlertReady",      ("🚨", ConsoleColor.Red) },
            { "NAAD",            ("📡", ConsoleColor.DarkYellow) },

            // Radar & Maps
            { "RadarAnimation",  ("📡", ConsoleColor.Magenta) },
            { "Radar",           ("📡", ConsoleColor.Magenta) },
            { "RadarImageService", ("📡", ConsoleColor.Magenta) },
            { "MapCache",        ("🗺", ConsoleColor.DarkCyan) },
            { "GlobalWeatherMap",("🌍", ConsoleColor.DarkCyan) },
            { "OpenMap",         ("🗺", ConsoleColor.DarkCyan) },

            // Media / Video
            { "FFmpeg",          ("🎬", ConsoleColor.Blue) },
            { "MUSIC",           ("🎵", ConsoleColor.Magenta) },
            { "OVERLAY",         ("🎞", ConsoleColor.Blue) },
            { "AUDIO",           ("🔊", ConsoleColor.Blue) },

            // Processing
            { "RUNNING",         ("▶", ConsoleColor.Green) },
            { "DONE",            ("■", ConsoleColor.Green) },
            { "FAIL",            ("✖", ConsoleColor.Red) },
            { "CLEANUP",         ("🧹", ConsoleColor.DarkGray) },
            { "MEMORY",          ("💾", ConsoleColor.DarkGray) },

            // Alerts & TTS
            { "PiperTTS",        ("🗣", ConsoleColor.DarkMagenta) },
            { "EdgeTTS",         ("🗣", ConsoleColor.DarkMagenta) },
            { "SAPI",            ("🗣", ConsoleColor.DarkMagenta) },
            { "EmergencyAlertGenerator", ("🚨", ConsoleColor.Red) },

            // System
            { "WebUI",           ("🌐", ConsoleColor.Green) },
            { "Boot",            ("⚡", ConsoleColor.White) },
            { "SMOKE",           ("🔍", ConsoleColor.DarkGray) },
            { "INFO",            ("ℹ", ConsoleColor.White) },
            { "WARNING",         ("⚠", ConsoleColor.Yellow) },
            { "ERROR",           ("✖", ConsoleColor.Red) },
            { "DEBUG",           ("·", ConsoleColor.DarkGray) },
        };

        // Regex to detect [Tag] prefix in messages
        private static readonly Regex TagRegex = new(@"^\[([^\]]+)\]", RegexOptions.Compiled);

        public static void RequestArchive()
        {
            try { ArchiveRequested?.Invoke(); } catch { }
        }

        // ── Formatting helpers ─────────────────────────────────────────────────

        private static string Timestamp() => IncludeTimestamp ? $"{DateTime.Now:HH:mm:ss}" : "";

        private static bool ShouldThrottle(string message, out string? summaryMessage)
        {
            summaryMessage = null;
            var now = DateTime.Now;

            if (_lastMessage == message && (now - _lastMessageTime) < ThrottleWindow)
            {
                _repeatCount++;
                _lastMessageTime = now;
                return true;
            }

            if (_repeatCount > 0 && _lastMessage != null)
            {
                summaryMessage = $"       ↳ repeated {_repeatCount}x";
            }

            _lastMessage = message;
            _lastMessageTime = now;
            _repeatCount = 0;
            return false;
        }

        /// <summary>Detect status symbols and auto-assign level.</summary>
        private static LogLevel DetectLevel(string message, ConsoleColor? hintColor)
        {
            var trimmed = message.TrimStart();
            if (trimmed.StartsWith("✗") || trimmed.StartsWith("[FAIL]"))
                return LogLevel.Error;
            if (trimmed.StartsWith("⚠") || trimmed.Contains("[WARN]"))
                return LogLevel.Warning;

            if (hintColor == ConsoleColor.Red) return LogLevel.Error;
            if (hintColor == ConsoleColor.Yellow || hintColor == ConsoleColor.DarkYellow) return LogLevel.Warning;
            if (hintColor == ConsoleColor.DarkGray) return LogLevel.Debug;
            return LogLevel.Info;
        }

        /// <summary>
        /// Is this a "success" line? (✓ or [DONE])
        /// </summary>
        private static bool IsSuccess(string msg)
        {
            var t = msg.TrimStart();
            return t.StartsWith("✓") || t.Contains("[DONE]");
        }

        /// <summary>
        /// Is this a section header? (--- ... --- or ═══ lines)
        /// </summary>
        private static bool IsSectionHeader(string msg)
        {
            var t = msg.Trim();
            return (t.StartsWith("---") && t.EndsWith("---")) || t.StartsWith("═══");
        }

        /// <summary>
        /// Extract [Tag] from message and return (tag, remainingMessage).
        /// </summary>
        private static (string? Tag, string Body) ExtractTag(string message)
        {
            var m = TagRegex.Match(message.TrimStart());
            if (m.Success)
            {
                var tag = m.Groups[1].Value;
                var body = message.TrimStart().Substring(m.Length).TrimStart();
                return (tag, body);
            }
            return (null, message);
        }

        // ── Core write routines ────────────────────────────────────────────────

        private static void WriteThrottleSummary(string summary)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(summary);
            Console.ResetColor();
            MessageLoggedWithLevel?.Invoke(summary + Environment.NewLine, LogLevel.Debug);
        }

        /// <summary>
        /// Renders a single formatted log line to the console with colors.
        /// </summary>
        private static void RenderLine(string originalMessage, LogLevel level, ConsoleColor? overrideColor)
        {
            var (tag, body) = ExtractTag(originalMessage);
            bool isSuccess = IsSuccess(originalMessage);
            bool isSectionHeader = IsSectionHeader(originalMessage);

            // ── Section headers get special treatment ───────────────────────
            if (isSectionHeader)
            {
                CloseSectionIfOpen();
                Console.WriteLine();

                // Parse section title from "--- Title ---" format
                var title = originalMessage.Trim().Trim('-', ' ');

                _currentSection = title;
                _sectionItemCount = 0;
                _sectionTimer.Restart();

                // Top border (console only)
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  ┌─");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" {title} ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var padLen = Math.Max(0, 60 - title.Length);
                Console.WriteLine(new string('─', padLen) + "┐");
                Console.ResetColor();

                // Send clean version to UI
                var uiText = (IncludeTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "") + originalMessage + Environment.NewLine;
                MessageLogged?.Invoke(uiText);
                MessageLoggedWithLevel?.Invoke(uiText, LogLevel.Info);
                return;
            }

            // Track items in current section
            if (_currentSection != null) _sectionItemCount++;

            // ── Build the formatted console line ───────────────────────────
            // Timestamp
            var ts = Timestamp();
            if (!string.IsNullOrEmpty(ts))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  {ts} ");
            }
            else
            {
                Console.Write("  ");
            }

            // Level indicator (thin colored bar)
            switch (level)
            {
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("│ ✖ ");
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("│ ⚠ ");
                    break;
                case LogLevel.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("│ · ");
                    break;
                default:
                    if (isSuccess)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("│ ✔ ");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("│   ");
                    }
                    break;
            }

            // Category tag with icon
            if (tag != null && _categories.TryGetValue(tag, out var cat))
            {
                Console.ForegroundColor = cat.Color;
                Console.Write($"{cat.Icon} {tag,-18} ");
            }
            else if (tag != null)
            {
                // Unknown tag — still render it neatly
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"  {tag,-18} ");
            }
            else
            {
                Console.Write(new string(' ', 21));
            }

            // Message body
            ConsoleColor bodyColor;
            if (isSuccess)
                bodyColor = ConsoleColor.Green;
            else if (level == LogLevel.Error)
                bodyColor = ConsoleColor.Red;
            else if (level == LogLevel.Warning)
                bodyColor = ConsoleColor.Yellow;
            else if (level == LogLevel.Debug)
                bodyColor = ConsoleColor.DarkGray;
            else if (overrideColor.HasValue && overrideColor != ConsoleColor.Gray)
                bodyColor = overrideColor.Value;
            else
                bodyColor = ConsoleColor.White;

            // Clean body: strip redundant symbols since we already show them in the level bar
            var displayBody = body;
            if (displayBody.StartsWith("✓ ")) displayBody = displayBody.Substring(2);
            if (displayBody.StartsWith("✗ ")) displayBody = displayBody.Substring(2);
            if (displayBody.StartsWith("⚠ ")) displayBody = displayBody.Substring(2);

            // Strip leading [Tag] from body if it duplicated the already-extracted tag
            if (tag != null)
            {
                var dupePrefix = $"[{tag}] ";
                if (displayBody.StartsWith(dupePrefix, StringComparison.OrdinalIgnoreCase))
                    displayBody = displayBody.Substring(dupePrefix.Length);
            }

            Console.ForegroundColor = bodyColor;
            Console.WriteLine(displayBody);
            Console.ResetColor();

            // ── Fire events with the original message for UI sink ────
            // UI has its own coloring logic — send the original unmodified message
            var fullFormatted = (IncludeTimestamp ? $"[{DateTime.Now:HH:mm:ss}] " : "") + originalMessage + Environment.NewLine;
            MessageLogged?.Invoke(fullFormatted);
            MessageLoggedWithLevel?.Invoke(fullFormatted, level);
        }

        /// <summary>Close current section with a summary footer.</summary>
        private static void CloseSectionIfOpen()
        {
            if (_currentSection == null) return;

            _sectionTimer.Stop();
            var elapsed = _sectionTimer.Elapsed;
            var summary = elapsed.TotalSeconds >= 1
                ? $"{elapsed.TotalSeconds:F1}s"
                : $"{elapsed.TotalMilliseconds:F0}ms";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  └── {_sectionItemCount} items · {summary} ──────────────────────────────────────────────┘");
            Console.ResetColor();

            // Don't send box-drawing footer to UI — it handles its own formatting

            _currentSection = null;
            _sectionItemCount = 0;
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Log with optional color hint (existing callers).</summary>
        public static void Log(string message, ConsoleColor? color = null)
        {
            lock (_sync)
            {
                if (ShouldThrottle(message, out var summary))
                    return;

                if (summary != null)
                    WriteThrottleSummary(summary);

                var level = DetectLevel(message, color);
                RenderLine(message, level, color);
            }
        }

        /// <summary>Log with explicit severity level.</summary>
        public static void Log(string message, LogLevel level)
        {
            ConsoleColor? color = level switch
            {
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Debug => ConsoleColor.DarkGray,
                _ => null
            };

            lock (_sync)
            {
                if (ShouldThrottle(message, out var summary))
                    return;

                if (summary != null)
                    WriteThrottleSummary(summary);

                RenderLine(message, level, color);
            }
        }

        /// <summary>Print an empty separator line.</summary>
        public static void LogPlain()
        {
            lock (_sync)
            {
                Console.WriteLine();
                MessageLogged?.Invoke(Environment.NewLine);
            }
        }

        /// <summary>
        /// Start a named section — draws a header box. 
        /// Automatically closes the previous section if one was open.
        /// </summary>
        public static void Section(string title)
        {
            lock (_sync)
            {
                CloseSectionIfOpen();
                Console.WriteLine();

                _currentSection = title;
                _sectionItemCount = 0;
                _sectionTimer.Restart();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  ┌─");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" {title} ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var padLen = Math.Max(0, 60 - title.Length);
                Console.WriteLine(new string('─', padLen) + "┐");
                Console.ResetColor();

                // Send clean section header to UI
                var uiText = $"── {title} ──" + Environment.NewLine;
                MessageLogged?.Invoke(uiText);
                MessageLoggedWithLevel?.Invoke(uiText, LogLevel.Info);
            }
        }

        /// <summary>
        /// End the current section with a summary footer.
        /// </summary>
        public static void EndSection()
        {
            lock (_sync)
            {
                CloseSectionIfOpen();
            }
        }

        /// <summary>
        /// Print a prominent banner (used for app startup, major state changes).
        /// </summary>
        public static void Banner(string title, string? subtitle = null)
        {
            lock (_sync)
            {
                CloseSectionIfOpen();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"  ╔{'═'.Repeat(64)}╗");
                Console.Write("  ║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(title.PadRight(63));
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("║");
                if (subtitle != null)
                {
                    Console.Write("  ║ ");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(subtitle.PadRight(63));
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("║");
                }
                Console.WriteLine($"  ╚{'═'.Repeat(64)}╝");
                Console.ResetColor();
                Console.WriteLine();

                // Send clean banner text to UI
                var uiText = $"═══ {title}" + (subtitle != null ? $" — {subtitle}" : "") + " ═══" + Environment.NewLine;
                MessageLogged?.Invoke(uiText);
                MessageLoggedWithLevel?.Invoke(uiText, LogLevel.Info);
            }
        }

        /// <summary>
        /// Log a success message (shorthand with green checkmark).
        /// </summary>
        public static void Success(string message)
        {
            Log($"✓ {message}", ConsoleColor.Green);
        }

        /// <summary>
        /// Log a warning message (shorthand).
        /// </summary>
        public static void Warn(string message)
        {
            Log(message, LogLevel.Warning);
        }

        /// <summary>
        /// Log an error message (shorthand).
        /// </summary>
        public static void Error(string message)
        {
            Log(message, LogLevel.Error);
        }

        /// <summary>
        /// Log a table of key-value pairs (used for config summaries, status displays).
        /// </summary>
        public static void Table(params (string Key, string Value)[] rows)
        {
            lock (_sync)
            {
                if (_currentSection != null) _sectionItemCount++;

                var ts = Timestamp();
                foreach (var (key, value) in rows)
                {
                    if (!string.IsNullOrEmpty(ts))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"  {ts} ");
                    }
                    else
                    {
                        Console.Write("  ");
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("│   ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"  {key,-20} ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(value);

                    var line = $"  {key,-20} {value}" + Environment.NewLine;
                    MessageLogged?.Invoke(line);
                    MessageLoggedWithLevel?.Invoke(line, LogLevel.Info);
                }
                Console.ResetColor();
            }
        }
    }

    /// <summary>Extension to repeat chars for box drawing.</summary>
    internal static class CharExtensions
    {
        public static string Repeat(this char c, int count) => new string(c, Math.Max(0, count));
    }
}
