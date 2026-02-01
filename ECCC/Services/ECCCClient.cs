#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using ECCC.Api;
using ECCC.Data;
using ECCC.Models;

namespace ECCC.Services
{
    /// <summary>
    /// Main client for fetching weather data from ECCC.
    /// Uses the OGC API (JSON) as primary source, with RSS as fallback.
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
        /// Uses OGC API first, falls back to RSS if needed.
        /// Uses OGC API first, falls back to RSS if needed.
        /// Automatically finds nearby reference cities for small towns.
        /// </summary>
        public async Task<EcccWeatherData?> GetWeatherByNameAsync(string cityName)
        {
            try
            {
                // First, try to find the city in our database
                var city = CityDatabase.GetCityByName(cityName);
                
                if (city == null)
                {
                    // City not found - try to find using search
                    var searchResults = CityDatabase.SearchCities(cityName, 1);
                    if (searchResults.Count > 0)
                    {
                        city = searchResults[0];
                    }
                }
                
                if (city == null)
                {
                    return new EcccWeatherData
                    {
                        Success = false,
                        ErrorMessage = $"City '{cityName}' not found in ECCC database"
                    };
                }
                
                // Log the city info for debugging
                System.Diagnostics.Debug.WriteLine($"[ECCC] Found city: {city.Name}, code: {city.Province}-{city.CityCode}, coord-based: {city.IsCoordinateBased}");
                
                // Try the OGC API first (more reliable, returns JSON)
                if (!city.IsCoordinateBased)
                {
                    var cityId = $"{city.Province.ToLower()}-{city.CityCode}";
                    System.Diagnostics.Debug.WriteLine($"[ECCC] Fetching API for cityId: {cityId}");
                    var apiResult = await FetchWeatherFromApiAsync(city, cityId);
                    if (apiResult != null && apiResult.Success)
                    {
                        return apiResult;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[ECCC] API failed, falling back to RSS for {cityId}");
                    // Fall back to RSS if API fails
                    return await FetchWeatherFromRssAsync(city);
                }
                
                // For coordinate-based cities, find nearest reference city
                var nearestCity = CityDatabase.FindNearestCityWithWeatherData(
                    city.Latitude, city.Longitude, _settings.MaxReferenceCityDistanceKm);
                    
                if (nearestCity != null)
                {
                    var cityId = $"{nearestCity.Province.ToLower()}-{nearestCity.CityCode}";
                    var apiResult = await FetchWeatherFromApiAsync(nearestCity, cityId);
                    if (apiResult != null && apiResult.Success)
                    {
                        apiResult.IsFromReferenceCity = true;
                        apiResult.ReferenceCityName = nearestCity.Name;
                        apiResult.City = city;
                        return apiResult;
                    }
                }
                
                return new EcccWeatherData
                {
                    Success = false,
                    ErrorMessage = $"No weather data available for '{cityName}'"
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
        /// Uses OGC API first, falls back to RSS if needed.
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
                
                // Try API first, then RSS as fallback
                var cityId = $"{nearestCity.Province.ToLower()}-{nearestCity.CityCode}";
                var weatherData = await FetchWeatherFromApiAsync(nearestCity, cityId);
                
                if (weatherData == null || !weatherData.Success)
                {
                    weatherData = await FetchWeatherFromRssAsync(nearestCity);
                }
                
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
        /// Fetches weather data from the ECCC OGC API (JSON format).
        /// This is the primary method - more reliable than RSS feeds.
        /// </summary>
        private async Task<EcccWeatherData?> FetchWeatherFromApiAsync(CityInfo city, string cityId)
        {
            try
            {
                var url = $"{UrlBuilder.BaseApiUrl}/collections/citypageweather-realtime/items?f=json&identifier={cityId}&limit=1";
                
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                
                var jsonResponse = await response.Content.ReadAsStringAsync();
                return ParseApiJsonResponse(jsonResponse, city, url);
            }
            catch
            {
                return null; // Silent fail, will fall back to RSS
            }
        }

        /// <summary>
        /// Fetches weather data from ECCC RSS feed (fallback method).
        /// </summary>
        private async Task<EcccWeatherData?> FetchWeatherFromRssAsync(CityInfo city)
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
                
                var response = await _httpClient.GetAsync(feedUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return new EcccWeatherData
                    {
                        City = city,
                        Success = false,
                        ErrorMessage = $"RSS feed returned {response.StatusCode}"
                    };
                }
                
                var content = await response.Content.ReadAsStringAsync();
                return ParseWeatherRss(content, city, feedUrl);
            }
            catch (Exception ex)
            {
                return new EcccWeatherData
                {
                    City = city,
                    Success = false,
                    ErrorMessage = $"Error fetching RSS: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parses ECCC OGC API JSON response.
        /// Handles both direct values and language-nested values (en/fr).
        /// </summary>
        private EcccWeatherData ParseApiJsonResponse(string json, CityInfo city, string sourceUrl)
        {
            var weatherData = new EcccWeatherData
            {
                City = city,
                SourceUrl = sourceUrl,
                FetchTime = DateTime.Now,
                Success = false,
                Current = new WeatherObservation()
            };

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // Navigate to the feature properties
                if (!root.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
                {
                    weatherData.ErrorMessage = "No features in API response";
                    return weatherData;
                }
                    
                var feature = features[0];
                if (!feature.TryGetProperty("properties", out var props))
                {
                    weatherData.ErrorMessage = "No properties in API response";
                    return weatherData;
                }
                
                // Extract current conditions
                if (props.TryGetProperty("currentConditions", out var currentCond))
                {
                    // Temperature and dewpoint use the standard structure
                    weatherData.Current.Temperature = ExtractDoubleValue(currentCond, "temperature");
                    weatherData.Current.DewPoint = ExtractDoubleValue(currentCond, "dewpoint");
                    
                    // Humidity is named "relativeHumidity" in the API
                    weatherData.Current.Humidity = ExtractDoubleValue(currentCond, "relativeHumidity");
                    
                    // Visibility (may not always be present)
                    weatherData.Current.Visibility = ExtractDoubleValue(currentCond, "visibility");
                    
                    // Pressure needs conversion from kPa to hPa
                    var pressure = ExtractDoubleValue(currentCond, "pressure");
                    if (pressure.HasValue)
                        weatherData.Current.Pressure = pressure.Value * 10;
                    
                    // Wind data is nested under "wind" object
                    if (currentCond.TryGetProperty("wind", out var windObj))
                    {
                        weatherData.Current.WindSpeed = ExtractDoubleValue(windObj, "speed");
                        weatherData.Current.WindGust = ExtractDoubleValue(windObj, "gust");
                        weatherData.Current.WindDirection = ExtractStringValue(windObj, "direction");
                    }
                    
                    // Icon code is nested: iconCode.value (number)
                    if (currentCond.TryGetProperty("iconCode", out var iconCodeObj) &&
                        iconCodeObj.TryGetProperty("value", out var iconVal))
                    {
                        if (iconVal.ValueKind == JsonValueKind.Number)
                            weatherData.Current.WeatherCode = ParseIconCodeToWmoCode(iconVal.GetInt32().ToString());
                    }
                    
                    // Condition text is directly a language object
                    weatherData.Current.Condition = ExtractStringFromElement(currentCond, "condition");
                    
                    // Parse observation time from "timestamp" (language object)
                    var timestampStr = ExtractStringFromElement(currentCond, "timestamp");
                    if (!string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, out var obsTime))
                    {
                        weatherData.Current.ObservationTime = obsTime;
                    }
                }
                
                // Extract daily forecasts - try "forecasts" (plural, OGC API) or "forecast" (singular, older format)
                JsonElement? forecastArray = null;
                if (props.TryGetProperty("forecastGroup", out var forecastGrp))
                {
                    if (forecastGrp.TryGetProperty("forecasts", out var fcs))
                        forecastArray = fcs;
                    else if (forecastGrp.TryGetProperty("forecast", out var fc))
                        forecastArray = fc;
                }
                
                if (forecastArray.HasValue && forecastArray.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fc in forecastArray.Value.EnumerateArray())
                    {
                        try
                        {
                            var dailyForecast = new DailyForecast();
                            
                            var periodName = ExtractStringValue(fc, "period", "textForecastName");
                            dailyForecast.Period = periodName ?? "";
                            
                            dailyForecast.Summary = ExtractStringValue(fc, "textSummary") ?? "";
                            
                            // Extract temperature - handle array or single object
                            if (fc.TryGetProperty("temperatures", out var temps) &&
                                temps.TryGetProperty("temperature", out var tempField))
                            {
                                // Temperature can be an array, object, or empty string
                                if (tempField.ValueKind == JsonValueKind.Array)
                                {
                                    // Array of temperatures (e.g., high and low)
                                    foreach (var tempObj in tempField.EnumerateArray())
                                    {
                                        var tempValue = ExtractDoubleFromElement(tempObj, "value");
                                        if (tempValue.HasValue)
                                        {
                                            var clsStr = ExtractStringFromElement(tempObj, "class") ?? "";
                                            if (clsStr.Contains("low", StringComparison.OrdinalIgnoreCase))
                                                dailyForecast.LowTemperature = tempValue;
                                            else
                                                dailyForecast.HighTemperature = tempValue;
                                        }
                                    }
                                }
                                else if (tempField.ValueKind == JsonValueKind.Object)
                                {
                                    // Single temperature object
                                    var tempValue = ExtractDoubleFromElement(tempField, "value");
                                    if (tempValue.HasValue)
                                    {
                                        var clsStr = ExtractStringFromElement(tempField, "class") ?? "";
                                        if (clsStr.Contains("low", StringComparison.OrdinalIgnoreCase))
                                            dailyForecast.LowTemperature = tempValue;
                                        else
                                            dailyForecast.HighTemperature = tempValue;
                                    }
                                }
                                // If empty string or null, skip temperature extraction
                            }
                            
                            // Extract icon code - located at abbreviatedForecast.icon.value (number)
                            if (fc.TryGetProperty("abbreviatedForecast", out var abbrev) &&
                                abbrev.TryGetProperty("icon", out var iconObj) &&
                                iconObj.TryGetProperty("value", out var iconValue))
                            {
                                // Icon value is a number (not a language object)
                                if (iconValue.ValueKind == JsonValueKind.Number)
                                {
                                    dailyForecast.WeatherCode = ParseIconCodeToWmoCode(iconValue.GetInt32().ToString());
                                }
                            }
                            
                            weatherData.DailyForecasts.Add(dailyForecast);
                        }
                        catch
                        {
                            // Skip malformed forecast entries silently
                        }
                    }
                }
                
                weatherData.Success = weatherData.Current.Temperature.HasValue || weatherData.DailyForecasts.Count > 0;
                
                if (!weatherData.Success)
                {
                    weatherData.ErrorMessage = "No weather data parsed from API response";
                }
            }
            catch (Exception ex)
            {
                weatherData.ErrorMessage = $"Error parsing API JSON: {ex.Message}";
            }

            return weatherData;
        }

        /// <summary>
        /// Extracts a double value from a property, handling both direct numbers and language-nested values.
        /// </summary>
        private double? ExtractDoubleValue(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var prop))
                return null;
            return ExtractDoubleFromElement(prop, "value");
        }

        /// <summary>
        /// Extracts a double from an element's property, handling both direct numbers and language-nested values.
        /// </summary>
        private double? ExtractDoubleFromElement(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var valueElem))
                return null;
            
