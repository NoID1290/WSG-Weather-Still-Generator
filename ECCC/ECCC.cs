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
using ECCC.Data;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services
{
    // Minimal, well-typed ECCC library implementation (extracted).
    public static class ECCC
    {
        /// <summary>Optional logging callback for diagnostic messages</summary>
        public static Action<string>? Log { get; set; }
        
        private static void LogMessage(string message)
        {
            Log?.Invoke(message);
        }
        
        #region ECCC Base URLs and Endpoints
        
        /// <summary>Base URL for ECCC weather RSS feeds</summary>
        public const string BaseWeatherUrl = "https://weather.gc.ca";
        
        /// <summary>Base URL for ECCC official OGC API (JSON weather data)</summary>
        public const string BaseApiUrl = "https://api.weather.gc.ca";
        
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
        /// Searches for Canadian cities by name and returns available ECCC city configurations.
        /// </summary>
        /// <param name="query">City name to search for (e.g., "Montreal", "Québec")</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>List of matching city configurations with their ECCC feed URLs</returns>
        public static List<CityInfo> SearchCities(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<CityInfo>();
            
            var normalizedQuery = NormalizeCity(query);
            var results = new List<CityInfo>();
            
            // Search through known cities
            foreach (var city in GetKnownCities())
            {
                var normalizedCity = NormalizeCity(city.Name);
                if (normalizedCity.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedCity))
                {
                    results.Add(city);
                    if (results.Count >= maxResults) break;
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// Searches for cities online using OpenMeteo geocoding API.
        /// Returns cities from anywhere in the world with ECCC alerts feed URLs.
        /// </summary>
        /// <param name="client">OpenMeteoClient instance</param>
        /// <param name="query">City name to search for</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>List of found cities with their ECCC alerts feed URLs</returns>
        public static async Task<List<CityInfo>> SearchCitiesOnlineAsync(OpenMeteo.OpenMeteoClient client, string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<CityInfo>();
            
            try
            {
                var geocodingOptions = new OpenMeteo.GeocodingOptions(query, "en", maxResults);
                var response = await client.GetLocationDataAsync(geocodingOptions);
                
                if (response == null || response.Locations == null || response.Locations.Length == 0)
                    return new List<CityInfo>();
                
                var results = new List<CityInfo>();
                
                // Convert OpenMeteo locations to CityInfo
                foreach (var loc in response.Locations)
                {
                    if (loc.Name == null) continue;
                    
                    // Determine province/state code for Canadian cities
                    string provinceCode = "ca"; // default
                    if (loc.CountryCode?.Equals("CA", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        provinceCode = GetProvinceCodeFromAdmin(loc.Admin1);
                    }
                    
                    // Use a special code to indicate this is coordinate-based (alerts feed only)
                    var cityInfo = new CityInfo(
                        loc.Name,
                        provinceCode,
                        "coord", // Special marker for coordinate-based feeds
                        loc.Latitude,
                        loc.Longitude
                    );
                    
                    // Add location context
                    if (!string.IsNullOrEmpty(loc.Admin1))
                    {
                        cityInfo.Name = $"{loc.Name}, {loc.Admin1}";
                    }
                    if (!string.IsNullOrEmpty(loc.Country) && !loc.Country.Equals("Canada", StringComparison.OrdinalIgnoreCase))
                    {
                        cityInfo.Name += $" ({loc.Country})";
                    }
                    
                    results.Add(cityInfo);
                }
                
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ECCC] Online search error: {ex.Message}");
                return new List<CityInfo>();
            }
        }
        
        /// <summary>
        /// Maps Canadian province/territory names to their 2-letter codes.
        /// </summary>
        private static string GetProvinceCodeFromAdmin(string? admin)
        {
            if (string.IsNullOrWhiteSpace(admin)) return "ca";
            
            var normalized = admin.ToLowerInvariant();
            
            if (normalized.Contains("quebec") || normalized.Contains("québec")) return "qc";
            if (normalized.Contains("ontario")) return "on";
            if (normalized.Contains("british columbia")) return "bc";
            if (normalized.Contains("alberta")) return "ab";
            if (normalized.Contains("manitoba")) return "mb";
            if (normalized.Contains("saskatchewan")) return "sk";
            if (normalized.Contains("nova scotia")) return "ns";
            if (normalized.Contains("new brunswick")) return "nb";
            if (normalized.Contains("newfoundland") || normalized.Contains("labrador")) return "nl";
            if (normalized.Contains("prince edward")) return "pe";
            if (normalized.Contains("yukon")) return "yt";
            if (normalized.Contains("northwest")) return "nt";
            if (normalized.Contains("nunavut")) return "nu";
            
            return "ca";
        }
        
        /// <summary>
        /// Gets a database of known ECCC cities with their province and city codes.
        /// </summary>
        public static List<CityInfo> GetKnownCities()
        {
            return new List<CityInfo>
            {
                // Quebec (75 cities)
                new CityInfo("Montréal", "qc", "147", 45.5017, -73.5673),
                new CityInfo("Québec", "qc", "133", 46.8139, -71.2080),
                new CityInfo("Laval", "qc", "147", 45.6066, -73.7124),
                new CityInfo("Gatineau", "qc", "59", 45.4765, -75.7013),
                new CityInfo("Longueuil", "qc", "147", 45.5312, -73.5186),
                new CityInfo("Sherbrooke", "qc", "30", 45.4042, -71.8929),
                new CityInfo("Saguenay", "qc", "50", 48.4284, -71.0656),
                new CityInfo("Lévis", "qc", "108", 46.8000, -71.1772),
                new CityInfo("Trois-Rivières", "qc", "48", 46.3432, -72.5477),
                new CityInfo("Terrebonne", "qc", "147", 45.7000, -73.6470),
                new CityInfo("Saint-Jean-sur-Richelieu", "qc", "43", 45.3075, -73.2625),
                new CityInfo("Repentigny", "qc", "147", 45.7333, -73.4500),
                new CityInfo("Brossard", "qc", "147", 45.4667, -73.4500),
                new CityInfo("Drummondville", "qc", "34", 45.8833, -72.4833),
                new CityInfo("Saint-Jérôme", "qc", "20", 45.7833, -74.0000),
                new CityInfo("Granby", "qc", "111", 45.4000, -72.7333),
                new CityInfo("Blainville", "qc", "147", 45.6667, -73.8833),
                new CityInfo("Shawinigan", "qc", "48", 46.5667, -72.7500),
                new CityInfo("Dollard-des-Ormeaux", "qc", "147", 45.4833, -73.8167),
                new CityInfo("Rimouski", "qc", "58", 48.4489, -68.5236),
                new CityInfo("Val-d'Or", "qc", "95", 48.0971, -77.7827),
                new CityInfo("Victoriaville", "qc", "34", 46.0500, -71.9667),
                new CityInfo("Saint-Hyacinthe", "qc", "147", 45.6304, -72.9569),
                new CityInfo("Joliette", "qc", "147", 46.0167, -73.4333),
                new CityInfo("Sorel-Tracy", "qc", "48", 46.0333, -73.1167),
                new CityInfo("Alma", "qc", "106", 48.5500, -71.6500),
                new CityInfo("Rouyn-Noranda", "qc", "94", 48.2351, -79.0233),
                new CityInfo("Sept-Îles", "qc", "100", 50.2167, -66.3833),
                new CityInfo("Baie-Comeau", "qc", "101", 49.2167, -68.1500),
                new CityInfo("Amos", "qc", "96", 48.5667, -78.1167),
                new CityInfo("Thetford Mines", "qc", "133", 46.0833, -71.3000),
                new CityInfo("Vaudreuil-Dorion", "qc", "147", 45.4000, -74.0333),
                new CityInfo("Rivière-du-Loup", "qc", "58", 47.8333, -69.5333),
                new CityInfo("Saint-Georges", "qc", "133", 46.1167, -70.6667),
                new CityInfo("Mirabel", "qc", "147", 45.6500, -74.0833),
                new CityInfo("Boucherville", "qc", "147", 45.5833, -73.4500),
                new CityInfo("Châteauguay", "qc", "147", 45.3833, -73.7500),
                new CityInfo("Saint-Eustache", "qc", "147", 45.5667, -73.9000),
                new CityInfo("Mascouche", "qc", "147", 45.7500, -73.6000),
                new CityInfo("Cowansville", "qc", "111", 45.2000, -72.7500),
                new CityInfo("Magog", "qc", "30", 45.2667, -72.1500),
                new CityInfo("Sainte-Thérèse", "qc", "147", 45.6333, -73.8333),
                new CityInfo("Rougemont", "qc", "147", 45.4333, -73.0500),
                new CityInfo("Mont-Laurier", "qc", "20", 46.5500, -75.5000),
                new CityInfo("Matane", "qc", "58", 48.8500, -67.5333),
                new CityInfo("Gaspé", "qc", "58", 48.8333, -64.4833),
                new CityInfo("La Tuque", "qc", "48", 47.4333, -72.7833),
                new CityInfo("Mont-Tremblant", "qc", "20", 46.1167, -74.5833),
                new CityInfo("Sainte-Marie", "qc", "133", 46.4500, -71.0333),
                new CityInfo("Varennes", "qc", "147", 45.6833, -73.4333),
                
                // Ontario (70 cities)
                new CityInfo("Toronto", "on", "143", 43.6532, -79.3832),
                new CityInfo("Ottawa", "on", "118", 45.4215, -75.6972),
                new CityInfo("Mississauga", "on", "143", 43.5890, -79.6441),
                new CityInfo("Brampton", "on", "143", 43.7315, -79.7624),
                new CityInfo("Hamilton", "on", "77", 43.2557, -79.8711),
                new CityInfo("London", "on", "72", 42.9849, -81.2453),
                new CityInfo("Markham", "on", "143", 43.8561, -79.3370),
                new CityInfo("Vaughan", "on", "143", 43.8361, -79.4983),
                new CityInfo("Kitchener", "on", "48", 43.4516, -80.4925),
                new CityInfo("Windsor", "on", "94", 42.3149, -83.0364),
                new CityInfo("Richmond Hill", "on", "143", 43.8828, -79.4403),
                new CityInfo("Oakville", "on", "143", 43.4675, -79.6877),
                new CityInfo("Burlington", "on", "77", 43.3255, -79.7990),
                new CityInfo("Greater Sudbury", "on", "40", 46.4917, -80.9930),
                new CityInfo("Oshawa", "on", "143", 43.8971, -78.8658),
                new CityInfo("Barrie", "on", "18", 44.3894, -79.6903),
                new CityInfo("St. Catharines", "on", "89", 43.1594, -79.2469),
                new CityInfo("Cambridge", "on", "48", 43.3616, -80.3144),
                new CityInfo("Kingston", "on", "69", 44.2312, -76.4860),
                new CityInfo("Whitby", "on", "143", 43.8975, -78.9429),
                new CityInfo("Guelph", "on", "52", 43.5448, -80.2482),
                new CityInfo("Ajax", "on", "143", 43.8508, -79.0204),
                new CityInfo("Thunder Bay", "on", "100", 48.3809, -89.2477),
                new CityInfo("Waterloo", "on", "48", 43.4643, -80.5204),
                new CityInfo("Chatham-Kent", "on", "94", 42.4048, -82.1910),
                new CityInfo("Pickering", "on", "143", 43.8384, -79.0868),
                new CityInfo("Niagara Falls", "on", "89", 43.0896, -79.0849),
                new CityInfo("Clarington", "on", "143", 43.9350, -78.6083),
                new CityInfo("Sarnia", "on", "93", 42.9745, -82.4066),
                new CityInfo("Brantford", "on", "15", 43.1394, -80.2644),
                new CityInfo("Peterborough", "on", "81", 44.3091, -78.3197),
                new CityInfo("Kawartha Lakes", "on", "81", 44.3501, -78.7452),
                new CityInfo("Belleville", "on", "69", 44.1628, -77.3832),
                new CityInfo("Sault Ste. Marie", "on", "40", 46.5332, -84.3465),
                new CityInfo("North Bay", "on", "74", 46.3091, -79.4608),
                new CityInfo("Cornwall", "on", "118", 45.0212, -74.7300),
                new CityInfo("Welland", "on", "89", 42.9834, -79.2482),
                new CityInfo("Owen Sound", "on", "18", 44.5678, -80.9433),
                new CityInfo("Stratford", "on", "72", 43.3701, -80.9799),
                new CityInfo("Timmins", "on", "101", 48.4758, -81.3304),
                new CityInfo("St. Thomas", "on", "72", 42.7784, -81.1756),
                new CityInfo("Woodstock", "on", "72", 43.1315, -80.7467),
                new CityInfo("Orangeville", "on", "143", 43.9197, -80.0942),
                new CityInfo("Leamington", "on", "94", 42.0534, -82.5998),
                new CityInfo("Orillia", "on", "18", 44.6084, -79.4198),
                new CityInfo("Milton", "on", "143", 43.5183, -79.8774),
                new CityInfo("Newmarket", "on", "143", 44.0592, -79.4613),
                new CityInfo("Brant", "on", "15", 43.1169, -80.3486),
                new CityInfo("Port Colborne", "on", "89", 42.8898, -79.2501),
                new CityInfo("Quinte West", "on", "69", 44.1833, -77.5667),
                
                // British Columbia (45 cities)
                new CityInfo("Vancouver", "bc", "133", 49.2827, -123.1207),
                new CityInfo("Surrey", "bc", "133", 49.1913, -122.8490),
                new CityInfo("Burnaby", "bc", "133", 49.2488, -122.9805),
                new CityInfo("Richmond", "bc", "133", 49.1666, -123.1336),
                new CityInfo("Abbotsford", "bc", "1", 49.0504, -122.3045),
                new CityInfo("Coquitlam", "bc", "133", 49.2838, -122.7932),
                new CityInfo("Kelowna", "bc", "48", 49.8880, -119.4960),
                new CityInfo("Saanich", "bc", "85", 48.4850, -123.3830),
                new CityInfo("Delta", "bc", "133", 49.0847, -123.0586),
                new CityInfo("Kamloops", "bc", "37", 50.6745, -120.3273),
                new CityInfo("Langley", "bc", "133", 49.1044, -122.6603),
                new CityInfo("Victoria", "bc", "85", 48.4284, -123.3656),
                new CityInfo("Maple Ridge", "bc", "133", 49.2192, -122.5987),
                new CityInfo("Nanaimo", "bc", "10", 49.1659, -123.9401),
                new CityInfo("New Westminster", "bc", "133", 49.2069, -122.9111),
                new CityInfo("Port Coquitlam", "bc", "133", 49.2625, -122.7811),
                new CityInfo("North Vancouver", "bc", "133", 49.3200, -123.0724),
                new CityInfo("West Vancouver", "bc", "133", 49.3289, -123.1654),
                new CityInfo("Port Moody", "bc", "133", 49.2831, -122.8317),
                new CityInfo("Chilliwack", "bc", "1", 49.1577, -121.9509),
                new CityInfo("Prince George", "bc", "71", 53.9171, -122.7497),
                new CityInfo("Vernon", "bc", "48", 50.2671, -119.2720),
                new CityInfo("Penticton", "bc", "48", 49.4991, -119.5937),
                new CityInfo("Campbell River", "bc", "10", 50.0244, -125.2476),
                new CityInfo("Courtenay", "bc", "10", 49.6878, -124.9945),
                new CityInfo("Whistler", "bc", "133", 50.1163, -122.9574),
                new CityInfo("Fort St. John", "bc", "71", 56.2466, -120.8476),
                new CityInfo("Prince Rupert", "bc", "71", 54.3150, -130.3209),
                new CityInfo("Terrace", "bc", "71", 54.5164, -128.6037),
                new CityInfo("Cranbrook", "bc", "20", 49.5100, -115.7690),
                new CityInfo("Nelson", "bc", "20", 49.4928, -117.2829),
                new CityInfo("Trail", "bc", "20", 49.0956, -117.7103),
                new CityInfo("Dawson Creek", "bc", "71", 55.7596, -120.2377),
                new CityInfo("Quesnel", "bc", "71", 52.9784, -122.4927),
                new CityInfo("Williams Lake", "bc", "71", 52.1417, -122.1417),
                
                // Alberta (40 cities)
                new CityInfo("Calgary", "ab", "52", 51.0447, -114.0719),
                new CityInfo("Edmonton", "ab", "50", 53.5461, -113.4938),
                new CityInfo("Red Deer", "ab", "34", 52.2681, -113.8111),
                new CityInfo("Lethbridge", "ab", "94", 49.6942, -112.8328),
                new CityInfo("St. Albert", "ab", "50", 53.6303, -113.6258),
                new CityInfo("Medicine Hat", "ab", "31", 50.0417, -110.6775),
                new CityInfo("Grande Prairie", "ab", "97", 55.1707, -118.7947),
                new CityInfo("Airdrie", "ab", "52", 51.2917, -114.0144),
                new CityInfo("Spruce Grove", "ab", "50", 53.5447, -113.9008),
                new CityInfo("Okotoks", "ab", "52", 50.7267, -113.9778),
                new CityInfo("Fort McMurray", "ab", "50", 56.7267, -111.3800),
                new CityInfo("Leduc", "ab", "50", 53.2594, -113.5514),
                new CityInfo("Lloydminster", "ab", "97", 53.2783, -110.0061),
                new CityInfo("Camrose", "ab", "50", 53.0167, -112.8333),
                new CityInfo("Cold Lake", "ab", "50", 54.4639, -110.1825),
                new CityInfo("Brooks", "ab", "31", 50.5642, -111.8989),
                new CityInfo("Wetaskiwin", "ab", "50", 52.9692, -113.3769),
                new CityInfo("Sylvan Lake", "ab", "34", 52.3086, -114.0958),
                new CityInfo("Cochrane", "ab", "52", 51.1881, -114.4689),
                new CityInfo("Chestermere", "ab", "52", 51.0506, -113.8222),
                new CityInfo("Canmore", "ab", "52", 51.0892, -115.3581),
                new CityInfo("Jasper", "ab", "50", 52.8737, -118.0814),
                new CityInfo("Banff", "ab", "52", 51.1784, -115.5708),
                new CityInfo("High River", "ab", "52", 50.5797, -113.8697),
                new CityInfo("Strathmore", "ab", "52", 51.0375, -113.4003),
                new CityInfo("Whitecourt", "ab", "97", 54.1428, -115.6836),
                new CityInfo("Peace River", "ab", "97", 56.2361, -117.2894),
                new CityInfo("Slave Lake", "ab", "97", 55.2833, -114.7692),
                
                // Manitoba (25 cities)
                new CityInfo("Winnipeg", "mb", "38", 49.8951, -97.1384),
                new CityInfo("Brandon", "mb", "83", 49.8483, -99.9501),
                new CityInfo("Steinbach", "mb", "38", 49.5256, -96.6839),
                new CityInfo("Thompson", "mb", "38", 55.7433, -97.8553),
                new CityInfo("Portage la Prairie", "mb", "38", 49.9728, -98.2919),
                new CityInfo("Winkler", "mb", "83", 49.1817, -97.9397),
                new CityInfo("Selkirk", "mb", "38", 50.1436, -96.8839),
                new CityInfo("Morden", "mb", "83", 49.1917, -98.1011),
                new CityInfo("Dauphin", "mb", "83", 51.1486, -100.0503),
                new CityInfo("The Pas", "mb", "38", 53.8250, -101.2539),
                new CityInfo("Flin Flon", "mb", "38", 54.7667, -101.8833),
                new CityInfo("Churchill", "mb", "38", 58.7684, -94.1647),
                
                // Saskatchewan (30 cities)
                new CityInfo("Saskatoon", "sk", "40", 52.1332, -106.6700),
                new CityInfo("Regina", "sk", "32", 50.4452, -104.6189),
                new CityInfo("Prince Albert", "sk", "59", 53.2033, -105.7531),
                new CityInfo("Moose Jaw", "sk", "32", 50.3933, -105.5519),
                new CityInfo("Swift Current", "sk", "46", 50.2881, -107.7939),
                new CityInfo("Yorkton", "sk", "40", 51.2139, -102.4628),
                new CityInfo("North Battleford", "sk", "40", 52.7575, -108.2861),
                new CityInfo("Lloydminster", "sk", "40", 53.2783, -110.0061),
                new CityInfo("Estevan", "sk", "32", 49.1392, -102.9861),
                new CityInfo("Weyburn", "sk", "32", 49.6617, -103.8528),
                new CityInfo("Martensville", "sk", "40", 52.2897, -106.6667),
                new CityInfo("Warman", "sk", "40", 52.3214, -106.5842),
                new CityInfo("Melville", "sk", "40", 50.9289, -102.8075),
                new CityInfo("Humboldt", "sk", "40", 52.2017, -105.1231),
                new CityInfo("Meadow Lake", "sk", "59", 54.1256, -108.4353),
                
                // Nova Scotia (25 cities)
                new CityInfo("Halifax", "ns", "12", 44.6488, -63.5752),
                new CityInfo("Cape Breton", "ns", "7", 46.1389, -60.1931),
                new CityInfo("Truro", "ns", "12", 45.3631, -63.2769),
                new CityInfo("New Glasgow", "ns", "12", 45.5928, -62.6489),
                new CityInfo("Dartmouth", "ns", "12", 44.6711, -63.5764),
                new CityInfo("Sydney", "ns", "7", 46.1364, -60.1942),
                new CityInfo("Glace Bay", "ns", "7", 46.1969, -59.9572),
                new CityInfo("Kentville", "ns", "12", 45.0775, -64.4958),
                new CityInfo("Amherst", "ns", "12", 45.8167, -64.2167),
                new CityInfo("Bridgewater", "ns", "12", 44.3772, -64.5231),
                new CityInfo("Yarmouth", "ns", "12", 43.8361, -66.1175),
                new CityInfo("Antigonish", "ns", "12", 45.6219, -61.9975),
                
                // New Brunswick (20 cities)
                new CityInfo("Moncton", "nb", "36", 46.0878, -64.7782),
                new CityInfo("Saint John", "nb", "40", 45.2733, -66.0633),
                new CityInfo("Fredericton", "nb", "29", 45.9636, -66.6431),
                new CityInfo("Dieppe", "nb", "36", 46.0989, -64.6889),
                new CityInfo("Miramichi", "nb", "36", 47.0282, -65.5023),
                new CityInfo("Edmundston", "nb", "29", 47.3733, -68.3253),
                new CityInfo("Bathurst", "nb", "36", 47.6186, -65.6511),
                new CityInfo("Campbellton", "nb", "36", 48.0054, -66.6732),
                new CityInfo("Riverview", "nb", "36", 46.0631, -64.8039),
                new CityInfo("Quispamsis", "nb", "40", 45.4322, -65.9431),
                new CityInfo("Oromocto", "nb", "29", 45.8456, -66.4792),
                
                // Newfoundland and Labrador (15 cities)
                new CityInfo("St. John's", "nl", "24", 47.5615, -52.7126),
                new CityInfo("Conception Bay South", "nl", "24", 47.5167, -52.9983),
                new CityInfo("Mount Pearl", "nl", "24", 47.5189, -52.8058),
                new CityInfo("Corner Brook", "nl", "8", 48.9501, -57.9522),
                new CityInfo("Paradise", "nl", "24", 47.5333, -52.8667),
                new CityInfo("Grand Falls-Windsor", "nl", "8", 48.9333, -55.6667),
                new CityInfo("Gander", "nl", "13", 48.9564, -54.6089),
                new CityInfo("Labrador City", "nl", "52", 52.9425, -66.9119),
                new CityInfo("Happy Valley-Goose Bay", "nl", "52", 53.3094, -60.3258),
                
                // Prince Edward Island (10 cities)
                new CityInfo("Charlottetown", "pe", "5", 46.2382, -63.1311),
                new CityInfo("Summerside", "pe", "5", 46.3950, -63.7883),
                new CityInfo("Stratford", "pe", "5", 46.2167, -63.0908),
                new CityInfo("Cornwall", "pe", "5", 46.2289, -63.2211),
                new CityInfo("Montague", "pe", "5", 46.1667, -62.6500),
                new CityInfo("Kensington", "pe", "5", 46.4333, -63.6333),
                
                // Yukon (5 cities)
                new CityInfo("Whitehorse", "yt", "50", 60.7212, -135.0568),
                new CityInfo("Dawson City", "yt", "50", 64.0608, -139.4331),
                
                // Northwest Territories (5 cities)
                new CityInfo("Yellowknife", "nt", "24", 62.4540, -114.3718),
                new CityInfo("Hay River", "nt", "24", 60.8156, -115.7999),
                new CityInfo("Inuvik", "nt", "24", 68.3607, -133.7230),
                
                // Nunavut (5 cities)
                new CityInfo("Iqaluit", "nu", "21", 63.7467, -68.5170),
                new CityInfo("Rankin Inlet", "nu", "21", 62.8097, -92.0894),
            };
        }
        
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
        /// Fetches weather forecast from the official ECCC OGC API (JSON format).
        /// This provides better structured data than RSS feeds.
        /// </summary>
        /// <param name="client">HttpClient instance</param>
        /// <param name="cityId">ECCC city identifier (e.g., "qc-147" for Montreal)</param>
        /// <param name="language">Language: "en" for English, "f" for French</param>
        /// <returns>Parsed weather forecast in OpenMeteo-compatible format</returns>
        public static async Task<OpenMeteo.WeatherForecast?> FetchWeatherFromApiAsync(
            HttpClient client,
            string cityId,
            string language = "f")
        {
            try
            {
                var url = $"{BaseApiUrl}/collections/citypageweather-realtime/items?f=json&properties=identifier&identifier={cityId}&limit=1";
                
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                
                var jsonResponse = await client.GetStringAsync(url);
                return ParseEcccApiJson(jsonResponse);
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error fetching weather for {cityId}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Parses ECCC OGC API JSON response and converts to OpenMeteo format.
        /// </summary>
        private static OpenMeteo.WeatherForecast? ParseEcccApiJson(string jsonResponse)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                
                // Navigate to the feature properties
                if (!root.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
                    return null;
                    
                var feature = features[0];
                if (!feature.TryGetProperty("properties", out var props))
                    return null;
                
                var forecast = new OpenMeteo.WeatherForecast
                {
                    Timezone = "America/Toronto",
                    TimezoneAbbreviation = "EST",
                    Current = new OpenMeteo.Current(),
                    Daily = new OpenMeteo.Daily(),
                    Hourly = new OpenMeteo.Hourly()
                };
                
                // Extract current conditions
                if (props.TryGetProperty("currentConditions", out var currentCond))
                {
                    if (currentCond.TryGetProperty("temperature", out var temp) && temp.TryGetProperty("value", out var tempVal))
                        forecast.Current.Temperature_2m = (float)tempVal.GetDouble();
                    
                    if (currentCond.TryGetProperty("humidity", out var hum) && hum.TryGetProperty("value", out var humVal))
                        forecast.Current.Relativehumidity_2m = humVal.GetInt32();
                    
                    if (currentCond.TryGetProperty("windSpeed", out var windSpd) && windSpd.TryGetProperty("value", out var wsVal))
                        forecast.Current.Windspeed_10m = (float)wsVal.GetDouble();
                    
                    if (currentCond.TryGetProperty("pressure", out var pres) && pres.TryGetProperty("value", out var presVal))
                        forecast.Current.Surface_pressure = (float)(presVal.GetDouble() * 10); // kPa to hPa
                }
                
                // Extract daily forecasts
                if (props.TryGetProperty("forecastGroup", out var forecastGrp) &&
                    forecastGrp.TryGetProperty("forecast", out var forecasts))
                {
                    var tempMax = new List<float>();
                    var tempMin = new List<float>();
                    var times = new List<string>();
                    var weatherCodes = new List<float>();
                    
                    foreach (var fc in forecasts.EnumerateArray())
                    {
                        if (fc.TryGetProperty("period", out var period))
                        {
                            var periodText = period.GetProperty("textForecastName").GetString() ?? "";
                            times.Add(periodText);
                        }
                        
                        // Check if this is a temperature entry
                        if (fc.TryGetProperty("temperatures", out var temps))
                        {
                            var tempElem = temps.TryGetProperty("temperature", out var tempObj) ? tempObj : temps;
                            if (tempElem.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                tempElem.TryGetProperty("value", out var tVal))
                            {
                                var tempValue = (float)tVal.GetDouble();
                                
                                // Determine if this is min or max based on class attribute
                                if (tempElem.TryGetProperty("class", out var cls) && 
                                    cls.GetString()?.Contains("low", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    tempMin.Add(tempValue);
                                    if (tempMax.Count < tempMin.Count) tempMax.Add(float.NaN);
                                }
                                else
                                {
                                    tempMax.Add(tempValue);
                                    if (tempMin.Count < tempMax.Count) tempMin.Add(float.NaN);
                                }
                            }
                        }
                        
                        weatherCodes.Add(0); // Default weather code
                    }
                    
                    forecast.Daily.Temperature_2m_max = tempMax.ToArray();
                    forecast.Daily.Temperature_2m_min = tempMin.ToArray();
                    forecast.Daily.Time = times.ToArray();
                    forecast.Daily.Weathercode = weatherCodes.ToArray();
                }
                
                // Extract hourly forecasts
                if (props.TryGetProperty("hourlyForecastGroup", out var hourlyGrp) &&
                    hourlyGrp.TryGetProperty("hourlyForecast", out var hourlyForecasts))
                {
                    var hourlyTimes = new List<string>();
                    var hourlyTemps = new List<float?>();
                    var hourlyWindSpeeds = new List<float?>();
                    
                    foreach (var hf in hourlyForecasts.EnumerateArray())
                    {
                        if (hf.TryGetProperty("dateTimeUTC", out var dt))
                            hourlyTimes.Add(dt.GetString() ?? "");
                        
                        if (hf.TryGetProperty("temperature", out var t) && t.TryGetProperty("value", out var tv))
                            hourlyTemps.Add((float?)tv.GetDouble());
                        else
                            hourlyTemps.Add(null);
                        
                        if (hf.TryGetProperty("windSpeed", out var ws) && ws.TryGetProperty("value", out var wsv))
                            hourlyWindSpeeds.Add((float?)wsv.GetDouble());
                        else
                            hourlyWindSpeeds.Add(null);
                    }
                    
                    forecast.Hourly.Time = hourlyTimes.ToArray();
                    forecast.Hourly.Temperature_2m = hourlyTemps.ToArray();
                    forecast.Hourly.Windspeed_10m = hourlyWindSpeeds.ToArray();
                }
                
                return forecast;
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error parsing JSON: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Searches for a Canadian city online using the ECCC API and returns its identifier.
        /// </summary>
        /// <param name="client">HttpClient instance</param>
        /// <param name="cityName">Name of the city to search for</param>
        /// <returns>City identifier (e.g., "qc-147") or null if not found</returns>
        public static async Task<string?> SearchCityIdOnlineAsync(HttpClient client, string cityName)
        {
            try
            {
                LogMessage($"[ECCC API] Starting city search for: {cityName}");
                
                // Query the ECCC API to find cities matching the name
                var url = $"{BaseApiUrl}/collections/citypageweather-realtime/items?f=json&limit=1000";
                LogMessage($"[ECCC API] Querying: {url}");
                
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                
                LogMessage($"[ECCC API] Fetching data from API...");
                var jsonResponse = await client.GetStringAsync(url);
                LogMessage($"[ECCC API] Received response, parsing JSON...");
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;
                
                if (!root.TryGetProperty("features", out var features))
                    return null;
                
                var normalizedSearch = NormalizeCity(cityName);
                LogMessage($"[ECCC API] Normalized search term: {normalizedSearch}");
                LogMessage($"[ECCC API] Searching through {features.GetArrayLength()} cities...");
                
                // Search through all features for matching city names
                foreach (var feature in features.EnumerateArray())
                {
                    if (!feature.TryGetProperty("properties", out var props))
                        continue;
                    
                    if (!props.TryGetProperty("location", out var location))
                        continue;
                    
                    if (!location.TryGetProperty("name", out var nameElem))
                        continue;
                    
                    var cityNameFromApi = nameElem.GetProperty("en").GetString() ?? "";
                    var normalizedApiCity = NormalizeCity(cityNameFromApi);
                    
                    // Match if normalized names are equal or one contains the other
                    if (normalizedApiCity == normalizedSearch || 
                        normalizedApiCity.Contains(normalizedSearch) ||
                        normalizedSearch.Contains(normalizedApiCity))
                    {
                        // Extract the identifier (e.g., "qc-147")
                        if (feature.TryGetProperty("id", out var id))
                        {
                            var fullId = id.GetString() ?? "";
                            // ID format is like "citypageweather-qc-147" - extract the city part
                            var match = System.Text.RegularExpressions.Regex.Match(fullId, @"([a-z]{2}-\d+)");
                            if (match.Success)
                            {
                                var cityId = match.Groups[1].Value;
                                LogMessage($"[ECCC API] Found city ID for {cityName}: {cityId} ({cityNameFromApi})");
                                return cityId;
                            }
                        }
                    }
                }
                
                LogMessage($"[ECCC API] No city ID found for: {cityName}");
                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC API] Error searching for city {cityName}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Fetches weather forecast by city name using online API lookup first, configured feeds as fallback.
        /// </summary>
        public static async Task<OpenMeteo.WeatherForecast?> FetchWeatherForecastByCityAsync(
            HttpClient client,
            string cityName,
            EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            
            try
            {
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    client.DefaultRequestHeaders.Add("User-Agent", cfg.UserAgent ?? "Mozilla/5.0");
                
                // FIRST: Try searching online via API
                LogMessage($"[ECCC] Searching online for {cityName}...");
                var foundCityId = await SearchCityIdOnlineAsync(client, cityName);
                
                if (!string.IsNullOrEmpty(foundCityId))
                {
                    LogMessage($"[ECCC] Fetching weather from API for {cityName} ({foundCityId})");
                    var apiResult = await FetchWeatherFromApiAsync(client, foundCityId);
                    if (apiResult != null)
                        return apiResult;
                }
                
                // FALLBACK: Try configured feeds as last resort
                LogMessage($"[ECCC] Online search failed, checking configured feeds...");
                var feeds = cfg.CityFeeds ?? new Dictionary<string, string>();
                var normalizedCity = NormalizeCity(cityName);
                string? feedUrl = null;
                
                // Try exact match first
                if (feeds.TryGetValue(cityName, out var exactUrl))
                {
                    feedUrl = exactUrl;
                }
                else
                {
                    // Try normalized match
                    feedUrl = feeds.FirstOrDefault(kv => NormalizeCity(kv.Key) == normalizedCity).Value;
                }
                
                if (!string.IsNullOrEmpty(feedUrl))
                {
                    LogMessage($"[ECCC] Using configured feed for {cityName}");
                    var cityIdMatch = System.Text.RegularExpressions.Regex.Match(feedUrl, @"/city/([a-z]{2}-\d+)");
                    if (cityIdMatch.Success)
                    {
                        var cityId = cityIdMatch.Groups[1].Value;
                        var apiResult = await FetchWeatherFromApiAsync(client, cityId);
                        if (apiResult != null)
                            return apiResult;
                    }
                    
                    // Fallback to RSS parsing
                    var xml = await client.GetStringAsync(feedUrl);
                    return ParseEcccWeatherRss(xml);
                }
                
                LogMessage($"[ECCC] City not found in ECCC database or configured feeds: {cityName}");
                return null;
            }
            catch (Exception ex)
            {
                LogMessage($"[ECCC] Error fetching weather for {cityName}: {ex.Message}");
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
            var alertLanguage = string.IsNullOrWhiteSpace(cfg.AlertLanguage) ? "f" : cfg.AlertLanguage.Trim().ToLowerInvariant();

            // If caller supplied a list of desired cities, ensure we have feeds for them (dynamic fallback).
            if (wantedCities != null && wantedCities.Any())
            {
                var wantedList = wantedCities.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                var wantedSet = new HashSet<string>(wantedList.Select(NormalizeCity));
                var feedKeySet = new HashSet<string>(feeds.Keys.Select(NormalizeCity));

                foreach (var city in wantedList)
                {
                    var normalized = NormalizeCity(city);
                    if (feedKeySet.Contains(normalized)) continue;

                    var cityInfo = CityDatabase.GetCityByName(city);
                    if (cityInfo == null)
                    {
                        LogMessage($"[ECCC] No city match for '{city}' in CityDatabase; skipping dynamic alerts feed.");
                        continue;
                    }

                    feeds[city] = cityInfo.GetAlertsFeedUrl(alertLanguage);
                    feedKeySet.Add(normalized);
                }

                // Filter to only wanted cities (normalize names)
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
            
            public CityInfo(string name, string province, string cityCode, double latitude, double longitude)
            {
                Name = name;
                Province = province;
                CityCode = cityCode;
                Latitude = latitude;
                Longitude = longitude;
            }
            
            /// <summary>Gets the ECCC city weather feed URL (French) if available, otherwise alerts feed</summary>
            public string GetCityFeedUrl(string language = "f")
            {
                // If cityCode is "coord", use alerts feed (coordinate-based)
                if (CityCode == "coord")
                    return BuildAlertsUrl(Latitude, Longitude, language);
                    
                return BuildCityWeatherUrl(Province, CityCode, language);
            }
            
            /// <summary>Gets the ECCC alerts feed URL (French)</summary>
            public string GetAlertsFeedUrl(string language = "f") => BuildAlertsUrl(Latitude, Longitude, language);
            
            /// <summary>Indicates if this city uses coordinate-based feed (no city-specific RSS)</summary>
            public bool IsCoordinateBased => CityCode == "coord";
            
            public override string ToString() => Name;
        }

        public class EcccSettings
        {
            public Dictionary<string,string>? CityFeeds { get; set; }
            public Dictionary<string,string>? RadarFeeds { get; set; }
            public string? AlertLanguage { get; set; } = "f";
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
