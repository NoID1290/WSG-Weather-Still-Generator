using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EAS.AlertReady
{
    /// <summary>
    /// Settings for the Alert Ready (NAAD) CAP-CP feeds.
    /// </summary>
    public class AlertReadyOptions
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("GenerateVideoOnAlert")]
        public bool GenerateVideoOnAlert { get; set; } = true;

        /// <summary>List of CAP-CP feed URLs (Atom or raw CAP documents)</summary>
        [JsonPropertyName("FeedUrls")]
        public List<string>? FeedUrls { get; set; }

        [JsonPropertyName("IncludeTests")]
        public bool IncludeTests { get; set; } = false;

        /// <summary>Ignore alerts older than this many hours (0 disables filtering)</summary>
        [JsonPropertyName("MaxAgeHours")]
        public int MaxAgeHours { get; set; } = 24;

        /// <summary>Preferred CAP language code (e.g., en-CA, fr-CA)</summary>
        [JsonPropertyName("PreferredLanguage")]
        public string PreferredLanguage { get; set; } = "en-CA";

        /// <summary>Optional list of area names to keep (case-insensitive substring match)</summary>
        [JsonPropertyName("AreaFilters")]
        public List<string>? AreaFilters { get; set; }

        /// <summary>Jurisdictions to include (e.g., ["QC", "CA"]). Matches areaDesc/geocode/sender.</summary>
        [JsonPropertyName("Jurisdictions")]
        public List<string>? Jurisdictions { get; set; } = new List<string> { "QC", "CA" };

        /// <summary>If true, keep only high-risk alerts (Severe/Extreme).</summary>
        [JsonPropertyName("HighRiskOnly")]
        public bool HighRiskOnly { get; set; } = true;

        /// <summary>If true, exclude weather/meteorological alerts (handled by ECCC).</summary>
        [JsonPropertyName("ExcludeWeatherAlerts")]
        public bool ExcludeWeatherAlerts { get; set; } = true;

        /// <summary>TCP reconnection delay in seconds (default: 30)</summary>
        [JsonPropertyName("ReconnectDelaySeconds")]
        public int ReconnectDelaySeconds { get; set; } = 30;

        /// <summary>HTTP request timeout in seconds (default: 30)</summary>
        [JsonPropertyName("HttpTimeoutSeconds")]
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>Maximum number of seen identifiers to cache (prevents memory bloat)</summary>
        [JsonPropertyName("MaxCachedIdentifiers")]
        public int MaxCachedIdentifiers { get; set; } = 10000;

        /// <summary>Enable automatic generation of alert tone for broadcast-immediate alerts</summary>
        [JsonPropertyName("GenerateAlertTone")]
        public bool GenerateAlertTone { get; set; } = true;

        /// <summary>
        /// Gets the default NAAD TCP stream URLs for Alert Ready Canada.
        /// Primary and backup servers for redundancy.
        /// </summary>
        public static List<string> GetDefaultNaadUrls()
        {
            return new List<string>
            {
                "tcp://streaming1.naad-adna.pelmorex.com:8080",
                "tcp://streaming2.naad-adna.pelmorex.com:8080"
            };
        }

        /// <summary>
        /// Gets the default HTTP API URLs for Alert Ready Canada historical/current alerts.
        /// </summary>
        public static List<string> GetDefaultHttpUrls()
        {
            return new List<string>
            {
                "http://capcp1.naad-adna.pelmorex.com",
                "http://capcp2.naad-adna.pelmorex.com"
            };
        }
    }
}
