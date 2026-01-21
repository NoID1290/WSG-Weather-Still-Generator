#nullable enable
using System;
using System.Net.Http;
using System.Threading.Tasks;
using ECCC.Data;
using ECCC.Models;
using ECCC.Services;

namespace ECCC
{
    /// <summary>
    /// Main entry point for ECCC weather API.
    /// Provides simple methods to fetch weather data for Canadian locations.
    /// </summary>
    public static class ECCCApi
    {
        /// <summary>Optional logging callback for diagnostic messages</summary>
        public static Action<string>? Log { get; set; }
        
        private static void LogMessage(string message)
        {
            Log?.Invoke(message);
        }

        /// <summary>
        /// Fetches weather data for a city by name and converts to OpenMeteo format.
        /// Automatically handles small towns by finding nearby reference cities.
        /// </summary>
        /// <param name="httpClient">HttpClient instance</param>
        /// <param name="cityName">City name (e.g., "Montreal", "Delson", "Amos")</param>
        /// <param name="settings">Optional ECCC settings</param>
        /// <returns>Weather forecast in OpenMeteo format, or null if failed</returns>
        public static async Task<OpenMeteo.WeatherForecast?> GetWeatherAsync(
            HttpClient httpClient, 
            string cityName, 
            EcccSettings? settings = null)
        {
            try
            {
                LogMessage($"[ECCC API] Fetching weather for {cityName}...");
                
                var client = new ECCCClient(httpClient, settings);
                var ecccData = await client.GetWeatherByNameAsync(cityName);
                
                if (ecccData == null || !ecccData.Success)
                {
                    LogMessage($"[ECCC API] Failed to fetch weather: {ecccData?.ErrorMessage ?? "Unknown error"}");
                    return null;
                }
                
                var sourceDesc = OpenMeteoConverter.GetSourceDescription(ecccData);
                var tempStr = ecccData.Current?.Temperature.HasValue == true 
                    ? $"{ecccData.Current.Temperature.Value}°C" 
                    : "no temp parsed";
                LogMessage($"[ECCC API] ✓ Fetched weather from {sourceDesc} ({tempStr})");
                
                return OpenMeteoConverter.ToOpenMeteoFormat(ecccData);
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetches weather data for a location by coordinates.
        /// Finds the nearest ECCC weather station within the specified distance.
        /// </summary>
        /// <param name="httpClient">HttpClient instance</param>
        /// <param name="locationName">Location name for display</param>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="settings">Optional ECCC settings</param>
        /// <returns>Weather forecast in OpenMeteo format, or null if failed</returns>
        public static async Task<OpenMeteo.WeatherForecast?> GetWeatherByCoordinatesAsync(
            HttpClient httpClient,
            string locationName,
            double latitude,
            double longitude,
            EcccSettings? settings = null)
        {
            try
            {
                LogMessage($"[ECCC API] Fetching weather for {locationName} ({latitude}, {longitude})...");
                
                var client = new ECCCClient(httpClient, settings);
                var ecccData = await client.GetWeatherByCoordinatesAsync(locationName, latitude, longitude);
                
                if (ecccData == null || !ecccData.Success)
                {
                    LogMessage($"[ECCC API] Failed to fetch weather: {ecccData?.ErrorMessage ?? "Unknown error"}");
                    return null;
                }
                
                var sourceDesc = OpenMeteoConverter.GetSourceDescription(ecccData);
                LogMessage($"[ECCC API] ✓ Fetched weather from {sourceDesc}");
                
                return OpenMeteoConverter.ToOpenMeteoFormat(ecccData);
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Searches for cities in the ECCC database.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="maxResults">Maximum number of results</param>
        /// <returns>List of matching cities</returns>
        public static System.Collections.Generic.List<CityInfo> SearchCities(string query, int maxResults = 10)
        {
            return CityDatabase.SearchCities(query, maxResults);
        }

        /// <summary>
        /// Gets a city by exact name from the ECCC database.
        /// </summary>
        /// <param name="cityName">City name</param>
        /// <returns>City info, or null if not found</returns>
        public static CityInfo? GetCity(string cityName)
        {
            return CityDatabase.GetCityByName(cityName);
        }

        /// <summary>
        /// Finds the nearest ECCC city with weather data for given coordinates.
        /// </summary>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="maxDistanceKm">Maximum search distance in kilometers</param>
        /// <returns>Nearest city info, or null if none found</returns>
        public static CityInfo? FindNearestCity(double latitude, double longitude, double maxDistanceKm = 100)
        {
            return CityDatabase.FindNearestCityWithWeatherData(latitude, longitude, maxDistanceKm);
        }

        /// <summary>
        /// Calculates the distance between two coordinates in kilometers.
        /// </summary>
        public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            return CityDatabase.CalculateDistanceKm(lat1, lon1, lat2, lon2);
        }

        /// <summary>
        /// Fetches a radar image for the specified location coordinates.
        /// </summary>
        /// <param name="httpClient">HttpClient instance</param>
        /// <param name="latitude">Location latitude</param>
        /// <param name="longitude">Location longitude</param>
        /// <param name="radiusKm">Radius in kilometers for the bounding box (default: 100km)</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <returns>Radar image data or null if failed</returns>
        public static async Task<byte[]?> GetRadarImageAsync(
            System.Net.Http.HttpClient httpClient,
            double latitude,
            double longitude,
            double radiusKm = 100,
            int width = 800,
            int height = 600)
        {
            try
            {
                LogMessage($"[ECCC API] Fetching radar image for ({latitude}, {longitude})...");
                
                var radarService = new RadarImageService(httpClient);
                var imageData = await radarService.FetchRadarImageAsync(
                    latitude, longitude, radiusKm, width, height);
                
                if (imageData != null && imageData.Length > 0)
                {
                    LogMessage($"[ECCC API] ✓ Radar image fetched successfully ({imageData.Length} bytes)");
                }
                else
                {
                    LogMessage("[ECCC API] Failed to fetch radar image");
                }
                
                return imageData;
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error fetching radar: {ex.Message}");
                return null;
            }
        }
    }
}
