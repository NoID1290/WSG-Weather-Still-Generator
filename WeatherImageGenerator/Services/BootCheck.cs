#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Represents the result of a single boot check.
    /// </summary>
    public class BootCheckResult
    {
        public string Name { get; set; } = "";
        public BootCheckStatus Status { get; set; } = BootCheckStatus.Pending;
        public string StatusMessage { get; set; } = "";
        public string? Detail { get; set; }
        public Exception? Error { get; set; }
    }

    public enum BootCheckStatus
    {
        Pending,
        Running,
        Passed,
        Repaired,
        Warning,
        Failed,
        Skipped
    }

    /// <summary>
    /// Base class for all boot-time checks.
    /// </summary>
    public abstract class BootCheck
    {
        /// <summary>Display name shown in the boot screen.</summary>
        public abstract string Name { get; }

        /// <summary>Short description of what this check does.</summary>
        public abstract string Description { get; }

        /// <summary>
        /// Execute the check. Return a result with status and optional detail.
        /// </summary>
        public abstract Task<BootCheckResult> RunAsync(CancellationToken ct);
    }
}
