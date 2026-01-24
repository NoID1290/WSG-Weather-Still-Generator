#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;
using EAS;

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

        [JsonPropertyName("AlertReady")]
        public AlertReadyOptions? AlertReady { get; set; }

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

        [JsonPropertyName("AutoStartCycle")]
        public bool AutoStartCycle { get; set; } = false;

        [JsonPropertyName("StartWithWindows")]
        public bool StartWithWindows { get; set; } = false;

        [JsonPropertyName("StartMinimizedToTray")]
        public bool StartMinimizedToTray { get; set; } = false;

        [JsonPropertyName("Music")]
        public MusicSettings? Music { get; set; }

        [JsonPropertyName("FFmpeg")]
        public FFmpegSettings? FFmpeg { get; set; }

        [JsonPropertyName("TTS")]
        public TTSSettings? TTS { get; set; }

        [JsonPropertyName("OpenMap")]
        public OpenMapSettings? OpenMap { get; set; }
    }

    /// <summary>
    /// FFmpeg configuration settings
    /// </summary>
    public class FFmpegSettings
    {
        /// <summary>
        /// The source to use for FFmpeg binaries: Bundled, SystemPath, or Custom.
        /// </summary>
        [JsonPropertyName("Source")]
        public string Source { get; set; } = "Bundled";

        /// <summary>
        /// Custom path to FFmpeg directory (only used when Source is "Custom").
        /// </summary>
        [JsonPropertyName("CustomPath")]
        public string? CustomPath { get; set; }
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
        /// Weather API preference for Location0
        /// </summary>
        [JsonPropertyName("Location0Api")]
        public Models.WeatherApiType Location0Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location1
        /// </summary>
        [JsonPropertyName("Location1Api")]
        public Models.WeatherApiType Location1Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location2
        /// </summary>
        [JsonPropertyName("Location2Api")]
        public Models.WeatherApiType Location2Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location3
        /// </summary>
        [JsonPropertyName("Location3Api")]
        public Models.WeatherApiType Location3Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location4
        /// </summary>
        [JsonPropertyName("Location4Api")]
        public Models.WeatherApiType Location4Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location5
        /// </summary>
        [JsonPropertyName("Location5Api")]
        public Models.WeatherApiType Location5Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location6
        /// </summary>
        [JsonPropertyName("Location6Api")]
        public Models.WeatherApiType Location6Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location7
        /// </summary>
        [JsonPropertyName("Location7Api")]
        public Models.WeatherApiType Location7Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Weather API preference for Location8
        /// </summary>
        [JsonPropertyName("Location8Api")]
        public Models.WeatherApiType Location8Api { get; set; } = Models.WeatherApiType.OpenMeteo;

        /// <summary>
        /// Returns all locations as an array
        /// </summary>
        public string?[] GetLocationsArray()
        {
            return new[] { Location0, Location1, Location2, Location3, Location4, Location5, Location6, Location7, Location8 };
        }

        /// <summary>
        /// Returns all location API preferences as an array
        /// </summary>
        public Models.WeatherApiType[] GetApiPreferencesArray()
        {
            return new[] { Location0Api, Location1Api, Location2Api, Location3Api, Location4Api, Location5Api, Location6Api, Location7Api, Location8Api };
        }

        /// <summary>
        /// Returns all locations as LocationEntry objects with their API preferences
        /// </summary>
        public Models.LocationEntry[] GetLocationEntries()
        {
            var names = GetLocationsArray();
            var apis = GetApiPreferencesArray();
            var entries = new Models.LocationEntry[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                entries[i] = new Models.LocationEntry(names[i] ?? string.Empty, apis[i]);
            }
            return entries;
        }

        /// <summary>
        /// Sets the API preference for a specific location index
        /// </summary>
        public void SetApiPreference(int index, Models.WeatherApiType api)
        {
            switch (index)
            {
                case 0: Location0Api = api; break;
                case 1: Location1Api = api; break;
                case 2: Location2Api = api; break;
                case 3: Location3Api = api; break;
                case 4: Location4Api = api; break;
                case 5: Location5Api = api; break;
                case 6: Location6Api = api; break;
                case 7: Location7Api = api; break;
                case 8: Location8Api = api; break;
            }
        }

        /// <summary>
        /// Gets the API preference for a specific location index
        /// </summary>
        public Models.WeatherApiType GetApiPreference(int index)
        {
            return index switch
            {
                0 => Location0Api,
                1 => Location1Api,
                2 => Location2Api,
                3 => Location3Api,
                4 => Location4Api,
                5 => Location5Api,
                6 => Location6Api,
                7 => Location7Api,
                8 => Location8Api,
                _ => Models.WeatherApiType.OpenMeteo
            };
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

        [JsonPropertyName("EnableRadarAnimation")]
        public bool EnableRadarAnimation { get; set; } = true;

        [JsonPropertyName("EnableGlobalWeatherMap")]
        public bool EnableGlobalWeatherMap { get; set; } = true;
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

        [JsonPropertyName("UseCrfEncoding")]
        public bool UseCrfEncoding { get; set; } = true;

        [JsonPropertyName("CrfValue")]
        public int CrfValue { get; set; } = 23;

        [JsonPropertyName("MaxBitrate")]
        public string? MaxBitrate { get; set; }

        [JsonPropertyName("BufferSize")]
        public string? BufferSize { get; set; }

        [JsonPropertyName("EncoderPreset")]
        public string? EncoderPreset { get; set; } = "medium";

        [JsonPropertyName("ExperimentalEnabled")]
        public bool ExperimentalEnabled { get; set; } = false;

        [JsonPropertyName("SkipDetailedWeatherOnAlert")]
        public bool SkipDetailedWeatherOnAlert { get; set; } = false;

        [JsonPropertyName("PlayRadarAnimationTwiceOnAlert")]
        public bool PlayRadarAnimationTwiceOnAlert { get; set; } = false;
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

        // Dynamic URL generation settings
        [JsonPropertyName("DefaultLanguage")]
        public string DefaultLanguage { get; set; } = "f";

        [JsonPropertyName("DefaultProvince")]
        public string DefaultProvince { get; set; } = "qc";

        [JsonPropertyName("DynamicCities")]
        public List<DynamicCityConfig>? DynamicCities { get; set; }

        [JsonPropertyName("DynamicLayers")]
        public List<DynamicLayerConfig>? DynamicLayers { get; set; }

        [JsonPropertyName("DefaultBoundingBox")]
        public BoundingBoxConfig? DefaultBoundingBox { get; set; }
    }

    /// <summary>
    /// Configuration for a dynamically-configured city
    /// </summary>
    public class DynamicCityConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("province")]
        public string Province { get; set; } = "qc";

        [JsonPropertyName("cityCode")]
        public string CityCode { get; set; } = "";

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Configuration for a dynamically-configured WMS layer
    /// </summary>
    public class DynamicLayerConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Bounding box configuration
    /// </summary>
    public class BoundingBoxConfig
    {
        [JsonPropertyName("minLat")]
        public double MinLat { get; set; }

        [JsonPropertyName("minLon")]
        public double MinLon { get; set; }

        [JsonPropertyName("maxLat")]
        public double MaxLat { get; set; }

        [JsonPropertyName("maxLon")]
        public double MaxLon { get; set; }

        public (double MinLat, double MinLon, double MaxLat, double MaxLon) ToTuple()
            => (MinLat, MinLon, MaxLat, MaxLon);
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

    /// <summary>
    /// Text-to-Speech settings for EdgeTTS
    /// </summary>
    public class TTSSettings
    {
        /// <summary>
        /// Voice identifier (e.g., fr-CA-SylvieNeural, en-CA-ClaraNeural)
        /// </summary>
        [JsonPropertyName("Voice")]
        public string Voice { get; set; } = "fr-CA-SylvieNeural";

        /// <summary>
        /// Speech rate modifier (e.g., +0%, +10%, -10%)
        /// </summary>
        [JsonPropertyName("Rate")]
        public string Rate { get; set; } = "+0%";

        /// <summary>
        /// Pitch modifier (e.g., +0Hz, +10Hz, -10Hz)
        /// </summary>
        [JsonPropertyName("Pitch")]
        public string Pitch { get; set; } = "+0Hz";
    }

    /// <summary>
    /// OpenStreetMap tile and rendering settings
    /// </summary>
    public class OpenMapSettings
    {
        /// <summary>
        /// Default map style: Standard, Minimal, Terrain, or Satellite
        /// </summary>
        [JsonPropertyName("DefaultMapStyle")]
        public string DefaultMapStyle { get; set; } = "Standard";

        /// <summary>
        /// Default zoom level for map generation (0-18)
        /// </summary>
        [JsonPropertyName("DefaultZoomLevel")]
        public int DefaultZoomLevel { get; set; } = 10;

        /// <summary>
        /// Background color for maps (hex format: #RRGGBB)
        /// </summary>
        [JsonPropertyName("BackgroundColor")]
        public string BackgroundColor { get; set; } = "#D3D3D3";

        /// <summary>
        /// Overlay opacity for radar/weather layers (0.0 to 1.0)
        /// </summary>
        [JsonPropertyName("OverlayOpacity")]
        public float OverlayOpacity { get; set; } = 0.7f;

        /// <summary>
        /// Timeout for tile downloads in seconds
        /// </summary>
        [JsonPropertyName("TileDownloadTimeoutSeconds")]
        public int TileDownloadTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Enable tile caching
        /// </summary>
        [JsonPropertyName("EnableTileCache")]
        public bool EnableTileCache { get; set; } = true;

        /// <summary>
        /// Tile cache directory path (relative to application directory)
        /// </summary>
        [JsonPropertyName("TileCacheDirectory")]
        public string TileCacheDirectory { get; set; } = "MapCache";

        /// <summary>
        /// Cache duration in hours (tiles older than this will be re-downloaded)
        /// </summary>
        [JsonPropertyName("CacheDurationHours")]
        public int CacheDurationHours { get; set; } = 168; // 7 days

        /// <summary>
        /// Use dark mode for terrain maps
        /// </summary>
        [JsonPropertyName("UseDarkMode")]
        public bool UseDarkMode { get; set; } = false;

        /// <summary>
        /// Map style presets for different visualization types
        /// </summary>
        [JsonPropertyName("StylePresets")]
        public Dictionary<string, MapStylePreset>? StylePresets { get; set; }

        /// <summary>
        /// Custom color overrides for map elements
        /// </summary>
        [JsonPropertyName("ColorOverrides")]
        public MapColorOverrides? ColorOverrides { get; set; }
    }

    /// <summary>
    /// Map style preset configuration
    /// </summary>
    public class MapStylePreset
    {
        /// <summary>
        /// Map style type: Standard, Minimal, Terrain, or Satellite
        /// </summary>
        [JsonPropertyName("Style")]
        public string Style { get; set; } = "Standard";

        /// <summary>
        /// Default zoom level for this preset
        /// </summary>
        [JsonPropertyName("ZoomLevel")]
        public int ZoomLevel { get; set; } = 10;

        /// <summary>
        /// Background color for this preset
        /// </summary>
        [JsonPropertyName("BackgroundColor")]
        public string? BackgroundColor { get; set; }

        /// <summary>
        /// Overlay opacity for this preset
        /// </summary>
        [JsonPropertyName("OverlayOpacity")]
        public float? OverlayOpacity { get; set; }
    }

    /// <summary>
    /// Custom color overrides for map elements
    /// </summary>
    public class MapColorOverrides
    {
        /// <summary>
        /// Background/ocean color
        /// </summary>
        [JsonPropertyName("Background")]
        public string? Background { get; set; }

        /// <summary>
        /// Water bodies color
        /// </summary>
        [JsonPropertyName("Water")]
        public string? Water { get; set; }

        /// <summary>
        /// Land/terrain color
        /// </summary>
        [JsonPropertyName("Land")]
        public string? Land { get; set; }

        /// <summary>
        /// Road/street color
        /// </summary>
        [JsonPropertyName("Roads")]
        public string? Roads { get; set; }

        /// <summary>
        /// City/urban area color
        /// </summary>
        [JsonPropertyName("Urban")]
        public string? Urban { get; set; }

        /// <summary>
        /// Border/boundary color
        /// </summary>
        [JsonPropertyName("Borders")]
        public string? Borders { get; set; }
    }
}
