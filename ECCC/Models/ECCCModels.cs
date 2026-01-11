#nullable enable
using System;
using System.Collections.Generic;

namespace ECCC.Models
{
    /// <summary>
    /// Types of data that can be fetched from ECCC.
    /// </summary>
    public enum EcccDataType
    {
        /// <summary>City weather RSS feed</summary>
        CityWeather,
        /// <summary>Weather alerts by coordinates</summary>
        Alerts,
        /// <summary>WMS map layer (radar, temperature, etc.)</summary>
        WmsLayer,
        /// <summary>WMS GetCapabilities to discover layers</summary>
        Capabilities,
        /// <summary>Raw data from Datamart</summary>
        Datamart,
        /// <summary>Custom URL (for any ECCC endpoint)</summary>
        CustomUrl
    }

    /// <summary>
    /// Configuration for fetching ECCC data dynamically.
    /// </summary>
    public class EcccDataRequest
    {
        /// <summary>Type of data to fetch</summary>
        public EcccDataType DataType { get; set; }
        
        /// <summary>Language code: "e" for English, "f" for French</summary>
        public string Language { get; set; } = "f";
        
        // For CityWeather
        /// <summary>Province code (e.g., "qc", "on")</summary>
        public string? Province { get; set; }
        /// <summary>City code number (e.g., "147" for Montreal)</summary>
        public string? CityCode { get; set; }
        
        // For Alerts
        /// <summary>Latitude for coordinate-based requests</summary>
        public double Latitude { get; set; }
        /// <summary>Longitude for coordinate-based requests</summary>
        public double Longitude { get; set; }
        
        // For WMS
        /// <summary>WMS layer name</summary>
        public string? Layer { get; set; }
        /// <summary>Bounding box (MinLat, MinLon, MaxLat, MaxLon)</summary>
        public (double MinLat, double MinLon, double MaxLat, double MaxLon) BoundingBox { get; set; } = (45.0, -80.0, 53.0, -57.0);
        /// <summary>Image width in pixels</summary>
        public int Width { get; set; } = 1920;
        /// <summary>Image height in pixels</summary>
        public int Height { get; set; } = 1080;
        /// <summary>Image format (e.g., "image/png")</summary>
        public string Format { get; set; } = "image/png";
        /// <summary>ISO 8601 time for temporal layers</summary>
        public string? Time { get; set; }
        /// <summary>Coordinate reference system</summary>
        public string Crs { get; set; } = "EPSG:4326";
        
        // For Capabilities
        /// <summary>Service type: WMS, WCS, WFS</summary>
        public string Service { get; set; } = "WMS";
        
        // For Datamart
        /// <summary>Path within Datamart</summary>
        public string? DatamartPath { get; set; }
        
        // For CustomUrl
        /// <summary>Custom URL for direct access</summary>
        public string? CustomUrl { get; set; }
        
        /// <summary>Optional identifier/name for this request</summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Result from fetching ECCC data.
    /// </summary>
    public class EcccDataResult
    {
        /// <summary>The original request</summary>
        public EcccDataRequest? Request { get; set; }
        /// <summary>Whether the request succeeded</summary>
        public bool Success { get; set; }
        /// <summary>The URL that was fetched</summary>
        public string? Url { get; set; }
        /// <summary>HTTP status code</summary>
        public int StatusCode { get; set; }
        /// <summary>Content type of the response</summary>
        public string? ContentType { get; set; }
        /// <summary>Text data (for XML, JSON, etc.)</summary>
        public string? TextData { get; set; }
        /// <summary>Binary data (for images)</summary>
        public byte[]? BinaryData { get; set; }
        /// <summary>Parsed XML document (if applicable)</summary>
        public System.Xml.Linq.XDocument? XmlDocument { get; set; }
        /// <summary>Error message if failed</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Information about a Canadian city with ECCC data availability.
    /// </summary>
    public class CityInfo
    {
        public string Name { get; set; }
        public string Province { get; set; }
        public string CityCode { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        
        /// <summary>The parent/reference city for weather data (for small cities)</summary>
        public string? ParentCityCode { get; set; }
        
        /// <summary>Distance from parent city in km</summary>
        public double? DistanceFromParent { get; set; }
        
        public CityInfo(string name, string province, string cityCode, double latitude, double longitude)
        {
            Name = name;
            Province = province;
            CityCode = cityCode;
            Latitude = latitude;
            Longitude = longitude;
        }
        
        /// <summary>Gets the ECCC city weather feed URL if available</summary>
        public string? GetCityFeedUrl(string language = "f")
        {
            // If cityCode is "coord" or empty, no city feed available
            if (CityCode == "coord" || string.IsNullOrEmpty(CityCode))
                return null;
                
            return $"https://weather.gc.ca/rss/city/{Province.ToLower()}-{CityCode}_{language}.xml";
        }
        
        /// <summary>Gets the ECCC alerts feed URL based on coordinates</summary>
        public string GetAlertsFeedUrl(string language = "f") 
            => $"https://weather.gc.ca/rss/alerts/{Latitude:F3}_{Longitude:F3}_{language}.xml";
        
        /// <summary>Indicates if this city uses coordinate-based feed only (no city-specific RSS)</summary>
        public bool IsCoordinateBased => CityCode == "coord" || string.IsNullOrEmpty(CityCode);
        
        /// <summary>Indicates if weather data should be fetched from a parent city</summary>
        public bool HasParentCity => !string.IsNullOrEmpty(ParentCityCode);
        
        public override string ToString() => Name;
    }

