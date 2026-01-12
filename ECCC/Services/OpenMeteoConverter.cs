#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ECCC.Models;

namespace ECCC.Services
{
    /// <summary>
    /// Converts ECCC weather data to OpenMeteo format for compatibility.
    /// </summary>
    public static class OpenMeteoConverter
    {
        /// <summary>
        /// Converts ECCC weather data to OpenMeteo WeatherForecast format.
        /// </summary>
        public static OpenMeteo.WeatherForecast? ToOpenMeteoFormat(EcccWeatherData? ecccData)
        {
            if (ecccData == null || !ecccData.Success)
                return null;

            var forecast = new OpenMeteo.WeatherForecast
            {
                Timezone = "America/Toronto",
                TimezoneAbbreviation = "EST",
                Latitude = (float)(ecccData.City?.Latitude ?? 0),
                Longitude = (float)(ecccData.City?.Longitude ?? 0)
            };

            // Convert current conditions
            if (ecccData.Current != null)
            {
                // Determine apparent (feels like) temperature:
                // - Use WindChill if available (winter, when temp < 10°C and windy)
                // - Use Humidex if available (summer, when temp > 20°C and humid)
                // - Calculate from temperature, humidity, and wind if neither available
                float? apparentTemp = null;
                if (ecccData.Current.WindChill.HasValue)
                {
                    apparentTemp = (float)ecccData.Current.WindChill.Value;
                }
                else if (ecccData.Current.Humidex.HasValue)
                {
                    apparentTemp = (float)ecccData.Current.Humidex.Value;
                }
                else if (ecccData.Current.Temperature.HasValue)
                {
                    // Calculate approximate apparent temperature if not provided by ECCC
                    apparentTemp = CalculateApparentTemperature(
                        ecccData.Current.Temperature.Value,
                        ecccData.Current.Humidity ?? 50,
                        ecccData.Current.WindSpeed ?? 0);
                }

                // Convert wind direction to degrees
                int? windDirection = null;
                if (!string.IsNullOrEmpty(ecccData.Current.WindDirection))
                {
                    windDirection = DirectionToDegrees(ecccData.Current.WindDirection);
                }

                // Estimate cloud cover from weather condition
                int cloudCover = EstimateCloudCover(ecccData.Current.Condition, ecccData.Current.WeatherCode);

                forecast.Current = new OpenMeteo.Current
                {
                    Temperature_2m = ecccData.Current.Temperature.HasValue ? (float)ecccData.Current.Temperature.Value : float.NaN,
                    Apparent_temperature = apparentTemp,
                    Relativehumidity_2m = ecccData.Current.Humidity.HasValue ? (int)Math.Round(ecccData.Current.Humidity.Value) : 0,
                    Windspeed_10m = ecccData.Current.WindSpeed.HasValue ? (float)Math.Round(ecccData.Current.WindSpeed.Value, 1) : 0,
                    Windgusts_10m = ecccData.Current.WindGust.HasValue ? (float)Math.Round(ecccData.Current.WindGust.Value, 1) : null,
                    Winddirection_10m = windDirection,
                    Surface_pressure = ecccData.Current.Pressure.HasValue ? (float)Math.Round(ecccData.Current.Pressure.Value, 1) : 0,
                    Pressure_msl = ecccData.Current.Pressure.HasValue ? (float)Math.Round(ecccData.Current.Pressure.Value, 1) : 0,
                    Cloudcover = cloudCover,
                    Weathercode = ecccData.Current.WeatherCode ?? 0,
                    Is_day = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 20 ? 1 : 0
                };
                
                // Set time
                if (ecccData.Current.ObservationTime.HasValue)
                {
                    forecast.Current.Time = ecccData.Current.ObservationTime.Value.ToString("yyyy-MM-ddTHH:mm");
                }
                else
                {
                    forecast.Current.Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
                }

                // Set units (ECCC uses metric)
                forecast.CurrentUnits = new OpenMeteo.CurrentUnits
                {
                    Temperature_2m = "°C",
                    Apparent_temperature = "°C",
                    Relativehumidity_2m = "%",
                    Windspeed_10m = "km/h",
                    Windgusts_10m = "km/h",
                    Winddirection_10m = "°",
                    Surface_pressure = "hPa",
                    Pressure_msl = "hPa",
                    Precipitation = "mm",
                    Cloudcover = "%"
                };
            }
            else
            {
                forecast.Current = new OpenMeteo.Current();
            }

            // Convert daily forecasts - consolidate day/night entries by date
            if (ecccData.DailyForecasts != null && ecccData.DailyForecasts.Count > 0)
            {
                // Group forecasts by date and merge day/night data
                var consolidatedByDate = new Dictionary<string, (double? high, double? low, string? condition, int? code)>();
                
                foreach (var f in ecccData.DailyForecasts)
                {
                    var date = f.Date ?? "";
                    if (string.IsNullOrEmpty(date)) continue;
                    
                    if (!consolidatedByDate.TryGetValue(date, out var existing))
                    {
                        // First entry for this date
                        consolidatedByDate[date] = (f.HighTemperature, f.LowTemperature, f.Condition, f.WeatherCode);
                    }
                    else
                    {
                        // Merge with existing - take non-null values
                        var newHigh = existing.high ?? f.HighTemperature;
                        var newLow = existing.low ?? f.LowTemperature;
                        // Prefer daytime condition (first entry usually)
                        var newCondition = existing.condition ?? f.Condition;
                        var newCode = existing.code ?? f.WeatherCode;
                        
                        // If current entry has High temp, it's likely the daytime forecast - prefer its condition
                        if (f.HighTemperature.HasValue && !existing.high.HasValue)
                        {
                            newCondition = f.Condition ?? existing.condition;
                            newCode = f.WeatherCode ?? existing.code;
                        }
                        
                        consolidatedByDate[date] = (newHigh, newLow, newCondition, newCode);
                    }
                }
                
                // Sort by date and build arrays
                var sortedDates = consolidatedByDate.Keys.OrderBy(d => d).ToList();
                
                forecast.Daily = new OpenMeteo.Daily
                {
                    Time = sortedDates.ToArray(),
                    Temperature_2m_max = sortedDates.Select(d => (float)(consolidatedByDate[d].high ?? float.NaN)).ToArray(),
                    Temperature_2m_min = sortedDates.Select(d => (float)(consolidatedByDate[d].low ?? float.NaN)).ToArray(),
                    Weathercode = sortedDates.Select(d => (float)(consolidatedByDate[d].code ?? 0)).ToArray()
                };
            }
            else
            {
                forecast.Daily = new OpenMeteo.Daily();
            }

            // Initialize empty hourly data
            forecast.Hourly = new OpenMeteo.Hourly();

            return forecast;
        }

