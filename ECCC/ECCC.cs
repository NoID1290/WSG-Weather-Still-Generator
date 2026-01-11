using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Globalization;
using System.Text;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services
{
    // Minimal, well-typed ECCC library implementation (extracted).
    public static class ECCC
    {
        #region ECCC Base URLs and Endpoints
        
        /// <summary>Base URL for ECCC weather RSS feeds</summary>
        public const string BaseWeatherUrl = "https://weather.gc.ca";
        
        /// <summary>Base URL for ECCC GeoMet WMS service</summary>
        public const string BaseGeoMetUrl = "https://geo.weather.gc.ca/geomet";
        
        /// <summary>Base URL for ECCC Datamart (raw data files)</summary>
        public const string BaseDatamartUrl = "https://dd.weather.gc.ca";
        
        #endregion
        
        #region Dynamic URL Builders
        
        /// <summary>
        /// Builds a dynamic RSS feed URL for city weather data.
        /// </summary>
        /// <param name="province">Province code (e.g., "qc", "on", "bc")</param>
        /// <param name="cityCode">City code number (e.g., "147" for Montreal)</param>
        /// <param name="language">Language code: "e" for English, "f" for French</param>
        /// <returns>The constructed RSS feed URL</returns>
        public static string BuildCityWeatherUrl(string province, string cityCode, string language = "f")
            => $"{BaseWeatherUrl}/rss/city/{province.ToLower()}-{cityCode}_{language}.xml";
        
        /// <summary>
        /// Builds a dynamic RSS feed URL for weather alerts by coordinates.
        /// </summary>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        /// <param name="language">Language code: "e" for English, "f" for French</param>
        /// <returns>The constructed alerts RSS feed URL</returns>
        public static string BuildAlertsUrl(double latitude, double longitude, string language = "f")
            => $"{BaseWeatherUrl}/rss/alerts/{latitude:F3}_{longitude:F3}_{language}.xml";
        
        /// <summary>
        /// Builds a dynamic WMS URL for any GeoMet layer.
        /// </summary>
        /// <param name="layer">WMS layer name (e.g., "RADAR_1KM_RRAI", "GDPS.ETA_TT", "HRDPS.CONTINENTAL_TT")</param>
        /// <param name="bbox">Bounding box as (minLat, minLon, maxLat, maxLon)</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <param name="format">Image format (e.g., "image/png", "image/gif")</param>
        /// <param name="time">Optional ISO 8601 time for temporal layers</param>
        /// <param name="crs">Coordinate reference system (default: EPSG:4326)</param>
        /// <returns>The constructed WMS GetMap URL</returns>
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
        /// <returns>The GetCapabilities URL</returns>
        public static string BuildCapabilitiesUrl(string service = "WMS")
            => $"{BaseGeoMetUrl}?SERVICE={service}&VERSION=1.3.0&REQUEST=GetCapabilities";
        
        /// <summary>
        /// Builds a Datamart URL for raw data files.
        /// </summary>
        /// <param name="path">Path within Datamart (e.g., "model_gem_global/25km/grib2/lat_lon/00/003")</param>
        /// <returns>The constructed Datamart URL</returns>
        public static string BuildDatamartUrl(string path)
            => $"{BaseDatamartUrl}/{path.TrimStart('/')}";
        
        /// <summary>
        /// Parses an ECCC feed URL into a structured data request.
        /// Supports City Weather and Alerts RSS feeds.
        /// </summary>
        /// <param name="url">The full ECCC feed URL</param>
        /// <returns>An EcccDataRequest with the extracted parameters</returns>
        public static EcccDataRequest ParseFeedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid URL format", nameof(url));

            var path = uri.AbsolutePath.TrimStart('/');
            // Format: rss/city/qc-147_f.xml or rss/alerts/45.500_-73.567_f.xml
            
            var segments = path.Split('/');
            if (segments.Length < 3 || segments[0] != "rss")
                throw new ArgumentException("Not a valid ECCC RSS feed URL", nameof(url));

            var type = segments[1];
            var filename = segments[2];
            
            // Remove extension
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
            
            // Extract language (last part after underscore)
            var parts = nameWithoutExt.Split('_');
            if (parts.Length < 2)
                throw new ArgumentException("Invalid filename format (missing language)", nameof(url));
                
            var lang = parts.Last();
            var dataPart = string.Join("_", parts.Take(parts.Length - 1));

            if (type == "city")
            {
                // Format: pp-cityCode (e.g., qc-147)
                var codeParts = dataPart.Split('-');
                if (codeParts.Length < 2)
                    throw new ArgumentException("Invalid city code format", nameof(url));
                    
                return new EcccDataRequest
                {
                    DataType = EcccDataType.CityWeather,
                    Province = codeParts[0],
                    CityCode = codeParts[1],
                    Language = lang
                };
            }
            else if (type == "alerts")
            {
                // Format: lat_lon (e.g., 48.574_-78.116)
                var coords = dataPart.Split('_');
                
                if (coords.Length >= 2 && 
                    double.TryParse(coords[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(coords[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                {
                    return new EcccDataRequest
                    {
                        DataType = EcccDataType.Alerts,
                        Latitude = lat,
                        Longitude = lon,
                        Language = lang
                    };
                }
                 throw new ArgumentException("Invalid alerts coordinate format", nameof(url));
            }

            throw new ArgumentException($"Unknown feed type: {type}", nameof(url));
        }

        #endregion
        
        #region Dynamic Data Fetching
        
        /// <summary>
        /// Fetches any ECCC data using a dynamic URL configuration.
        /// </summary>
        /// <param name="client">HttpClient instance</param>
        /// <param name="request">The data request configuration</param>
        /// <returns>The fetched data as EcccDataResult</returns>
        public static async Task<EcccDataResult> FetchDataAsync(HttpClient client, EcccDataRequest request)
        {
            var result = new EcccDataResult { Request = request };
            
            try
            {
                string url = request.DataType switch
                {
                    EcccDataType.CityWeather => BuildCityWeatherUrl(request.Province!, request.CityCode!, request.Language),
                    EcccDataType.Alerts => BuildAlertsUrl(request.Latitude, request.Longitude, request.Language),
                    EcccDataType.WmsLayer => BuildWmsUrl(
                        request.Layer!,
                        request.BoundingBox,
                        request.Width,
                        request.Height,
                        request.Format,
                        request.Time,
                        request.Crs),
                    EcccDataType.Capabilities => BuildCapabilitiesUrl(request.Service),
                    EcccDataType.Datamart => BuildDatamartUrl(request.DatamartPath!),
                    EcccDataType.CustomUrl => request.CustomUrl!,
                    _ => throw new ArgumentException($"Unknown data type: {request.DataType}")
                };
                
                result.Url = url;
                
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                
                var response = await client.GetAsync(url);
                result.StatusCode = (int)response.StatusCode;
                result.Success = response.IsSuccessStatusCode;
                
                if (response.IsSuccessStatusCode)
                {
                    result.ContentType = response.Content.Headers.ContentType?.MediaType;
                    
                    // Determine if binary or text content
                    if (result.ContentType?.StartsWith("image/") == true ||
                        result.ContentType?.StartsWith("application/octet") == true)
                    {
                        result.BinaryData = await response.Content.ReadAsByteArrayAsync();
                    }
                    else
                    {
                        result.TextData = await response.Content.ReadAsStringAsync();
                        
                        // Auto-parse XML if applicable
                        if (result.ContentType?.Contains("xml") == true || result.TextData.TrimStart().StartsWith("<"))
                        {
                            try { result.XmlDocument = XDocument.Parse(result.TextData); }
                            catch { /* Not valid XML, keep as text */ }
                        }
                    }
                }
                else
                {
                    result.ErrorMessage = $"HTTP {result.StatusCode}: {response.ReasonPhrase}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }
        
        /// <summary>
        /// Fetches multiple ECCC data requests in parallel.
        /// </summary>
        public static async Task<List<EcccDataResult>> FetchMultipleAsync(HttpClient client, IEnumerable<EcccDataRequest> requests, int delayMs = 100)
        {
            var results = new List<EcccDataResult>();
            foreach (var request in requests)
            {
                results.Add(await FetchDataAsync(client, request));
                if (delayMs > 0) await Task.Delay(delayMs);
            }
            return results;
        }
        
        /// <summary>
        /// Gets available WMS layers from GeoMet capabilities.
        /// </summary>
        public static async Task<List<string>> GetAvailableLayersAsync(HttpClient client)
        {
            var layers = new List<string>();
            var result = await FetchDataAsync(client, new EcccDataRequest { DataType = EcccDataType.Capabilities });
            
            if (result.Success && result.XmlDocument != null)
            {
                var ns = result.XmlDocument.Root?.GetDefaultNamespace() ?? XNamespace.None;
                var layerElements = result.XmlDocument.Descendants(ns + "Layer");
                
                foreach (var layer in layerElements)
                {
                    var name = layer.Element(ns + "Name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                        layers.Add(name);
                }
            }
            
            return layers;
        }
        
        #endregion
        
        #region Common Layer Constants
        
        /// <summary>Common ECCC GeoMet WMS layer names</summary>
        public static class Layers
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
        
        /// <summary>Canadian province codes for ECCC</summary>
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
        
        #endregion

        #region Weather Forecast Fetching
        
        /// <summary>
        /// Fetches weather forecast data from ECCC for a specific city.
        /// </summary>
        /// <param name="client">HttpClient instance</param>
        /// <param name="province">Province code (e.g., "qc")</param>
        /// <param name="cityCode">City code (e.g., "147" for Montreal)</param>
        /// <param name="language">Language: "e" for English, "f" for French</param>
        /// <returns>Parsed weather forecast in OpenMeteo-compatible format</returns>
        public static async Task<OpenMeteo.WeatherForecast?> FetchWeatherForecastAsync(
            HttpClient client, 
            string province, 
            string cityCode, 
            string language = "f")
        {
            try
            {
                var url = BuildCityWeatherUrl(province, cityCode, language);
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                
                var xml = await client.GetStringAsync(url);
                return ParseEcccWeatherRss(xml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ECCC] Error fetching weather for {province}-{cityCode}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Fetches weather forecast by city name using the configured city feeds.
        /// </summary>
        public static async Task<OpenMeteo.WeatherForecast?> FetchWeatherForecastByCityAsync(
            HttpClient client,
            string cityName,
            EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            var feeds = cfg.CityFeeds ?? new Dictionary<string, string>();
            
            // Find matching feed URL
            var normalizedCity = NormalizeCity(cityName);
            var feedUrl = feeds.FirstOrDefault(kv => NormalizeCity(kv.Key) == normalizedCity).Value;
            
            if (string.IsNullOrEmpty(feedUrl))
            {
                Console.WriteLine($"[ECCC] No feed configured for city: {cityName}");
                return null;
            }
            
            try
            {
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", cfg.UserAgent ?? "Mozilla/5.0");
                
                var xml = await client.GetStringAsync(feedUrl);
                return ParseEcccWeatherRss(xml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ECCC] Error fetching weather for {cityName}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Parses ECCC RSS weather feed XML into a WeatherForecast object.
        /// </summary>
        private static OpenMeteo.WeatherForecast? ParseEcccWeatherRss(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var atom = "http://www.w3.org/2005/Atom";
                var entries = doc.Root?.Elements(XName.Get("entry", atom))?.ToList() ?? new List<XElement>();
                
                var forecast = new OpenMeteo.WeatherForecast
                {
                    Timezone = "America/Toronto", // Default, could be parsed from feed
                    TimezoneAbbreviation = "EST",
                    Current = new OpenMeteo.Current(),
                    Daily = new OpenMeteo.Daily(),
                    Hourly = new OpenMeteo.Hourly()
                };
                
                // Parse entries to extract weather data
                var dailyTimes = new List<string>();
                var dailyMaxTemps = new List<float>();
                var dailyMinTemps = new List<float>();
                var dailyWeatherCodes = new List<float>();
                var dailyPrecipitation = new List<float>();
                
                foreach (var entry in entries)
                {
                    var title = entry.Element(XName.Get("title", atom))?.Value ?? string.Empty;
                    var summary = entry.Element(XName.Get("summary", atom))?.Value ?? string.Empty;
                    var category = entry.Element(XName.Get("category", atom))?.Attribute("term")?.Value ?? string.Empty;
                    var updated = entry.Element(XName.Get("updated", atom))?.Value;
                    
                    // Parse current conditions
                    if (category.Equals("Current Conditions", StringComparison.OrdinalIgnoreCase) ||
                        category.Equals("Conditions actuelles", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseCurrentConditions(summary, forecast.Current);
                    }
                    // Parse forecasts
                    else if (category.Contains("Forecast", StringComparison.OrdinalIgnoreCase) ||
                             category.Contains("Prévisions", StringComparison.OrdinalIgnoreCase) ||
                             category.Contains("Weather Forecasts", StringComparison.OrdinalIgnoreCase))
                    {
                        var dayData = ParseForecastEntry(title, summary);
                        if (dayData != null)
                        {
                            dailyTimes.Add(dayData.Value.Date);
                            dailyMaxTemps.Add(dayData.Value.MaxTemp);
                            dailyMinTemps.Add(dayData.Value.MinTemp);
                            dailyWeatherCodes.Add(dayData.Value.WeatherCode);
                            dailyPrecipitation.Add(dayData.Value.Precipitation);
                        }
                    }
                }
                
                // Populate daily arrays
                if (dailyTimes.Count > 0)
                {
                    forecast.Daily.Time = dailyTimes.ToArray();
                    forecast.Daily.Temperature_2m_max = dailyMaxTemps.ToArray();
                    forecast.Daily.Temperature_2m_min = dailyMinTemps.ToArray();
                    forecast.Daily.Weathercode = dailyWeatherCodes.ToArray();
                    forecast.Daily.Precipitation_sum = dailyPrecipitation.ToArray();
                }
                
                return forecast;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ECCC] Error parsing RSS: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Parses current conditions from ECCC summary text.
        /// </summary>
        private static void ParseCurrentConditions(string summary, OpenMeteo.Current? current)
        {
            if (current == null) return;
            
            // ECCC summaries contain HTML-like content with temperature, conditions, etc.
            // Example: "Temperature: -5°C, Conditions: Partly Cloudy, Wind: NW 20 km/h"
            
            // Extract temperature
            var tempMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"(-?\d+(?:\.\d+)?)\s*°?\s*[CF]", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tempMatch.Success && float.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float temp))
            {
                current.Temperature_2m = temp;
            }
            
            // Extract wind speed
            var windMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"(\d+(?:\.\d+)?)\s*km/h", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (windMatch.Success && float.TryParse(windMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float wind))
            {
                current.Windspeed_10m = wind;
            }
            
            // Extract humidity
            var humidityMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"[Hh]umidit[éy]?\s*:?\s*(\d+)\s*%");
            if (humidityMatch.Success && int.TryParse(humidityMatch.Groups[1].Value, out int humidity))
            {
                current.Relativehumidity_2m = humidity;
            }
            
            // Determine weather code from conditions text
            current.Weathercode = DetermineWeatherCode(summary);
            current.Is_day = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 20 ? 1 : 0;
        }
        
        /// <summary>
        /// Parses a forecast entry into structured data.
        /// </summary>
        private static (string Date, float MaxTemp, float MinTemp, float WeatherCode, float Precipitation)? ParseForecastEntry(string title, string summary)
        {
            try
            {
                // Extract date from title (e.g., "Monday: Sunny, High 5°C")
                var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                
                // Try to parse day name and offset
                var dayNames = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
                                       "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };
                foreach (var day in dayNames)
                {
                    if (title.StartsWith(day, StringComparison.OrdinalIgnoreCase))
                    {
                        var today = DateTime.Now;
                        var targetDay = Array.FindIndex(new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
                            d => d.Equals(day, StringComparison.OrdinalIgnoreCase) || 
                                 GetFrenchDayName(d).Equals(day, StringComparison.OrdinalIgnoreCase));
                        if (targetDay >= 0)
                        {
                            var daysUntil = (targetDay - (int)today.DayOfWeek + 7) % 7;
                            if (daysUntil == 0 && title.Contains("night", StringComparison.OrdinalIgnoreCase)) daysUntil = 0;
                            dateStr = today.AddDays(daysUntil).ToString("yyyy-MM-dd");
                        }
                        break;
                    }
                }
                
                // Extract temperatures
                float maxTemp = 0, minTemp = 0;
                
                // High temperature
                var highMatch = System.Text.RegularExpressions.Regex.Match(
                    title + " " + summary,
                    @"[Hh]igh\s*:?\s*(-?\d+)|[Mm]ax\s*:?\s*(-?\d+)|(-?\d+)\s*°?\s*[CF]?\s*[Hh]igh",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (highMatch.Success)
                {
                    var val = highMatch.Groups[1].Success ? highMatch.Groups[1].Value :
                              highMatch.Groups[2].Success ? highMatch.Groups[2].Value : highMatch.Groups[3].Value;
                    float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out maxTemp);
                }
                
                // Low temperature
                var lowMatch = System.Text.RegularExpressions.Regex.Match(
                    title + " " + summary,
                    @"[Ll]ow\s*:?\s*(-?\d+)|[Mm]in\s*:?\s*(-?\d+)|(-?\d+)\s*°?\s*[CF]?\s*[Ll]ow",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lowMatch.Success)
                {
                    var val = lowMatch.Groups[1].Success ? lowMatch.Groups[1].Value :
                              lowMatch.Groups[2].Success ? lowMatch.Groups[2].Value : lowMatch.Groups[3].Value;
                    float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out minTemp);
                }
                
                // If only one temp found, use it for both
                if (maxTemp != 0 && minTemp == 0) minTemp = maxTemp - 5;
                if (minTemp != 0 && maxTemp == 0) maxTemp = minTemp + 5;
                
                // Fallback: extract any temperature
                if (maxTemp == 0 && minTemp == 0)
                {
                    var anyTemp = System.Text.RegularExpressions.Regex.Match(
                        title + " " + summary,
                        @"(-?\d+)\s*°");
                    if (anyTemp.Success && float.TryParse(anyTemp.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                    {
                        maxTemp = t;
                        minTemp = t - 5;
                    }
                }
                
                // Weather code
                float weatherCode = DetermineWeatherCode(title + " " + summary);
                
                // Precipitation (estimate from conditions)
                float precip = 0;
                if (summary.Contains("rain", StringComparison.OrdinalIgnoreCase) ||
                    summary.Contains("pluie", StringComparison.OrdinalIgnoreCase))
                    precip = 5;
                if (summary.Contains("snow", StringComparison.OrdinalIgnoreCase) ||
                    summary.Contains("neige", StringComparison.OrdinalIgnoreCase))
                    precip = 3;
                
                return (dateStr, maxTemp, minTemp, weatherCode, precip);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Determines WMO weather code from text description.
        /// </summary>
        private static int DetermineWeatherCode(string text)
        {
            var lower = text.ToLowerInvariant();
            
            // Thunderstorm
            if (lower.Contains("thunder") || lower.Contains("orage")) return 95;
            
            // Snow
            if (lower.Contains("heavy snow") || lower.Contains("forte neige")) return 75;
            if (lower.Contains("snow") || lower.Contains("neige")) return 71;
            if (lower.Contains("flurries") || lower.Contains("averses de neige")) return 73;
            
            // Freezing rain
            if (lower.Contains("freezing rain") || lower.Contains("pluie verglaçante")) return 67;
            if (lower.Contains("ice pellets") || lower.Contains("grésil")) return 79;
            
            // Rain
            if (lower.Contains("heavy rain") || lower.Contains("forte pluie")) return 65;
            if (lower.Contains("rain") || lower.Contains("pluie")) return 61;
            if (lower.Contains("drizzle") || lower.Contains("bruine")) return 51;
            if (lower.Contains("showers") || lower.Contains("averses")) return 80;
            
            // Fog
            if (lower.Contains("fog") || lower.Contains("brouillard")) return 45;
            if (lower.Contains("mist") || lower.Contains("brume")) return 45;
            
            // Cloudy
            if (lower.Contains("overcast") || lower.Contains("couvert")) return 3;
            if (lower.Contains("mostly cloudy") || lower.Contains("généralement nuageux")) return 3;
            if (lower.Contains("cloudy") || lower.Contains("nuageux")) return 2;
            if (lower.Contains("partly cloudy") || lower.Contains("partiellement nuageux")) return 2;
            if (lower.Contains("a few clouds") || lower.Contains("quelques nuages")) return 1;
            
            // Clear/Sunny
            if (lower.Contains("sunny") || lower.Contains("ensoleillé")) return 0;
            if (lower.Contains("clear") || lower.Contains("dégagé")) return 0;
            
            return 0; // Default: clear
        }
        
        private static string GetFrenchDayName(string englishDay) => englishDay switch
        {
            "Sunday" => "Dimanche",
            "Monday" => "Lundi",
            "Tuesday" => "Mardi",
            "Wednesday" => "Mercredi",
            "Thursday" => "Jeudi",
            "Friday" => "Vendredi",
            "Saturday" => "Samedi",
            _ => englishDay
        };
        
        #endregion

        public static async Task<List<AlertEntry>> FetchAllAlerts(HttpClient client, IEnumerable<string>? wantedCities = null, EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            var feeds = cfg.CityFeeds ?? new Dictionary<string, string>();
            // If caller supplied a list of desired cities, filter the feeds to only those cities (normalize names)
            if (wantedCities != null && wantedCities.Any())
            {
                var wantedSet = new HashSet<string>(wantedCities.Select(NormalizeCity));
                feeds = feeds.Where(kv => wantedSet.Contains(NormalizeCity(kv.Key)))
                             .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            var result = new List<AlertEntry>();
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", cfg.UserAgent ?? "Mozilla/5.0");

            foreach (var kv in feeds)
            {
                try
                {
                    var xml = await client.GetStringAsync(kv.Value);
                    var doc = XDocument.Parse(xml);
                    var atom = "http://www.w3.org/2005/Atom";
                    var entries = doc.Root?.Elements(XName.Get("entry", atom)) ?? Enumerable.Empty<XElement>();
                    foreach (var e in entries)
                    {
                        var title = e.Element(XName.Get("title", atom))?.Value ?? string.Empty;
                        var summary = e.Element(XName.Get("summary", atom))?.Value ?? string.Empty;
                        var category = e.Element(XName.Get("category", atom))?.Attribute("term")?.Value ?? string.Empty;
                        if (category.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("avertiss", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("watch", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (title.IndexOf("no watches", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("aucune veille", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            result.Add(new AlertEntry { City = kv.Key, Title = title, Summary = summary });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ECCC] Error fetching {kv.Key}: {ex.Message}");
                }
                await Task.Delay(cfg.DelayBetweenRequestsMs);
            }
            return result;
        }

        public static async Task FetchRadarImages(HttpClient client, string outputDir, EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            // Minimal implementation: download any RadarFeeds direct URLs.
            foreach (var kv in cfg.RadarFeeds ?? new Dictionary<string,string>())
            {
                try
                {
                    var resp = await client.GetAsync(kv.Value);
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        var ext = GetExtFromContent(resp.Content.Headers.ContentType?.MediaType, kv.Value);
                        var path = Path.Combine(outputDir, $"radar_{Sanitize(kv.Key)}{ext}");
                        await File.WriteAllBytesAsync(path, bytes);
                        Console.WriteLine($"✓ Downloaded radar {kv.Key} -> {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ECCC] Radar fetch failed for {kv.Key}: {ex.Message}");
                }
            }
        }

        public static Task CreateProvinceRadarAnimation(HttpClient client, string outputDir, EcccSettings? settings = null)
        {
            // For quick extraction, keep a no-op that can be extended later.
            Console.WriteLine("ECCC.CreateProvinceRadarAnimation: not implemented in minimal library (no-op).");
            return Task.CompletedTask;
        }

        private static string GetExtFromContent(string? contentType, string url)
        {
            var ct = contentType?.ToLowerInvariant() ?? string.Empty;
            if (ct.Contains("png")) return ".png";
            if (ct.Contains("gif")) return ".gif";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return ".jpg";
            if (ct.Contains("mp4") || ct.Contains("video")) return ".mp4";
            try { var e = Path.GetExtension(new Uri(url).AbsolutePath); if (!string.IsNullOrWhiteSpace(e)) return e; } catch { }
            return ".png";
        }

        private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static EcccSettings LoadSettings()
        {
            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(p)) return new EcccSettings();
                using var fs = File.OpenRead(p);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("ECCC", out var elem))
                {
                    return JsonSerializer.Deserialize<EcccSettings>(elem.GetRawText()) ?? new EcccSettings();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[ECCC] Failed to load settings: {ex.Message}"); }
            return new EcccSettings();
        }

        // Normalize city names by removing diacritics, trimming and lowercasing to allow matching user-configured locations
        private static string NormalizeCity(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var normalized = s.Normalize(NormalizationForm.FormD);
            var filtered = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return filtered.Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
        }

        public class EcccSettings
        {
            public Dictionary<string,string>? CityFeeds { get; set; }
            public Dictionary<string,string>? RadarFeeds { get; set; }
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
        }
        
        #region Data Request/Result Types
        
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
            public XDocument? XmlDocument { get; set; }
            /// <summary>Error message if failed</summary>
            public string? ErrorMessage { get; set; }
        }
        
        #endregion
    }
}
