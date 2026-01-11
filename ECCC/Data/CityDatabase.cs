#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ECCC.Models;

namespace ECCC.Data
{
    /// <summary>
    /// Database of known ECCC cities with their province and city codes.
    /// Includes reference city mapping for small towns.
    /// </summary>
    public static class CityDatabase
    {
        private static readonly List<CityInfo> _cities;
        private static readonly Dictionary<string, CityInfo> _cityByNormalizedName;
        
        static CityDatabase()
        {
            _cities = BuildCityList();
            // Use GroupBy to handle duplicates (keep first entry for each normalized name)
            _cityByNormalizedName = _cities
                .GroupBy(c => NormalizeCity(c.Name))
                .ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>
        /// Gets all known cities.
        /// </summary>
        public static IReadOnlyList<CityInfo> GetAllCities() => _cities;

        /// <summary>
        /// Searches for cities matching the query.
        /// </summary>
        public static List<CityInfo> SearchCities(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<CityInfo>();
            
            var normalizedQuery = NormalizeCity(query);
            var results = new List<CityInfo>();
            
            // Exact match first
            if (_cityByNormalizedName.TryGetValue(normalizedQuery, out var exactMatch))
            {
                results.Add(exactMatch);
            }
            
            // Partial matches
            foreach (var city in _cities)
            {
                if (results.Count >= maxResults) break;
                if (results.Contains(city)) continue;
                
                var normalizedCity = NormalizeCity(city.Name);
                if (normalizedCity.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedCity))
                {
                    results.Add(city);
                }
            }
            
            return results;
        }

        /// <summary>
        /// Gets a city by exact name (normalized comparison).
        /// </summary>
        public static CityInfo? GetCityByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var normalized = NormalizeCity(name);
            return _cityByNormalizedName.TryGetValue(normalized, out var city) ? city : null;
        }

        /// <summary>
        /// Finds the nearest known ECCC city with weather data for a given location.
        /// Useful for small towns that don't have their own ECCC station.
        /// </summary>
        public static CityInfo? FindNearestCityWithWeatherData(double latitude, double longitude, double maxDistanceKm = 100)
        {
            CityInfo? nearest = null;
            double nearestDistance = double.MaxValue;
            
            foreach (var city in _cities)
            {
                // Only consider cities with actual city codes (not coordinate-based)
                if (city.IsCoordinateBased) continue;
                
                var distance = CalculateDistanceKm(latitude, longitude, city.Latitude, city.Longitude);
                if (distance < nearestDistance && distance <= maxDistanceKm)
                {
                    nearestDistance = distance;
                    nearest = city;
                }
            }
            
            if (nearest != null)
            {
                // Create a copy with distance info
                var result = new CityInfo(nearest.Name, nearest.Province, nearest.CityCode, nearest.Latitude, nearest.Longitude)
                {
                    DistanceFromParent = nearestDistance
                };
                return result;
            }
            
            return null;
        }

        /// <summary>
        /// Creates a CityInfo for a location by finding the nearest reference city.
        /// </summary>
        public static CityInfo? CreateCityForLocation(string name, double latitude, double longitude, double maxDistanceKm = 50)
        {
            var nearestCity = FindNearestCityWithWeatherData(latitude, longitude, maxDistanceKm);
            if (nearestCity == null) return null;
            
            return new CityInfo(name, nearestCity.Province, "coord", latitude, longitude)
            {
                ParentCityCode = nearestCity.CityCode,
                DistanceFromParent = nearestCity.DistanceFromParent
            };
        }

        /// <summary>
        /// Calculates the distance between two coordinates using Haversine formula.
        /// </summary>
        public static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Earth's radius in km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;

        /// <summary>
        /// Normalizes city names for comparison (removes diacritics, lowercase, trim).
        /// </summary>
        public static string NormalizeCity(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var normalized = s.Normalize(NormalizationForm.FormD);
            var filtered = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return filtered.Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
        }

        /// <summary>
        /// Gets the province code from a province/state name.
        /// </summary>
        public static string GetProvinceCode(string? provinceName)
        {
            if (string.IsNullOrWhiteSpace(provinceName)) return "qc";
            
            var normalized = provinceName.ToLowerInvariant();
            
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
            
            return "qc";
        }

