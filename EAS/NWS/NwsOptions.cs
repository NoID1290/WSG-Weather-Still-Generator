using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EAS.NWS
{
    /// <summary>
    /// Settings for the NWS (US National Weather Service) CAP feed provider.
    /// </summary>
    public class NwsOptions
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>List of NWS CAP/Atom feed URLs</summary>
        [JsonPropertyName("FeedUrls")]
        public List<string>? FeedUrls { get; set; }

        /// <summary>HTTP request timeout in seconds (default: 30)</summary>
        [JsonPropertyName("HttpTimeoutSeconds")]
        public int HttpTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Gets the default NWS CAP feed URLs.
        /// </summary>
        public static List<string> GetDefaultFeedUrls()
        {
            return new List<string>
            {
                "https://alerts.weather.gov/cap/us.php?x=0",
                "https://alerts.weather.gov/cap/wwaatmget.php?x=1"
            };
        }
    }
}
