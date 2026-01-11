#nullable enable
using System;

namespace ECCC.Api
{
    /// <summary>
    /// Provides URL builders for all ECCC endpoints.
    /// </summary>
    public static class UrlBuilder
    {
        #region Base URLs
        
        /// <summary>Base URL for ECCC weather RSS feeds</summary>
        public const string BaseWeatherUrl = "https://weather.gc.ca";
        
        /// <summary>Base URL for ECCC official OGC API (JSON weather data)</summary>
        public const string BaseApiUrl = "https://api.weather.gc.ca";
        
        /// <summary>Base URL for ECCC GeoMet WMS service</summary>
        public const string BaseGeoMetUrl = "https://geo.weather.gc.ca/geomet";
        
        /// <summary>Base URL for ECCC Datamart (raw data files)</summary>
        public const string BaseDatamartUrl = "https://dd.weather.gc.ca";
        
        #endregion

        #region City Weather RSS URLs
        
        /// <summary>
        /// Builds a city weather RSS feed URL.
        /// </summary>
        /// <param name="province">Province code (e.g., "qc", "on", "bc")</param>
        /// <param name="cityCode">City code number (e.g., "147" for Montreal)</param>
        /// <param name="language">Language code: "e" for English, "f" for French</param>
        public static string BuildCityWeatherUrl(string province, string cityCode, string language = "f")
            => $"{BaseWeatherUrl}/rss/city/{province.ToLower()}-{cityCode}_{language}.xml";
        
        /// <summary>
        /// Builds a city weather RSS feed URL from a combined city ID.
        /// </summary>
        /// <param name="cityId">Combined city ID (e.g., "qc-147")</param>
        /// <param name="language">Language code: "e" for English, "f" for French</param>
        public static string BuildCityWeatherUrlFromId(string cityId, string language = "f")
        {
            var parts = cityId.Split('-');
            if (parts.Length != 2)
                throw new ArgumentException($"Invalid city ID format: {cityId}. Expected format: 'province-code' (e.g., 'qc-147')");
            return BuildCityWeatherUrl(parts[0], parts[1], language);
        }
        
        #endregion

        #region Alerts RSS URLs
        
        /// <summary>
        /// Builds an alerts RSS feed URL by coordinates.
        /// </summary>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        /// <param name="language">Language code: "e" for English, "f" for French</param>
        public static string BuildAlertsUrl(double latitude, double longitude, string language = "f")
            => $"{BaseWeatherUrl}/rss/alerts/{latitude:F3}_{longitude:F3}_{language}.xml";
        
        #endregion

        #region OGC API URLs
        
        /// <summary>
        /// Builds the URL to query the ECCC OGC API for city weather data.
        /// </summary>
        /// <param name="cityId">City identifier (e.g., "qc-147")</param>
        public static string BuildApiCityWeatherUrl(string cityId)
            => $"{BaseApiUrl}/collections/citypageweather-realtime/items?f=json&properties=identifier&identifier={cityId}&limit=1";
        
        /// <summary>
        /// Builds the URL to list all cities in the ECCC API.
        /// </summary>
        /// <param name="limit">Maximum number of results</param>
        public static string BuildApiCityListUrl(int limit = 1000)
            => $"{BaseApiUrl}/collections/citypageweather-realtime/items?f=json&limit={limit}";
        
        /// <summary>
        /// Builds the URL for ECCC weather station observations.
        /// </summary>
        /// <param name="stationId">Weather station ID</param>
        public static string BuildStationObservationsUrl(string stationId)
            => $"{BaseApiUrl}/collections/swob-realtime/items?IATA_ID={stationId}&f=json&sortby=-resultTime&limit=1";
        
        #endregion

        #region WMS (GeoMet) URLs
        
