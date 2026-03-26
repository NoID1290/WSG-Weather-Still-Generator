#nullable enable
using System.Text.Json.Serialization;

namespace BZTG.Models
{
    /// <summary>
    /// A single lightning strike record from a Blitzortung archive minute-file.
    /// Fields use lowercase names to match the NDJSON wire format directly.
    /// </summary>
    internal sealed class BlitzortungStrike
    {
        /// <summary>Unix timestamp in microseconds since epoch.</summary>
        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>Latitude in decimal degrees.</summary>
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        /// <summary>Longitude in decimal degrees.</summary>
        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        /// <summary>Polarity: positive or negative value.</summary>
        [JsonPropertyName("pol")]
        public int Pol { get; set; }

        /// <summary>Number of detecting stations.</summary>
        [JsonPropertyName("mds")]
        public int Mds { get; set; }

        /// <summary>
        /// Cloud-to-ground indicator.
        /// > 0 = cloud-to-ground; 0 = in-cloud or unknown.
        /// </summary>
        [JsonPropertyName("mcg")]
        public int Mcg { get; set; }
    }
}