        /// <summary>
        /// Calculates apparent temperature (feels like) based on temperature, humidity, and wind speed.
        /// Uses wind chill formula for cold temps and heat index for warm temps.
        /// </summary>
        private static float CalculateApparentTemperature(double tempC, double humidity, double windSpeedKmh)
        {
            // For cold weather (below 10°C), use Wind Chill formula
            if (tempC <= 10 && windSpeedKmh > 4.8)
            {
                // Canadian wind chill formula (Environment Canada)
                double windChill = 13.12 + 0.6215 * tempC - 11.37 * Math.Pow(windSpeedKmh, 0.16) + 0.3965 * tempC * Math.Pow(windSpeedKmh, 0.16);
                return (float)Math.Round(windChill, 1);
            }
            // For hot weather (above 27°C), use Humidex formula
            else if (tempC >= 27 && humidity > 40)
            {
                // Humidex formula (Environment Canada)
                double dewPoint = tempC - ((100 - humidity) / 5);
                double e = 6.11 * Math.Exp(5417.7530 * ((1 / 273.16) - (1 / (273.16 + dewPoint))));
                double humidex = tempC + (0.5555 * (e - 10));
                return (float)Math.Round(humidex, 1);
            }
            
            // For moderate temperatures, apparent temp ≈ actual temp
            return (float)tempC;
        }

        /// <summary>
        /// Estimates cloud cover percentage from ECCC condition text and weather code.
        /// </summary>
        private static int EstimateCloudCover(string? condition, int? weatherCode)
        {
            if (string.IsNullOrEmpty(condition))
            {
                // Estimate from weather code if condition not available
                return weatherCode switch
                {
                    0 => 0,      // Clear
                    1 => 15,     // Mainly clear
                    2 => 50,     // Partly cloudy
                    3 => 90,     // Overcast
                    >= 45 and <= 48 => 100,  // Fog
                    >= 51 => 80, // Any precipitation implies clouds
                    _ => 50
                };
            }

            var lower = condition.ToLowerInvariant();
            
            // Clear conditions
            if (lower.Contains("sunny") || lower.Contains("ensoleillé") || 
                lower.Contains("clear") || lower.Contains("dégagé"))
                return 0;
            
            // Mainly clear
            if (lower.Contains("mainly sunny") || lower.Contains("mainly clear") ||
                lower.Contains("généralement ensoleillé") || lower.Contains("généralement dégagé"))
                return 15;
            
            // A few clouds
            if (lower.Contains("a few clouds") || lower.Contains("quelques nuages"))
                return 25;
            
            // Mix of sun and cloud / partly cloudy
            if (lower.Contains("mix of sun") || lower.Contains("partly cloudy") ||
                lower.Contains("alternance") || lower.Contains("partiellement nuageux"))
                return 50;
            
            // Mostly cloudy
            if (lower.Contains("mostly cloudy") || lower.Contains("généralement nuageux"))
                return 75;
            
            // Cloudy / Overcast
            if (lower.Contains("cloudy") || lower.Contains("nuageux") || 
                lower.Contains("overcast") || lower.Contains("couvert"))
                return 90;
            
            // Fog, precipitation imply full cover
            if (lower.Contains("fog") || lower.Contains("brouillard") ||
                lower.Contains("rain") || lower.Contains("pluie") ||
                lower.Contains("snow") || lower.Contains("neige"))
                return 100;
            
            return 50; // Default
        }

