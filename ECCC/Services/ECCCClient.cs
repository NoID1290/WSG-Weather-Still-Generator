#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using ECCC.Api;
using ECCC.Data;
using ECCC.Models;

namespace ECCC.Services
{
    /// <summary>
    /// Main client for fetching weather data from ECCC.
    /// Handles finding nearby reference cities for small towns.
    /// </summary>
    public class ECCCClient
    {
        private readonly HttpClient _httpClient;
        private readonly EcccSettings _settings;

        public ECCCClient(HttpClient httpClient, EcccSettings? settings = null)
        {
            _httpClient = httpClient;
            _settings = settings ?? new EcccSettings();
            
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                    _settings.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }
        }

        /// <summary>
        /// Fetches weather data for a city by name.
        /// Automatically finds nearby reference cities for small towns.
        /// </summary>
        public async Task<EcccWeatherData?> GetWeatherByNameAsync(string cityName)
        {
            try
            {
                // First, try to find the city in our database
                var city = CityDatabase.GetCityByName(cityName);
                
                if (city != null)
                {
                    // Found in database, fetch weather directly
                    if (!city.IsCoordinateBased)
                    {
                        return await FetchWeatherFromCityAsync(city);
                    }
                }
                
                // City not found or no city code - try to find using geocoding
                // For now, we'll use a simple approach: search nearby cities
                var searchResults = CityDatabase.SearchCities(cityName, 1);
                if (searchResults.Count > 0)
                {
                    var foundCity = searchResults[0];
                    return await FetchWeatherFromCityAsync(foundCity);
                }
                
                return new EcccWeatherData
                {
                    Success = false,
                    ErrorMessage = $"City '{cityName}' not found in ECCC database"
                };
            }
            catch (Exception ex)
            {
                return new EcccWeatherData
                {
                    Success = false,
                    ErrorMessage = $"Error fetching weather: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fetches weather data for a location by coordinates.
        /// Finds the nearest reference city with weather data.
        /// </summary>
        public async Task<EcccWeatherData?> GetWeatherByCoordinatesAsync(
            string locationName, 
            double latitude, 
            double longitude)
        {
            try
            {
                // Find the nearest city with weather data
                var nearestCity = CityDatabase.FindNearestCityWithWeatherData(
                    latitude, 
                    longitude, 
                    _settings.MaxReferenceCityDistanceKm);
                
                if (nearestCity == null)
                {
                    return new EcccWeatherData
                    {
                        Success = false,
                        ErrorMessage = $"No ECCC weather station found within {_settings.MaxReferenceCityDistanceKm}km of {locationName}"
                    };
                }
                
                // Fetch weather from the nearest city
                var weatherData = await FetchWeatherFromCityAsync(nearestCity);
                
                if (weatherData != null && weatherData.Success)
                {
                    // Update the weather data to indicate it's from a reference city
                    weatherData.IsFromReferenceCity = true;
                    weatherData.ReferenceCityName = nearestCity.Name;
                    
                    // Create a custom city info for the requested location
                    weatherData.City = new CityInfo(
                        locationName, 
                        nearestCity.Province, 
                        "coord", 
                        latitude, 
                        longitude)
                    {
                        ParentCityCode = nearestCity.CityCode,
                        DistanceFromParent = nearestCity.DistanceFromParent
                    };
                }
                
                return weatherData;
            }
            catch (Exception ex)
            {
                return new EcccWeatherData
                {
                    Success = false,
                    ErrorMessage = $"Error fetching weather: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fetches weather data directly from an ECCC city feed.
        /// </summary>
        private async Task<EcccWeatherData?> FetchWeatherFromCityAsync(CityInfo city)
        {
            try
            {
                var feedUrl = city.GetCityFeedUrl(_settings.DefaultLanguage);
                if (string.IsNullOrEmpty(feedUrl))
                {
                    return new EcccWeatherData
                    {
                        Success = false,
                        ErrorMessage = $"No weather feed available for {city.Name}"
                    };
                }
                
                var response = await _httpClient.GetStringAsync(feedUrl);
                var weatherData = ParseWeatherRss(response, city, feedUrl);
                
                return weatherData;
            }
            catch (Exception ex)
            {
                return new EcccWeatherData
                {
                    City = city,
                    Success = false,
                    ErrorMessage = $"Error fetching weather: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parses ECCC weather RSS feed.
        /// </summary>
        private EcccWeatherData ParseWeatherRss(string xml, CityInfo city, string sourceUrl)
        {
            var weatherData = new EcccWeatherData
            {
                City = city,
                SourceUrl = sourceUrl,
                FetchTime = DateTime.Now,
                Success = false
            };

            try
            {
                var doc = XDocument.Parse(xml);
                var atom = "http://www.w3.org/2005/Atom";
                var entries = doc.Root?.Elements(XName.Get("entry", atom)) ?? Enumerable.Empty<XElement>();

                weatherData.Current = new WeatherObservation();
                
                foreach (var entry in entries)
                {
                    var title = entry.Element(XName.Get("title", atom))?.Value ?? string.Empty;
                    var summary = entry.Element(XName.Get("summary", atom))?.Value ?? string.Empty;
                    var category = entry.Element(XName.Get("category", atom))?.Attribute("term")?.Value ?? string.Empty;
                    var updated = entry.Element(XName.Get("updated", atom))?.Value;

                    // Parse current conditions
                    if (category.Contains("Current Conditions", StringComparison.OrdinalIgnoreCase) ||
                        category.Contains("Conditions actuelles", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseCurrentConditions(summary, weatherData.Current, updated);
                    }
                    // Parse forecasts
                    else if (category.Contains("Forecast", StringComparison.OrdinalIgnoreCase) ||
                             category.Contains("Prévisions", StringComparison.OrdinalIgnoreCase))
                    {
                        var forecast = ParseForecastEntry(title, summary);
                        if (forecast != null)
                        {
                            weatherData.DailyForecasts.Add(forecast);
                        }
                    }
                }

                weatherData.Success = weatherData.Current != null || weatherData.DailyForecasts.Count > 0;
                
                if (!weatherData.Success)
                {
                    weatherData.ErrorMessage = "No weather data found in RSS feed";
                }
            }
            catch (Exception ex)
            {
                weatherData.ErrorMessage = $"Error parsing RSS: {ex.Message}";
            }

            return weatherData;
        }

        /// <summary>
        /// Parses current conditions from ECCC summary text.
        /// </summary>
        private void ParseCurrentConditions(string summary, WeatherObservation current, string? updatedTime)
        {
            if (updatedTime != null && DateTime.TryParse(updatedTime, out var dt))
            {
                current.ObservationTime = dt;
            }

            // Debug: Log the summary content (first 500 chars)
            System.Diagnostics.Debug.WriteLine($"[ECCC] Parsing conditions, summary length: {summary.Length}");
            System.Diagnostics.Debug.WriteLine($"[ECCC] Summary preview: {(summary.Length > 500 ? summary.Substring(0, 500) : summary)}");

            // Extract temperature - multiple formats from ECCC RSS:
            // Actual format: "<b>Temperature:</b> -1.0&deg;C" or "<b>Température:</b> -1.0&deg;C"
            var tempPatterns = new[]
            {
                @"<b>(?:Temperature|Température)\s*:\s*</b>\s*(-?\d+(?:[.,]\d+)?)\s*(?:&deg;|°)?\s*C",  // Primary format from ECCC
                @"(?:Temperature|Température)\s*:?\s*(-?\d+(?:[.,]\d+)?)\s*(?:&deg;|°)?\s*C",  // Without bold tags
                @"(-?\d+(?:[.,]\d+)?)\s*(?:&deg;|°)\s*C\b",  // General: -5°C, -5&deg;C
            };
            
            foreach (var pattern in tempPatterns)
            {
                var tempMatch = System.Text.RegularExpressions.Regex.Match(
                    summary, 
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (tempMatch.Success)
                {
                    var tempStr = tempMatch.Groups[1].Value.Replace(",", ".");
                    System.Diagnostics.Debug.WriteLine($"[ECCC] Temperature matched with pattern: {pattern}, value: {tempStr}");
                    if (double.TryParse(tempStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double temp))
                    {
                        current.Temperature = temp;
                        System.Diagnostics.Debug.WriteLine($"[ECCC] ✓ Temperature parsed: {temp}°C");
                        break;
                    }
                }
            }
            
            if (!current.Temperature.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("[ECCC] ✗ No temperature found in summary!");
            }

            // Extract wind speed - format: "<b>Wind:</b> NE 28 km/h" or "<b>Vent:</b> NE 28 km/h"
            var windMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"<b>(?:Wind|Vent)\s*:\s*</b>\s*(?:[A-Z]+\s+)?(\d+(?:\.\d+)?)\s*km/h",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (windMatch.Success && double.TryParse(windMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double wind))
            {
                current.WindSpeed = wind;
            }

            // Extract humidity - format: "<b>Humidity:</b> 94 %"
            var humidityMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"<b>(?:Humidity|Humidité)\s*:\s*</b>\s*(\d+)\s*%",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (humidityMatch.Success && double.TryParse(humidityMatch.Groups[1].Value, out double humidity))
            {
                current.Humidity = humidity;
            }

            // Extract pressure - format: "<b>Pressure / Tendency:</b> 101.1 kPa falling"
            var pressureMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"<b>(?:Pressure|Pression)[^<]*:\s*</b>\s*(\d+(?:\.\d+)?)\s*kPa",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (pressureMatch.Success && double.TryParse(pressureMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pressure))
            {
                current.Pressure = pressure * 10; // Convert kPa to hPa
            }

            // Extract condition - format: "<b>Condition:</b> Light Snow and Drifting Snow"
            var conditionMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"<b>Condition\s*:\s*</b>\s*([^<]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (conditionMatch.Success)
            {
                current.Condition = conditionMatch.Groups[1].Value.Trim();
                current.WeatherCode = DetermineWeatherCode(current.Condition);
            }
        }

        /// <summary>
        /// Parses a forecast entry into structured data.
        /// </summary>
        private DailyForecast? ParseForecastEntry(string title, string summary)
        {
            try
            {
                var forecast = new DailyForecast
                {
                    Period = title,
                    Summary = summary
                };

                // Extract date
                var today = DateTime.Today;
                forecast.Date = today.ToString("yyyy-MM-dd");
                
                // Try to determine which day this is
                var dayNames = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
                                       "Dimanche", "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi" };
                foreach (var day in dayNames)
                {
                    if (title.StartsWith(day, StringComparison.OrdinalIgnoreCase))
                    {
                        var targetDay = Array.FindIndex(new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
                            d => d.Equals(day, StringComparison.OrdinalIgnoreCase) || 
                                 GetFrenchDayName(d).Equals(day, StringComparison.OrdinalIgnoreCase));
                        if (targetDay >= 0)
                        {
                            var daysUntil = (targetDay - (int)today.DayOfWeek + 7) % 7;
                            forecast.Date = today.AddDays(daysUntil).ToString("yyyy-MM-dd");
                        }
                        break;
                    }
                }

                // Extract temperatures
                var highMatch = System.Text.RegularExpressions.Regex.Match(
                    title + " " + summary,
                    @"(?:High|Maximum)\s*:?\s*(-?\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (highMatch.Success && double.TryParse(highMatch.Groups[1].Value, out double high))
                {
                    forecast.HighTemperature = high;
                }

                var lowMatch = System.Text.RegularExpressions.Regex.Match(
                    title + " " + summary,
                    @"(?:Low|Minimum)\s*:?\s*(-?\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lowMatch.Success && double.TryParse(lowMatch.Groups[1].Value, out double low))
                {
                    forecast.LowTemperature = low;
                }

                // Extract condition
                var condMatch = System.Text.RegularExpressions.Regex.Match(
                    title,
                    @":\s*([^,\.]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (condMatch.Success)
                {
                    forecast.Condition = condMatch.Groups[1].Value.Trim();
                    forecast.WeatherCode = DetermineWeatherCode(forecast.Condition);
                }

                return forecast;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines WMO weather code from text description.
        /// </summary>
        private int DetermineWeatherCode(string text)
        {
            var lower = text.ToLowerInvariant();
            
            if (lower.Contains("thunder") || lower.Contains("orage")) return 95;
            if (lower.Contains("heavy snow") || lower.Contains("forte neige")) return 75;
            if (lower.Contains("snow") || lower.Contains("neige")) return 71;
            if (lower.Contains("flurries") || lower.Contains("averses de neige")) return 73;
            if (lower.Contains("freezing rain") || lower.Contains("pluie verglaçante")) return 67;
            if (lower.Contains("ice pellets") || lower.Contains("grésil")) return 79;
            if (lower.Contains("heavy rain") || lower.Contains("forte pluie")) return 65;
            if (lower.Contains("rain") || lower.Contains("pluie")) return 61;
            if (lower.Contains("drizzle") || lower.Contains("bruine")) return 51;
            if (lower.Contains("showers") || lower.Contains("averses")) return 80;
            if (lower.Contains("fog") || lower.Contains("brouillard")) return 45;
            if (lower.Contains("mist") || lower.Contains("brume")) return 45;
            if (lower.Contains("overcast") || lower.Contains("couvert")) return 3;
            if (lower.Contains("mostly cloudy") || lower.Contains("généralement nuageux")) return 3;
            if (lower.Contains("cloudy") || lower.Contains("nuageux")) return 2;
            if (lower.Contains("partly cloudy") || lower.Contains("partiellement nuageux")) return 2;
            if (lower.Contains("a few clouds") || lower.Contains("quelques nuages")) return 1;
            if (lower.Contains("sunny") || lower.Contains("ensoleillé")) return 0;
            if (lower.Contains("clear") || lower.Contains("dégagé")) return 0;
            
            return 0;
        }

        private string GetFrenchDayName(string englishDay) => englishDay switch
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
    }
}
