using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ECCC.Services;
using OpenMap;
using OpenMeteo;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// Manages dynamic weather overlays (radar, temperature, wind, etc.)
    /// Handles fetching, caching, and rendering of weather data layers
    /// </summary>
    public class WeatherOverlayManager : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly RadarImageService _radarService;
        private readonly OpenMeteoClient _openMeteoClient;
        private readonly string _cacheDirectory;
        
        // Active overlays
        private byte[]? _radarOverlay;
        private byte[]? _temperatureOverlay;
        private DateTime _lastRadarUpdate = DateTime.MinValue;
        private DateTime _lastTemperatureUpdate = DateTime.MinValue;
        
        // Configuration
        private readonly TimeSpan _radarUpdateInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _temperatureUpdateInterval = TimeSpan.FromMinutes(30);
        
        public bool RadarEnabled { get; set; } = true;
        public bool TemperatureEnabled { get; set; } = false;
        public float RadarOpacity { get; set; } = 0.75f;
        public float TemperatureOpacity { get; set; } = 0.6f;

        public WeatherOverlayManager(HttpClient httpClient, MapOverlayService? mapService = null, string? cacheDir = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _radarService = new RadarImageService(httpClient, mapService);
            _openMeteoClient = new OpenMeteoClient();
            
            _cacheDirectory = cacheDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSG", "weather_cache");
            
            Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Fetches and updates radar overlay for the given region
        /// </summary>
        public async Task<byte[]?> UpdateRadarOverlayAsync(double centerLat, double centerLon, int width, int height, int mapZoom)
        {
            if (!RadarEnabled)
                return null;

            // Check if we need to update (avoid unnecessary API calls)
            if (DateTime.UtcNow - _lastRadarUpdate < _radarUpdateInterval && _radarOverlay != null)
                return _radarOverlay;

            try
            {
                double radiusKm = CalculateRadiusFromZoom(mapZoom, width, height);
                
                var radarData = await _radarService.FetchRadarImageAsync(
                    centerLat, 
                    centerLon, 
                    width, 
                    height, 
                    radiusKm,
                    mapZoom);

                if (radarData != null)
                {
                    _radarOverlay = radarData;
                    _lastRadarUpdate = DateTime.UtcNow;
                    
                    // Cache to disk
                    var cachePath = Path.Combine(_cacheDirectory, $"radar_{centerLat:F2}_{centerLon:F2}.png");
                    await File.WriteAllBytesAsync(cachePath, radarData);
                }

                return _radarOverlay;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] Radar update error: {ex.Message}");
                return _radarOverlay; // Return cached version if available
            }
        }

        /// <summary>
        /// Generates a temperature overlay for the given region
        /// </summary>
        public async Task<byte[]?> UpdateTemperatureOverlayAsync(
            double centerLat, 
            double centerLon, 
            int width, 
            int height, 
            int mapZoom)
        {
            if (!TemperatureEnabled)
                return null;

            // Check if we need to update
            if (DateTime.UtcNow - _lastTemperatureUpdate < _temperatureUpdateInterval && _temperatureOverlay != null)
                return _temperatureOverlay;

            try
            {
                // Calculate grid bounds
                var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);
                
                // Generate temperature grid
                var tempData = await GenerateTemperatureGridAsync(bbox, width, height, mapZoom);
                
                if (tempData != null)
                {
                    _temperatureOverlay = tempData;
                    _lastTemperatureUpdate = DateTime.UtcNow;
                }

                return _temperatureOverlay;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] Temperature update error: {ex.Message}");
                return _temperatureOverlay;
            }
        }

        /// <summary>
        /// Gets all enabled overlays composited together
        /// </summary>
        public async Task<byte[]?> GetCompositedOverlaysAsync(
            double centerLat, 
            double centerLon, 
            int width, 
            int height, 
            int mapZoom)
        {
            var layers = new List<(byte[] data, float opacity)>();

            // Fetch radar if enabled
            if (RadarEnabled)
            {
                var radar = await UpdateRadarOverlayAsync(centerLat, centerLon, width, height, mapZoom);
                if (radar != null)
                    layers.Add((radar, RadarOpacity));
            }

            // Fetch temperature if enabled
            if (TemperatureEnabled)
            {
                var temp = await UpdateTemperatureOverlayAsync(centerLat, centerLon, width, height, mapZoom);
                if (temp != null)
                    layers.Add((temp, TemperatureOpacity));
            }

            if (layers.Count == 0)
                return null;

            // Composite all layers
            return CompositeOverlays(layers, width, height);
        }

        /// <summary>
        /// Generates a temperature grid overlay
        /// </summary>
        private async Task<byte[]?> GenerateTemperatureGridAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height,
            int mapZoom)
        {
            try
            {
                // Sample temperature at grid points (reduce API calls)
                int gridSize = 5; // 5x5 grid
                var tempPoints = new List<(double lat, double lon, float temp, string location)>();

                double latStep = (bbox.MaxLat - bbox.MinLat) / (gridSize - 1);
                double lonStep = (bbox.MaxLon - bbox.MinLon) / (gridSize - 1);

                for (int i = 0; i < gridSize; i++)
                {
                    for (int j = 0; j < gridSize; j++)
                    {
                        double lat = bbox.MinLat + (i * latStep);
                        double lon = bbox.MinLon + (j * lonStep);

                        try
                        {
                            // Use OpenMeteo API with weather forecast request
                            var options = new WeatherForecastOptions
                            {
                                Current = new CurrentOptions(CurrentOptionsParameter.temperature_2m),
                                Latitude = (float)lat,
                                Longitude = (float)lon
                            };
                            var weatherData = await _openMeteoClient.QueryAsync(options);

                            if (weatherData?.Current?.Temperature_2m != null)
                            {
                                tempPoints.Add((lat, lon, weatherData.Current.Temperature_2m.Value, $"{lat:F2},{lon:F2}"));
                            }
                        }
                        catch
                        {
                            // Skip failed points
                        }
                    }
                }

                if (tempPoints.Count == 0)
                    return null;

                // Render temperature overlay
                return RenderTemperatureOverlay(tempPoints, bbox, width, height, mapZoom);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Renders temperature data as a colored overlay
        /// </summary>
        private byte[]? RenderTemperatureOverlay(
            List<(double lat, double lon, float temp, string location)> points,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height,
            int zoom)
        {
            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // Draw temperature points with gradient circles
                foreach (var point in points)
                {
                    // Convert lat/lon to pixel coordinates
                    var pixelPos = LatLonToPixel(point.lat, point.lon, bbox, width, height);
                    
                    // Get color based on temperature
                    var color = GetTemperatureColor(point.temp);
                    
                    // Draw gradient circle
                    int radius = 80;
                    using var path = new GraphicsPath();
                    path.AddEllipse(pixelPos.x - radius, pixelPos.y - radius, radius * 2, radius * 2);
                    
                    using var pgb = new PathGradientBrush(path);
                    pgb.CenterColor = Color.FromArgb(120, color);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, color) };
                    
                    g.FillEllipse(pgb, pixelPos.x - radius, pixelPos.y - radius, radius * 2, radius * 2);
                    
                    // Draw temperature text
                    string tempText = $"{point.temp:F1}°C";
                    using var font = new Font("Segoe UI", 14, FontStyle.Bold);
                    using var textBrush = new SolidBrush(Color.White);
                    using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                    
                    var textSize = g.MeasureString(tempText, font);
                    float textX = pixelPos.x - textSize.Width / 2;
                    float textY = pixelPos.y - textSize.Height / 2;
                    
                    // Shadow
                    g.DrawString(tempText, font, shadowBrush, textX + 2, textY + 2);
                    // Text
                    g.DrawString(tempText, font, textBrush, textX, textY);
                }

                // Convert to PNG bytes
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Composites multiple overlay layers
        /// </summary>
        private byte[]? CompositeOverlays(List<(byte[] data, float opacity)> layers, int width, int height)
        {
            try
            {
                using var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(result);
                
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;

                foreach (var (data, opacity) in layers)
                {
                    using var ms = new MemoryStream(data);
                    using var layerImage = Image.FromStream(ms);
                    
                    var colorMatrix = new ColorMatrix { Matrix33 = opacity };
                    var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(colorMatrix);
                    
                    g.DrawImage(layerImage, 
                        new Rectangle(0, 0, width, height),
                        0, 0, layerImage.Width, layerImage.Height,
                        GraphicsUnit.Pixel, attributes);
                }

                using var outMs = new MemoryStream();
                result.Save(outMs, ImageFormat.Png);
                return outMs.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets color based on temperature (blue=cold, red=hot)
        /// </summary>
        private Color GetTemperatureColor(float tempC)
        {
            // Temperature color mapping
            if (tempC < -20) return Color.FromArgb(0, 0, 139);      // Dark blue
            if (tempC < -10) return Color.FromArgb(0, 102, 204);    // Blue
            if (tempC < 0) return Color.FromArgb(102, 178, 255);    // Light blue
            if (tempC < 10) return Color.FromArgb(144, 238, 144);   // Light green
            if (tempC < 20) return Color.FromArgb(255, 255, 0);     // Yellow
            if (tempC < 30) return Color.FromArgb(255, 165, 0);     // Orange
            return Color.FromArgb(220, 20, 60);                      // Red
        }

        private (int x, int y) LatLonToPixel(double lat, double lon, 
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox, 
            int width, int height)
        {
            double x = (lon - bbox.MinLon) / (bbox.MaxLon - bbox.MinLon) * width;
            double y = (1.0 - (lat - bbox.MinLat) / (bbox.MaxLat - bbox.MinLat)) * height;
            return ((int)x, (int)y);
        }

        private double CalculateRadiusFromZoom(int zoom, int width, int height)
        {
            // Approximate kilometers per pixel at different zoom levels
            double kmPerPixel = 40075.0 / (256.0 * Math.Pow(2, zoom)); // Earth circumference
            double pixelRadius = Math.Max(width, height) / 2.0;
            return pixelRadius * kmPerPixel;
        }

        private (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBoundingBox(
            double centerLat, double centerLon, int zoom, int width, int height)
        {
            double kmPerPixel = 40075.0 / (256.0 * Math.Pow(2, zoom));
            double latDelta = (height / 2.0) * kmPerPixel / 111.0; // ~111 km per degree
            double lonDelta = (width / 2.0) * kmPerPixel / (111.0 * Math.Cos(centerLat * Math.PI / 180.0));

            return (
                centerLat - latDelta,
                centerLon - lonDelta,
                centerLat + latDelta,
                centerLon + lonDelta
            );
        }

        public void Dispose()
        {
            // Cleanup
        }
    }
}
