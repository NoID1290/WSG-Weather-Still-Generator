#nullable enable

namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Defines the available weather API providers for fetching weather data
    /// </summary>
    public enum WeatherApiType
    {
        /// <summary>
        /// Open-Meteo free weather API (default)
        /// </summary>
        OpenMeteo = 0,

        /// <summary>
        /// Environment and Climate Change Canada (ECCC) weather service
        /// </summary>
        ECCC = 1
    }
}
