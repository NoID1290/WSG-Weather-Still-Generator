#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Manages application configuration loaded from appsettings.json
    /// </summary>
    public static class ConfigManager
    {
        private static AppSettings? _settings;
        private static readonly string ConfigFilePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        /// <summary>
        /// Loads configuration from appsettings.json. Caches the result after first load.
        /// </summary>
        public static AppSettings LoadConfig()
        {
            if (_settings != null)
            {
                return _settings;
            }

            if (!File.Exists(ConfigFilePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {ConfigFilePath}");
            }

            try
            {
                string jsonContent = File.ReadAllText(ConfigFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                _settings = JsonSerializer.Deserialize<AppSettings>(jsonContent, options)
                    ?? throw new InvalidOperationException("Failed to deserialize configuration");

                Logger.Log($"✓ Configuration loaded from: {ConfigFilePath}");
                return _settings;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON in configuration file: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error loading configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reloads configuration from file (clears cache)
        /// </summary>
        public static AppSettings ReloadConfig()
        {
            _settings = null;
            return LoadConfig();
        }

        /// <summary>
        /// Saves the provided settings back to appsettings.json and updates the cached settings.
        /// </summary>
        public static void SaveConfig(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(ConfigFilePath, json);
                _settings = settings;
                Logger.Log($"✓ Configuration saved to: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Root configuration object
    /// </summary>
    public class AppSettings
    {
        [JsonPropertyName("Locations")]
        public LocationSettings? Locations { get; set; }

        [JsonPropertyName("RefreshTimeMinutes")]
        public int RefreshTimeMinutes { get; set; } = 10;

        [JsonPropertyName("ImageGeneration")]
        public ImageGenerationSettings? ImageGeneration { get; set; }

        [JsonPropertyName("Video")]
        public VideoSettings? Video { get; set; }

        [JsonPropertyName("Alerts")]
        public AlertsSettings? Alerts { get; set; }

        [JsonPropertyName("WeatherImages")]
        public WeatherImagesSettings? WeatherImages { get; set; }

        [JsonPropertyName("MapLocations")]
        public Dictionary<string, MapLocationSettings>? MapLocations { get; set; }

        [JsonPropertyName("ECCC")]
        public ECCCSettings? ECCC { get; set; }

        [JsonPropertyName("Theme")]
        public string Theme { get; set; } = "Blue";

        [JsonPropertyName("MinimizeToTray")]
        public bool MinimizeToTray { get; set; } = false;

        [JsonPropertyName("MinimizeToTrayOnClose")]
        public bool MinimizeToTrayOnClose { get; set; } = false;

        [JsonPropertyName("Music")]
        public MusicSettings? Music { get; set; }
    }

    /// <summary>
    /// Location configuration
    /// </summary>
    public class LocationSettings
    {
        [JsonPropertyName("Location0")]
        public string? Location0 { get; set; }

        [JsonPropertyName("Location1")]
        public string? Location1 { get; set; }

        [JsonPropertyName("Location2")]
        public string? Location2 { get; set; }

        [JsonPropertyName("Location3")]
        public string? Location3 { get; set; }

        [JsonPropertyName("Location4")]
        public string? Location4 { get; set; }

        [JsonPropertyName("Location5")]
        public string? Location5 { get; set; }

        [JsonPropertyName("Location6")]
        public string? Location6 { get; set; }

        [JsonPropertyName("Location7")]
        public string? Location7 { get; set; }

        [JsonPropertyName("Location8")]
        public string? Location8 { get; set; }

        /// <summary>
        /// Returns all locations as an array
        /// </summary>
        public string[] GetLocationsArray()
        {
            return new[] { Location0, Location1, Location2, Location3, Location4, Location5, Location6, Location7, Location8 };
        }
    }

    /// <summary>
    /// Image generation settings
    /// </summary>
    public class ImageGenerationSettings
    {
        [JsonPropertyName("OutputDirectory")]
        public string? OutputDirectory { get; set; }

        [JsonPropertyName("ImageWidth")]
        public int ImageWidth { get; set; } = 1920;

        [JsonPropertyName("ImageHeight")]
        public int ImageHeight { get; set; } = 1080;

        [JsonPropertyName("ImageFormat")]
        public string? ImageFormat { get; set; } = "png";

        [JsonPropertyName("MarginPixels")]
        public float MarginPixels { get; set; } = 50f;

        [JsonPropertyName("FontFamily")]
        public string? FontFamily { get; set; } = "Arial";

        [JsonPropertyName("EnableWeatherMaps")]
        public bool EnableWeatherMaps { get; set; } = true;
    }

    /// <summary>
    /// Video generation settings
    /// </summary>
    public class VideoSettings
    {
        [JsonPropertyName("OutputFileName")]
        public string? OutputFileName { get; set; }

        [JsonPropertyName("MusicFileName")]
        public string? MusicFileName { get; set; }

        [JsonPropertyName("StaticDurationSeconds")]
        public double StaticDurationSeconds { get; set; } = 8;

        [JsonPropertyName("FadeDurationSeconds")]
        public double FadeDurationSeconds { get; set; } = 0.5;

        [JsonPropertyName("UseTotalDuration")]
        public bool UseTotalDuration { get; set; } = false;

        [JsonPropertyName("TotalDurationSeconds")]
        public double TotalDurationSeconds { get; set; } = 60;

        [JsonPropertyName("FrameRate")]
        public int FrameRate { get; set; } = 30;

        [JsonPropertyName("ResolutionMode")]
        public string? ResolutionMode { get; set; } = "Mode1080p";

        [JsonPropertyName("VideoCodec")]
        public string? VideoCodec { get; set; } = "libx264";

        [JsonPropertyName("VideoBitrate")]
        public string? VideoBitrate { get; set; } = "4M";

        [JsonPropertyName("Container")]
        public string? Container { get; set; } = "mp4";

        [JsonPropertyName("OutputDirectory")]
        public string? OutputDirectory { get; set; }

        [JsonPropertyName("EnableFadeTransitions")]
        public bool EnableFadeTransitions { get; set; } = false;

        [JsonPropertyName("VerboseFfmpeg")]
        public bool VerboseFfmpeg { get; set; } = false;

        [JsonPropertyName("ShowFfmpegOutputInGui")]
        public bool ShowFfmpegOutputInGui { get; set; } = true;

        [JsonPropertyName("EnableHardwareEncoding")]
        public bool EnableHardwareEncoding { get; set; } = false;

        [JsonPropertyName("doVideoGeneration")]
        public bool doVideoGeneration { get; set; } = true;

        [JsonPropertyName("QualityPreset")]
        public string? QualityPreset { get; set; } = "Balanced";
    }

    /// <summary>
    /// Alerts display settings
    /// </summary>
    public class AlertsSettings
    {
        [JsonPropertyName("HeaderText")]
        public string? HeaderText { get; set; }

        [JsonPropertyName("NoAlertsText")]
        public string? NoAlertsText { get; set; }

        [JsonPropertyName("HeaderFontSize")]
        public float HeaderFontSize { get; set; } = 48;

        [JsonPropertyName("CityFontSize")]
        public float CityFontSize { get; set; } = 28;

        [JsonPropertyName("TypeFontSize")]
        public float TypeFontSize { get; set; } = 28;

        [JsonPropertyName("DetailsFontSize")]
        public float DetailsFontSize { get; set; } = 22;

        [JsonPropertyName("AlertFilename")]
        public string? AlertFilename { get; set; }
    }

    /// <summary>
    /// Weather image filenames
    /// </summary>
    public class WeatherImagesSettings
    {
        [JsonPropertyName("CurrentWeatherFilename")]
        public string? CurrentWeatherFilename { get; set; }

        [JsonPropertyName("DailyForecastFilename")]
        public string? DailyForecastFilename { get; set; }

        [JsonPropertyName("DetailedWeatherFilename")]
        public string? DetailedWeatherFilename { get; set; }

        [JsonPropertyName("WeatherMapsFilename")]
        public string? WeatherMapsFilename { get; set; }

        [JsonPropertyName("TemperatureWatermarkFilename")]
        public string? TemperatureWatermarkFilename { get; set; }

        [JsonPropertyName("StaticMapFilename")]
        public string? StaticMapFilename { get; set; }
    }

    /// <summary>
    /// Map location coordinates for a specific location
    /// </summary>
    public class MapLocationSettings
    {
        [JsonPropertyName("CityPositionX")]
        public float CityPositionX { get; set; }

        [JsonPropertyName("CityPositionY")]
        public float CityPositionY { get; set; }

        [JsonPropertyName("TemperaturePositionX")]
        public float TemperaturePositionX { get; set; }

        [JsonPropertyName("TemperaturePositionY")]
        public float TemperaturePositionY { get; set; }
    }

    /// <summary>
    /// ECCC (Environment and Climate Change Canada) settings
    /// </summary>
    public class ECCCSettings
    {
        [JsonPropertyName("CityFeeds")]
        public Dictionary<string, string>? CityFeeds { get; set; }

        [JsonPropertyName("RadarFeeds")]
        public Dictionary<string, string>? RadarFeeds { get; set; }

        [JsonPropertyName("EnableCityRadar")]
        public bool EnableCityRadar { get; set; } = false;

        [JsonPropertyName("EnableProvinceRadar")]
        public bool EnableProvinceRadar { get; set; } = true;

        [JsonPropertyName("UseGeoMetWms")]
        public bool UseGeoMetWms { get; set; } = true;

        [JsonPropertyName("CityRadarLayer")]
        public string? CityRadarLayer { get; set; } = "RADAR_1KM_RRAI";

        [JsonPropertyName("ProvinceRadarLayer")]
        public string? ProvinceRadarLayer { get; set; } = "RADAR_1KM_RRAI";

        [JsonPropertyName("ProvinceFrames")]
        public int ProvinceFrames { get; set; } = 8;

        [JsonPropertyName("ProvinceImageWidth")]
        public int ProvinceImageWidth { get; set; } = 1920;

        [JsonPropertyName("ProvinceImageHeight")]
        public int ProvinceImageHeight { get; set; } = 1080;

        [JsonPropertyName("ProvincePaddingDegrees")]
        public double ProvincePaddingDegrees { get; set; } = 0.5;

        [JsonPropertyName("ProvinceEnsureCities")]
        public string[]? ProvinceEnsureCities { get; set; }

        [JsonPropertyName("ProvinceFrameStepMinutes")]
        public int ProvinceFrameStepMinutes { get; set; } = 6;

        [JsonPropertyName("ProvinceAnimationUrl")]
        public string? ProvinceAnimationUrl { get; set; }

        [JsonPropertyName("ProvinceRadarUrl")]
        public string? ProvinceRadarUrl { get; set; }

        [JsonPropertyName("CityMapTemplate")]
        public string? CityMapTemplate { get; set; }

        [JsonPropertyName("DelayBetweenRequestsMs")]
        public int DelayBetweenRequestsMs { get; set; } = 200;

        [JsonPropertyName("UserAgent")]
        public string? UserAgent { get; set; }
    }

    /// <summary>
    /// Music settings for video generation
    /// </summary>
    public class MusicSettings
    {
        [JsonPropertyName("musicTracks")]
        public List<MusicEntry>? MusicTracks { get; set; }

        [JsonPropertyName("selectedMusicIndex")]
        public int SelectedMusicIndex { get; set; } = -1; // -1 means random

        [JsonPropertyName("useRandomMusic")]
        public bool UseRandomMusic { get; set; } = true;

        [JsonPropertyName("enableMusicInVideo")]
        public bool EnableMusicInVideo { get; set; } = true;

        [JsonPropertyName("autoTrimMusic")]
        public bool AutoTrimMusic { get; set; } = true;

        [JsonPropertyName("audioFadeDuration")]
        public double AudioFadeDuration { get; set; } = 2.0;

        /// <summary>
        /// Gets the selected music entry, or a random one if UseRandomMusic is true
        /// </summary>
        public MusicEntry? GetSelectedMusic()
        {
            if (MusicTracks == null || MusicTracks.Count == 0)
                return null;

            // Filter to only existing files
            var validTracks = MusicTracks.Where(m => m.FileExists()).ToList();
            if (validTracks.Count == 0)
                return null;

            // If random is enabled, pick a random valid track
            if (UseRandomMusic || SelectedMusicIndex < 0 || SelectedMusicIndex >= validTracks.Count)
            {
                var random = new Random();
                return validTracks[random.Next(validTracks.Count)];
            }

            // Otherwise return the selected index from valid tracks
            return validTracks[SelectedMusicIndex];
        }

        /// <summary>
        /// Initializes default demo music tracks
        /// </summary>
        public static MusicSettings CreateDefault(string basePath)
        {
            var musicDir = Path.Combine(basePath, "Music");
            
            return new MusicSettings
            {
                UseRandomMusic = true,
                SelectedMusicIndex = -1,
                MusicTracks = new List<MusicEntry>
                {
                    new MusicEntry("Calm Ambient", Path.Combine(musicDir, "demo_calm_ambient.mp3"), true),
                    new MusicEntry("Upbeat Energy", Path.Combine(musicDir, "demo_upbeat_energy.mp3"), true),
                    new MusicEntry("Smooth Jazz", Path.Combine(musicDir, "demo_smooth_jazz.mp3"), true),
                    new MusicEntry("Electronic Chill", Path.Combine(musicDir, "demo_electronic_chill.mp3"), true)
                }
            };
        }
    }
}
