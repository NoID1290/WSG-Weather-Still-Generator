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
using CommonStrings; 
using QuebecWeatherAlertMonitor;

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
                CreateTestImages();
                return;
            }

            if (args.Contains("--make-video-now"))
            {
                var config = ConfigManager.LoadConfig();
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                StartMakeVideo(outputDir);
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
                    CreateTestImages();
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
                        // Fetch Weather Alert Data from ECCC
                        Logger.Log("Checking ECCC weather alerts...");
                        try
                        {
                            List<AlertEntry> alerts = await ECCC.FetchAllAlerts(httpClient);
                            Logger.Log($"✓ Found {alerts.Count} active alerts.");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"X Failed to generate alerts image: {ex.Message}");
                        }
                       
                        
                        



                        // Check if we have at least the primary location data to proceed
                        if (allForecasts[0] == null) 
                        {
                            Logger.Log("Primary location data missing. Retrying in 1 minute...");
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
                        //    GenerateCurrentWeatherImage(allForecasts[0]!, outputDir);

                        // 2. Forecast Summary
                        //if (allForecasts[0] != null)
                        //    GenerateForecastSummaryImage(allForecasts[0]!, outputDir);

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

                            GenerateDetailedWeatherImageBatch(batchItems.ToArray(), outputDir, batch);

                            imageStepsCompleted++;
                            var pct = 15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0;
                            ProgressUpdated?.Invoke(pct, $"Generating images ({imageStepsCompleted}/{imageSteps})");
                        }  



                        // 4. Maps Image
                        GenerateMapsImage(allForecasts, locations, outputDir);
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 5. APNG Helper
                        if (allForecasts[0] != null)
                            GenerateAPNGcurrentTemperature(allForecasts[0]!, outputDir);
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 6. WEATHER ALERTS from ECCC
                        GenerateAlertsImage(await ECCC.FetchAllAlerts(httpClient), outputDir);
                        imageStepsCompleted++;
                        ProgressUpdated?.Invoke(15.0 + (imageStepsCompleted / (double)imageSteps) * 65.0, $"Generating images ({imageStepsCompleted}/{imageSteps})");

                        // 7. Video Generation (Optional)
                        ProgressUpdated?.Invoke(80.0, "Starting video generation");

                        // If video settings are configured, create a video from the generated images
                        if (config.Video != null)
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
                        Logger.Log("Retrying in 1 minute...");
                        try { await Task.Delay(60000, cancellationToken); } catch (OperationCanceledException) { break; }
                    }
                }
            }
        }

        static void GenerateAlertsImage(List<AlertEntry> alerts, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();
            var alertConfig = config.Alerts ?? new AlertsSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            float margin = imgConfig.MarginPixels;
            float contentWidth = width - (margin * 2);
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // 1. Background
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(30, 30, 30), Color.FromArgb(10, 10, 10)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font headerFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.HeaderFontSize, FontStyle.Bold))
                using (Font cityFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.CityFontSize, FontStyle.Bold))
                using (Font typeFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.TypeFontSize, FontStyle.Bold))
                using (Font detailFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.DetailsFontSize, FontStyle.Regular))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    // Main Header
                    graphics.DrawString(alertConfig.HeaderText ?? "⚠️ Environment Canada Alerts", headerFont, whiteBrush, new PointF(margin, margin));

                    float currentY = 150f; // Start position for alerts

                    if (alerts.Count == 0)
                    {
                        using(Brush greenBrush = new SolidBrush(Color.LightGreen))
                        {
                             graphics.DrawString(alertConfig.NoAlertsText ?? "No Active Warnings or Watches", cityFont, greenBrush, new PointF(margin, currentY));
                        }
                    }
                    else
                    {
                        foreach (var alert in alerts) 
                        {
                            // 1. Determine Color
                            Color alertColor = Color.LightGray;
                            if (alert.SeverityColor == "Red") alertColor = Color.Red;
                            if (alert.SeverityColor == "Yellow") alertColor = Color.Yellow;

                            using (Brush alertBrush = new SolidBrush(alertColor))
                            {
                                // 2. Draw Alert Header (City - Type)
                                string headerLine = $"> {alert.City.ToUpper()} : {alert.Type}";
                                graphics.DrawString(headerLine, cityFont, alertBrush, new PointF(margin, currentY));
                                currentY += 45; // Move down

                                // 3. Draw Title (Wrapped)
                                // Measure height required for the title
                                SizeF titleSize = graphics.MeasureString(alert.Title, typeFont, (int)contentWidth);
                                RectangleF titleRect = new RectangleF(margin, currentY, contentWidth, titleSize.Height);
                                
                                graphics.DrawString(alert.Title, typeFont, whiteBrush, titleRect);
                                currentY += titleSize.Height + 10; // Move down + padding
                            }

                            // 4. Draw Full Summary (Wrapped)
                            // Measure height required for the summary
                            SizeF summarySize = graphics.MeasureString(alert.Summary, detailFont, (int)contentWidth);
                            RectangleF summaryRect = new RectangleF(margin, currentY, contentWidth, summarySize.Height);

                            graphics.DrawString(alert.Summary, detailFont, whiteBrush, summaryRect);
                            
                            // 5. Update Y Position for next alert
                            currentY += summarySize.Height + 60; // Extra padding between separate alerts

                            // Check if we ran out of space on the image
                            if (currentY > height - 50) 
                            {
                                Logger.Log("Warning: Not all alerts fit on the screen.");
                                break; 
                            }
                        }
                    }
                }

                // Default alert filename set to 10_ so it sorts / displays after primary images
                string filename = Path.Combine(outputDir, alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        static void GenerateMapsImage(WeatherForecast?[] allData, string[] locationNames, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // Background
                string staticMapPath = Path.Combine(outputDir, config.WeatherImages?.StaticMapFilename ?? "STATIC_MAP.IGNORE");
                if (File.Exists(staticMapPath))
                {
                    using (var importedMap = Image.FromFile(staticMapPath))
                    {
                        graphics.DrawImage(importedMap, 0, 0, width, height);
                    }
                }
                else
                {
                    graphics.Clear(Color.DarkBlue); 
                }

                using (Font cityFont = new Font(imgConfig.FontFamily ?? "Arial", 24, FontStyle.Bold))
                using (Font tempFont = new Font(imgConfig.FontFamily ?? "Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font(imgConfig.FontFamily ?? "Arial", 20, FontStyle.Regular))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    for (int i = 0; i < 7 && i < allData.Length; i++)
                    {
                        var data = allData[i];
                        string locName = locationNames[i];

                        // Get position from config if available
                        PointF cityPosition = new PointF(0, 0);
                        PointF tempPosition = new PointF(0, 0);

                        if (config.MapLocations != null && config.MapLocations.TryGetValue($"Location{i}", out var mapLoc))
                        {
                            cityPosition = new PointF(mapLoc.CityPositionX, mapLoc.CityPositionY);
                            tempPosition = new PointF(mapLoc.TemperaturePositionX, mapLoc.TemperaturePositionY);
                        }
                        
                        string tempText = "N/A";

                        if (data?.Current != null)
                        {
                            tempText = $"{data.Current.Temperature}{data.CurrentUnits?.Temperature}";
                        }

                        if (cityPosition.X == 0 && cityPosition.Y == 0) continue; 
                        if (tempPosition.X == 0 && tempPosition.Y == 0) continue; 

                        graphics.DrawString(locName, cityFont, whiteBrush, cityPosition);
                        graphics.DrawString(tempText, tempFont, whiteBrush, tempPosition);
                    }

                    string timestamp = $"Updated: {DateTime.Now}";
                    graphics.DrawString(timestamp, labelFont, whiteBrush, new PointF(50, 100));
                }

                // Default maps filename uses 00_ prefix so it's easily readable/first in listings
                string filename = Path.Combine(outputDir, config.WeatherImages?.WeatherMapsFilename ?? "00_WeatherMaps.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        static void GenerateCurrentWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(70, 130, 180), Color.FromArgb(100, 150, 200)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font("Arial", 24, FontStyle.Regular))
                using (Font dataFont = new Font("Arial", 32, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    graphics.DrawString("Current Weather", titleFont, whiteBrush, new PointF(50, 50));

                    string tempText = weatherData.Current?.Temperature != null
                        ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                        : "N/A";
                    
                    string conditionText = weatherData.Current?.Weathercode != null
                        ? GetWeatherDescription((int)weatherData.Current.Weathercode)
                        : "N/A";

                    string windText = weatherData.Current?.Windspeed_10m != null
                        ? $"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m}"
                        : "N/A";

                    graphics.DrawString("Temperature:", labelFont, whiteBrush, new PointF(50, 150));
                    graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(50, 200));

                    graphics.DrawString("Condition:", labelFont, whiteBrush, new PointF(50, 280));
                    graphics.DrawString(conditionText, dataFont, whiteBrush, new PointF(50, 330));

                    graphics.DrawString("Wind Speed:", labelFont, whiteBrush, new PointF(50, 410));
                    graphics.DrawString(windText, dataFont, whiteBrush, new PointF(50, 460));
                }

                string filename = Path.Combine(outputDir, config.WeatherImages?.CurrentWeatherFilename ?? "1_CurrentWeather.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        static void GenerateForecastSummaryImage(WeatherForecast weatherData, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(60, 120, 150), Color.FromArgb(90, 140, 180)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font("Arial", 20, FontStyle.Regular))
                using (Font dataFont = new Font("Arial", 24, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    graphics.DrawString("Daily Forecast Summary", titleFont, whiteBrush, new PointF(50, 50));

                    if (weatherData.Daily != null && weatherData.Daily.Time != null && weatherData.Daily.Time.Length > 0)
                    {
                        int yPosition = 150;
                        int daysToShow = Math.Min(3, weatherData.Daily.Time.Length);

                        for (int i = 0; i < daysToShow; i++)
                        {
                            if(DateTime.TryParse(weatherData.Daily.Time[i], out DateTime dateTime))
                            {
                                string date = dateTime.ToString("ddd, MMM d");
                                string maxTemp = weatherData.Daily.Temperature_2m_max != null ? weatherData.Daily.Temperature_2m_max[i].ToString() : "-";
                                string minTemp = weatherData.Daily.Temperature_2m_min != null ? weatherData.Daily.Temperature_2m_min[i].ToString() : "-";
                                string condition = weatherData.Daily.Weathercode != null 
                                    ? GetWeatherDescription((int)weatherData.Daily.Weathercode[i])
                                    : "";

                                graphics.DrawString($"{date}", labelFont, whiteBrush, new PointF(50, yPosition));
                                graphics.DrawString($"High: {maxTemp}° | Low: {minTemp}° | {condition}", dataFont, whiteBrush,
                                    new PointF(50, yPosition + 35));

                                yPosition += 120;
                            }
                        }
                    }
                    else
                    {
                        graphics.DrawString("No forecast data available", labelFont, whiteBrush, new PointF(50, 200));
                    }
                }

                string filename = Path.Combine(outputDir, config.WeatherImages?.DailyForecastFilename ?? "2_DailyForecast.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        static void GenerateDetailedWeatherImage(WeatherForecast weatherData, string outputDir, int cityIndex, string locationName)
        {
            // Redirect to the new batch renderer for consistent look & feel (single-item batch)
            GenerateDetailedWeatherImageBatch(new[] { (Forecast: (WeatherForecast?)weatherData, Name: locationName, Index: cityIndex) }, outputDir, cityIndex);
        }

        // New: render up to 3 cities per detailed image with improved layout and fonts
        static void GenerateDetailedWeatherImageBatch((WeatherForecast? Forecast, string Name, int Index)[] cities, string outputDir, int batchIndex)
        {
            // Filter out cities with empty/null names (no location set)
            var validCities = cities.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToArray();
            
            // If no valid cities, skip rendering this batch entirely
            if (validCities.Length == 0)
            {
                Logger.Log($"[INFO] Skipping batch {batchIndex}: no valid cities to display.");
                return;
            }

            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            int margin = (int)imgConfig.MarginPixels;
            int spacing = 30;
            
            // Always use 3 columns layout
            int columns = 3;
            int colWidth = (width - margin * 2 - spacing * (columns - 1)) / columns;

            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // Background gradient
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(40, 50, 60), Color.FromArgb(10, 20, 30)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                // Fonts (modern UI feel)
                using (Font cityFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 28, FontStyle.Bold))
                using (Font tempFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 40, FontStyle.Bold))
                using (Font labelFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 14, FontStyle.Regular))
                using (Font dataFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 18, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    // Fixed 3-column layout
                    for (int i = 0; i < columns; i++)
                    {
                        int x = margin + i * (colWidth + spacing);
                        int y = margin;
                        var rect = new RectangleF(x, y, colWidth, height - margin * 2);

                        // Draw card background
                        using (Brush cardBrush = new SolidBrush(Color.FromArgb(200, 20, 24, 30)))
                        using (Pen cardBorder = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
                        {
                            graphics.FillRectangle(cardBrush, rect);
                            graphics.DrawRectangle(cardBorder, x, y, colWidth, height - margin * 2);
                        }

                        // Only render city data if we have a valid city for this column
                        if (i < validCities.Length)
                        {
                            var item = validCities[i];
                            string cityName = item.Name;

                        // Header (city name)
                        graphics.DrawString(cityName, cityFont, whiteBrush, new PointF(x + 20, y + 20));

                        if (item.Forecast?.Current != null)
                        {
                            var cur = item.Forecast.Current;
                            string tempText = cur.Temperature != null ? $"{cur.Temperature}{item.Forecast.CurrentUnits?.Temperature}" : "N/A";
                            string cond = cur.Weathercode != null ? GetWeatherDescription((int)cur.Weathercode) : "";

                            // Temperature
                            graphics.DrawString(tempText, tempFont, whiteBrush, new PointF(x + 20, y + 80));

                            // Small weather icon to the right of temperature
                            var iconRect = new RectangleF(x + colWidth - 100, y + 70, 64, 64);
                            DrawWeatherIcon(graphics, iconRect, item.Forecast?.Current?.Weathercode);

                            // Condition
                            graphics.DrawString(cond, dataFont, whiteBrush, new PointF(x + 20, y + 140));

                            // Small details
                            float detailY = y + 190;
                            graphics.DrawString($"Humidity: {cur.Relativehumidity_2m}%", labelFont, whiteBrush, new PointF(x + 20, detailY));
                            graphics.DrawString($"Wind: {cur.Windspeed_10m} {item.Forecast.CurrentUnits?.Windspeed_10m} @ {cur.Winddirection_10m}°", labelFont, whiteBrush, new PointF(x + 20, detailY + 22));
                            graphics.DrawString($"Pressure: {cur.Surface_pressure} hPa", labelFont, whiteBrush, new PointF(x + 20, detailY + 44));

                            // 5-Day Forecast Section
                            float forecastY = detailY + 80;
                            using (Font forecastHeaderFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 16, FontStyle.Bold))
                            using (Font forecastDayFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 12, FontStyle.Bold))
                            using (Font forecastTempFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 11, FontStyle.Regular))
                            using (Brush accentBrush = new SolidBrush(Color.FromArgb(255, 100, 180, 255)))
                            {
                                // Section header
                                graphics.DrawString("5-Day Forecast", forecastHeaderFont, accentBrush, new PointF(x + 20, forecastY));
                                forecastY += 28;

                                // Draw separator line
                                using (Pen sepPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
                                {
                                    graphics.DrawLine(sepPen, x + 20, forecastY, x + colWidth - 20, forecastY);
                                }
                                forecastY += 10;

                                if (item.Forecast?.Daily != null && item.Forecast.Daily.Time != null && item.Forecast.Daily.Time.Length > 0)
                                {
                                    int daysToShow = Math.Min(5, item.Forecast.Daily.Time.Length);
                                    float rowHeight = 50;

                                    for (int d = 0; d < daysToShow; d++)
                                    {
                                        if (DateTime.TryParse(item.Forecast.Daily.Time[d], out DateTime dateTime))
                                        {
                                            string dayName = dateTime.ToString("ddd");
                                            string dateStr = dateTime.ToString("MMM d");
                                            string maxTemp = item.Forecast.Daily.Temperature_2m_max != null 
                                                ? $"{item.Forecast.Daily.Temperature_2m_max[d]:F0}°" : "-";
                                            string minTemp = item.Forecast.Daily.Temperature_2m_min != null 
                                                ? $"{item.Forecast.Daily.Temperature_2m_min[d]:F0}°" : "-";
                                            int? dayCode = item.Forecast.Daily.Weathercode != null 
                                                ? (int?)item.Forecast.Daily.Weathercode[d] : null;

                                            // Day name and date
                                            graphics.DrawString(dayName, forecastDayFont, whiteBrush, new PointF(x + 20, forecastY));
                                            graphics.DrawString(dateStr, forecastTempFont, whiteBrush, new PointF(x + 20, forecastY + 16));

                                            // Small weather icon for the day
                                            var dayIconRect = new RectangleF(x + 80, forecastY, 32, 32);
                                            DrawWeatherIcon(graphics, dayIconRect, dayCode);

                                            // High/Low temps
                                            using (Brush highBrush = new SolidBrush(Color.FromArgb(255, 255, 150, 100)))
                                            using (Brush lowBrush = new SolidBrush(Color.FromArgb(255, 150, 200, 255)))
                                            {
                                                graphics.DrawString($"H:{maxTemp}", forecastTempFont, highBrush, new PointF(x + 120, forecastY + 4));
                                                graphics.DrawString($"L:{minTemp}", forecastTempFont, lowBrush, new PointF(x + 120, forecastY + 20));
                                            }

                                            forecastY += rowHeight;
                                        }
                                    }
                                }
                                else
                                {
                                    graphics.DrawString("No forecast data", forecastTempFont, whiteBrush, new PointF(x + 20, forecastY));
                                }
                            }

                            // Index badge (top-right of each card)
                            int badgeSize = 48;
                            float bx = x + colWidth - badgeSize - 12;
                            float by = y + 12;
                            using (Brush b = new SolidBrush(Color.FromArgb(220, 0, 122, 204)))
                            using (Brush nb = new SolidBrush(Color.White))
                            using (Font nbFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 18, FontStyle.Bold))
                            {
                                graphics.FillEllipse(b, bx, by, badgeSize, badgeSize);
                                string idx = (item.Index + 1).ToString();
                                var s = graphics.MeasureString(idx, nbFont);
                                graphics.DrawString(idx, nbFont, nb, bx + (badgeSize - s.Width) / 2, by + (badgeSize - s.Height) / 2 - 2);
                            }
                        }
                        else
                        {
                            // No weather data available - show placeholder message
                            using (Font ndFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 16, FontStyle.Italic))
                            using (Brush gray = new SolidBrush(Color.FromArgb(200, 200, 200, 200)))
                            {
                                graphics.DrawString("Weather data unavailable", ndFont, gray, new RectangleF(x + 20, y + 100, colWidth - 40, 80));
                            }
                        }
                        }
                        // Empty column slot - card background already drawn, leave it empty
                    }

                    // Timestamp
                    using (Font tsFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 12, FontStyle.Regular))
                    {
                        string timestamp = $"Updated: {DateTime.Now}";
                        graphics.DrawString(timestamp, tsFont, whiteBrush, new PointF(margin + 10, height - margin - 20));
                    }
                }

                // Build filename
                var names = string.Join("_", validCities.Select(c => string.Concat(c.Name?.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)) ?? new char[0]).Replace(' ', '_')));
                if (string.IsNullOrWhiteSpace(names)) names = $"batch{batchIndex}";
                string filename = Path.Combine(outputDir, $"3_DetailedWeather_batch{batchIndex}_{names}.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        // Draw a simple vector weather icon into the given rectangle
        static void DrawWeatherIcon(Graphics g, RectangleF area, int? weatherCode)
        {
            // Normalize background
            using (var bg = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) { /* placeholder if needed */ }

            // Default: clear icon (sun)
            int code = weatherCode ?? 0;

            if (code == 0 || code == 1)
            {
                // Sun
                var center = new PointF(area.X + area.Width / 2, area.Y + area.Height / 2);
                float r = Math.Min(area.Width, area.Height) * 0.28f;
                using (Brush sun = new SolidBrush(Color.FromArgb(255, 245, 178)))
                using (Pen ray = new Pen(Color.FromArgb(255, 230, 128), 2))
                {
                    g.FillEllipse(sun, center.X - r, center.Y - r, r * 2, r * 2);
                    for (int i = 0; i < 8; i++)
                    {
                        var angle = i * (float)(Math.PI * 2 / 8);
                        var x1 = center.X + (r + 4) * (float)Math.Cos(angle);
                        var y1 = center.Y + (r + 4) * (float)Math.Sin(angle);
                        var x2 = center.X + (r + 12) * (float)Math.Cos(angle);
                        var y2 = center.Y + (r + 12) * (float)Math.Sin(angle);
                        g.DrawLine(ray, x1, y1, x2, y2);
                    }
                }
            }
            else if (code == 2 || code == 3 || code == 45)
            {
                // Cloudy / overcast
                using (Brush cloud = new SolidBrush(Color.FromArgb(230, 230, 235)))
                {
                    float cw = area.Width * 0.9f;
                    float ch = area.Height * 0.5f;
                    float cx = area.X + (area.Width - cw) / 2;
                    float cy = area.Y + (area.Height - ch) / 2 + 8;
                    g.FillEllipse(cloud, cx + cw * 0.05f, cy, cw * 0.6f, ch);
                    g.FillEllipse(cloud, cx + cw * 0.35f, cy - ch * 0.25f, cw * 0.6f, ch);
                    g.FillRectangle(cloud, cx + cw * 0.05f, cy + ch * 0.25f, cw * 0.9f, ch * 0.45f);
                }
            }
            else if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82))
            {
                // Rain
                using (Brush cloud = new SolidBrush(Color.FromArgb(230, 230, 235)))
                using (Pen drop = new Pen(Color.FromArgb(180, 180, 255), 3))
                {
                    float cw = area.Width * 0.9f;
                    float ch = area.Height * 0.45f;
                    float cx = area.X + (area.Width - cw) / 2;
                    float cy = area.Y + (area.Height - ch) / 2 + 6;
                    g.FillEllipse(cloud, cx + cw * 0.08f, cy, cw * 0.6f, ch);
                    g.FillEllipse(cloud, cx + cw * 0.35f, cy - ch * 0.2f, cw * 0.6f, ch);
                    g.FillRectangle(cloud, cx + cw * 0.05f, cy + ch * 0.2f, cw * 0.9f, ch * 0.5f);

                    // drops
                    var dx = cx + cw * 0.2f;
                    for (int i = 0; i < 3; i++)
                    {
                        g.DrawLine(drop, dx + i * 12, cy + ch + 6, dx + i * 12, cy + ch + 18);
                    }
                }
            }
            else if ((code >= 71 && code <= 77) || (code >= 85 && code <= 86))
            {
                // Snow
                using (Brush cloud = new SolidBrush(Color.FromArgb(230, 230, 235)))
                using (Pen snow = new Pen(Color.White, 2))
                {
                    float cw = area.Width * 0.9f;
                    float ch = area.Height * 0.45f;
                    float cx = area.X + (area.Width - cw) / 2;
                    float cy = area.Y + (area.Height - ch) / 2 + 6;
                    g.FillEllipse(cloud, cx + cw * 0.08f, cy, cw * 0.6f, ch);
                    g.FillEllipse(cloud, cx + cw * 0.35f, cy - ch * 0.2f, cw * 0.6f, ch);
                    g.FillRectangle(cloud, cx + cw * 0.05f, cy + ch * 0.2f, cw * 0.9f, ch * 0.5f);

                    var sx = cx + cw * 0.2f;
                    for (int i = 0; i < 3; i++)
                    {
                        var cxp = sx + i * 12;
                        g.DrawLine(snow, cxp - 4, cy + ch + 8, cxp + 4, cy + ch + 8);
                        g.DrawLine(snow, cxp, cy + ch + 4, cxp, cy + ch + 12);
                    }
                }
            }
            else
            {
                // Fallback: small circle
                using (Brush b = new SolidBrush(Color.FromArgb(255, 200, 200, 200)))
                {
                    g.FillEllipse(b, area.X + area.Width * 0.25f, area.Y + area.Height * 0.25f, area.Width * 0.5f, area.Height * 0.5f);
                }
            }
        }

        static void GenerateAPNGcurrentTemperature(WeatherForecast weatherData, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            
            int width = 400;
            int height = 100;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);

                using (Font dataFont = new Font(config.ImageGeneration?.FontFamily ?? "Arial", 72, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string tempText = weatherData.Current?.Temperature != null
                        ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                        : "N/A";
                    
                    graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(0, 0));
                }

                string filename = Path.Combine(outputDir, config.WeatherImages?.TemperatureWatermarkFilename ?? "temp_watermark_alpha.png.IGNORE");
                var saved = SaveImage(bitmap, filename, config.ImageGeneration ?? new ImageGenerationSettings());
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        private static string SaveImage(Bitmap bitmap, string filename, ImageGenerationSettings imgConfig)
        {
            var ext = (imgConfig.ImageFormat ?? "png").Trim().Trim('.');
            var format = ResolveImageFormat(ext);
            var finalPath = Path.ChangeExtension(filename, ext);
            bitmap.Save(finalPath, format);
            return finalPath;
        }

        private static ImageFormat ResolveImageFormat(string ext)
        {
            return ext.ToLowerInvariant() switch
            {
                "png" => ImageFormat.Png,
                "jpg" => ImageFormat.Jpeg,
                "jpeg" => ImageFormat.Jpeg,
                "bmp" => ImageFormat.Bmp,
                "gif" => ImageFormat.Gif,
                _ => ImageFormat.Png
            };
        }

        static void CreateTestImages()
        {
            var config = ConfigManager.LoadConfig();
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            Logger.Log($"Creating 4 test images in: {outputDir}");

            for (int i = 0; i < 4; i++)
            {
                int width = config.ImageGeneration?.ImageWidth ?? 1920;
                int height = config.ImageGeneration?.ImageHeight ?? 1080;
                using (var bmp = new System.Drawing.Bitmap(width, height))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    var colors = new[] { System.Drawing.Color.Red, System.Drawing.Color.Green, System.Drawing.Color.Blue, System.Drawing.Color.Orange };
                    g.Clear(colors[i % colors.Length]);
                    using (var f = new System.Drawing.Font("Arial", 72, System.Drawing.FontStyle.Bold))
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                    {
                        g.DrawString($"Test Image {i + 1}", f, brush, new System.Drawing.PointF(100, 100));
                    }

                    string filename = Path.Combine(outputDir, $"test_{i + 1:D2}.png");
                    var saved = SaveImage(bmp, filename, config.ImageGeneration ?? new ImageGenerationSettings());
                    Logger.Log($"Created: {saved}");
                }
            }

            Logger.Log("Test images created.");
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
                    ShowFfmpegOutputInGui = videoConfig.ShowFfmpegOutputInGui
                };
                
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

        static string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear sky",
                1 => "Mainly clear",
                2 => "Partly cloudy",
                3 => "Overcast",
                45 => "Fog",
                48 => "Depositing rime Fog",
                51 => "Light drizzle",
                53 => "Moderate drizzle",
                55 => "Dense drizzle",
                56 => "Light freezing drizzle",
                57 => "Dense freezing drizzle",
                61 => "Slight rain",
                63 => "Moderate rain",
                65 => "Heavy rain",
                66 => "Light freezing rain",
                67 => "Heavy freezing rain",
                71 => "Slight snow fall",
                73 => "Moderate snow fall",
                75 => "Heavy snow fall",
                77 => "Snow grains",
                80 => "Slight rain showers",
                81 => "Moderate rain showers",
                82 => "Violent rain showers",
                85 => "Slight snow showers",
                86 => "Heavy snow showers",
                95 => "Thunderstorm",
                96 => "Thunderstorm with light hail",
                99 => "Thunderstorm with heavy hail",
                _ => "Unknown"
            };
        }
    }
}