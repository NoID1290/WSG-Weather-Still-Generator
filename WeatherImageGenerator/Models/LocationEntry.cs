#nullable enable

using System.Text.Json.Serialization;

namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Represents a location entry with its name and preferred weather API
    /// </summary>
    public class LocationEntry
    {
        /// <summary>
        /// The name of the location (e.g., "Montréal", "Québec")
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The weather API to use for fetching data for this location
        /// </summary>
        [JsonPropertyName("api")]
        public WeatherApiType Api { get; set; } = WeatherApiType.OpenMeteo;

        /// <summary>
        /// Creates a new empty LocationEntry
        /// </summary>
        public LocationEntry() { }

        /// <summary>
        /// Creates a new LocationEntry with the specified name and API
        /// </summary>
        public LocationEntry(string name, WeatherApiType api = WeatherApiType.OpenMeteo)
        {
            Name = name;
            Api = api;
        }

        /// <summary>
        /// Returns a display string for the location
        /// </summary>
        public override string ToString()
        {
            return $"{Name} ({Api})";
        }

        /// <summary>
        /// Creates a LocationEntry from a simple location name string (for backward compatibility)
        /// </summary>
        public static LocationEntry FromString(string locationName)
        {
            return new LocationEntry(locationName, WeatherApiType.OpenMeteo);
        }
    }
}
