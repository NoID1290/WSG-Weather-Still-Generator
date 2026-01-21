#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenMap;
using OpenMeteo;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Service for generating global weather maps with temperature overlays
    /// </summary>
    public class GlobalWeatherMapService
    {
        private readonly MapOverlayService _mapService;
        private readonly int _width;
        private readonly int _height;

        public GlobalWeatherMapService(int width = 1920, int height = 1080)
        {
            _width = width;
            _height = height;
            _mapService = new MapOverlayService(width, height);
        }

        /// <summary>
        /// Generates a global or regional weather map with temperature data overlaid
        /// </summary>
        /// <param name="weatherData">Array of weather forecasts for different locations</param>
        /// <param name="locationNames">Names of the locations</param>
        /// <param name="outputPath">Output file path</param>
        /// <param name="centerLat">Center latitude for map (default: 46.5 for Quebec)</param>
        /// <param name="centerLon">Center longitude for map (default: -72.0 for Quebec)</param>
        /// <param name="zoomLevel">Map zoom level (default: 6 for provincial view)</param>
        /// <returns>Path to the generated map file</returns>
        public async Task<string> GenerateWeatherMapAsync(
            WeatherForecast?[] weatherData,
            string?[] locationNames,
            string outputPath,
            double centerLat = 46.5,
            double centerLon = -72.0,
            int zoomLevel = 6)
        {
            Logger.Log($"[GlobalWeatherMap] Generating weather map with {weatherData.Length} locations", ConsoleColor.Cyan);

            // Generate base map
            Logger.Log("[GlobalWeatherMap] Fetching base map...", ConsoleColor.Cyan);
            using var baseMap = await _mapService.GenerateMapBackgroundAsync(
                centerLat, centerLon, zoomLevel, _width, _height, MapStyle.Standard);

            // Create final image with base map
            var finalImage = new Bitmap(_width, _height);
            using (var g = Graphics.FromImage(finalImage))
            {
                // Enable high quality rendering
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Draw base map
                g.DrawImage(baseMap, 0, 0, _width, _height);

                // Overlay temperature data
                await DrawTemperatureOverlaysAsync(g, weatherData, locationNames);

                // Draw title and timestamp
                DrawMapHeader(g);
            }

            // Save the image
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            finalImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            finalImage.Dispose();

            Logger.Log($"✓ Weather map saved: {outputPath}", ConsoleColor.Green);
            return outputPath;
        }

        /// <summary>
        /// Draws temperature overlays for each location on the map
        /// </summary>
        private async Task DrawTemperatureOverlaysAsync(
            Graphics g,
            WeatherForecast?[] weatherData,
            string?[] locationNames)
        {
            var config = ConfigManager.LoadConfig();

            using var cityFont = new Font("Arial", 24, FontStyle.Bold);
            using var tempFont = new Font("Arial", 48, FontStyle.Bold);
            using var whiteBrush = new SolidBrush(Color.White);
            using var shadowBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));

            for (int i = 0; i < weatherData.Length && i < locationNames.Length; i++)
            {
                var data = weatherData[i];
                var locName = locationNames[i];

                if (data == null || string.IsNullOrWhiteSpace(locName))
                    continue;

                // Get configured position or skip
                if (config.MapLocations == null ||
                    !config.MapLocations.TryGetValue($"Location{i}", out var mapLoc))
                    continue;

                var cityPos = new PointF(mapLoc.CityPositionX, mapLoc.CityPositionY);
                var tempPos = new PointF(mapLoc.TemperaturePositionX, mapLoc.TemperaturePositionY);

                // Skip if positions are not configured (0,0)
                if (cityPos.X == 0 && cityPos.Y == 0) continue;
                if (tempPos.X == 0 && tempPos.Y == 0) continue;

                // Get temperature
                string tempText = "N/A";
                if (data.Current != null && data.Current.Temperature_2m.HasValue)
                {
                    var temp = data.Current.Temperature_2m.Value;
                    var unit = data.CurrentUnits?.Temperature ?? "°C";
                    tempText = $"{temp:F0}{unit}";
                }

                // Draw with shadow for better visibility
                DrawTextWithShadow(g, locName, cityFont, whiteBrush, shadowBrush, cityPos);
                DrawTextWithShadow(g, tempText, tempFont, whiteBrush, shadowBrush, tempPos);

                // Draw weather icon if available
                if (data.Current?.Weathercode != null)
                {
                    var iconPos = new PointF(tempPos.X - 60, tempPos.Y);
                    DrawWeatherIconOnMap(g, iconPos, data.Current.Weathercode.Value, data.Current.Is_day == 1);
                }
            }
        }

        /// <summary>
        /// Draws text with a shadow for better visibility on maps
        /// </summary>
        private void DrawTextWithShadow(
            Graphics g,
            string text,
            Font font,
            Brush textBrush,
            Brush shadowBrush,
            PointF position)
        {
            // Draw shadow (offset by 2 pixels)
            g.DrawString(text, font, shadowBrush, position.X + 2, position.Y + 2);
            // Draw text
            g.DrawString(text, font, textBrush, position);
        }

        /// <summary>
        /// Draws a small weather icon on the map
        /// </summary>
        private void DrawWeatherIconOnMap(Graphics g, PointF position, int weatherCode, bool isDay)
        {
            // Simple icon representation based on weather code
            var iconSize = 50f;
            var iconRect = new RectangleF(position.X, position.Y, iconSize, iconSize);

            // Draw a simple circle background
            using (var bgBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                g.FillEllipse(bgBrush, iconRect);
            }

            // Draw icon based on weather code (simplified)
            using (var iconPen = new Pen(Color.Black, 2f))
            using (var iconBrush = new SolidBrush(GetWeatherColor(weatherCode)))
            {
                if (weatherCode >= 0 && weatherCode <= 3)
                {
                    // Clear/Partly cloudy - draw sun
                    g.FillEllipse(Brushes.Gold, iconRect.X + 15, iconRect.Y + 15, 20, 20);
                }
                else if (weatherCode >= 51 && weatherCode <= 67)
                {
                    // Rain
                    g.FillEllipse(Brushes.DodgerBlue, iconRect.X + 10, iconRect.Y + 10, 30, 30);
                }
                else if (weatherCode >= 71 && weatherCode <= 77)
                {
                    // Snow
                    g.FillEllipse(Brushes.LightBlue, iconRect.X + 10, iconRect.Y + 10, 30, 30);
                }
                else
                {
                    // Default cloud
                    g.FillEllipse(Brushes.LightGray, iconRect.X + 10, iconRect.Y + 10, 30, 30);
                }
            }
        }

        /// <summary>
        /// Gets a color representing the weather condition
        /// </summary>
        private Color GetWeatherColor(int weatherCode)
        {
            if (weatherCode >= 0 && weatherCode <= 3) return Color.Gold; // Clear/Partly cloudy
            if (weatherCode >= 51 && weatherCode <= 67) return Color.DodgerBlue; // Rain
            if (weatherCode >= 71 && weatherCode <= 77) return Color.LightBlue; // Snow
            if (weatherCode >= 80 && weatherCode <= 99) return Color.DarkBlue; // Heavy rain/thunderstorm
            return Color.LightGray; // Default
        }

        /// <summary>
        /// Draws the map header with title and timestamp
        /// </summary>
        private void DrawMapHeader(Graphics g)
        {
            using var titleFont = new Font("Arial", 36, FontStyle.Bold);
            using var timestampFont = new Font("Arial", 20, FontStyle.Regular);
            using var whiteBrush = new SolidBrush(Color.White);
            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));

            // Draw semi-transparent header background
            using (var headerBg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.FillRectangle(headerBg, 0, 0, _width, 100);
            }

            // Draw title
            var title = "Current Weather Conditions";
            DrawTextWithShadow(g, title, titleFont, whiteBrush, shadowBrush, new PointF(50, 20));

            // Draw timestamp
            var timestamp = $"Updated: {DateTime.Now:yyyy-MM-dd HH:mm}";
            DrawTextWithShadow(g, timestamp, timestampFont, whiteBrush, shadowBrush, new PointF(50, 65));
        }

        /// <summary>
        /// Generates a weather map using custom bounding box
        /// </summary>
        public async Task<string> GenerateWeatherMapWithBboxAsync(
            WeatherForecast?[] weatherData,
            string?[] locationNames,
            string outputPath,
            double minLat,
            double minLon,
            double maxLat,
            double maxLon)
        {
            Logger.Log($"[GlobalWeatherMap] Generating weather map with custom bbox", ConsoleColor.Cyan);

            // Generate base map with bounding box
            using var baseMap = await _mapService.GenerateMapWithBoundingBoxAsync(
                minLat, minLon, maxLat, maxLon, _width, _height, MapStyle.Standard);

            // Create final image
            var finalImage = new Bitmap(_width, _height);
            using (var g = Graphics.FromImage(finalImage))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Draw base map
                g.DrawImage(baseMap, 0, 0, _width, _height);

                // Overlay temperature data
                await DrawTemperatureOverlaysAsync(g, weatherData, locationNames);

                // Draw header
                DrawMapHeader(g);
            }

            // Save
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            finalImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            finalImage.Dispose();

            Logger.Log($"✓ Weather map saved: {outputPath}", ConsoleColor.Green);
            return outputPath;
        }
    }
}
