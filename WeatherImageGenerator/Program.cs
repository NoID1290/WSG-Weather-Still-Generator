#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing; // NuGet: System.Drawing.Common
using System.Drawing.Imaging;
using System.IO;
using System.Linq; 
using System.Net.Http; 
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMeteo; // NuGet: OpenMeteo
using EAS;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Forms;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator
{
    class Program
    {
        // Static instance of Web UI service
        private static WebUIService? _webUIService;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
        [STAThread]
        static void Main(string[] args)
        {
            // Initialize FFmpeg settings from configuration early
            FFmpegLocator.ConfigureFromSettings();

            // Helper flags for testing
            if (args.Contains("--create-test-images"))
            {
                ImageGenerator.CreateTestImages();
                return;
            }

            if (args.Contains("--generate-icons"))
            {
                var config = ConfigManager.LoadConfig();
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "WeatherImages", "Icons");
                IconGenerator.GenerateAll(outputDir);
                return;
            }

            if (args.Contains("--make-video-now"))
            {
                var config = ConfigManager.LoadConfig();
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                StartMakeVideo(outputDir);
                return;
            }

            if (args.Contains("--generate-province-animation"))
            {
                var config = ConfigManager.LoadConfig();
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                using (var http = new System.Net.Http.HttpClient())
                {
                    WeatherImageGenerator.Services.ECCC.CreateProvinceRadarAnimation(http, outputDir).GetAwaiter().GetResult();
                }

                Logger.Log("Province animation generation requested (one-off). Exiting.");
                return;
            }

            if (args.Contains("--test-emergency-alerts"))
            {
                AllocConsole();
                try
                {
                    TestEmergencyAlerts.RunTest().GetAwaiter().GetResult();
                }
                finally
                {
                    FreeConsole();
                }
                return;
            }

            // If user supplies --nogui, run as console as before
            if (args.Contains("--nogui"))
            {
                // Ensure a console is available for nogui mode (when running as WinExe)
                AllocConsole();
                try
                {
                    RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                finally
                {
                    FreeConsole();
                }
                return;
            }

            if (args.Contains("--smoke-gui"))
            {
                // Create MainForm and exercise the logger handlers without showing the UI (smoke test)
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var mf = new MainForm())
                {
                    Logger.Log("[SMOKE] Created MainForm");
                    Logger.Log("[RUNNING] smoke");
                    Logger.Log("[DONE] smoke");
                }

                return;
            }

            if (args.Contains("--smoke-make-video"))
            {
                // Instantiate MainForm (subscribe to Logger events) and trigger a video generation (smoke test)
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var mf = new MainForm())
                {
                    ImageGenerator.CreateTestImages();
                    var config = ConfigManager.LoadConfig();
                    var outputDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                    StartMakeVideo(outputDir);
                }

                return;
            }

            if (args.Contains("--smoke-save-config"))
            {
                var cfg = ConfigManager.LoadConfig();
                cfg.Video ??= new VideoSettings();
                ConfigManager.SaveConfig(cfg);
                ConfigManager.ReloadConfig();
                Logger.Log("[SMOKE] Config save & reload completed");
                return;
            }

            // Launch WinForms GUI which hosts an embedded console
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Initialize Web UI if enabled in settings
            var initialConfig = ConfigManager.LoadConfig();
            if (initialConfig.WebUI?.Enabled ?? false)
            {
                _webUIService = new WebUIService(initialConfig.WebUI.Port);
                Task.Run(async () => await _webUIService.StartAsync());
            }
            
            Application.Run(new MainForm());
            
            // Stop Web UI service when application closes
            if (_webUIService?.IsRunning ?? false)
            {
                Task.Run(async () => await _webUIService.StopAsync()).GetAwaiter().GetResult();
            }
        }

        // Event used by the GUI to show the remaining time while the background worker is sleeping
        public static event Action<TimeSpan>? SleepRemainingUpdated;

        // Event that reports overall progress of the update cycle (0-100) and a short status message
        public static event Action<double, string>? ProgressUpdated;

        // Event that reports the fetched weather data
        public static event Action<WeatherForecast?[]>? WeatherDataFetched;

        // Event that reports the fetched alerts
        public static event Action<List<AlertEntry>>? AlertsFetched;

        /// <summary>
        /// Fetches weather data for a single location using the specified API.
        /// Returns (forecast, actualApiUsed) tuple.
        /// When using ECCC, also fetches OpenMeteo data to fill in missing fields (hourly, precipitation, etc.)
        /// </summary>
        private static async Task<(WeatherForecast? Forecast, string ApiUsed)> FetchWeatherForLocationAsync(
            string locationName, 
            WeatherApiType api, 
            OpenMeteoClient openMeteoClient,
            HttpClient httpClient)
        {
            string preferredApi = api == WeatherApiType.ECCC ? "ECCC" : "OpenMeteo";
            Logger.Log($"[{preferredApi}] Fetching weather for {locationName}...");

            switch (api)
            {
                case WeatherApiType.ECCC:
                    // Set up ECCC API logging
                    ECCC.ECCCApi.Log = (msg) => Logger.Log(msg);
                    
                    // Fetch ECCC and OpenMeteo in parallel for hybrid data
                    var ecccTask = ECCC.ECCCApi.GetWeatherAsync(httpClient, locationName);
                    var openMeteoTask = openMeteoClient.QueryAsync(locationName);
                    
                    await Task.WhenAll(ecccTask, openMeteoTask);
                    
                    var ecccForecast = ecccTask.Result;
                    var openMeteoFallback = openMeteoTask.Result;
                    
                    // Accept ECCC data if we have either temperature or daily forecasts
                    if (ecccForecast != null && ecccForecast.Current != null)
                    {
                        var tempValue = ecccForecast.Current.Temperature_2m ?? float.NaN;
                        var hasTemp = !float.IsNaN(tempValue);
                        var hasDaily = ecccForecast.Daily?.Time?.Length > 0;
                        
                        if (hasTemp || hasDaily)
                        {
                            // Merge with OpenMeteo to fill missing data (hourly, precipitation, etc.)
                            var mergedForecast = ECCC.Services.OpenMeteoConverter.MergeWithOpenMeteo(ecccForecast, openMeteoFallback);
                            
                            var hasHourly = mergedForecast.Hourly?.Time?.Length > 0;
                            Logger.Log($"✓ [ECCC+OpenMeteo] Using hybrid data for {locationName} (temp={tempValue}°C, hourly={hasHourly})");
                            return (mergedForecast, "ECCC+OpenMeteo");
                        }
                        else
                        {
                            Logger.Log($"[ECCC] No temperature data parsed for {locationName}");
                        }
                    }
                    
                    // Fall back to pure OpenMeteo if ECCC fails completely
                    if (openMeteoFallback != null)
                    {
                        Logger.Log($"[ECCC] Failed to fetch ECCC data for {locationName}, using OpenMeteo only");
                        return (openMeteoFallback, "OpenMeteo (fallback)");
                    }
                    
                    // Retry OpenMeteo once more as last resort
                    try
                    {
                        Logger.Log($"[ECCC] Retrying OpenMeteo for {locationName}...");
                        await Task.Delay(500);
                        var retryForecast = await openMeteoClient.QueryAsync(locationName);
                        if (retryForecast != null)
                        {
                            Logger.Log($"✓ [OpenMeteo Retry] Successfully fetched weather for {locationName}");
                            return (retryForecast, "OpenMeteo (retry)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[OpenMeteo Retry] Also failed for {locationName}: {ex.Message}");
                    }
                    
                    Logger.Log($"✗ Failed to fetch weather for {locationName} from any source");
                    return (null, "None");

                case WeatherApiType.OpenMeteo:
                default:
                    // Try OpenMeteo with retry and ECCC fallback
                    WeatherForecast? omForecast = null;
                    
                    // First attempt
                    try
                    {
                        omForecast = await openMeteoClient.QueryAsync(locationName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[OpenMeteo] First attempt failed for {locationName}: {ex.Message}");
                    }
                    
                    // Retry once if first attempt failed
                    if (omForecast == null)
                    {
                        try
                        {
                            await Task.Delay(500); // Brief delay before retry
                            omForecast = await openMeteoClient.QueryAsync(locationName);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[OpenMeteo] Retry failed for {locationName}: {ex.Message}");
                        }
                    }
                    
                    if (omForecast != null)
                    {
                        return (omForecast, "OpenMeteo");
                    }
                    
                    // Fallback to ECCC if OpenMeteo fails completely
                    Logger.Log($"[OpenMeteo] Falling back to ECCC for {locationName}...");
                    try
                    {
                        ECCC.ECCCApi.Log = (msg) => Logger.Log(msg);
                        var ecccFallback = await ECCC.ECCCApi.GetWeatherAsync(httpClient, locationName);
                        if (ecccFallback != null && ecccFallback.Current != null)
                        {
                            Logger.Log($"✓ [ECCC Fallback] Successfully fetched weather for {locationName}");
                            return (ecccFallback, "ECCC (fallback)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ECCC Fallback] Also failed for {locationName}: {ex.Message}");
                    }
                    
                    Logger.Log($"✗ Failed to fetch weather for {locationName} from any source");
                    return (null, "None");
            }
        }

        /// <summary>
        /// Fetches alerts from ECCC and Alert Ready, applies optional filtering and de-duplicates the results.
        /// </summary>
        private static async Task<List<AlertEntry>> FetchCombinedAlertsAsync(HttpClient httpClient, string[] locations, AppSettings config)
        {
            var alerts = new List<AlertEntry>();

            try
            {
                var ecccAlerts = await WeatherImageGenerator.Services.ECCC.FetchAllAlerts(httpClient, locations);
                Logger.Log($"✓ [ECCC] Found {ecccAlerts.Count} active alerts.");
                alerts.AddRange(ecccAlerts);
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ [ECCC] Failed to fetch alerts: {ex.Message}");
            }

            var alertReadyOptions = config.AlertReady ?? new AlertReadyOptions { Enabled = false };
            if (alertReadyOptions.Enabled)
            {
                if (alertReadyOptions.FeedUrls?.Any(u => !string.IsNullOrWhiteSpace(u)) == true)
                {
                    var arClient = new AlertReadyClient(httpClient, alertReadyOptions)
                    {
                        Log = msg => Logger.Log($"[AlertReady] {msg}")
                    };

                    try
                    {
                        var arAlerts = await arClient.FetchAlertsAsync(locations);
                        Logger.Log($"✓ [AlertReady] Found {arAlerts.Count} active alerts.");
                        alerts.AddRange(arAlerts);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"✗ [AlertReady] Failed to fetch alerts: {ex.Message}");
                    }
                }
                else
                {
                    Logger.Log("[AlertReady] Enabled but no feed URLs configured; skipping.");
                }
            }

            return DeduplicateAlerts(alerts);
        }

        /// <summary>
        /// Fetches Alert Ready (NAAD) alerts only, filtered per options, de-duplicated.
        /// </summary>
        private static async Task<List<AlertEntry>> FetchAlertReadyOnlyAsync(HttpClient httpClient, string?[] locations, AppSettings config)
        {
            var list = new List<AlertEntry>();
            var alertReadyOptions = config.AlertReady ?? new AlertReadyOptions { Enabled = false };
            if (!alertReadyOptions.Enabled)
            {
                Logger.Log("[AlertReady] Disabled; returning empty list.");
                return list;
            }

            if (alertReadyOptions.FeedUrls?.Any(u => !string.IsNullOrWhiteSpace(u)) == true)
            {
                var arClient = new AlertReadyClient(httpClient, alertReadyOptions)
                {
                    Log = msg => Logger.Log($"[AlertReady] {msg}")
                };

                try
                {
                    list = await arClient.FetchAlertsAsync(locations.Where(l => l != null).Cast<string>());
                    Logger.Log($"✓ [AlertReady] Found {list.Count} active alerts.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"✗ [AlertReady] Failed to fetch alerts: {ex.Message}");
                }
            }
            else
            {
                Logger.Log("[AlertReady] Enabled but no feed URLs configured; returning empty set.");
            }

            return DeduplicateAlerts(list);
        }

        private static List<AlertEntry> DeduplicateAlerts(IEnumerable<AlertEntry> alerts)
        {
            // Group alerts by normalized summary content (the actual message)
            // If two alerts have the same message, they're the same alert even if titles differ slightly
            var grouped = alerts
                .GroupBy(a => NormalizeSummary(a.Summary), StringComparer.OrdinalIgnoreCase)
                .Select(g => 
                {
                    // Pick the most important/specific title from the group
                    var bestAlert = g.OrderByDescending(a => GetAlertPriority(a.Title)).First();
                    
                    // Combine cities from all matching alerts
                    var allCities = g.SelectMany(a => a.City.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(c => c)
                        .ToList();
                    
                    return new AlertEntry
                    {
                        City = string.Join(", ", allCities),
                        Type = bestAlert.Type,
                        Title = CleanAlertTitle(bestAlert.Title),
                        Summary = bestAlert.Summary,
                        SeverityColor = bestAlert.SeverityColor,
                        // Preserve new CAP fields
                        Impact = bestAlert.Impact,
                        Confidence = bestAlert.Confidence,
                        IssuedAt = bestAlert.IssuedAt,
                        ExpiresAt = bestAlert.ExpiresAt,
                        Description = bestAlert.Description,
                        Instructions = bestAlert.Instructions,
                        DetailUrl = bestAlert.DetailUrl,
                        Region = string.Join(", ", allCities)
                    };
                })
                .ToList();

            return grouped;
        }

        private static string NormalizeSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return "";
            
            // Take first 500 chars as fingerprint (enough to identify unique messages)
            var normalized = summary.Length > 500 ? summary.Substring(0, 500) : summary;
            
            // Remove time/date variations
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\d{1,2}h\d{2}", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\d{1,2}:\d{2}", "");
            
            // Collapse whitespace
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
            
            return normalized.Trim();
        }

        private static int GetAlertPriority(string title)
        {
            // Higher priority = more important/specific alert type
            if (title.Contains("WARNING", StringComparison.OrdinalIgnoreCase) || 
                title.Contains("AVERTISSEMENT", StringComparison.OrdinalIgnoreCase))
                return 3;
            
            if (title.Contains("WATCH", StringComparison.OrdinalIgnoreCase) || 
                title.Contains("VEILLE", StringComparison.OrdinalIgnoreCase))
                return 2;
            
            if (title.Contains("STATEMENT", StringComparison.OrdinalIgnoreCase) || 
                title.Contains("BULLETIN", StringComparison.OrdinalIgnoreCase))
                return 1;
            
            return 0;
        }

        private static string NormalizeAlertTitle(string title)
        {
            // Remove city names but KEEP the core alert type for proper grouping
            var normalized = title.Trim();
            
            // Remove everything after comma (usually city names)
            var commaIndex = normalized.IndexOf(',');
            if (commaIndex > 0)
            {
                normalized = normalized.Substring(0, commaIndex);
            }
            
            // Remove time/date stamps but keep the alert type intact
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\d{1,2}h\d{2}", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\d{1,2}:\d{2}", "");
            
            // Collapse multiple spaces
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
            
            return normalized.Trim();
        }

        private static string CleanAlertTitle(string title)
        {
            // Keep the alert type but remove city name for display
            var cleaned = title.Trim();
            
            // Remove everything after comma (city names)
            var commaIndex = cleaned.IndexOf(',');
            if (commaIndex > 0)
            {
                cleaned = cleaned.Substring(0, commaIndex);
            }
            
            return cleaned.Trim();
        }

        public static async Task FetchDataOnlyAsync(CancellationToken cancellationToken = default)
        {            EnsureIconsExist();            var config = ConfigManager.LoadConfig();
            OpenMeteoClient client = new OpenMeteoClient();

            using (HttpClient httpClient = new HttpClient())
            {
                string?[] locations = config.Locations?.GetLocationsArray() ?? Array.Empty<string>();
                var apiPreferences = config.Locations?.GetApiPreferencesArray() ?? new WeatherApiType[0];
                Logger.Log($"Fetching weather data (Fetch Only)...");
                ProgressUpdated?.Invoke(0, "Starting fetch only...");

                WeatherForecast?[] allForecasts = new WeatherForecast?[locations.Length];

                for (int i = 0; i < locations.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    string? loc = locations[i];
                    if (string.IsNullOrWhiteSpace(loc)) continue;

                    var api = i < apiPreferences.Length ? apiPreferences[i] : WeatherApiType.OpenMeteo;
                    string apiName = api == WeatherApiType.ECCC ? "ECCC" : "OpenMeteo";

                    try 
                    {
                        var (forecast, actualApi) = await FetchWeatherForLocationAsync(loc, api, client, httpClient);
                        allForecasts[i] = forecast;
                        Logger.Log($"✓ [{actualApi}] Fetched weather data for {loc}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"✗ [{apiName}] Failed to fetch for {loc}: {ex.Message}");
                    }
                    
                    if (locations.Length > 0)
                    {
                        var fetchPct = ((i + 1) / (double)locations.Length) * 80.0;
                        ProgressUpdated?.Invoke(fetchPct, $"Fetching {loc} ({i + 1}/{locations.Length})");
                    }
                }

                WeatherDataFetched?.Invoke(allForecasts);

                if (cancellationToken.IsCancellationRequested) return;

                Logger.Log("[Alerts] Fetching weather alerts...");
                try
                {
                    var alerts = await FetchCombinedAlertsAsync(httpClient, locations, config);
                    Logger.Log($"✓ [Alerts] Found {alerts.Count} active alerts.");
                    AlertsFetched?.Invoke(alerts);
                }
                catch (Exception ex)
                {
                    Logger.Log($"✗ [Alerts] Failed to fetch alerts: {ex.Message}");
                }

                ProgressUpdated?.Invoke(100, "Fetch complete");
            }
        }

        public static async Task GenerateStillsOnlyAsync(CancellationToken cancellationToken = default)
        {
            EnsureIconsExist();
            var config = ConfigManager.LoadConfig();
            OpenMeteoClient client = new OpenMeteoClient();

            using (HttpClient httpClient = new HttpClient())
            {
                string?[] locations = config.Locations?.GetLocationsArray() ?? Array.Empty<string>();
                var apiPreferences = config.Locations?.GetApiPreferencesArray() ?? new WeatherApiType[0];
                Logger.Log($"Fetching weather data (Stills Only)...");
                ProgressUpdated?.Invoke(0, "Starting stills generation...");

                WeatherForecast?[] allForecasts = new WeatherForecast?[locations.Length];

                // Fetch Weather
                for (int i = 0; i < locations.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    string? loc = locations[i];
                    if (string.IsNullOrWhiteSpace(loc)) continue;

                    var api = i < apiPreferences.Length ? apiPreferences[i] : WeatherApiType.OpenMeteo;
                    string apiName = api == WeatherApiType.ECCC ? "ECCC" : "OpenMeteo";

                    try 
                    {
                        var (forecast, actualApi) = await FetchWeatherForLocationAsync(loc, api, client, httpClient);
                        allForecasts[i] = forecast;
                        Logger.Log($"✓ [{actualApi}] Fetched weather data for {loc}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"✗ [{apiName}] Failed to fetch for {loc}: {ex.Message}");
                    }
                    
                    if (locations.Length > 0)
                    {
                        var fetchPct = ((i + 1) / (double)locations.Length) * 15.0;
                        ProgressUpdated?.Invoke(fetchPct, $"Fetching {loc} ({i + 1}/{locations.Length})");
                    }
                }

                WeatherDataFetched?.Invoke(allForecasts);

                if (cancellationToken.IsCancellationRequested) return;

                // Fetch weather alerts from ECCC and Alert Ready
                Logger.Log("[Alerts] Fetching weather alerts...");
                List<AlertEntry> alerts = new List<AlertEntry>();
                try
                {
                    alerts = await FetchCombinedAlertsAsync(httpClient, locations, config);
                    Logger.Log($"✓ [Alerts] Found {alerts.Count} active alerts.");
                    AlertsFetched?.Invoke(alerts);
                }
                catch (Exception ex)
                {
                    Logger.Log($"✗ [Alerts] Failed to fetch alerts: {ex.Message}");
                }

                if (allForecasts[0] == null) 
                {
                    Logger.Log("Primary location data missing. Aborting stills generation.");
                    return;
                }

                // Generate Images
                string outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Clear existing images before generating new ones
                ClearOutputImages(outputDir);

                Logger.Log("Generating still images...");
                ProgressUpdated?.Invoke(15, "Generating still images");

                int detailedPerImage = 3;
                int numDetailedImages = Math.Max(1, (locations.Length + detailedPerImage - 1) / detailedPerImage);
                int imageSteps = Math.Max(1, numDetailedImages + 3); 
                int imageStepsCompleted = 0;

                // Use alerts as provided (they should already be properly filtered)
                var activeAlerts = alerts;

                // Check if we should skip DetailedWeather due to active alerts
                bool skipDetailedWeather = config.Video?.SkipDetailedWeatherOnAlert == true && activeAlerts.Count > 0;
                
                if (skipDetailedWeather)
                {
                    Logger.Log("Skipping Detailed Weather generation (alert is active and 'Skip Detailed Weather on Alert' is enabled).");
                }
                else
                {
                    // Detailed Weather
                    for (int batch = 0; batch < numDetailedImages; batch++)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        int start = batch * detailedPerImage;
                        int end = Math.Min(locations.Length, start + detailedPerImage);

                        var batchItems = new List<(WeatherForecast? Forecast, string? Name, int Index)>();
                        for (int j = start; j < end; j++)
                        {
                            batchItems.Add((allForecasts[j], locations[j], j));
                        }

                        ImageGenerator.GenerateDetailedWeatherImageBatch(batchItems.ToArray(), outputDir, batch);

                        imageStepsCompleted++;
                        var pct = 15.0 + (imageStepsCompleted / (double)imageSteps) * 85.0;
                        ProgressUpdated?.Invoke(pct, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                    }
                }

                // Maps
                if (cancellationToken.IsCancellationRequested) return;
                
                if (config.ImageGeneration?.EnableWeatherMaps == true)
                {
                    try
                    {
                        Logger.Log("Fetching radar images...");
                        await WeatherImageGenerator.Services.ECCC.FetchRadarImages(httpClient, outputDir);
                        Logger.Log("✓ Radar images fetched.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"X Failed to fetch radar images: {ex.Message}");
                    }

                    ImageGenerator.GenerateMapsImage(allForecasts, locations, outputDir);
                }
                else
                {
                    Logger.Log("Skipping Weather Maps generation (disabled in settings).");
                }

                imageStepsCompleted++;
                ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 85.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                // Alerts - Only generate if there are active alerts
                if (cancellationToken.IsCancellationRequested) return;
                if (activeAlerts.Count > 0)
                {
                    ImageGenerator.GenerateAlertsImage(activeAlerts, outputDir);
                    Logger.Log($"✓ Generated alerts image with {activeAlerts.Count} alert(s)");
                }
                else
                {
                    Logger.Log("No active alerts - skipping alerts image generation");
                }
                imageStepsCompleted++;
                ProgressUpdated?.Invoke(100, "Stills generation complete");
                Logger.Log($"✓ Stills Generation Complete. Images saved to: {outputDir}");
            }
        }

        public static async Task RunAsync(CancellationToken cancellationToken = default)
        {
            // Load configuration
            var config = ConfigManager.LoadConfig();
            
            // Set up ECCC logging callback
            WeatherImageGenerator.Services.ECCC.Log = (msg) => Logger.Log(msg);

            // Initialize the API client
            OpenMeteoClient client = new OpenMeteoClient();

            // Initialize HttpClient for ECCC Alerts
            using (HttpClient httpClient = new HttpClient())
            {
                // Load locations into an array for easier handling
                string?[] locations = config.Locations?.GetLocationsArray() ?? Array.Empty<string>();
                var apiPreferences = config.Locations?.GetApiPreferencesArray() ?? new WeatherApiType[0];

                // Infinite loop to keep the program running
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        Logger.Log($"\n--- Starting Update Cycle: {DateTime.Now} ---");
                        // Notify GUI we are at the beginning of the cycle
                        ProgressUpdated?.Invoke(0, "Starting update cycle");
                        Logger.Log($"Fetching weather data...");
                        
                        // Array to store results for all locations
                        WeatherForecast?[] allForecasts = new WeatherForecast?[locations.Length];

                        // Fetch weather data for each location
                        for (int i = 0; i < locations.Length; i++)
                        {
                            string? loc = locations[i];
                            if (string.IsNullOrWhiteSpace(loc)) continue;

                            var api = i < apiPreferences.Length ? apiPreferences[i] : WeatherApiType.OpenMeteo;
                            string apiName = api == WeatherApiType.ECCC ? "ECCC" : "OpenMeteo";

                            try 
                            {
                                var (forecast, actualApi) = await FetchWeatherForLocationAsync(loc, api, client, httpClient);
                                allForecasts[i] = forecast;
                                Logger.Log($"✓ [{actualApi}] Fetched weather data for {loc}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"✗ [{apiName}] Failed to fetch for {loc}: {ex.Message}");
                            }

                            // Report incremental fetch progress (15% of total cycle)
                            if (locations.Length > 0)
                            {
                                var fetchPct = ((i + 1) / (double)locations.Length) * 15.0;
                                ProgressUpdated?.Invoke(fetchPct, $"Fetching {loc} ({i + 1}/{locations.Length})");
                            }
                        }

                        // Notify GUI that weather data has been fetched
                        WeatherDataFetched?.Invoke(allForecasts);

                        // Fetch weather alerts from ECCC and Alert Ready
                        var alerts = new List<AlertEntry>();
                        Logger.Log("[Alerts] Fetching weather alerts...");
                        try
                        {
                            alerts = await FetchCombinedAlertsAsync(httpClient, locations, config);
                            Logger.Log($"✓ [Alerts] Found {alerts.Count} active alerts.");
                            AlertsFetched?.Invoke(alerts);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"✗ [Alerts] Failed to fetch alerts: {ex.Message}");
                        }



                        // Check if we have at least the primary location data to proceed
                        if (allForecasts[0] == null) 
                        {
                            Logger.Log("Primary location data missing. Retrying in 1 minute...");
                            // Archive current logs before waiting/retrying
                            Logger.RequestArchive();
                            try { await Task.Delay(60000, cancellationToken); } catch (OperationCanceledException) { break; }
                            continue;
                        }

                        // Create output directory
                        string outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        // Clear existing images before generating new ones
                        ClearOutputImages(outputDir);

                        Logger.Log("Generating still images...");
                        // Image generation comprises the bulk of the cycle (about 65%)
                        ProgressUpdated?.Invoke(15, "Generating still images");

                        int detailedPerImage = 3;
                        int numDetailedImages = Math.Max(1, (locations.Length + detailedPerImage - 1) / detailedPerImage);
                        int imageSteps = Math.Max(1, numDetailedImages + 3); // detailed images + maps + radar + alerts
                        int imageStepsCompleted = 0;

                        // --- IMAGE GENERATION ---
                        // Order: 00-07_Radar (8 frames), 08_Alerts, 09+_Detailed, Last_WeatherMap
                        
                        // 1. Radar Animation Frames (00-07) - Generate as individual numbered images
                        int nextImageNumber = 0; // Track next available image number
                        if (config.ImageGeneration?.EnableWeatherMaps == true && 
                            config.ImageGeneration.EnableRadarAnimation && 
                            config.ECCC?.EnableProvinceRadar == true)
                        {
                            try
                            {
                                Logger.Log("[Radar Animation] Generating radar animation with OpenMap overlay...");
                                var radarService = new RadarAnimationService(httpClient, 
                                    config.ECCC.ProvinceImageWidth, 
                                    config.ECCC.ProvinceImageHeight);
                                
                                // Calculate center point (default to Quebec center)
                                double centerLat = 48.5;
                                double centerLon = -71.0;
                                
                                // If specific cities are configured, calculate center
                                if (config.ECCC.ProvinceEnsureCities != null && config.ECCC.ProvinceEnsureCities.Length > 0)
                                {
                                    var cityCoords = GetCityCoordinates(config.ECCC.ProvinceEnsureCities);
                                    if (cityCoords.Count > 0)
                                    {
                                        centerLat = cityCoords.Average(c => c.lat);
                                        centerLon = cityCoords.Average(c => c.lon);
                                    }
                                }
                                
                                var frames = await radarService.GenerateRadarAnimationWithMapAsync(
                                    centerLat, centerLon, outputDir,
                                    numFrames: config.ECCC.ProvinceFrames,
                                    frameStepMinutes: config.ECCC.ProvinceFrameStepMinutes,
                                    width: config.ECCC.ProvinceImageWidth,
                                    height: config.ECCC.ProvinceImageHeight,
                                    radarLayer: config.ECCC.ProvinceRadarLayer ?? "RADAR_1KM_RRAI",
                                    zoomLevel: 7);
                                
                                nextImageNumber = frames.Count; // Set next number after radar frames
                                Logger.Log($"✓ Radar animation: {frames.Count} frames created (00-{frames.Count-1:D2})");
                                
                                // Check if we need to duplicate radar frames for alerts
                                // Note: We'll duplicate after we know if there are active alerts, so store frames for later
                                if (config.Video?.PlayRadarAnimationCountOnAlert > 1)
                                {
                                    // Check if we have active alerts
                                    var alertsCheck = alerts;
                                    
                                    if (alertsCheck.Count > 0)
                                    {
                                        try
                                        {
                                            int replayCount = (config.Video.PlayRadarAnimationCountOnAlert - 1); // Subtract 1 because we already have 1 play
                                            Logger.Log($"[Radar Animation] Duplicating {frames.Count} radar frames {replayCount} more time(s) due to active alert...");
                                            // Duplicate the radar frames by copying them with new numbering
                                            for (int replay = 0; replay < replayCount; replay++)
                                            {
                                                for (int i = 0; i < frames.Count; i++)
                                                {
                                                    string originalFile = frames[i];
                                                    string originalName = Path.GetFileName(originalFile);
                                                    // Insert copy at a higher number to avoid conflicts
                                                    string newName = originalName.Replace($"{i:D2}_", $"{frames.Count * (replay + 1) + i:D2}_");
                                                    string newFile = Path.Combine(outputDir, newName);
                                                    if (File.Exists(originalFile) && !File.Exists(newFile))
                                                    {
                                                        File.Copy(originalFile, newFile);
                                                    }
                                                }
                                            }
                                            nextImageNumber += frames.Count * replayCount; // Increment for the duplicated frames
                                            Logger.Log($"✓ Radar animation replayed {replayCount} additional time(s): {frames.Count * replayCount} additional frames added");
                                        }
                                        catch (Exception dupEx)
                                        {
                                            Logger.Log($"✗ Failed to duplicate radar frames: {dupEx.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"✗ Failed to generate radar animation: {ex.Message}", ConsoleColor.Red);
                            }
                        }
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 2. Weather Alerts (next available number after radar)
                        // Only generate if there are active alerts
                        var activeAlerts = alerts;
                        
                        if (activeAlerts.Count > 0)
                        {
                            // Generate alerts image with active alerts
                            string alertFilename = $"{nextImageNumber:D2}_WeatherAlerts.png";
                            string alertPath = Path.Combine(outputDir, alertFilename);
                            
                            ImageGenerator.GenerateAlertsImage(activeAlerts, outputDir);
                            
                            // Rename the alert image to use correct number
                            string defaultAlertPath = Path.Combine(outputDir, "01_WeatherAlerts.png");
                            if (File.Exists(defaultAlertPath) && !File.Exists(alertPath))
                            {
                                File.Move(defaultAlertPath, alertPath);
                            }
                            nextImageNumber++; // Increment after alerts
                            Logger.Log($"✓ Generated alerts image with {activeAlerts.Count} alert(s)");
                        }
                        else
                        {
                            Logger.Log("No active alerts - skipping alerts image generation");
                        }
                        
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 3. Detailed Weather (starting from next available number, batched, up to 3 cities per image)
                        // Check if we should skip DetailedWeather due to active alerts
                        bool skipDetailedWeather = config.Video?.SkipDetailedWeatherOnAlert == true && activeAlerts.Count > 0;
                        
                        if (skipDetailedWeather)
                        {
                            Logger.Log("Skipping Detailed Weather generation (alert is active and 'Skip Detailed Weather on Alert' is enabled).");
                        }
                        else
                        {
                            int detailedStartNumber = nextImageNumber;
                            for (int batch = 0; batch < numDetailedImages; batch++)
                            {
                                int start = batch * detailedPerImage;
                                int end = Math.Min(locations.Length, start + detailedPerImage);

                                var batchItems = new List<(WeatherForecast? Forecast, string? Name, int Index)>();
                                for (int j = start; j < end; j++)
                                {
                                    batchItems.Add((allForecasts[j], locations[j], j));
                                }

                                ImageGenerator.GenerateDetailedWeatherImageBatch(batchItems.ToArray(), outputDir, batch);
                                
                                // Rename detailed weather images to use correct numbering
                                var oldPattern = $"{2 + batch:D2}_DetailedWeather_*.png";
                                var oldFiles = Directory.GetFiles(outputDir, oldPattern);
                                if (oldFiles.Length > 0)
                                {
                                    string oldFile = oldFiles[0];
                                    string fileName = Path.GetFileName(oldFile);
                                    string newFileName = fileName.Replace($"{2 + batch:D2}_", $"{detailedStartNumber + batch:D2}_");
                                    string newFile = Path.Combine(outputDir, newFileName);
                                    if (File.Exists(oldFile) && !File.Exists(newFile))
                                    {
                                        File.Move(oldFile, newFile);
                                    }
                                }

                                imageStepsCompleted++;
                                var pct = 15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0;
                                ProgressUpdated?.Invoke(pct, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                            }
                            nextImageNumber += numDetailedImages; // Update for next images
                        }

                        // 4. Global Weather Map with Temperatures (last)
                        if (config.ImageGeneration?.EnableWeatherMaps == true && config.ImageGeneration.EnableGlobalWeatherMap)
                        {
                            try
                            {
                                Logger.Log("[Weather Map] Generating Quebec weather map with static cities...");
                                var weatherMapService = new GlobalWeatherMapService(
                                    config.ImageGeneration.ImageWidth,
                                    config.ImageGeneration.ImageHeight);
                                
                                // Use next available number for weather map
                                var weatherMapPath = Path.Combine(outputDir, $"{nextImageNumber:D2}_WeatherMaps.png");
                                
                                // Use static Quebec cities with exact coordinates (ignores user location settings)
                                await weatherMapService.GenerateStaticQuebecWeatherMapAsync(weatherMapPath);
                                
                                Logger.Log($"✓ Quebec weather map generated ({nextImageNumber:D2})");
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"✗ Failed to generate weather map: {ex.Message}", ConsoleColor.Red);
                            }
                        }
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 5. Video Generation (Optional)
                        ProgressUpdated?.Invoke(80.0, "Starting video generation");

                        // If video settings are configured, create a video from the generated images
                        if (config.Video?.doVideoGeneration == true)
                        {
                            StartMakeVideo(outputDir);
                        }
                        else
                        {
                            Logger.Log("[INFO] Video settings not configured; skipping video generation.");
                        }

                        Logger.Log($"✓ Cycle Complete. Images saved to: {outputDir}");

                        // Ensure GUI reaches 100% at completion
                        ProgressUpdated?.Invoke(100.0, "Cycle complete");

                        // Memory and resource cleanup to prevent buildup over long runtime
                        CleanupTempFiles();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        LogMemoryUsage();

                        // Wait Logic
                        if (config.RefreshTimeMinutes > 0)
                        {
                            Logger.Log($"Sleeping for {config.RefreshTimeMinutes} minutes...");

                            var totalMs = Math.Max(1, config.RefreshTimeMinutes * 60000);
                            var end = DateTime.UtcNow.AddMilliseconds(totalMs);
                            // Periodically notify GUI about remaining time (1s resolution)
                            while (DateTime.UtcNow < end)
                            {
                                var remaining = end - DateTime.UtcNow;
                                try
                                {
                                    SleepRemainingUpdated?.Invoke(remaining);
                                    await Task.Delay(1000, cancellationToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Ensure GUI is cleared and exit sleeping early
                                    SleepRemainingUpdated?.Invoke(TimeSpan.Zero);
                                    break;
                                }
                            }

// Request archival of logs for this completed cycle (keep UI small & avoid overflow)
                        Logger.RequestArchive();

                        // Clear the GUI countdown when sleep completes
                            SleepRemainingUpdated?.Invoke(TimeSpan.Zero);
                        }
                        else
                        {
                            Logger.Log("Invalid refresh time setting. Defaulting to 15 minutes.");

                            var totalMs = 15 * 60000;
                            var end = DateTime.UtcNow.AddMilliseconds(totalMs);
                            while (DateTime.UtcNow < end)
                            {
                                var remaining = end - DateTime.UtcNow;
                                try
                                {
                                    SleepRemainingUpdated?.Invoke(remaining);
                                    await Task.Delay(1000, cancellationToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    SleepRemainingUpdated?.Invoke(TimeSpan.Zero);
                                    break;
                                }
                            }

                            SleepRemainingUpdated?.Invoke(TimeSpan.Zero);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Critical Global Error: {ex.Message}", ConsoleColor.Red);
                        // Notify GUI of failure
                        ProgressUpdated?.Invoke(0.0, "Error");
                        // Archive logs now so we can capture the error and keep GUI small
                        Logger.RequestArchive();
                        Logger.Log("Retrying in 1 minute...");
                        try { await Task.Delay(60000, cancellationToken); } catch (OperationCanceledException) { break; }
                    }
                }
            }
        }



        static void StartMakeVideo(string outputDir)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var videoConfig = config.Video ?? new VideoSettings();

                Logger.Log("Starting video generation...");
                ProgressUpdated?.Invoke(80.0, "Starting video generation");
                var container = (videoConfig.Container ?? "mp4").Trim().Trim('.');
                var videoDir = Path.Combine(Directory.GetCurrentDirectory(), videoConfig.OutputDirectory ?? config.ImageGeneration?.OutputDirectory ?? outputDir);
                var outputName = Path.ChangeExtension(videoConfig.OutputFileName ?? "slideshow_v3.mp4", container);
                var outputPath = Path.Combine(videoDir, outputName);

                if (!Directory.Exists(videoDir)) Directory.CreateDirectory(videoDir);
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                var videoGenerator = new VideoGenerator(outputDir)
                {
                    WorkingDirectory = videoDir,
                    ImageFolder = outputDir,
                    OutputFile = outputPath,
                    StaticDuration = videoConfig.StaticDurationSeconds,
                    FadeDuration = videoConfig.FadeDurationSeconds,
                    FrameRate = videoConfig.FrameRate,
                    ResolutionMode = Enum.Parse<ResolutionMode>(videoConfig.ResolutionMode ?? "Mode1080p"),
                    EnableFadeTransitions = videoConfig.EnableFadeTransitions,
                    VideoCodec = videoConfig.VideoCodec ?? "libx264",
                    VideoBitrate = videoConfig.VideoBitrate ?? "4M",
                    Container = container,
                    FfmpegVerbose = videoConfig.VerboseFfmpeg,
                    ShowFfmpegOutputInGui = videoConfig.ShowFfmpegOutputInGui,
                    EnableHardwareEncoding = videoConfig.EnableHardwareEncoding,
                    UseCrfEncoding = videoConfig.UseCrfEncoding,
                    CrfValue = videoConfig.CrfValue,
                    MaxBitrate = videoConfig.MaxBitrate,
                    BufferSize = videoConfig.BufferSize,
                    EncoderPreset = videoConfig.EncoderPreset ?? "medium",
                    UseTotalDuration = videoConfig.UseTotalDuration,
                    TotalDurationSeconds = videoConfig.TotalDurationSeconds,
                    StaticMapPath = Path.Combine(outputDir, config.WeatherImages?.StaticMapFilename ?? "STATIC_MAP.IGNORE")
                };
                
                // Load music from configuration
                videoGenerator.LoadMusicFromConfig();

                if (videoGenerator.GenerateVideo())
                {
                    Logger.Log("✓ Video generation completed successfully.");
                    ProgressUpdated?.Invoke(100.0, "Video complete");
                }
                else
                {
                    Logger.Log("✗ Video generation failed.");
                    ProgressUpdated?.Invoke(0.0, "Video failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to generate video: {ex.Message}", ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Gets coordinates for known cities (used for calculating radar animation center)
        /// </summary>
        private static List<(double lat, double lon)> GetCityCoordinates(string[] cityNames)
        {
            var cityCoords = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Montreal", (45.50884, -73.58781) },
                { "Quebec City", (46.813878, -71.207981) },
                { "Amos", (48.574, -78.116) },
                { "Gatineau", (45.4765, -75.7013) },
                { "Sherbrooke", (45.4042, -71.8929) },
                { "Trois-Rivières", (46.3432, -72.5477) },
                { "Saguenay", (48.4167, -71.0667) },
                { "Lévis", (46.8139, -71.1725) },
                { "Laval", (45.6066, -73.7124) },
                { "Longueuil", (45.5312, -73.5187) }
            };

            var results = new List<(double lat, double lon)>();
            foreach (var cityName in cityNames)
            {
                if (cityCoords.TryGetValue(cityName, out var coords))
                {
                    results.Add(coords);
                }
            }
            return results;
        }

        private static void EnsureIconsExist()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string iconsDir = Path.Combine(baseDir, "WeatherImages", "Icons");

                // Check if directory exists and has files
                if (!Directory.Exists(iconsDir) || Directory.GetFiles(iconsDir, "*.png").Length == 0)
                {
                    Logger.Log("Icons missing or incomplete. Generating icons...");
                    IconGenerator.GenerateAll(iconsDir);
                    Logger.Log("✓ Icons generated.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Warning] Failed to ensure icons exist: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Clear all image files from the output directory before generating new ones
        /// </summary>
        private static void ClearOutputImages(string outputDir)
        {
            try
            {
                if (!Directory.Exists(outputDir))
                {
                    return;
                }

                // Delete all image files (PNG, JPG, JPEG)
                var imageExtensions = new[] { "*.png", "*.jpg", "*.jpeg" };
                int deletedCount = 0;
                
                foreach (var pattern in imageExtensions)
                {
                    var files = Directory.GetFiles(outputDir, pattern);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[CLEANUP] Failed to delete {Path.GetFileName(file)}: {ex.Message}", ConsoleColor.Yellow);
                        }
                    }
                }
                
                if (deletedCount > 0)
                {
                    Logger.Log($"[CLEANUP] Cleared {deletedCount} existing image files from output directory", ConsoleColor.Gray);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[CLEANUP] Error clearing output directory: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Cleanup temporary files to prevent temp storage buildup during long-running sessions
        /// </summary>
        private static void CleanupTempFiles()
        {
            try
            {
                string tempPath = Path.GetTempPath();
                var oldFiles = Directory.GetFiles(tempPath, "ffmpeg_*.txt")
                    .Concat(Directory.GetFiles(tempPath, "weather_*.tmp"))
                    .Where(f => (DateTime.Now - File.GetCreationTime(f)).TotalHours > 24);
                
                int cleaned = 0;
                foreach (var file in oldFiles)
                {
                    try
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                    catch { /* Ignore locked files */ }
                }
                
                if (cleaned > 0)
                {
                    Logger.Log($"[CLEANUP] Removed {cleaned} old temporary files", ConsoleColor.Gray);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[CLEANUP] Error during temp file cleanup: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        /// <summary>
        /// Log current memory usage for monitoring long-running performance
        /// </summary>
        private static void LogMemoryUsage()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                long memoryMB = process.WorkingSet64 / (1024 * 1024);
                Logger.Log($"[MEMORY] Current usage: {memoryMB} MB", ConsoleColor.Gray);
            }
            catch { /* Silent fail */ }
        }
    }
}