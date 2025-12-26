using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using OpenMeteo;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    public static class ImageGenerator
    {
        public static void GenerateAlertsImage(List<AlertEntry> alerts, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();
            var alertConfig = config.Alerts ?? new AlertsSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            float margin = imgConfig.MarginPixels;
            float contentWidth = width - (margin * 2);

            // Cleanup old alert images to avoid stale pages
            string baseName = Path.GetFileNameWithoutExtension(alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
            string ext = Path.GetExtension(alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
            
            // Delete exact match
            string exactPath = Path.Combine(outputDir, alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
            if (File.Exists(exactPath)) File.Delete(exactPath);

            // Delete numbered variations
            var oldFiles = Directory.GetFiles(outputDir, $"{baseName}_*{ext}");
            foreach (var f in oldFiles)
            {
                try { File.Delete(f); } catch { }
            }
            
            // Helper to create a fresh bitmap with background and header
            (Bitmap, Graphics) CreateNewPage()
            {
                Bitmap bmp = new Bitmap(width, height);
                Graphics g = Graphics.FromImage(bmp);
                
                // Background
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(30, 30, 30), Color.FromArgb(10, 10, 10)))
                {
                    g.FillRectangle(brush, 0, 0, width, height);
                }

                // Header
                using (Font headerFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.HeaderFontSize, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(alertConfig.HeaderText ?? "⚠️ Environment Canada Alerts", headerFont, whiteBrush, new PointF(margin, margin));
                }
                
                return (bmp, g);
            }

            var (currentBitmap, currentGraphics) = CreateNewPage();
            float currentY = 150f; 
            int pageIndex = 1;

            using (Font cityFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.CityFontSize, FontStyle.Bold))
            using (Font typeFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.TypeFontSize, FontStyle.Bold))
            using (Font detailFont = new Font(imgConfig.FontFamily ?? "Arial", alertConfig.DetailsFontSize, FontStyle.Regular))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            {
                if (alerts.Count == 0)
                {
                    using(Brush greenBrush = new SolidBrush(Color.LightGreen))
                    {
                         currentGraphics.DrawString(alertConfig.NoAlertsText ?? "No Active Warnings or Watches", cityFont, greenBrush, new PointF(margin, currentY));
                    }
                }
                else
                {
                    foreach (var alert in alerts) 
                    {
                        // Calculate required height for this alert
                        float requiredHeight = 45; // Header
                        SizeF titleSize = currentGraphics.MeasureString(alert.Title, typeFont, (int)contentWidth);
                        requiredHeight += titleSize.Height + 10;
                        
                        SizeF summarySize = currentGraphics.MeasureString(alert.Summary, detailFont, (int)contentWidth);
                        requiredHeight += summarySize.Height + 60;

                        // Check if we need a new page
                        if (currentY + requiredHeight > height - 50)
                        {
                            // Save current page
                            string filename;
                            if (pageIndex == 1)
                                filename = Path.Combine(outputDir, alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
                            else
                                filename = Path.Combine(outputDir, $"{baseName}_{pageIndex}{ext}");

                            SaveImage(currentBitmap, filename, imgConfig);
                            Logger.Log($"✓ Generated: {filename}");
                            
                            // Dispose current
                            currentGraphics.Dispose();
                            currentBitmap.Dispose();

                            // Start new page
                            pageIndex++;
                            (currentBitmap, currentGraphics) = CreateNewPage();
                            currentY = 150f;
                        }

                        // Draw Alert
                        Color alertColor = Color.LightGray;
                        if (alert.SeverityColor == "Red") alertColor = Color.Red;
                        if (alert.SeverityColor == "Yellow") alertColor = Color.Yellow;

                        using (Brush alertBrush = new SolidBrush(alertColor))
                        {
                            string headerLine = $"> {alert.City.ToUpper()} : {alert.Type}";
                            currentGraphics.DrawString(headerLine, cityFont, alertBrush, new PointF(margin, currentY));
                            currentY += 45;

                            RectangleF titleRect = new RectangleF(margin, currentY, contentWidth, titleSize.Height);
                            currentGraphics.DrawString(alert.Title, typeFont, whiteBrush, titleRect);
                            currentY += titleSize.Height + 10;
                        }

                        RectangleF summaryRect = new RectangleF(margin, currentY, contentWidth, summarySize.Height);
                        currentGraphics.DrawString(alert.Summary, detailFont, whiteBrush, summaryRect);
                        currentY += summarySize.Height + 60;
                    }
                }
            }

            // Save the final page
            string finalFilename;
            if (pageIndex == 1)
                finalFilename = Path.Combine(outputDir, alertConfig.AlertFilename ?? "10_WeatherAlerts.png");
            else
                finalFilename = Path.Combine(outputDir, $"{baseName}_{pageIndex}{ext}");

            SaveImage(currentBitmap, finalFilename, imgConfig);
            Logger.Log($"✓ Generated: {finalFilename}");

            currentGraphics.Dispose();
            currentBitmap.Dispose();
        }

        public static void GenerateMapsImage(WeatherForecast?[] allData, string[] locationNames, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var imgConfig = config.ImageGeneration ?? new ImageGenerationSettings();

            int width = imgConfig.ImageWidth;
            int height = imgConfig.ImageHeight;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // Background: prefer a province-level radar animation (first frame) if present
                string staticMapPath = Path.Combine(outputDir, config.WeatherImages?.StaticMapFilename ?? "STATIC_MAP.IGNORE");

                // Look for an animated/static province radar file with the 00_ prefix
                var provinceRadarFiles = Directory.GetFiles(outputDir, "00_ProvinceRadar.*");
                if (provinceRadarFiles.Length > 0)
                {
                    try
                    {
                        // Use the first match (should be only one)
                        string pr = provinceRadarFiles.OrderBy(f => f).First();
                        using (var provImg = Image.FromFile(pr))
                        {
                            // If animated (GIF), Image.FromFile returns the GIF; drawing draws current frame (frame 0)
                            graphics.DrawImage(provImg, 0, 0, width, height);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[Radar] Failed to draw province radar: {ex.Message}", ConsoleColor.Yellow);
                        // fallback to static map
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
                    }
                }
                else if (File.Exists(staticMapPath))
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

        public static void GenerateCurrentWeatherImage(WeatherForecast weatherData, string outputDir)
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
                        ? $"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m} {DegreesToCardinal(weatherData.Current.Winddirection_10m)}"
                        : "N/A";

                    graphics.DrawString("Temperature:", labelFont, whiteBrush, new PointF(50, 150));
                    graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(50, 200));

                    graphics.DrawString("Condition:", labelFont, whiteBrush, new PointF(50, 280));
                    graphics.DrawString(conditionText, dataFont, whiteBrush, new PointF(50, 330));

                    graphics.DrawString("Wind:", labelFont, whiteBrush, new PointF(50, 410));
                    graphics.DrawString(windText, dataFont, whiteBrush, new PointF(50, 460));
                }

                string filename = Path.Combine(outputDir, config.WeatherImages?.CurrentWeatherFilename ?? "1_CurrentWeather.png");
                var saved = SaveImage(bitmap, filename, imgConfig);
                Logger.Log($"✓ Generated: {saved}");
            }
        }

        public static void GenerateForecastSummaryImage(WeatherForecast weatherData, string outputDir)
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

                                string windSpeed = weatherData.Daily.Windspeed_10m_max != null ? weatherData.Daily.Windspeed_10m_max[i].ToString() : "-";
                                string windUnit = weatherData.DailyUnits?.Windspeed_10m_max ?? "km/h";
                                string windDir = weatherData.Daily.Winddirection_10m_dominant != null ? DegreesToCardinal(weatherData.Daily.Winddirection_10m_dominant[i]) : "";

                                graphics.DrawString($"{date}", labelFont, whiteBrush, new PointF(50, yPosition));
                                graphics.DrawString($"High: {maxTemp}° | Low: {minTemp}° | {condition}", dataFont, whiteBrush,
                                    new PointF(50, yPosition + 35));
                                graphics.DrawString($"Wind: {windSpeed} {windUnit} {windDir}", labelFont, whiteBrush, new PointF(50, yPosition + 75));

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

        public static void GenerateDetailedWeatherImage(WeatherForecast weatherData, string outputDir, int cityIndex, string locationName)
        {
            // Redirect to the new batch renderer for consistent look & feel (single-item batch)
            GenerateDetailedWeatherImageBatch(new[] { (Forecast: (WeatherForecast?)weatherData, Name: locationName, Index: cityIndex) }, outputDir, cityIndex);
        }

        // New: render up to 3 cities per detailed image with improved layout and fonts
        public static void GenerateDetailedWeatherImageBatch((WeatherForecast? Forecast, string Name, int Index)[] cities, string outputDir, int batchIndex)
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

                            // Large weather icon to the right of temperature, taking up available space
                            float iconSize = 180;
                            var iconRect = new RectangleF(x + colWidth - iconSize - 30, y + 60, iconSize, iconSize);
                            DrawWeatherIcon(graphics, iconRect, item.Forecast?.Current?.Weathercode);

                            // Condition
                            graphics.DrawString(cond, dataFont, whiteBrush, new PointF(x + 20, y + 140));

                            // Small details
                            float detailY = y + 190;
                            graphics.DrawString($"Feels Like: {cur.Apparent_temperature}{item.Forecast.CurrentUnits?.Apparent_temperature}", labelFont, whiteBrush, new PointF(x + 20, detailY));
                            graphics.DrawString($"Humidity: {cur.Relativehumidity_2m}%", labelFont, whiteBrush, new PointF(x + 20, detailY + 22));
                            graphics.DrawString($"Wind: {cur.Windspeed_10m} {item.Forecast.CurrentUnits?.Windspeed_10m} {DegreesToCardinal(cur.Winddirection_10m)}", labelFont, whiteBrush, new PointF(x + 20, detailY + 44));
                            graphics.DrawString($"Pressure: {cur.Surface_pressure} hPa", labelFont, whiteBrush, new PointF(x + 20, detailY + 66));

                            // 5-Day Forecast Section
                            float forecastY = detailY + 100;
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
                                    float rowHeight = 95;

                                    for (int d = 0; d < daysToShow; d++)
                                    {
                                        if (DateTime.TryParse(item.Forecast.Daily.Time[d], out DateTime dateTime))
                                        {
                                            // Draw row background
                                            using (Brush rowBg = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
                                            {
                                                graphics.FillRectangle(rowBg, x + 10, forecastY, colWidth - 20, rowHeight - 5);
                                            }

                                            string dayName = dateTime.ToString("ddd");
                                            string dateStr = dateTime.ToString("MMM d");
                                            string maxTemp = item.Forecast.Daily.Temperature_2m_max != null 
                                                ? $"{item.Forecast.Daily.Temperature_2m_max[d]:F0}°" : "-";
                                            string minTemp = item.Forecast.Daily.Temperature_2m_min != null 
                                                ? $"{item.Forecast.Daily.Temperature_2m_min[d]:F0}°" : "-";
                                            
                                            // Feels like
                                            string maxFeel = item.Forecast.Daily.Apparent_temperature_max != null 
                                                ? $"{item.Forecast.Daily.Apparent_temperature_max[d]:F0}°" : "-";
                                            string minFeel = item.Forecast.Daily.Apparent_temperature_min != null 
                                                ? $"{item.Forecast.Daily.Apparent_temperature_min[d]:F0}°" : "-";

                                            int? dayCode = item.Forecast.Daily.Weathercode != null 
                                                ? (int?)item.Forecast.Daily.Weathercode[d] : null;

                                            float paddingY = 10;

                                            // Day name and date (Larger)
                                            using (Font largeDayFont = new Font(imgConfig.FontFamily ?? "Segoe UI", 14, FontStyle.Bold))
                                            {
                                                graphics.DrawString(dayName, largeDayFont, whiteBrush, new PointF(x + 20, forecastY + paddingY));
                                            }
                                            graphics.DrawString(dateStr, forecastTempFont, whiteBrush, new PointF(x + 20, forecastY + paddingY + 25));

                                            // Weather icon
                                            var dayIconRect = new RectangleF(x + 90, forecastY + paddingY, 60, 60);
                                            DrawWeatherIcon(graphics, dayIconRect, dayCode);

                                            // High/Low temps
                                            using (Brush highBrush = new SolidBrush(Color.FromArgb(255, 255, 150, 100)))
                                            using (Brush lowBrush = new SolidBrush(Color.FromArgb(255, 150, 200, 255)))
                                            using (Brush feelBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
                                            {
                                                graphics.DrawString($"H: {maxTemp}", forecastDayFont, highBrush, new PointF(x + 160, forecastY + paddingY));
                                                graphics.DrawString($"L: {minTemp}", forecastDayFont, lowBrush, new PointF(x + 160, forecastY + paddingY + 20));
                                                
                                                // Feels like
                                                graphics.DrawString($"FL: {maxFeel} / {minFeel}", forecastTempFont, feelBrush, new PointF(x + 160, forecastY + paddingY + 42));
                                            }

                                            // Wind & Precip
                                            string windSpeed = item.Forecast.Daily.Windspeed_10m_max != null ? $"{item.Forecast.Daily.Windspeed_10m_max[d]:F0}" : "-";
                                            string windUnit = item.Forecast.DailyUnits?.Windspeed_10m_max ?? "km/h";
                                            string windDir = item.Forecast.Daily.Winddirection_10m_dominant != null ? DegreesToCardinal(item.Forecast.Daily.Winddirection_10m_dominant[d]) : "";
                                            
                                            string precip = "";
                                            
                                            // Calculate Rain (Rain + Showers)
                                            float rainVal = 0f;
                                            if (item.Forecast.Daily.Rain_sum != null) rainVal += item.Forecast.Daily.Rain_sum[d];
                                            if (item.Forecast.Daily.Showers_sum != null) rainVal += item.Forecast.Daily.Showers_sum[d];

                                            if (rainVal > 0)
                                            {
                                                string rainUnit = item.Forecast.DailyUnits?.Rain_sum ?? "mm";
                                                precip += $"{rainVal:F1}{rainUnit} ";
                                            }

                                            // Calculate Snow
                                            if (item.Forecast.Daily.Snowfall_sum != null && item.Forecast.Daily.Snowfall_sum[d] > 0)
                                            {
                                                string snowUnit = item.Forecast.DailyUnits?.Snowfall_sum ?? "cm";
                                                precip += $"{item.Forecast.Daily.Snowfall_sum[d]:F1}{snowUnit} ";
                                            }

                                            // Fallback to total precipitation if no specific rain/snow data found but precip exists
                                            if (string.IsNullOrWhiteSpace(precip) && item.Forecast.Daily.Precipitation_sum != null && item.Forecast.Daily.Precipitation_sum[d] > 0)
                                            {
                                                string precipUnit = item.Forecast.DailyUnits?.Precipitation_sum ?? "mm";
                                                precip = $"{item.Forecast.Daily.Precipitation_sum[d]:F1}{precipUnit}";
                                            }

                                            precip = precip.Trim();

                                            graphics.DrawString($"W: {windSpeed}{windUnit} {windDir}", forecastTempFont, whiteBrush, new PointF(x + 280, forecastY + paddingY));
                                            if (!string.IsNullOrEmpty(precip))
                                            {
                                                using (Brush rainBrush = new SolidBrush(Color.LightBlue))
                                                {
                                                    graphics.DrawString($"Precip: {precip}", forecastTempFont, rainBrush, new PointF(x + 280, forecastY + paddingY + 20));
                                                }
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

                            // Radar thumbnail (if available)
                            try
                            {
                                string safeName = string.Concat(cityName?.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)) ?? new char[0]).Replace(' ', '_');
                                var radarFiles = Directory.GetFiles(outputDir, $"radar_{safeName}.*");
                                if (radarFiles.Length > 0)
                                {
                                    string radarFile = radarFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                                    using (var ms = new MemoryStream(File.ReadAllBytes(radarFile)))
                                    using (var radarImg = Image.FromStream(ms))
                                    {
                                        float cardInnerHeight = height - margin * 2;
                                        float radarHeight = Math.Min(140f, cardInnerHeight * 0.28f);
                                        float radarWidth = colWidth - 40;
                                        float radarX = x + 20;
                                        float radarY = y + cardInnerHeight - radarHeight - 20;
                                        var radarRect = new RectangleF(radarX, radarY, radarWidth, radarHeight);
                                        graphics.DrawImage(radarImg, radarRect);

                                        // Draw a thin border and label
                                        using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
                                        {
                                            graphics.DrawRectangle(p, radarX, radarY, radarWidth, radarHeight);
                                        }
                                        using (Font rf = new Font(imgConfig.FontFamily ?? "Segoe UI", 10, FontStyle.Bold))
                                        using (Brush lb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                                        {
                                            graphics.DrawString("Radar", rf, lb, new PointF(radarX + 6, radarY + 6));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"[Radar] Failed to draw radar for {cityName}: {ex.Message}", ConsoleColor.Yellow);
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
        public static void DrawWeatherIcon(Graphics g, RectangleF area, int? weatherCode)
        {
            // Default: clear icon (sun)
            int code = weatherCode ?? 0;

            // Try to load custom icon from "WeatherImages/Icons/{code}.png"
            // We look in the application directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(baseDir, "WeatherImages", "Icons", $"{code}.png");
            
            // If specific code not found, try generic mapping
            if (!File.Exists(iconPath))
            {
                string? generic = GetGenericIconName(code);
                if (generic != null)
                {
                    string genericPath = Path.Combine(baseDir, "WeatherImages", "Icons", generic);
                    if (File.Exists(genericPath)) iconPath = genericPath;
                }
            }

            if (File.Exists(iconPath))
            {
                try
                {
                    using (var img = Image.FromFile(iconPath))
                    {
                        g.DrawImage(img, area);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Warning] Failed to load icon {iconPath}: {ex.Message}", ConsoleColor.Yellow);
                }
            }

            // Fallback to vector graphics
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

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
            else if (code == 2 || code == 3 || code == 45 || code == 48)
            {
                // Cloudy / overcast / fog
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
                using (Brush cloud = new SolidBrush(Color.FromArgb(200, 200, 210)))
                using (Pen drop = new Pen(Color.FromArgb(100, 160, 255), 3))
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
                using (Brush cloud = new SolidBrush(Color.FromArgb(240, 240, 250)))
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
            else if (code >= 95 && code <= 99)
            {
                 // Thunderstorm
                using (Brush cloud = new SolidBrush(Color.FromArgb(100, 100, 110)))
                using (Pen bolt = new Pen(Color.Yellow, 3))
                {
                    float cw = area.Width * 0.9f;
                    float ch = area.Height * 0.45f;
                    float cx = area.X + (area.Width - cw) / 2;
                    float cy = area.Y + (area.Height - ch) / 2 + 6;
                    g.FillEllipse(cloud, cx + cw * 0.08f, cy, cw * 0.6f, ch);
                    g.FillEllipse(cloud, cx + cw * 0.35f, cy - ch * 0.2f, cw * 0.6f, ch);
                    g.FillRectangle(cloud, cx + cw * 0.05f, cy + ch * 0.2f, cw * 0.9f, ch * 0.5f);

                    // Bolt
                    var bx = cx + cw * 0.4f;
                    var by = cy + ch;
                    g.DrawLine(bolt, bx, by, bx - 5, by + 10);
                    g.DrawLine(bolt, bx - 5, by + 10, bx + 5, by + 10);
                    g.DrawLine(bolt, bx + 5, by + 10, bx, by + 20);
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
            
            g.SmoothingMode = oldMode;
        }

        public static string? GetGenericIconName(int code)
        {
            return code switch
            {
                0 => "sunny.png",
                1 => "partly_cloudy.png",
                2 => "partly_cloudy.png",
                3 => "cloudy.png",
                45 => "fog.png",
                48 => "fog.png",
                51 => "rain.png",
                53 => "rain.png",
                55 => "rain.png",
                61 => "rain.png",
                63 => "rain.png",
                65 => "rain.png",
                66 => "freezing_rain.png",
                67 => "freezing_rain.png",
                71 => "snow.png",
                73 => "snow.png",
                75 => "snow.png",
                77 => "snow.png",
                80 => "rain.png",
                81 => "rain.png",
                82 => "rain.png",
                85 => "snow.png",
                86 => "snow.png",
                95 => "storm.png",
                96 => "storm.png",
                99 => "storm.png",
                _ => null
            };
        }

        public static void GenerateAPNGcurrentTemperature(WeatherForecast weatherData, string outputDir)
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

        public static string SaveImage(Bitmap bitmap, string filename, ImageGenerationSettings imgConfig)
        {
            var ext = (imgConfig.ImageFormat ?? "png").Trim().Trim('.');
            var format = ResolveImageFormat(ext);
            var finalPath = Path.ChangeExtension(filename, ext);
            bitmap.Save(finalPath, format);
            return finalPath;
        }

        public static ImageFormat ResolveImageFormat(string ext)
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

        public static void CreateTestImages()
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

        public static string DegreesToCardinal(double? degrees)
        {
            if (!degrees.HasValue) return "N/A";
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            return cardinals[(int)Math.Round(((double)degrees % 360) / 45)];
        }

        public static string GetWeatherDescription(int weatherCode)
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
