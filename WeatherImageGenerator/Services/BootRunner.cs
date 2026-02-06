#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Orchestrates all boot checks in sequence and reports progress.
    /// </summary>
    public class BootRunner
    {
        private readonly List<BootCheck> _checks = new();
        private readonly List<BootCheckResult> _results = new();

        /// <summary>Fired when a check starts. Args: (index, total, checkName)</summary>
        public event Action<int, int, string>? CheckStarted;

        /// <summary>Fired when a check completes. Args: (index, total, result)</summary>
        public event Action<int, int, BootCheckResult>? CheckCompleted;

        /// <summary>Fired when all checks are done. Args: (results)</summary>
        public event Action<List<BootCheckResult>>? AllCompleted;

        /// <summary>All results after the run completes.</summary>
        public IReadOnlyList<BootCheckResult> Results => _results.AsReadOnly();

        /// <summary>True if all checks passed, were repaired, or had warnings (no failures).</summary>
        public bool AllPassed
        {
            get
            {
                foreach (var r in _results)
                {
                    if (r.Status == BootCheckStatus.Failed)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Registers a check to be run during boot.
        /// </summary>
        public void Add(BootCheck check)
        {
            _checks.Add(check);
        }

        /// <summary>
        /// Runs all registered checks in order.
        /// </summary>
        public async Task RunAllAsync(CancellationToken ct = default)
        {
            _results.Clear();
            int total = _checks.Count;

            for (int i = 0; i < total; i++)
            {
                var check = _checks[i];
                CheckStarted?.Invoke(i, total, check.Name);

                BootCheckResult result;
                try
                {
                    result = await check.RunAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    result = new BootCheckResult
                    {
                        Name = check.Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Cancelled"
                    };
                }
                catch (Exception ex)
                {
                    result = new BootCheckResult
                    {
                        Name = check.Name,
                        Status = BootCheckStatus.Failed,
                        StatusMessage = $"Unexpected error: {ex.Message}",
                        Error = ex
                    };
                    Logger.Log($"[Boot] {check.Name}: FAILED — {ex.Message}", ConsoleColor.Red);
                }

                _results.Add(result);
                CheckCompleted?.Invoke(i, total, result);

                // Log the result
                var symbol = result.Status switch
                {
                    BootCheckStatus.Passed => "✓",
                    BootCheckStatus.Repaired => "🔧",
                    BootCheckStatus.Warning => "⚠",
                    BootCheckStatus.Failed => "✗",
                    BootCheckStatus.Skipped => "⊘",
                    _ => "?"
                };
                var color = result.Status switch
                {
                    BootCheckStatus.Passed => ConsoleColor.Green,
                    BootCheckStatus.Repaired => ConsoleColor.Cyan,
                    BootCheckStatus.Warning => ConsoleColor.Yellow,
                    BootCheckStatus.Failed => ConsoleColor.Red,
                    _ => ConsoleColor.Gray
                };
                Logger.Log($"[Boot] {symbol} {result.Name}: {result.StatusMessage}", color);
            }

            AllCompleted?.Invoke(_results);
        }
    }
}
