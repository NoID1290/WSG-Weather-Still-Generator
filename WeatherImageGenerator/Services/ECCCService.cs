#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// High-level service for fetching ECCC data using dynamic URL configuration.
    /// Bridges the ConfigManager settings with the ECCC library capabilities.
    /// </summary>
    public class ECCCService
    {
        private readonly HttpClient _client;
        private readonly ECCCSettings _settings;

        public ECCCService(HttpClient client, ECCCSettings? settings = null)
        {
            _client = client;
            _settings = settings ?? ConfigManager.LoadConfig().ECCC ?? new ECCCSettings();
        }

        #region Dynamic City Weather

        /// <summary>
        /// Fetches weather data for all dynamically configured cities.
        /// </summary>
        public async Task<List<ECCC.EcccDataResult>> FetchDynamicCityWeatherAsync()
        {
            var results = new List<ECCC.EcccDataResult>();
            var cities = _settings.DynamicCities?.Where(c => c.Enabled) ?? Enumerable.Empty<DynamicCityConfig>();

            foreach (var city in cities)
            {
                var request = new ECCC.EcccDataRequest
                {
                    DataType = ECCC.EcccDataType.CityWeather,
                    Name = city.Name,
                    Province = city.Province,
                    CityCode = city.CityCode,
                    Language = _settings.DefaultLanguage
                };

                var result = await ECCC.FetchDataAsync(_client, request);
                results.Add(result);

                if (result.Success)
                    Logger.Log($"✓ [ECCC] Fetched weather for {city.Name}");
                else
                    Logger.Log($"✗ [ECCC] Failed to fetch weather for {city.Name}: {result.ErrorMessage}");

                await Task.Delay(_settings.DelayBetweenRequestsMs);
            }

            return results;
        }

        /// <summary>
        /// Fetches weather data for a specific city by province and city code.
        /// </summary>
        public async Task<ECCC.EcccDataResult> FetchCityWeatherAsync(string province, string cityCode, string? language = null)
        {
            var request = new ECCC.EcccDataRequest
            {
                DataType = ECCC.EcccDataType.CityWeather,
                Province = province,
                CityCode = cityCode,
                Language = language ?? _settings.DefaultLanguage
            };

            return await ECCC.FetchDataAsync(_client, request);
        }

        #endregion

        #region Dynamic Alerts

        /// <summary>
        /// Fetches alerts for all dynamically configured cities by their coordinates.
        /// </summary>
        public async Task<List<ECCC.EcccDataResult>> FetchDynamicAlertsAsync()
        {
            var results = new List<ECCC.EcccDataResult>();
            var cities = _settings.DynamicCities?.Where(c => c.Enabled && c.Latitude != 0 && c.Longitude != 0) 
                         ?? Enumerable.Empty<DynamicCityConfig>();

            foreach (var city in cities)
            {
                var request = new ECCC.EcccDataRequest
                {
                    DataType = ECCC.EcccDataType.Alerts,
                    Name = city.Name,
                    Latitude = city.Latitude,
                    Longitude = city.Longitude,
                    Language = _settings.DefaultLanguage
                };

                var result = await ECCC.FetchDataAsync(_client, request);
                results.Add(result);
                
                await Task.Delay(_settings.DelayBetweenRequestsMs);
            }

            return results;
        }

        /// <summary>
        /// Fetches alerts for specific coordinates.
        /// </summary>
        public async Task<ECCC.EcccDataResult> FetchAlertsAsync(double latitude, double longitude, string? language = null)
        {
            var request = new ECCC.EcccDataRequest
            {
                DataType = ECCC.EcccDataType.Alerts,
                Latitude = latitude,
                Longitude = longitude,
                Language = language ?? _settings.DefaultLanguage
            };

            return await ECCC.FetchDataAsync(_client, request);
        }

        #endregion

        #region Dynamic WMS Layers

        /// <summary>
        /// Fetches all enabled dynamic WMS layers.
        /// </summary>
        public async Task<List<ECCC.EcccDataResult>> FetchDynamicLayersAsync(string? outputDir = null)
        {
            var results = new List<ECCC.EcccDataResult>();
            var layers = _settings.DynamicLayers?.Where(l => l.Enabled) ?? Enumerable.Empty<DynamicLayerConfig>();

            var bbox = _settings.DefaultBoundingBox?.ToTuple() ?? (45.0, -80.0, 53.0, -57.0);

            foreach (var layer in layers)
            {
                var request = new ECCC.EcccDataRequest
                {
                    DataType = ECCC.EcccDataType.WmsLayer,
                    Name = layer.Name,
                    Layer = layer.Layer,
                    BoundingBox = bbox,
                    Width = _settings.ProvinceImageWidth,
                    Height = _settings.ProvinceImageHeight
                };

                var result = await ECCC.FetchDataAsync(_client, request);
                results.Add(result);

                if (result.Success && result.BinaryData != null && !string.IsNullOrEmpty(outputDir))
                {
                    var fileName = $"{SanitizeFileName(layer.Name)}.png";
                    var path = Path.Combine(outputDir, fileName);
                    await File.WriteAllBytesAsync(path, result.BinaryData);
                    Logger.Log($"✓ [ECCC] Saved layer {layer.Name} -> {path}");
                }
                else if (!result.Success)
                {
                    Logger.Log($"✗ [ECCC] Failed to fetch layer {layer.Name}: {result.ErrorMessage}");
                }

                await Task.Delay(_settings.DelayBetweenRequestsMs);
            }

            return results;
        }

        /// <summary>
        /// Fetches a specific WMS layer by name.
        /// </summary>
        public async Task<ECCC.EcccDataResult> FetchLayerAsync(
            string layer,
            (double MinLat, double MinLon, double MaxLat, double MaxLon)? bbox = null,
            int? width = null,
            int? height = null,
            string? time = null)
        {
            var request = new ECCC.EcccDataRequest
            {
                DataType = ECCC.EcccDataType.WmsLayer,
                Layer = layer,
                BoundingBox = bbox ?? _settings.DefaultBoundingBox?.ToTuple() ?? (45.0, -80.0, 53.0, -57.0),
                Width = width ?? _settings.ProvinceImageWidth,
                Height = height ?? _settings.ProvinceImageHeight,
                Time = time
            };

            return await ECCC.FetchDataAsync(_client, request);
        }

        /// <summary>
        /// Fetches radar layer for the configured province area.
        /// </summary>
        public async Task<ECCC.EcccDataResult> FetchProvinceRadarAsync(string? time = null)
        {
            return await FetchLayerAsync(
                _settings.ProvinceRadarLayer ?? ECCC.Layers.Radar1KmRain,
                time: time);
        }

        /// <summary>
        /// Fetches multiple radar frames for animation.
        /// </summary>
        public async Task<List<ECCC.EcccDataResult>> FetchRadarAnimationFramesAsync(int? frameCount = null, int? stepMinutes = null)
        {
            var frames = frameCount ?? _settings.ProvinceFrames;
            var step = stepMinutes ?? _settings.ProvinceFrameStepMinutes;
            var results = new List<ECCC.EcccDataResult>();

            var now = DateTime.UtcNow;
            // Round down to nearest step
            var startTime = now.AddMinutes(-(now.Minute % step)).AddSeconds(-now.Second);

            for (int i = frames - 1; i >= 0; i--)
            {
                var frameTime = startTime.AddMinutes(-i * step);
                var timeStr = frameTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

                var result = await FetchProvinceRadarAsync(timeStr);
                result.Request!.Name = $"Frame_{frames - i}";
                results.Add(result);

                await Task.Delay(_settings.DelayBetweenRequestsMs);
            }

            return results;
        }

        #endregion

        #region Layer Discovery

        /// <summary>
        /// Gets all available WMS layers from ECCC GeoMet.
        /// </summary>
        public async Task<List<string>> GetAvailableLayersAsync()
        {
            return await ECCC.GetAvailableLayersAsync(_client);
        }

        /// <summary>
        /// Gets commonly used layer constants.
        /// </summary>
        public static class CommonLayers
        {
            // Radar
            public static string Radar => ECCC.Layers.Radar1KmRain;
            public static string RadarSnow => ECCC.Layers.Radar1KmSnow;

            // Temperature
            public static string TemperatureGlobal => ECCC.Layers.GdpsTemperature;
            public static string TemperatureHighRes => ECCC.Layers.HrdpsTemperature;
            public static string TemperatureRegional => ECCC.Layers.RdpsTemperature;

            // Precipitation
            public static string PrecipitationGlobal => ECCC.Layers.GdpsPrecipitation;
            public static string PrecipitationHighRes => ECCC.Layers.HrdpsPrecipitation;

            // Wind
            public static string WindGlobal => ECCC.Layers.GdpsWindSpeed;
            public static string WindHighRes => ECCC.Layers.HrdpsWindSpeed;

            // Other
            public static string CloudCover => ECCC.Layers.GdpsCloudCover;
            public static string Humidity => ECCC.Layers.GdpsHumidity;
            public static string Alerts => ECCC.Layers.WeatherAlerts;
        }

        #endregion

        #region Utility

        private static string SanitizeFileName(string name)
        {
            return string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        }

        /// <summary>
        /// Creates EcccDataRequest for a custom URL.
        /// </summary>
        public static ECCC.EcccDataRequest CreateCustomRequest(string url, string? name = null)
        {
            return new ECCC.EcccDataRequest
            {
                DataType = ECCC.EcccDataType.CustomUrl,
                CustomUrl = url,
                Name = name
            };
        }

        /// <summary>
        /// Fetches data from a custom ECCC URL.
        /// </summary>
        public async Task<ECCC.EcccDataResult> FetchCustomAsync(string url)
        {
            return await ECCC.FetchDataAsync(_client, CreateCustomRequest(url));
        }

        #endregion
    }
}
