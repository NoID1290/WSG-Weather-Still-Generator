using System;

namespace EAS
{
    /// <summary>
    /// Connection status for NAAD TCP streams
    /// </summary>
    public enum NaadConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Event args for heartbeat received
    /// </summary>
    public class HeartbeatEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public int ReferencedAlertCount { get; set; }
    }

    /// <summary>
    /// Event args for connection status changes
    /// </summary>
    public class ConnectionStatusEventArgs : EventArgs
    {
        public NaadConnectionStatus Status { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Event args for alert received from stream
    /// </summary>
    public class AlertReceivedEventArgs : EventArgs
    {
        public WeatherImageGenerator.Models.AlertEntry? Alert { get; set; }
        public int TotalActiveAlerts { get; set; }
    }

    /// <summary>
    /// Connection health statistics for monitoring the NAAD stream.
    /// </summary>
    public class ConnectionHealthStats
    {
        public NaadConnectionStatus Status { get; set; }
        public DateTimeOffset? LastHeartbeat { get; set; }
        public TimeSpan TimeSinceLastHeartbeat { get; set; }
        public bool IsHealthy { get; set; }
        public int ActiveAlertCount { get; set; }
        public int CachedIdentifierCount { get; set; }
        public int StreamTasksRunning { get; set; }

        public override string ToString()
        {
            var heartbeatStr = LastHeartbeat.HasValue
                ? $"{TimeSinceLastHeartbeat.TotalSeconds:F1}s ago"
                : "Never";
            return $"Status: {Status}, Healthy: {IsHealthy}, Heartbeat: {heartbeatStr}, " +
                   $"Alerts: {ActiveAlertCount}, Cached IDs: {CachedIdentifierCount}, Streams: {StreamTasksRunning}";
        }
    }
}