            // Direct number
            if (valueElem.ValueKind == JsonValueKind.Number)
                return valueElem.GetDouble();
            
            // Language-nested object (try "en" then "fr")
            if (valueElem.ValueKind == JsonValueKind.Object)
            {
                if (valueElem.TryGetProperty("en", out var enVal) && enVal.ValueKind == JsonValueKind.Number)
                    return enVal.GetDouble();
                if (valueElem.TryGetProperty("fr", out var frVal) && frVal.ValueKind == JsonValueKind.Number)
                    return frVal.GetDouble();
            }
            
            return null;
        }

        /// <summary>
        /// Extracts a string value from a property, handling both direct strings and language-nested values.
        /// </summary>
        private string? ExtractStringValue(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var prop))
                return null;
            return ExtractStringFromElement(prop);
        }

        /// <summary>
        /// Extracts a string value from a nested property path.
        /// </summary>
        private string? ExtractStringValue(JsonElement parent, string propertyName, string nestedPropertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var prop))
                return null;
            return ExtractStringFromElement(prop, nestedPropertyName);
        }

        /// <summary>
        /// Extracts a string from an element, handling both direct strings and language-nested values.
        /// </summary>
        private string? ExtractStringFromElement(JsonElement element)
        {
            // Direct string
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();
            
            // Try "value" property first
            if (element.TryGetProperty("value", out var valueElem))
                return ExtractStringFromElement(valueElem);
            
            // Language-nested object (try French then English for this app)
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("fr", out var frVal) && frVal.ValueKind == JsonValueKind.String)
                    return frVal.GetString();
                if (element.TryGetProperty("en", out var enVal) && enVal.ValueKind == JsonValueKind.String)
                    return enVal.GetString();
            }
            
            return null;
        }

        /// <summary>
        /// Extracts a string from an element's nested property, handling both direct strings and language-nested values.
        /// </summary>
        private string? ExtractStringFromElement(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;
            return ExtractStringFromElement(prop);
        }

        /// <summary>
        /// Converts ECCC icon codes to WMO weather codes.
        /// </summary>
        private int ParseIconCodeToWmoCode(string? iconCode)
        {
            if (string.IsNullOrEmpty(iconCode)) return 0;
            
            // ECCC icon codes: https://dd.weather.gc.ca/observations/doc/icon_code_descriptions_e.csv
            return iconCode switch
            {
                "00" or "01" => 0,   // Sunny / Clear
                "02" or "03" => 1,   // Mainly Sunny / Partly Cloudy
                "04" or "05" => 2,   // A mix of sun and cloud / Mostly Cloudy
                "06" or "07" or "08" => 3, // Overcast / Fog
                "09" => 45,          // Fog
                "10" => 3,           // Overcast
                "11" or "12" or "13" => 51, // Light rain / Drizzle
                "14" or "15" => 61,  // Rain / Heavy Rain
                "16" or "17" => 67,  // Freezing Rain
                "18" or "19" => 71,  // Snow
                "20" or "21" => 73,  // Light Snow / Flurries
                "22" or "23" => 75,  // Heavy Snow
                "24" or "25" => 79,  // Ice Pellets
                "26" or "27" => 95,  // Thunderstorms
                "28" => 80,          // Showers
                "30" or "31" or "32" or "33" => 0,  // Night clear variants
                "34" or "35" or "36" or "37" or "38" => 2, // Night cloudy variants
                "39" or "40" => 61,  // Night rain
                "41" or "42" or "43" or "44" or "45" or "46" or "47" => 71, // Night snow variants
                _ => 0
            };
        }

        /// <summary>
        /// Fetches weather data directly from an ECCC city feed (legacy method).
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

            // Extract wind speed and direction - formats:
            // English: "<b>Wind:</b> WSW 24 km/h gust 35 km/h"
            // French: "<b>Vents:</b> OSO 24 km/h rafale 35 km/h"
            var windMatch = System.Text.RegularExpressions.Regex.Match(
                summary, 
                @"<b>(?:Wind|Vents?)\s*:\s*</b>\s*([A-Z]+)\s+(\d+(?:\.\d+)?)\s*km/h",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (windMatch.Success)
            {
                current.WindDirection = windMatch.Groups[1].Value.ToUpperInvariant();
                if (double.TryParse(windMatch.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double wind))
                {
                    current.WindSpeed = wind;
                }
            }

            // Extract wind gusts if available - format: "gust 35 km/h" or "rafale 35 km/h"
            var gustMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"(?:gust|rafale)\s*(\d+(?:\.\d+)?)\s*km/h",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (gustMatch.Success && double.TryParse(gustMatch.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double gust))
            {
                current.WindGust = gust;
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

            // Extract visibility - format: "<b>Visibility:</b> 24 km" or "<b>Visibilité:</b> 24 km"
            var visibilityMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"<b>(?:Visibility|Visibilité)\s*:\s*</b>\s*(\d+(?:[.,]\d+)?)\s*km",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (visibilityMatch.Success)
            {
                var visStr = visibilityMatch.Groups[1].Value.Replace(",", ".");
                if (double.TryParse(visStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double visibility))
                {
                    current.Visibility = visibility;
                }
            }

            // Extract dew point - format: "<b>Dewpoint:</b> -5.7&deg;C" or "<b>Point de rosée:</b> -5,7&deg;C"
            var dewPointMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"<b>(?:Dewpoint|Point de rosée)\s*:\s*</b>\s*(-?\d+(?:[.,]\d+)?)\s*(?:&deg;|°)?\s*C",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (dewPointMatch.Success)
            {
                var dewStr = dewPointMatch.Groups[1].Value.Replace(",", ".");
                if (double.TryParse(dewStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dewPoint))
                {
                    current.DewPoint = dewPoint;
                }
            }

            // Extract wind chill (winter) - format: "<b>Wind Chill:</b> -15" or inline in current conditions
            var windChillMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"<b>(?:Wind\s*[Cc]hill|Refroidissement\s*[ée]olien)\s*:\s*</b>\s*(-?\d+(?:[.,]\d+)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (windChillMatch.Success)
            {
                var wcStr = windChillMatch.Groups[1].Value.Replace(",", ".");
                if (double.TryParse(wcStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double windChill))
                {
                    current.WindChill = windChill;
                }
            }

            // Extract humidex (summer) - format: "<b>Humidex:</b> 35"
            var humidexMatch = System.Text.RegularExpressions.Regex.Match(
                summary,
                @"<b>Humidex\s*:\s*</b>\s*(\d+(?:[.,]\d+)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (humidexMatch.Success)
            {
                var hxStr = humidexMatch.Groups[1].Value.Replace(",", ".");
                if (double.TryParse(hxStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double humidex))
                {
                    current.Humidex = humidex;
                }
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
                
                var lowerTitle = title.ToLowerInvariant();
                
                // Handle "Today", "Tonight", "This afternoon", etc. - all map to today
                if (lowerTitle.StartsWith("today") || lowerTitle.StartsWith("tonight") || 
                    lowerTitle.StartsWith("this ") || lowerTitle.StartsWith("aujourd'hui") ||
                    lowerTitle.StartsWith("ce soir") || lowerTitle.StartsWith("cet "))
                {
                    forecast.Date = today.ToString("yyyy-MM-dd");
                }
                else
                {
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
                                // If daysUntil is 0, it could be today or next week - assume today for current week
                                forecast.Date = today.AddDays(daysUntil).ToString("yyyy-MM-dd");
                            }
                            break;
                        }
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