    /// <summary>
    /// Weather observation from ECCC stations.
    /// </summary>
    public class WeatherObservation
    {
        public string? StationName { get; set; }
        public DateTime? ObservationTime { get; set; }
        public double? Temperature { get; set; }
        public double? Humidity { get; set; }
        public double? WindSpeed { get; set; }
        public string? WindDirection { get; set; }
        public double? Pressure { get; set; }
        public string? Condition { get; set; }
        public int? WeatherCode { get; set; }
        public double? Visibility { get; set; }
        public double? DewPoint { get; set; }
        public double? WindChill { get; set; }
        public double? Humidex { get; set; }
    }

    /// <summary>
    /// Daily forecast from ECCC.
    /// </summary>
    public class DailyForecast
    {
        public string? Date { get; set; }
        public string? Period { get; set; }
        public double? HighTemperature { get; set; }
        public double? LowTemperature { get; set; }
        public string? Condition { get; set; }
        public int? WeatherCode { get; set; }
        public double? PrecipitationProbability { get; set; }
        public string? Summary { get; set; }
    }

    /// <summary>
    /// Complete weather data from ECCC for a location.
    /// </summary>
    public class EcccWeatherData
    {
        public CityInfo? City { get; set; }
        public WeatherObservation? Current { get; set; }
        public List<DailyForecast> DailyForecasts { get; set; } = new();
        public DateTime? FetchTime { get; set; }
        public string? SourceUrl { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        
        /// <summary>Indicates if data was fetched from a nearby reference city</summary>
        public bool IsFromReferenceCity { get; set; }
        public string? ReferenceCityName { get; set; }
    }

    /// <summary>
    /// Settings for ECCC API operations.
    /// </summary>
    public class EcccSettings
    {
        public Dictionary<string, string>? CityFeeds { get; set; }
        public Dictionary<string, string>? RadarFeeds { get; set; }
        public int DelayBetweenRequestsMs { get; set; } = 200;
        public bool EnableCityRadar { get; set; } = false;
        public bool EnableProvinceRadar { get; set; } = true;
        public bool UseGeoMetWms { get; set; } = true;
        public string? UserAgent { get; set; }
        public string? ProvinceAnimationUrl { get; set; }
        public string? ProvinceRadarUrl { get; set; }
        public int ProvinceFrames { get; set; } = 8;
        public int ProvinceImageWidth { get; set; } = 1920;
        public int ProvinceImageHeight { get; set; } = 1080;
        public double ProvincePaddingDegrees { get; set; } = 0.5;
        public string[]? ProvinceEnsureCities { get; set; }
        public int ProvinceFrameStepMinutes { get; set; } = 6;
        public string DefaultLanguage { get; set; } = "f";
        public string DefaultProvince { get; set; } = "qc";
        
        /// <summary>Maximum distance in km to search for a reference city</summary>
        public double MaxReferenceCityDistanceKm { get; set; } = 50.0;
    }
}
