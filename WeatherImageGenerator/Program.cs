#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing; // NuGet: System.Drawing.Common
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
                RunAsync(CancellationToken.None).GetAwaiter().GetResult();
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
                        Logger.Log($"Fetching weather data...");
                        
                        // Array to store results for all 7 locations
                        WeatherForecast?[] allForecasts = new WeatherForecast?[7];
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

                        // --- IMAGE GENERATION ---
                        
                        // 1. Current Weather
                        //if(allForecasts[0] != null)
                        //    GenerateCurrentWeatherImage(allForecasts[0]!, outputDir);

                        // 2. Forecast Summary
                        //if (allForecasts[0] != null)
                        //    GenerateForecastSummaryImage(allForecasts[0]!, outputDir);

                        // 3. Detailed Weather Location 0 to 6
                        for (int i = 0; i < locations.Length; i++)
                        {
                            if (allForecasts[i] != null)
                                GenerateDetailedWeatherImage(allForecasts[i]!, outputDir, i, locations[i]);
                        }   



                        // 4. Maps Image
                        GenerateMapsImage(allForecasts, locations, outputDir);

                        // 5. APNG Helper
                        if (allForecasts[0] != null)
                        GenerateAPNGcurrentTemperature(allForecasts[0]!, outputDir);

                        // 6. WEATHER ALERTS from ECCC
                        GenerateAlertsImage(await ECCC.FetchAllAlerts(httpClient), outputDir);

                        // 7. Video Generation (Optional)
                        StartMakeVideo(outputDir);    

                        Logger.Log($"✓ Cycle Complete. Images saved to: {outputDir}");

                        // Wait Logic
                        if (config.RefreshTimeMinutes > 0)
                        {
                            Logger.Log($"Sleeping for {config.RefreshTimeMinutes} minutes...");
                            try { await Task.Delay(config.RefreshTimeMinutes * 60000, cancellationToken); } catch (OperationCanceledException) { break; }
                        }
                        else
                        {
                            Logger.Log("Invalid refresh time setting. Defaulting to 15 minutes.");
                            try { await Task.Delay(15 * 60000, cancellationToken); } catch (OperationCanceledException) { break; }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Critical Global Error: {ex.Message}", ConsoleColor.Red);
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
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
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
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
            }
        }

        static void GenerateCurrentWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
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

                string filename = Path.Combine(outputDir, "1_CurrentWeather.png");
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
            }
        }

        static void GenerateForecastSummaryImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
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

                string filename = Path.Combine(outputDir, "2_DailyForecast.png");
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
            }
        }

        static void GenerateDetailedWeatherImage(WeatherForecast weatherData, string outputDir, int cityIndex, string locationName)
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
                    Color.FromArgb(80, 140, 160), Color.FromArgb(110, 160, 190)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font(imgConfig.FontFamily ?? "Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font(imgConfig.FontFamily ?? "Arial", 18, FontStyle.Regular))
                using (Font dataFont = new Font(imgConfig.FontFamily ?? "Arial", 22, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string primaryLocation = locationName ?? (config.Locations?.Location0 ?? "Montreal");
                    graphics.DrawString($"Détail pour {primaryLocation}", titleFont, whiteBrush, new PointF(50, 30));

                    int yPosition = 120;
                    int lineHeight = 50;

                    if (weatherData.Current != null)
                    {
                        graphics.DrawString("Température:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}",
                            dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Humidité:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Relativehumidity_2m}%", dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Vitesse du vent:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m}",
                            dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Direction du vent:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Winddirection_10m}°", dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Pression atmosphérique:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Surface_pressure} hPa", dataFont, whiteBrush, new PointF(300, yPosition));
                    }
                }
                // Use the script's city index directly for filenames/badges (location0 -> 0)
                int displayNumber = cityIndex;

                // Draw a number badge in the top-right corner
                int badgeDiameter = 96;
                float margin = imgConfig.MarginPixels;
                RectangleF badgeRect = new RectangleF(imgConfig.ImageWidth - margin - badgeDiameter, margin, badgeDiameter, badgeDiameter);
                using (Brush badgeBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                using (Brush numberBrush = new SolidBrush(Color.White))
                using (Font numberFont = new Font(imgConfig.FontFamily ?? "Arial", 36, FontStyle.Bold))
                {
                    graphics.FillEllipse(badgeBrush, badgeRect);
                    string numberText = displayNumber.ToString();
                    SizeF numSize = graphics.MeasureString(numberText, numberFont);
                    PointF numPos = new PointF(badgeRect.X + (badgeRect.Width - numSize.Width) / 2, badgeRect.Y + (badgeRect.Height - numSize.Height) / 2 - 4);
                    graphics.DrawString(numberText, numberFont, numberBrush, numPos);
                }

                // Build a safe filename including the display number and sanitized location
                string sanitized = string.Concat(locationName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Replace(' ', '_');
                if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "location" + displayNumber;
                string filename = Path.Combine(outputDir, $"{displayNumber}_DetailedWeather_{sanitized}.png");
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
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
                bitmap.Save(filename);
                Logger.Log($"✓ Generated: {filename}");
            }
        }

        static void CreateTestImages()
        {
            var config = ConfigManager.LoadConfig();
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), config.ImageGeneration?.OutputDirectory ?? "WeatherImages");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            Logger.Log($"Creating 4 test images in: {outputDir}");

            for (int i = 0; i < 4; i++)
            {
                int width = 1920, height = 1080;
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
                    bmp.Save(filename);
                    Logger.Log($"Created: {filename}");
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
                var videoGenerator = new VideoGenerator(outputDir)
                {
                    StaticDuration = videoConfig.StaticDurationSeconds,
                    FadeDuration = videoConfig.FadeDurationSeconds,
                    ResolutionMode = Enum.Parse<ResolutionMode>(videoConfig.ResolutionMode ?? "Mode1080p"),
                    EnableFadeTransitions = videoConfig.EnableFadeTransitions
                };
                
                if (videoGenerator.GenerateVideo())
                {
                    Logger.Log("✓ Video generation completed successfully.");
                }
                else
                {
                    Logger.Log("✗ Video generation failed.");
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