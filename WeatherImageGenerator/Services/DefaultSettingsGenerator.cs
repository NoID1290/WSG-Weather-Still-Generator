#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;
using EAS;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Generates and repairs the appsettings.json file with sensible defaults.
    /// Ensures every required section exists even if the file is partial or missing.
    /// </summary>
    public static class DefaultSettingsGenerator
    {
        /// <summary>
        /// Creates a fully-populated AppSettings with sensible defaults for a new installation.
        /// </summary>
        public static AppSettings CreateDefaultSettings()
        {
            var basePath = Directory.GetCurrentDirectory();
            var musicDir = Path.Combine(basePath, "Music");

            return new AppSettings
            {
                Locations = new LocationSettings
                {
                    Location0 = "Montréal",
                    Location1 = null,
                    Location2 = null,
                    Location3 = null,
                    Location4 = null,
                    Location5 = null,
                    Location6 = null,
                    Location7 = null,
                    Location8 = null,
                    Location0Api = WeatherApiType.OpenMeteo,
                    Location1Api = WeatherApiType.OpenMeteo,
                    Location2Api = WeatherApiType.OpenMeteo,
                    Location3Api = WeatherApiType.OpenMeteo,
                    Location4Api = WeatherApiType.OpenMeteo,
                    Location5Api = WeatherApiType.OpenMeteo,
                    Location6Api = WeatherApiType.OpenMeteo,
                    Location7Api = WeatherApiType.OpenMeteo,
                    Location8Api = WeatherApiType.OpenMeteo
                },
                RefreshTimeMinutes = 10,
                ImageGeneration = new ImageGenerationSettings
                {
                    OutputDirectory = "WeatherImages",
                    ImageWidth = 1920,
                    ImageHeight = 1080,
                    ImageFormat = "png",
                    MarginPixels = 50,
                    FontFamily = "Arial",
                    EnableWeatherMaps = true,
                    EnableRadarAnimation = true,
                    EnableGlobalWeatherMap = true
                },
                Video = new VideoSettings
                {
                    OutputFileName = "slideshow_v3.mp4",
                    MusicFileName = "music.mp3",
                    StaticDurationSeconds = 6,
                    FadeDurationSeconds = 0.5,
                    UseTotalDuration = false,
                    TotalDurationSeconds = 60,
                    FrameRate = 30,
                    ResolutionMode = "Mode1080p",
                    VideoCodec = "libx264",
                    VideoBitrate = "4M",
                    Container = "mp4",
                    OutputDirectory = "WeatherImages",
                    EnableFadeTransitions = false,
                    VerboseFfmpeg = false,
                    ShowFfmpegOutputInGui = false,
                    EnableHardwareEncoding = false,
                    doVideoGeneration = true,
                    QualityPreset = "Balanced",
                    UseCrfEncoding = true,
                    CrfValue = 23,
                    MaxBitrate = null,
                    BufferSize = null,
                    EncoderPreset = "medium",
                    ExperimentalEnabled = false,
                    SkipDetailedWeatherOnAlert = false,
                    PlayRadarAnimationCountOnAlert = 1,
                    AlertDisplayDurationSeconds = 10
                },
                Alerts = new AlertsSettings
                {
                    HeaderText = "⚠ WEATHER WARNINGS ⚠",
                    NoAlertsText = "No active weather alerts.",
                    HeaderFontSize = 48,
                    CityFontSize = 28,
                    TypeFontSize = 28,
                    DetailsFontSize = 22,
                    AlertFilename = "0_Alerts.png"
                },
                AlertReady = new AlertReadyOptions
                {
                    Enabled = false,
                    FeedUrls = new List<string>
                    {
                        "tcp://streaming1.naad-adna.pelmorex.com:8080",
                        "tcp://streaming2.naad-adna.pelmorex.com:8080"
                    },
                    IncludeTests = false,
                    MaxAgeHours = 24,
                    PreferredLanguage = "en-CA",
                    AreaFilters = new List<string>(),
                    Jurisdictions = new List<string> { "CA" },
                    HighRiskOnly = true,
                    ExcludeWeatherAlerts = true
                },
                WeatherImages = new WeatherImagesSettings
                {
                    CurrentWeatherFilename = "1_CurrentWeather.png",
                    DailyForecastFilename = "2_DailyForecast.png",
                    DetailedWeatherFilename = "3_DetailedWeather.png",
                    WeatherMapsFilename = "7_WeatherMaps.png",
                    TemperatureWatermarkFilename = "temp_watermark_alpha.png.IGNORE",
                    StaticMapFilename = "STATIC_MAP.IGNORE"
                },
                MapLocations = new Dictionary<string, MapLocationSettings>(),
                ECCC = new ECCCSettings
                {
                    CityFeeds = new Dictionary<string, string>
                    {
                        { "Montreal", "https://weather.gc.ca/rss/city/qc-147_f.xml" }
                    },
                    RadarFeeds = null,
                    EnableCityRadar = false,
                    EnableProvinceRadar = true,
                    UseGeoMetWms = true,
                    CityRadarLayer = "RADAR_1KM_RRAI",
                    ProvinceRadarLayer = "RADAR_1KM_RRAI",
                    ProvinceFrames = 8,
                    ProvinceImageWidth = 1920,
                    ProvinceImageHeight = 1080,
                    ProvincePaddingDegrees = 0.5,
                    ProvinceEnsureCities = null,
                    ProvinceFrameStepMinutes = 6,
                    ProvinceAnimationUrl = null,
                    ProvinceRadarUrl = "https://geo.weather.gc.ca/geomet?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=RADAR_1KM_RRAI&CRS=EPSG:4326&BBOX=45.0,-80.5,53.5,-57.0&WIDTH=1920&HEIGHT=1080&FORMAT=image/png",
                    CityMapTemplate = null,
                    DelayBetweenRequestsMs = 200,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                    DefaultLanguage = "f",
                    DefaultProvince = "qc",
                    DynamicCities = null,
                    DynamicLayers = null,
                    DefaultBoundingBox = null
                },
                Theme = "Blue",
                MinimizeToTray = false,
                MinimizeToTrayOnClose = false,
                AutoStartCycle = false,
                StartWithWindows = false,
                StartMinimizedToTray = false,
                CheckForUpdatesOnStartup = true,
                Music = MusicSettings.CreateDefault(basePath),
                FFmpeg = new FFmpegSettings
                {
                    Source = "Bundled",
                    CustomPath = null
                },
                TTS = new TTSSettings
                {
                    Engine = "piper",
                    Voice = "fr-CA-SylvieNeural",
                    Rate = "+0%",
                    Pitch = "+0Hz",
                    PiperVoice = "fr_FR-siwis-medium",
                    PiperLengthScale = 1.0f
                },
                OpenMap = new OpenMapSettings
                {
                    DefaultMapStyle = "Standard",
                    DefaultZoomLevel = 10,
                    BackgroundColor = "#D3D3D3",
                    OverlayOpacity = 0.7f,
                    TileDownloadTimeoutSeconds = 30,
                    EnableTileCache = true,
                    TileCacheDirectory = "MapCache",
                    CacheDurationHours = 168,
                    UseDarkMode = false,
                    StylePresets = new Dictionary<string, MapStylePreset>
                    {
                        ["Weather"] = new MapStylePreset { Style = "Standard", ZoomLevel = 10, BackgroundColor = "#E8F4F8", OverlayOpacity = 0.75f },
                        ["Radar"] = new MapStylePreset { Style = "Terrain", ZoomLevel = 8, BackgroundColor = "#F0F0F0", OverlayOpacity = 0.8f },
                        ["Satellite"] = new MapStylePreset { Style = "Satellite", ZoomLevel = 11, BackgroundColor = null, OverlayOpacity = 0.65f },
                        ["Minimal"] = new MapStylePreset { Style = "Minimal", ZoomLevel = 10, BackgroundColor = "#FFFFFF", OverlayOpacity = 0.7f }
                    },
                    ColorOverrides = null
                },
                WebUI = new WebUISettings
                {
                    Enabled = false,
                    Port = 5000,
                    AllowRemoteAccess = false,
                    EnableCORS = true,
                    CORSOrigins = new List<string> { "*" }
                }
            };
        }

        /// <summary>
        /// Ensures the appsettings.json file exists and contains all required sections.
        /// Missing sections are filled from defaults. Existing values are preserved.
        /// Returns (settings, listOfRepairs) where listOfRepairs describes what was fixed.
        /// </summary>
        public static (AppSettings Settings, List<string> Repairs) EnsureValidSettings()
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            var repairs = new List<string>();
            AppSettings settings;

            // --- Case 1: File doesn't exist at all → generate from scratch ---
            if (!File.Exists(configPath))
            {
                settings = CreateDefaultSettings();
                SaveSettings(settings, configPath);
                repairs.Add("Created appsettings.json (file was missing)");
                return (settings, repairs);
            }

            // --- Case 2: File exists → try to load and repair missing sections ---
            try
            {
                var json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                settings = JsonSerializer.Deserialize<AppSettings>(json, options)
                           ?? throw new InvalidOperationException("Deserialized to null");
            }
            catch (Exception ex)
            {
                // File is corrupt or unparseable → back up and regenerate
                var backupPath = configPath + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                try { File.Copy(configPath, backupPath, overwrite: true); } catch { }
                repairs.Add($"Backed up corrupt config to {Path.GetFileName(backupPath)}");

                settings = CreateDefaultSettings();
                SaveSettings(settings, configPath);
                repairs.Add("Regenerated appsettings.json (file was corrupt: " + ex.Message + ")");
                return (settings, repairs);
            }

            // --- Repair missing sections ---
            var defaults = CreateDefaultSettings();
            bool needsSave = false;

            if (settings.Locations == null)
            {
                settings.Locations = defaults.Locations;
                repairs.Add("Added missing [Locations] section");
                needsSave = true;
            }

            if (settings.ImageGeneration == null)
            {
                settings.ImageGeneration = defaults.ImageGeneration;
                repairs.Add("Added missing [ImageGeneration] section");
                needsSave = true;
            }

            if (settings.Video == null)
            {
                settings.Video = defaults.Video;
                repairs.Add("Added missing [Video] section");
                needsSave = true;
            }

            if (settings.Alerts == null)
            {
                settings.Alerts = defaults.Alerts;
                repairs.Add("Added missing [Alerts] section");
                needsSave = true;
            }

            if (settings.AlertReady == null)
            {
                settings.AlertReady = defaults.AlertReady;
                repairs.Add("Added missing [AlertReady] section");
                needsSave = true;
            }

            if (settings.WeatherImages == null)
            {
                settings.WeatherImages = defaults.WeatherImages;
                repairs.Add("Added missing [WeatherImages] section");
                needsSave = true;
            }

            if (settings.ECCC == null)
            {
                settings.ECCC = defaults.ECCC;
                repairs.Add("Added missing [ECCC] section");
                needsSave = true;
            }

            if (settings.FFmpeg == null)
            {
                settings.FFmpeg = defaults.FFmpeg;
                repairs.Add("Added missing [FFmpeg] section");
                needsSave = true;
            }

            if (settings.TTS == null)
            {
                settings.TTS = defaults.TTS;
                repairs.Add("Added missing [TTS] section");
                needsSave = true;
            }

            if (settings.OpenMap == null)
            {
                settings.OpenMap = defaults.OpenMap;
                repairs.Add("Added missing [OpenMap] section");
                needsSave = true;
            }

            if (settings.WebUI == null)
            {
                settings.WebUI = defaults.WebUI;
                repairs.Add("Added missing [WebUI] section");
                needsSave = true;
            }

            if (settings.Music == null)
            {
                settings.Music = defaults.Music;
                repairs.Add("Added missing [Music] section");
                needsSave = true;
            }

            if (settings.MapLocations == null)
            {
                settings.MapLocations = defaults.MapLocations;
                repairs.Add("Added missing [MapLocations] section");
                needsSave = true;
            }

            if (string.IsNullOrWhiteSpace(settings.Theme))
            {
                settings.Theme = defaults.Theme;
                repairs.Add("Set default Theme to 'Blue'");
                needsSave = true;
            }

            if (settings.RefreshTimeMinutes <= 0)
            {
                settings.RefreshTimeMinutes = defaults.RefreshTimeMinutes;
                repairs.Add("Set default RefreshTimeMinutes to 10");
                needsSave = true;
            }

            // Save if anything was repaired
            if (needsSave)
            {
                SaveSettings(settings, configPath);
            }

            return (settings, repairs);
        }

        /// <summary>
        /// Serializes and writes settings to file.
        /// </summary>
        private static void SaveSettings(AppSettings settings, string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(path, json);
        }
    }
}