        private static List<CityInfo> BuildCityList()
        {
            return new List<CityInfo>
            {
                // Quebec (Major cities with ECCC weather stations)
                new CityInfo("Montréal", "qc", "147", 45.5017, -73.5673),
                new CityInfo("Montreal", "qc", "147", 45.5017, -73.5673),
                new CityInfo("Québec", "qc", "133", 46.8139, -71.2080),
                new CityInfo("Quebec City", "qc", "133", 46.8139, -71.2080),
                new CityInfo("Laval", "qc", "147", 45.6066, -73.7124),
                new CityInfo("Gatineau", "qc", "59", 45.4765, -75.7013),
                new CityInfo("Longueuil", "qc", "147", 45.5312, -73.5186),
                new CityInfo("Sherbrooke", "qc", "30", 45.4042, -71.8929),
                new CityInfo("Saguenay", "qc", "50", 48.4284, -71.0656),
                new CityInfo("Chicoutimi", "qc", "50", 48.4284, -71.0656),
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
                new CityInfo("Rivière-du-Loup", "qc", "58", 47.8333, -69.5333),
                new CityInfo("Saint-Georges", "qc", "133", 46.1167, -70.6667),
                new CityInfo("Mirabel", "qc", "147", 45.6500, -74.0833),
                new CityInfo("Châteauguay", "qc", "147", 45.3833, -73.7500),
                new CityInfo("Magog", "qc", "30", 45.2667, -72.1500),
                new CityInfo("Mont-Laurier", "qc", "20", 46.5500, -75.5000),
                new CityInfo("Matane", "qc", "58", 48.8500, -67.5333),
                new CityInfo("Gaspé", "qc", "58", 48.8333, -64.4833),
                new CityInfo("La Tuque", "qc", "48", 47.4333, -72.7833),
                new CityInfo("Mont-Tremblant", "qc", "20", 46.1167, -74.5833),
                
                // Montérégie region (South Shore Montreal area)
                new CityInfo("Saint-Constant", "qc", "147", 45.3667, -73.5667),
                new CityInfo("Candiac", "qc", "147", 45.3833, -73.5167),
                new CityInfo("La Prairie", "qc", "147", 45.4167, -73.5000),
                new CityInfo("Sainte-Catherine", "qc", "147", 45.4000, -73.5833),
                new CityInfo("Saint-Philippe", "qc", "147", 45.3500, -73.4667),
                new CityInfo("Mercier", "qc", "147", 45.3167, -73.7500),
                new CityInfo("Saint-Rémi", "qc", "147", 45.2667, -73.6167),
                new CityInfo("Delson", "qc", "147", 45.3667, -73.5500),
                new CityInfo("Kahnawake", "qc", "147", 45.4000, -73.6833),
                
                // Abitibi region
                new CityInfo("La Sarre", "qc", "96", 48.8000, -79.2000),
                new CityInfo("Senneterre", "qc", "95", 48.3833, -77.2333),
                new CityInfo("Malartic", "qc", "95", 48.1333, -78.1333),
                new CityInfo("Berry", "qc", "96", 48.8167, -78.2667),
                
                // Ontario (Major cities)
                new CityInfo("Toronto", "on", "143", 43.6532, -79.3832),
                new CityInfo("Ottawa", "on", "118", 45.4215, -75.6972),
                new CityInfo("Mississauga", "on", "143", 43.5890, -79.6441),
                new CityInfo("Brampton", "on", "143", 43.7315, -79.7624),
                new CityInfo("Hamilton", "on", "77", 43.2557, -79.8711),
                new CityInfo("London", "on", "72", 42.9849, -81.2453),
                new CityInfo("Kitchener", "on", "48", 43.4516, -80.4925),
                new CityInfo("Windsor", "on", "94", 42.3149, -83.0364),
                new CityInfo("Greater Sudbury", "on", "40", 46.4917, -80.9930),
                new CityInfo("Barrie", "on", "18", 44.3894, -79.6903),
                new CityInfo("Kingston", "on", "69", 44.2312, -76.4860),
                new CityInfo("Thunder Bay", "on", "100", 48.3809, -89.2477),
                new CityInfo("Niagara Falls", "on", "89", 43.0896, -79.0849),
                new CityInfo("Peterborough", "on", "81", 44.3091, -78.3197),
                new CityInfo("North Bay", "on", "74", 46.3091, -79.4608),
                new CityInfo("Timmins", "on", "101", 48.4758, -81.3304),
                
                // British Columbia
                new CityInfo("Vancouver", "bc", "133", 49.2827, -123.1207),
                new CityInfo("Surrey", "bc", "133", 49.1913, -122.8490),
                new CityInfo("Victoria", "bc", "85", 48.4284, -123.3656),
                new CityInfo("Kelowna", "bc", "48", 49.8880, -119.4960),
                new CityInfo("Kamloops", "bc", "37", 50.6745, -120.3273),
                new CityInfo("Nanaimo", "bc", "10", 49.1659, -123.9401),
                new CityInfo("Prince George", "bc", "71", 53.9171, -122.7497),
                
                // Alberta
                new CityInfo("Calgary", "ab", "52", 51.0447, -114.0719),
                new CityInfo("Edmonton", "ab", "50", 53.5461, -113.4938),
                new CityInfo("Red Deer", "ab", "34", 52.2681, -113.8111),
                new CityInfo("Lethbridge", "ab", "94", 49.6942, -112.8328),
                new CityInfo("Medicine Hat", "ab", "31", 50.0417, -110.6775),
                new CityInfo("Grande Prairie", "ab", "97", 55.1707, -118.7947),
                new CityInfo("Fort McMurray", "ab", "50", 56.7267, -111.3800),
                new CityInfo("Banff", "ab", "52", 51.1784, -115.5708),
                new CityInfo("Jasper", "ab", "50", 52.8737, -118.0814),
                
                // Manitoba
                new CityInfo("Winnipeg", "mb", "38", 49.8951, -97.1384),
                new CityInfo("Brandon", "mb", "83", 49.8483, -99.9501),
                new CityInfo("Thompson", "mb", "38", 55.7433, -97.8553),
                new CityInfo("Churchill", "mb", "38", 58.7684, -94.1647),
                
                // Saskatchewan
                new CityInfo("Saskatoon", "sk", "40", 52.1332, -106.6700),
                new CityInfo("Regina", "sk", "32", 50.4452, -104.6189),
                new CityInfo("Prince Albert", "sk", "59", 53.2033, -105.7531),
                new CityInfo("Moose Jaw", "sk", "32", 50.3933, -105.5519),
                
                // Nova Scotia
                new CityInfo("Halifax", "ns", "12", 44.6488, -63.5752),
                new CityInfo("Sydney", "ns", "7", 46.1364, -60.1942),
                new CityInfo("Dartmouth", "ns", "12", 44.6711, -63.5764),
                
                // New Brunswick
                new CityInfo("Moncton", "nb", "36", 46.0878, -64.7782),
                new CityInfo("Saint John", "nb", "40", 45.2733, -66.0633),
                new CityInfo("Fredericton", "nb", "29", 45.9636, -66.6431),
                
                // Newfoundland and Labrador
                new CityInfo("St. John's", "nl", "24", 47.5615, -52.7126),
                new CityInfo("Corner Brook", "nl", "8", 48.9501, -57.9522),
                new CityInfo("Gander", "nl", "13", 48.9564, -54.6089),
                new CityInfo("Labrador City", "nl", "52", 52.9425, -66.9119),
                
                // PEI
                new CityInfo("Charlottetown", "pe", "5", 46.2382, -63.1311),
                new CityInfo("Summerside", "pe", "5", 46.3950, -63.7883),
                
                // Territories
                new CityInfo("Whitehorse", "yt", "50", 60.7212, -135.0568),
                new CityInfo("Yellowknife", "nt", "24", 62.4540, -114.3718),
                new CityInfo("Iqaluit", "nu", "21", 63.7467, -68.5170),
            };
        }
    }
}
