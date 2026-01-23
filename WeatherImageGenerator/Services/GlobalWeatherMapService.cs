#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenMap;
using OpenMeteo;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Represents a static city for the weather map
    /// </summary>
    public class StaticCity
    {
        public string Name { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    /// <summary>
    /// Service for generating global weather maps with temperature overlays
    /// Uses static Quebec cities with exact coordinates
    /// </summary>
    public class GlobalWeatherMapService
    {
        private readonly MapOverlayService _mapService;
        private readonly int _width;
        private readonly int _height;
        private readonly HttpClient _httpClient;

        // Static Quebec cities with exact coordinates
        private static readonly List<StaticCity> QuebecCities = new()
        {
            new StaticCity { Name = "Montréal", Latitude = 45.5017, Longitude = -73.5673 },
            new StaticCity { Name = "Québec", Latitude = 46.8139, Longitude = -71.2080 },
            new StaticCity { Name = "Gatineau", Latitude = 45.4765, Longitude = -75.7013 },
            new StaticCity { Name = "Sherbrooke", Latitude = 45.4042, Longitude = -71.8929 },
            new StaticCity { Name = "Drummondville", Latitude = 45.8833, Longitude = -72.4833 },
            new StaticCity { Name = "Amos", Latitude = 48.5667, Longitude = -78.1167 },
            new StaticCity { Name = "Mont-Laurier", Latitude = 46.5500, Longitude = -75.5000 }
        };

        public GlobalWeatherMapService(int width = 1920, int height = 1080)
        {
            _width = width;
            _height = height;
            
            // Load OpenMap configuration
            var config = ConfigManager.LoadConfig();
            var openMapSettings = ConvertToOpenMapSettings(config.OpenMap);
            
            _mapService = new MapOverlayService(width, height, openMapSettings);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Generates a weather map with static Quebec cities (ignores user input locations)
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <returns>Path to the generated map file</returns>
        public async Task<string> GenerateStaticQuebecWeatherMapAsync(string outputPath)
        {
            Logger.Log($"[GlobalWeatherMap] Generating Quebec weather map with {QuebecCities.Count} static cities", ConsoleColor.Cyan);

            // Fetch weather data for all static cities
            var weatherData = new List<WeatherForecast?>();
            var openMeteoClient = new OpenMeteoClient();

            foreach (var city in QuebecCities)
            {
                try
                {
                    var options = new WeatherForecastOptions
                    {
                        Latitude = (float)city.Latitude,
                        Longitude = (float)city.Longitude,
                        Current = new CurrentOptions(new[] {
                            CurrentOptionsParameter.temperature_2m,
                            CurrentOptionsParameter.weathercode,
                            CurrentOptionsParameter.is_day
                        })
                    };

                    var forecast = await openMeteoClient.QueryAsync(options);
                    weatherData.Add(forecast);
                    Logger.Log($"  ✓ Fetched weather for {city.Name}: {forecast?.Current?.Temperature_2m ?? 0:F0}°C", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    Logger.Log($"  ✗ Failed to fetch weather for {city.Name}: {ex.Message}", ConsoleColor.Yellow);
                    weatherData.Add(null);
                }
            }

            // Calculate map center and zoom for Quebec region (including Amos in northwest)
            double centerLat = 47.3; // Moved higher to show Amos at top
            double centerLon = -74.5;
            int zoomLevel = 8; // Closer zoom while keeping all cities visible

            // Generate base map
            Logger.Log("[GlobalWeatherMap] Generating base map for Quebec region...", ConsoleColor.Cyan);
            using var baseMap = await _mapService.GenerateMapBackgroundAsync(
                centerLat, centerLon, zoomLevel, _width, _height);

            // Create final image with overlays
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

                // Overlay city temperatures at exact coordinates
                DrawStaticCityOverlays(g, weatherData, centerLat, centerLon, zoomLevel);

                // Draw title and timestamp at bottom
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

            Logger.Log($"✓ Quebec weather map saved: {outputPath}", ConsoleColor.Green);
            return outputPath;
        }

        /// <summary>
        /// Draws temperature overlays for static cities at their exact geographic coordinates
        /// </summary>
        private void DrawStaticCityOverlays(
            Graphics g,
            List<WeatherForecast?> weatherData,
            double centerLat,
            double centerLon,
            int zoomLevel)
        {
            using var cityFont = new Font("Arial", 22, FontStyle.Bold);
            using var tempFont = new Font("Arial", 42, FontStyle.Bold);
            using var whiteBrush = new SolidBrush(Color.White);
            using var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));

            for (int i = 0; i < QuebecCities.Count && i < weatherData.Count; i++)
            {
                var city = QuebecCities[i];
                var forecast = weatherData[i];

                // Convert lat/lon to pixel coordinates on the map
                var pixelCoords = LatLonToPixel(city.Latitude, city.Longitude, centerLat, centerLon, zoomLevel);

                // Adjust positions for text placement
                var cityPos = new PointF(pixelCoords.X - 50, pixelCoords.Y - 70); // City name above point
                var tempPos = new PointF(pixelCoords.X - 40, pixelCoords.Y - 30);  // Temperature below name

                // Draw location marker
                DrawLocationMarker(g, pixelCoords);

                // Draw city name
                DrawTextWithShadow(g, city.Name, cityFont, whiteBrush, shadowBrush, cityPos);

                // Draw temperature
                if (forecast?.Current?.Temperature_2m != null)
                {
                    var temp = forecast.Current.Temperature_2m.Value;
                    var tempText = $"{temp:F0}°C";
                    DrawTextWithShadow(g, tempText, tempFont, whiteBrush, shadowBrush, tempPos);

                    // Draw weather icon
                    if (forecast.Current.Weathercode != null)
                    {
                        var iconPos = new PointF(tempPos.X - 55, tempPos.Y + 5);
                        DrawWeatherIconOnMap(g, iconPos, forecast.Current.Weathercode.Value, forecast.Current.Is_day == 1);
                    }
                }
                else
                {
                    DrawTextWithShadow(g, "N/A", tempFont, whiteBrush, shadowBrush, tempPos);
                }
            }
        }

        /// <summary>
        /// Converts latitude/longitude to pixel coordinates on the map
        /// </summary>
        private PointF LatLonToPixel(double lat, double lon, double centerLat, double centerLon, int zoom)
        {
            // Web Mercator projection
            const double tileSize = 256.0;
            double scale = tileSize * Math.Pow(2, zoom);

            // Convert to world coordinates
            double worldX = (lon + 180.0) / 360.0 * scale;
            double sinLat = Math.Sin(lat * Math.PI / 180.0);
            double worldY = (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI)) * scale;

            // Convert center to world coordinates
            double centerWorldX = (centerLon + 180.0) / 360.0 * scale;
            double sinCenterLat = Math.Sin(centerLat * Math.PI / 180.0);
            double centerWorldY = (0.5 - Math.Log((1 + sinCenterLat) / (1 - sinCenterLat)) / (4 * Math.PI)) * scale;

            // Calculate pixel offset from center
            double pixelX = _width / 2.0 + (worldX - centerWorldX);
            double pixelY = _height / 2.0 + (worldY - centerWorldY);

            return new PointF((float)pixelX, (float)pixelY);
        }

        /// <summary>
        /// Draws a location marker at the specified position
        /// </summary>
        private void DrawLocationMarker(Graphics g, PointF position)
        {
            float markerSize = 12f;
            var markerRect = new RectangleF(
                position.X - markerSize / 2,
                position.Y - markerSize / 2,
                markerSize,
                markerSize);

            // Draw outer circle (white border)
            using (var outerPen = new Pen(Color.White, 3f))
            {
                g.DrawEllipse(outerPen, markerRect);
            }

            // Draw inner circle (colored)
            using (var innerBrush = new SolidBrush(Color.FromArgb(255, 255, 165, 0))) // Orange
            {
                g.FillEllipse(innerBrush, markerRect);
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
        /// Draws the map header with title and timestamp at the top
        /// </summary>
        private void DrawMapHeader(Graphics g)
        {
            using var titleFont = new Font("Arial", 28, FontStyle.Bold);
            using var timestampFont = new Font("Arial", 16, FontStyle.Regular);
            using var whiteBrush = new SolidBrush(Color.White);
            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));

            // Draw semi-transparent header background at top
            using (var headerBg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.FillRectangle(headerBg, 0, 0, _width, 80);
            }

            // Draw title
            var title = "Current Weather Conditions";
            DrawTextWithShadow(g, title, titleFont, whiteBrush, shadowBrush, new PointF(40, 15));

            // Draw timestamp
            var timestamp = $"Updated: {DateTime.Now:yyyy-MM-dd HH:mm}";
            DrawTextWithShadow(g, timestamp, timestampFont, whiteBrush, shadowBrush, new PointF(40, 50));
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

                // Note: Temperature overlays are only implemented for static Quebec cities
                // For custom bbox maps, you would need to add DrawCustomCityOverlays method

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
        
        private static OpenMap.OpenMapSettings? ConvertToOpenMapSettings(Services.OpenMapSettings? configSettings)
        {
            if (configSettings == null) return null;
            
            return new OpenMap.OpenMapSettings
            {
                DefaultMapStyle = configSettings.DefaultMapStyle,
                DefaultZoomLevel = configSettings.DefaultZoomLevel,
                BackgroundColor = configSettings.BackgroundColor,
                OverlayOpacity = configSettings.OverlayOpacity,
                TileDownloadTimeoutSeconds = configSettings.TileDownloadTimeoutSeconds,
                EnableTileCache = configSettings.EnableTileCache,
                TileCacheDirectory = configSettings.TileCacheDirectory,
                CacheDurationHours = configSettings.CacheDurationHours
            };
        }
    }
}