        /// <summary>
        /// Converts compass direction to degrees.
        /// </summary>
        private static int DirectionToDegrees(string direction)
        {
            return direction.ToUpperInvariant() switch
            {
                "N" => 0,
                "NNE" => 22,
                "NE" => 45,
                "ENE" => 67,
                "E" => 90,
                "ESE" => 112,
                "SE" => 135,
                "SSE" => 157,
                "S" => 180,
                "SSW" or "SSO" => 202,
                "SW" or "SO" => 225,
                "WSW" or "OSO" => 247,
                "W" or "O" => 270,
                "WNW" or "ONO" => 292,
                "NW" or "NO" => 315,
                "NNW" or "NNO" => 337,
                _ => 0
            };
        }

        /// <summary>
        /// Gets a human-readable description of the weather data source.
        /// </summary>
        public static string GetSourceDescription(EcccWeatherData? ecccData)
        {
            if (ecccData == null || !ecccData.Success)
                return "ECCC (Failed)";

            if (ecccData.IsFromReferenceCity)
            {
                var distance = ecccData.City?.DistanceFromParent;
                var distanceText = distance.HasValue ? $" ({distance.Value:F1}km away)" : "";
                return $"ECCC (via {ecccData.ReferenceCityName}{distanceText})";
            }

            return $"ECCC ({ecccData.City?.Name})";
        }

