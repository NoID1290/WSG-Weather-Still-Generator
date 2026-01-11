#nullable enable
using System;
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
                forecast.Current = new OpenMeteo.Current
                {
                    Temperature_2m = ecccData.Current.Temperature.HasValue ? (float)ecccData.Current.Temperature.Value : float.NaN,
                    Relativehumidity_2m = ecccData.Current.Humidity.HasValue ? (int)Math.Round(ecccData.Current.Humidity.Value) : 0,
                    Windspeed_10m = ecccData.Current.WindSpeed.HasValue ? (float)Math.Round(ecccData.Current.WindSpeed.Value, 1) : 0,
                    Surface_pressure = ecccData.Current.Pressure.HasValue ? (float)Math.Round(ecccData.Current.Pressure.Value, 1) : 0,
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
            }
            else
            {
                forecast.Current = new OpenMeteo.Current();
            }

            // Convert daily forecasts
            if (ecccData.DailyForecasts != null && ecccData.DailyForecasts.Count > 0)
            {
                forecast.Daily = new OpenMeteo.Daily
                {
                    Time = ecccData.DailyForecasts.Select(f => f.Date ?? "").ToArray(),
                    Temperature_2m_max = ecccData.DailyForecasts.Select(f => (float)(f.HighTemperature ?? float.NaN)).ToArray(),
                    Temperature_2m_min = ecccData.DailyForecasts.Select(f => (float)(f.LowTemperature ?? float.NaN)).ToArray(),
                    Weathercode = ecccData.DailyForecasts.Select(f => (float)(f.WeatherCode ?? 0)).ToArray()
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
