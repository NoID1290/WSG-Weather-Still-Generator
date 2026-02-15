using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EAS.NWS
{
    /// <summary>
    /// Settings for the NWS (US National Weather Service) alert provider.
    /// Supports the modern api.weather.gov JSON API and legacy CAP/Atom feeds.
    /// </summary>
    public class NwsOptions
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Primary NWS API base URL (api.weather.gov).
        /// Set to empty/null to skip the modern API and use only legacy CAP feeds.
        /// </summary>
        [JsonPropertyName("ApiBaseUrl")]
        public string ApiBaseUrl { get; set; } = "https://api.weather.gov";

        /// <summary>
        /// Optional legacy CAP/Atom feed URLs (fallback if the modern API is unavailable).
        /// </summary>
        [JsonPropertyName("FeedUrls")]
        public List<string>? FeedUrls { get; set; }

        /// <summary>
        /// Two-letter US state abbreviations to filter alerts (e.g. "IL", "CA", "TX").
        /// When empty, no state filter is applied (all US alerts are returned from the API).
        /// </summary>
        [JsonPropertyName("States")]
        public List<string>? States { get; set; }

        /// <summary>
        /// NWS forecast zone IDs to filter alerts (e.g. "ILZ014", "CAZ006").
        /// </summary>
        [JsonPropertyName("Zones")]
        public List<string>? Zones { get; set; }

        /// <summary>
        /// Filter alerts to a specific geographic point (latitude, longitude).
        /// Format: "lat,lon" e.g. "41.88,-87.63" for Chicago.
        /// </summary>
        [JsonPropertyName("Point")]
        public string? Point { get; set; }

        /// <summary>HTTP request timeout in seconds (default: 30)</summary>
        [JsonPropertyName("HttpTimeoutSeconds")]
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>Maximum alert age in hours (default: 24). Alerts older than this are discarded.</summary>
        [JsonPropertyName("MaxAgeHours")]
        public int MaxAgeHours { get; set; } = 24;

        /// <summary>Only show Extreme/Severe severity alerts with Immediate/Expected urgency.</summary>
        [JsonPropertyName("HighRiskOnly")]
        public bool HighRiskOnly { get; set; } = false;

        /// <summary>
        /// Severity levels to include (default: all). Values: Extreme, Severe, Moderate, Minor, Unknown.
        /// </summary>
        [JsonPropertyName("SeverityFilter")]
        public List<string>? SeverityFilter { get; set; }

        /// <summary>
        /// User-Agent string sent with NWS API requests (required by api.weather.gov).
        /// Should identify your application, e.g. "MyWeatherApp/1.0 (contact@example.com)".
        /// </summary>
        [JsonPropertyName("UserAgent")]
        public string UserAgent { get; set; } = "WSG-Weather-Still-Generator/1.0";

        /// <summary>
        /// Background polling interval in minutes for real-time NWS alert monitoring.
        /// Only used when NWS is enabled and background polling is active in MainForm.
        /// Default: 3 minutes (NWS API rate limits are generous with proper User-Agent).
        /// </summary>
        [JsonPropertyName("PollingIntervalMinutes")]
        public int PollingIntervalMinutes { get; set; } = 3;

        /// <summary>
        /// Gets the default NWS CAP feed URLs (legacy fallback).
        /// </summary>
        public static List<string> GetDefaultFeedUrls()
        {
            return new List<string>
            {
                "https://api.weather.gov/alerts/active?status=actual"
            };
        }
    }
}