        /// <summary>
        /// Merges ECCC data with OpenMeteo data, using ECCC as primary source
        /// and filling missing fields from OpenMeteo.
        /// </summary>
        /// <param name="ecccForecast">Primary ECCC forecast data</param>
        /// <param name="openMeteoForecast">Fallback OpenMeteo data for missing fields</param>
        /// <returns>Merged forecast with ECCC data preferred</returns>
        public static OpenMeteo.WeatherForecast MergeWithOpenMeteo(
            OpenMeteo.WeatherForecast ecccForecast, 
            OpenMeteo.WeatherForecast? openMeteoForecast)
        {
            if (openMeteoForecast == null)
                return ecccForecast;

            // Merge current conditions - ECCC values take priority
            if (ecccForecast.Current != null && openMeteoForecast.Current != null)
            {
                var ec = ecccForecast.Current;
                var om = openMeteoForecast.Current;

                // Fill missing current values from OpenMeteo
                if (!ec.Precipitation.HasValue || ec.Precipitation == 0)
                    ec.Precipitation = om.Precipitation;
                
                if (!ec.Rain.HasValue)
                    ec.Rain = om.Rain;
                
                if (!ec.Snowfall.HasValue)
                    ec.Snowfall = om.Snowfall;
                
                if (!ec.Showers.HasValue)
                    ec.Showers = om.Showers;
                
                // Use OpenMeteo cloud cover if ECCC estimate seems unreliable (exactly 50% default)
                if (ec.Cloudcover == 50 && om.Cloudcover.HasValue)
                    ec.Cloudcover = om.Cloudcover;
                
                // Fill wind gusts if missing
                if (!ec.Windgusts_10m.HasValue && om.Windgusts_10m.HasValue)
                    ec.Windgusts_10m = om.Windgusts_10m;
                
                // Use OpenMeteo Is_day if available (more accurate based on sunrise/sunset)
                if (om.Is_day.HasValue)
                    ec.Is_day = om.Is_day;
            }
            else if (ecccForecast.Current == null && openMeteoForecast.Current != null)
            {
                ecccForecast.Current = openMeteoForecast.Current;
            }

            // Merge hourly data - ECCC doesn't provide hourly, so use OpenMeteo's
            if ((ecccForecast.Hourly == null || ecccForecast.Hourly.Time == null || ecccForecast.Hourly.Time.Length == 0) 
                && openMeteoForecast.Hourly != null)
            {
                ecccForecast.Hourly = openMeteoForecast.Hourly;
                ecccForecast.HourlyUnits = openMeteoForecast.HourlyUnits;
            }

            // Keep ECCC daily forecasts if available, otherwise use OpenMeteo
            if ((ecccForecast.Daily == null || ecccForecast.Daily.Time == null || ecccForecast.Daily.Time.Length == 0)
                && openMeteoForecast.Daily != null)
            {
                ecccForecast.Daily = openMeteoForecast.Daily;
                ecccForecast.DailyUnits = openMeteoForecast.DailyUnits;
            }
            // Merge daily temperatures - fill NaN values from OpenMeteo
            else if (ecccForecast.Daily != null && openMeteoForecast.Daily != null)
            {
                var ecDaily = ecccForecast.Daily;
                var omDaily = openMeteoForecast.Daily;
                
                // Check if ECCC daily temps are all NaN and need filling from OpenMeteo
                bool needTempFill = ecDaily.Temperature_2m_max != null && 
                                    ecDaily.Temperature_2m_max.All(t => float.IsNaN(t));
                
                if (needTempFill && omDaily.Time != null && ecDaily.Time != null)
                {
                    // Create a lookup for OpenMeteo temps by date
                    var omTempsByDate = new Dictionary<string, (float max, float min)>();
                    for (int i = 0; i < omDaily.Time.Length; i++)
                    {
                        var date = omDaily.Time[i];
                        var max = omDaily.Temperature_2m_max?[i] ?? float.NaN;
                        var min = omDaily.Temperature_2m_min?[i] ?? float.NaN;
                        if (!string.IsNullOrEmpty(date))
                            omTempsByDate[date] = (max, min);
                    }
                    
                    // Fill ECCC temps from OpenMeteo by matching dates
                    var maxTemps = ecDaily.Temperature_2m_max?.ToArray() ?? new float[0];
                    var minTemps = ecDaily.Temperature_2m_min?.ToArray() ?? new float[0];
                    
                    for (int i = 0; i < ecDaily.Time.Length; i++)
                    {
                        var date = ecDaily.Time[i];
                        if (!string.IsNullOrEmpty(date) && omTempsByDate.TryGetValue(date, out var temps))
                        {
                            if (i < maxTemps.Length && float.IsNaN(maxTemps[i]))
                                maxTemps[i] = temps.max;
                            if (i < minTemps.Length && float.IsNaN(minTemps[i]))
                                minTemps[i] = temps.min;
                        }
                    }
                    
                    ecDaily.Temperature_2m_max = maxTemps;
                    ecDaily.Temperature_2m_min = minTemps;
                }
                
                // Also fill other missing daily fields from OpenMeteo
                if (ecDaily.Precipitation_sum == null && omDaily.Precipitation_sum != null)
                    ecDaily.Precipitation_sum = omDaily.Precipitation_sum;
                if (ecDaily.Windspeed_10m_max == null && omDaily.Windspeed_10m_max != null)
                    ecDaily.Windspeed_10m_max = omDaily.Windspeed_10m_max;
                if (ecDaily.Windgusts_10m_max == null && omDaily.Windgusts_10m_max != null)
                    ecDaily.Windgusts_10m_max = omDaily.Windgusts_10m_max;
                if (ecDaily.Winddirection_10m_dominant == null && omDaily.Winddirection_10m_dominant != null)
                    ecDaily.Winddirection_10m_dominant = omDaily.Winddirection_10m_dominant;
                    
                // Copy units if missing
                if (ecccForecast.DailyUnits == null && openMeteoForecast.DailyUnits != null)
                    ecccForecast.DailyUnits = openMeteoForecast.DailyUnits;
            }

            // Merge CurrentUnits - fill any missing unit strings
            if (ecccForecast.CurrentUnits != null && openMeteoForecast.CurrentUnits != null)
            {
                var ecu = ecccForecast.CurrentUnits;
                var omu = openMeteoForecast.CurrentUnits;
                
                if (string.IsNullOrEmpty(ecu.Precipitation)) ecu.Precipitation = omu.Precipitation;
                if (string.IsNullOrEmpty(ecu.Rain)) ecu.Rain = omu.Rain;
                if (string.IsNullOrEmpty(ecu.Snowfall)) ecu.Snowfall = omu.Snowfall;
                if (string.IsNullOrEmpty(ecu.Showers)) ecu.Showers = omu.Showers;
            }

            return ecccForecast;
        }

        /// <summary>
        /// Creates a weather forecast with error information.
        /// </summary>
        public static OpenMeteo.WeatherForecast CreateErrorForecast(string errorMessage)
        {
            return new OpenMeteo.WeatherForecast
            {
                Timezone = "America/Toronto",
                TimezoneAbbreviation = "EST",
                Current = new OpenMeteo.Current 
                { 
                    Temperature_2m = float.NaN,
                    Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm")
                },
                Daily = new OpenMeteo.Daily(),
                Hourly = new OpenMeteo.Hourly()
            };
        }
    }
}
