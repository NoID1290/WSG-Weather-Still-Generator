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
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Forms;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator
{
    class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
        [STAThread]
        static void Main(string[] args)
        {
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
                    ECCC.CreateProvinceRadarAnimation(http, outputDir).GetAwaiter().GetResult();
                }

                Logger.Log("Province animation generation requested (one-off). Exiting.");
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
            
            Application.Run(new MainForm());
        }

        // Event used by the GUI to show the remaining time while the background worker is sleeping
        public static event Action<TimeSpan>? SleepRemainingUpdated;

        // Event that reports overall progress of the update cycle (0-100) and a short status message
        public static event Action<double, string>? ProgressUpdated;

        // Event that reports the fetched weather data
        public static event Action<WeatherForecast?[]>? WeatherDataFetched;

        // Event that reports the fetched alerts
        public static event Action<List<AlertEntry>>? AlertsFetched;

        public static async Task FetchDataOnlyAsync(CancellationToken cancellationToken = default)
        {            EnsureIconsExist();            var config = ConfigManager.LoadConfig();
            OpenMeteoClient client = new OpenMeteoClient();

            using (HttpClient httpClient = new HttpClient())
            {
                string[] locations = config.Locations?.GetLocationsArray() ?? new string[0];
                Logger.Log($"Fetching weather data (Fetch Only)...");
                ProgressUpdated?.Invoke(0, "Starting fetch only...");

                WeatherForecast?[] allForecasts = new WeatherForecast?[locations.Length];

                for (int i = 0; i < locations.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    string loc = locations[i];
                    try 
                    {
                        allForecasts[i] = await client.QueryAsync(loc);
                        Logger.Log($"✓ Fetched weather data for {loc}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"X Failed to fetch for {loc}: {ex.Message}");
                    }
                    
                    if (locations.Length > 0)
                    {
                        var fetchPct = ((i + 1) / (double)locations.Length) * 80.0;
                        ProgressUpdated?.Invoke(fetchPct, $"Fetching weather ({i + 1}/{locations.Length})");
                    }
                }

                WeatherDataFetched?.Invoke(allForecasts);

                if (cancellationToken.IsCancellationRequested) return;

                Logger.Log("Checking ECCC weather alerts...");
                try
                {
                    List<AlertEntry> alerts = await ECCC.FetchAllAlerts(httpClient, locations);
                    Logger.Log($"✓ Found {alerts.Count} active alerts.");
                    AlertsFetched?.Invoke(alerts);
                }
                catch (Exception ex)
                {
                    Logger.Log($"X Failed to fetch alerts: {ex.Message}");
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
                string[] locations = config.Locations?.GetLocationsArray() ?? new string[0];
                Logger.Log($"Fetching weather data (Stills Only)...");
                ProgressUpdated?.Invoke(0, "Starting stills generation...");

                WeatherForecast?[] allForecasts = new WeatherForecast?[locations.Length];

                // Fetch Weather
                for (int i = 0; i < locations.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    string loc = locations[i];
                    try 
                    {
                        allForecasts[i] = await client.QueryAsync(loc);
                        Logger.Log($"✓ Fetched weather data for {loc}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"X Failed to fetch for {loc}: {ex.Message}");
                    }
                    
                    if (locations.Length > 0)
                    {
                        var fetchPct = ((i + 1) / (double)locations.Length) * 15.0;
                        ProgressUpdated?.Invoke(fetchPct, $"Fetching weather ({i + 1}/{locations.Length})");
                    }
                }

                WeatherDataFetched?.Invoke(allForecasts);

                if (cancellationToken.IsCancellationRequested) return;

                // Fetch Alerts
                Logger.Log("Checking ECCC weather alerts...");
                List<AlertEntry> alerts = new List<AlertEntry>();
                try
                {
                    alerts = await ECCC.FetchAllAlerts(httpClient, locations);
                    Logger.Log($"✓ Found {alerts.Count} active alerts.");
                    AlertsFetched?.Invoke(alerts);
                }
                catch (Exception ex)
                {
                    Logger.Log($"X Failed to fetch alerts: {ex.Message}");
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

                Logger.Log("Generating still images...");
                ProgressUpdated?.Invoke(15, "Generating still images");

                int detailedPerImage = 3;
                int numDetailedImages = Math.Max(1, (locations.Length + detailedPerImage - 1) / detailedPerImage);
                int imageSteps = Math.Max(1, numDetailedImages + 3); 
                int imageStepsCompleted = 0;

                // Detailed Weather
                for (int batch = 0; batch < numDetailedImages; batch++)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    int start = batch * detailedPerImage;
                    int end = Math.Min(locations.Length, start + detailedPerImage);

                    var batchItems = new List<(WeatherForecast? Forecast, string Name, int Index)>();
                    for (int j = start; j < end; j++)
                    {
                        batchItems.Add((allForecasts[j], locations[j], j));
                    }

                    ImageGenerator.GenerateDetailedWeatherImageBatch(batchItems.ToArray(), outputDir, batch);

                    imageStepsCompleted++;
                    var pct = 15.0 + (imageStepsCompleted / (double)imageSteps) * 85.0;
                    ProgressUpdated?.Invoke(pct, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                }

                // Maps
                if (cancellationToken.IsCancellationRequested) return;
                
                if (config.ImageGeneration?.EnableWeatherMaps == true)
                {
                    try
                    {
                        Logger.Log("Fetching radar images...");
                        await ECCC.FetchRadarImages(httpClient, outputDir);
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

                // Alerts
                if (cancellationToken.IsCancellationRequested) return;
                ImageGenerator.GenerateAlertsImage(alerts, outputDir);
                imageStepsCompleted++;
                ProgressUpdated?.Invoke(100, "Stills generation complete");
                Logger.Log($"✓ Stills Generation Complete. Images saved to: {outputDir}");
            }
        }

        public static async Task RunAsync(CancellationToken cancellationToken = default)
        {
            // Load configuration
            var config = ConfigManager.LoadConfig();

            // Initialize the API client
            OpenMeteoClient client = new OpenMeteoClient();

            // Initialize HttpClient for ECCC Alerts
            using (HttpClient httpClient = new HttpClient())
            {
                // Load locations into an array for easier handling
                string[] locations = config.Locations?.GetLocationsArray() ?? new string[0];

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
                        bool dataFetchSuccess = true;

                        // Fetch weather data for each location
                        for (int i = 0; i < locations.Length; i++)
                        {
                            string loc = locations[i];
                            try 
                            {
                                allForecasts[i] = await client.QueryAsync(loc);
                                Logger.Log($"✓ Fetched weather data for {loc}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"X Failed to fetch for {loc}: {ex.Message}");
                                dataFetchSuccess = false; 
                            }

                            // Report incremental fetch progress (15% of total cycle)
                            if (locations.Length > 0)
                            {
                                var fetchPct = ((i + 1) / (double)locations.Length) * 15.0;
                                ProgressUpdated?.Invoke(fetchPct, $"Fetching weather ({i + 1}/{locations.Length})");
                            }
                        }

                        // Notify GUI that weather data has been fetched
                        WeatherDataFetched?.Invoke(allForecasts);

                        // Fetch Weather Alert Data from ECCC
                        Logger.Log("Checking ECCC weather alerts...");
                        try
                        {
                            List<AlertEntry> alerts = await ECCC.FetchAllAlerts(httpClient, locations);
                            Logger.Log($"✓ Found {alerts.Count} active alerts.");
                            AlertsFetched?.Invoke(alerts);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"X Failed to generate alerts image: {ex.Message}");
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

                        Logger.Log("Generating still images...");
                        // Image generation comprises the bulk of the cycle (about 65%)
                        ProgressUpdated?.Invoke(15, "Generating still images");

                        int detailedPerImage = 3;
                        int numDetailedImages = Math.Max(1, (locations.Length + detailedPerImage - 1) / detailedPerImage);
                        int imageSteps = Math.Max(1, numDetailedImages + 3); // detailed images + maps + apng + alerts
                        int imageStepsCompleted = 0;

                        // --- IMAGE GENERATION ---
                        
                        // 1. Current Weather
                        //if(allForecasts[0] != null)
                        //    ImageGenerator.GenerateCurrentWeatherImage(allForecasts[0]!, outputDir);

                        // 2. Forecast Summary
                        //if (allForecasts[0] != null)
                        //    ImageGenerator.GenerateForecastSummaryImage(allForecasts[0]!, outputDir);

                        // 3. Detailed Weather (batched, up to 3 cities per image)
                        for (int batch = 0; batch < numDetailedImages; batch++)
                        {
                            int start = batch * detailedPerImage;
                            int end = Math.Min(locations.Length, start + detailedPerImage);

                            var batchItems = new List<(WeatherForecast? Forecast, string Name, int Index)>();
                            for (int j = start; j < end; j++)
                            {
                                batchItems.Add((allForecasts[j], locations[j], j));
                            }

                            ImageGenerator.GenerateDetailedWeatherImageBatch(batchItems.ToArray(), outputDir, batch);

                            imageStepsCompleted++;
                            var pct = 15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0;
                            ProgressUpdated?.Invoke(pct, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                        }  



                        // 4. Maps Image
                        if (config.ImageGeneration?.EnableWeatherMaps == true)
                        {
                            try
                            {
                                Logger.Log("Fetching radar images...");
                                await ECCC.FetchRadarImages(httpClient, outputDir);
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
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 5. APNG Helper
                        /*if (allForecasts[0] != null)
                            ImageGenerator.GenerateAPNGcurrentTemperature(allForecasts[0]!, outputDir);
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                        */
                        
                        // 6. WEATHER ALERTS from ECCC
                        ImageGenerator.GenerateAlertsImage(await ECCC.FetchAllAlerts(httpClient, locations), outputDir);
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 7. Video Generation (Optional)
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
                    UseOverlayMode = true,
                    UseTotalDuration = videoConfig.UseTotalDuration,
                    TotalDurationSeconds = videoConfig.TotalDurationSeconds,
                    StaticMapPath = Path.Combine(outputDir, config.WeatherImages?.StaticMapFilename ?? "STATIC_MAP.IGNORE")
                };

                // Configure which base image should receive the radar overlay. Prefer explicit filename from config if present.
                videoGenerator.OverlayTargetFilename = config.WeatherImages?.WeatherMapsFilename ?? "WeatherMaps";
                
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