        /// <summary>
        /// Builds a WMS GetMap URL for any GeoMet layer.
        /// </summary>
        /// <param name="layer">WMS layer name</param>
        /// <param name="bbox">Bounding box as (minLat, minLon, maxLat, maxLon)</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <param name="format">Image format (e.g., "image/png", "image/gif")</param>
        /// <param name="time">Optional ISO 8601 time for temporal layers</param>
        /// <param name="crs">Coordinate reference system (default: EPSG:4326)</param>
        public static string BuildWmsUrl(
            string layer,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width = 1920,
            int height = 1080,
            string format = "image/png",
            string? time = null,
            string crs = "EPSG:4326")
        {
            var url = $"{BaseGeoMetUrl}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap" +
                      $"&LAYERS={Uri.EscapeDataString(layer)}" +
                      $"&CRS={crs}" +
                      $"&BBOX={bbox.MinLat},{bbox.MinLon},{bbox.MaxLat},{bbox.MaxLon}" +
                      $"&WIDTH={width}&HEIGHT={height}" +
                      $"&FORMAT={Uri.EscapeDataString(format)}";
            
            if (!string.IsNullOrEmpty(time))
                url += $"&TIME={Uri.EscapeDataString(time)}";
            
            return url;
        }
        
        /// <summary>
        /// Builds a WMS GetCapabilities URL to discover available layers.
        /// </summary>
        /// <param name="service">Service type: "WMS", "WCS", "WFS"</param>
        public static string BuildCapabilitiesUrl(string service = "WMS")
            => $"{BaseGeoMetUrl}?SERVICE={service}&VERSION=1.3.0&REQUEST=GetCapabilities";
        
        #endregion

        #region Datamart URLs
        
        /// <summary>
        /// Builds a Datamart URL for raw data files.
        /// </summary>
        /// <param name="path">Path within Datamart</param>
        public static string BuildDatamartUrl(string path)
            => $"{BaseDatamartUrl}/{path.TrimStart('/')}";
        
        #endregion
    }

    /// <summary>
    /// Common ECCC GeoMet WMS layer names.
    /// </summary>
    public static class WmsLayers
    {
        // Radar layers
        public const string Radar1KmRain = "RADAR_1KM_RRAI";
        public const string Radar1KmSnow = "RADAR_1KM_RSNO";
        public const string RadarCoverage = "RADAR_COVERAGE_RRAI.INV";
        
        // GDPS (Global Deterministic Prediction System) layers
        public const string GdpsTemperature = "GDPS.ETA_TT";
        public const string GdpsPrecipitation = "GDPS.ETA_PR";
        public const string GdpsWindSpeed = "GDPS.ETA_WSPD";
        public const string GdpsCloudCover = "GDPS.ETA_NT";
        public const string GdpsHumidity = "GDPS.ETA_HR";
        
        // HRDPS (High Resolution) layers
        public const string HrdpsTemperature = "HRDPS.CONTINENTAL_TT";
        public const string HrdpsPrecipitation = "HRDPS.CONTINENTAL_PR";
        public const string HrdpsWindSpeed = "HRDPS.CONTINENTAL_WSPD";
        
        // RDPS (Regional) layers
        public const string RdpsTemperature = "RDPS.ETA_TT";
        public const string RdpsPrecipitation = "RDPS.ETA_PR";
        
        // Alerts layer
        public const string WeatherAlerts = "ALERTS";
        
        // Satellite layers
        public const string SatelliteVisible = "GOES-East_1km_DayVis";
        public const string SatelliteInfrared = "GOES-East_2km_Infrared";
    }

    /// <summary>
    /// Canadian province codes for ECCC.
    /// </summary>
    public static class Provinces
    {
        public const string Alberta = "ab";
        public const string BritishColumbia = "bc";
        public const string Manitoba = "mb";
        public const string NewBrunswick = "nb";
        public const string NewfoundlandLabrador = "nl";
        public const string NorthwestTerritories = "nt";
        public const string NovaScotia = "ns";
        public const string Nunavut = "nu";
        public const string Ontario = "on";
        public const string PrinceEdwardIsland = "pe";
        public const string Quebec = "qc";
        public const string Saskatchewan = "sk";
        public const string Yukon = "yt";
    }
}
