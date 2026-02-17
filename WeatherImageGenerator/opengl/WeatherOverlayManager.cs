using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
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
        
        // Track last radar parameters to detect when refresh is needed
        private double _lastRadarLat;
        private double _lastRadarLon;
        private int _lastRadarZoom;
        private string _lastRadarLayer = "RADAR_1KM_RRAI";
        private string? _lastRadarWmsStyle = "RADARURPPRECIPR14-LINEAR";
        
        // Track last temperature parameters to detect when refresh is needed
        private double _lastTempLat;
        private double _lastTempLon;
        private int _lastTempZoom;
        
        // Configuration
        private readonly TimeSpan _radarUpdateInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _temperatureUpdateInterval = TimeSpan.FromMinutes(30);
        
        // Store last calculated bounding boxes
        public (double MinLat, double MinLon, double MaxLat, double MaxLon)? LastRadarBBox { get; private set; }
        public (double MinLat, double MinLon, double MaxLat, double MaxLon)? LastTemperatureBBox { get; private set; }
        
        public bool RadarEnabled { get; set; } = true;
        public bool TemperatureEnabled { get; set; } = false;
        public float RadarOpacity { get; set; } = 0.75f;
        public float TemperatureOpacity { get; set; } = 0.6f;

        // Configurable radar layer and WMS style
        public string RadarLayer { get; set; } = "RADAR_1KM_RRAI";
        public string? RadarWmsStyle { get; set; } = "RADARURPPRECIPR14-LINEAR";

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

            // Calculate bounding box from the current viewport (matches RadarAnimationService / Generator)
            var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);

            // Check if position/zoom changed significantly (requires new radar fetch)
            // 0.5° tolerance prevents re-fetching on small pans — radar covers viewport + margin
            bool positionChanged = Math.Abs(centerLat - _lastRadarLat) > 0.5 || Math.Abs(centerLon - _lastRadarLon) > 0.5;
            bool zoomChanged = mapZoom != _lastRadarZoom;
            bool cacheExpired = DateTime.UtcNow - _lastRadarUpdate >= _radarUpdateInterval;
            bool layerChanged = RadarLayer != _lastRadarLayer || RadarWmsStyle != _lastRadarWmsStyle;

            // Use cached data if available and parameters haven't changed significantly
            if (_radarOverlay != null && !positionChanged && !zoomChanged && !cacheExpired && !layerChanged)
            {
                // Keep bbox consistent with the cached image (don't update to new viewport)
                Console.WriteLine($"[WeatherOverlay] Using cached radar data (bbox unchanged from last fetch)");
                return _radarOverlay;
            }

            // Force invalidation: clear stale cached data so we don't return old image with new bbox
            if (positionChanged || zoomChanged || layerChanged)
            {
                _radarOverlay = null;
                Console.WriteLine($"[WeatherOverlay] Cache invalidated: posChanged={positionChanged}, zoomChanged={zoomChanged}, layerChanged={layerChanged}");
            }

            // We're doing a real fetch — update the bbox to match the image we'll receive
            LastRadarBBox = bbox;

            try
            {
                Console.WriteLine($"[WeatherOverlay] Fetching radar (composite WMS) for viewport: center=({centerLat:F2},{centerLon:F2}), size={width}x{height}, zoom={mapZoom}");
                Console.WriteLine($"[WeatherOverlay] Radar bbox (viewport): ({bbox.MinLat:F4},{bbox.MinLon:F4}) to ({bbox.MaxLat:F4},{bbox.MaxLon:F4})");

                // Use configurable layer/style for radar overlay
                var radarData = await _radarService.FetchRadarOverlayOnlyAsync(
                    (MinLat: bbox.MinLat, MinLon: bbox.MinLon, MaxLat: bbox.MaxLat, MaxLon: bbox.MaxLon),
                    width,
                    height,
                    RadarLayer,
                    RadarWmsStyle);


                if (radarData != null)
                {
                    _radarOverlay = radarData;
                    _lastRadarUpdate = DateTime.UtcNow;
                    _lastRadarLat = centerLat;
                    _lastRadarLon = centerLon;
                    _lastRadarZoom = mapZoom;
                    _lastRadarLayer = RadarLayer;
                    _lastRadarWmsStyle = RadarWmsStyle;
                    
                    // NOTE: Disk caching removed — radar overlay is held in RAM and
                    // re-fetched from WMS when expired. The old disk cache was write-only
                    // (never read back), so it wasted I/O without any benefit.
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

            // Check if position/zoom changed significantly (requires re-fetch)
            // 0.5° tolerance prevents re-fetching on small pans
            bool positionChanged = Math.Abs(centerLat - _lastTempLat) > 0.5 || Math.Abs(centerLon - _lastTempLon) > 0.5;
            bool zoomChanged = mapZoom != _lastTempZoom;
            bool cacheExpired = DateTime.UtcNow - _lastTemperatureUpdate >= _temperatureUpdateInterval;

            // Use cached data if available and parameters haven't changed significantly
            if (_temperatureOverlay != null && !positionChanged && !zoomChanged && !cacheExpired)
                return _temperatureOverlay;

            // Force invalidation: clear stale cached data so we don't return old image with new bbox
            if (positionChanged || zoomChanged)
            {
                _temperatureOverlay = null;
                Console.WriteLine($"[WeatherOverlay] Temperature cache invalidated: posChanged={positionChanged}, zoomChanged={zoomChanged}");
            }

            try
            {
                // Calculate grid bounds
                var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);
                LastTemperatureBBox = bbox; // Store for later retrieval
                
                // Generate temperature grid
                var tempData = await GenerateTemperatureGridAsync(bbox, width, height, mapZoom);
                
                if (tempData != null)
                {
                    _temperatureOverlay = tempData;
                    _lastTemperatureUpdate = DateTime.UtcNow;
                    _lastTempLat = centerLat;
                    _lastTempLon = centerLon;
                    _lastTempZoom = mapZoom;
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
        /// Gets the combined bounding box from the last radar or temperature fetch.
        /// Used by WeatherMapControl to position the overlay on the GL control.
        /// </summary>
        public (double MinLat, double MinLon, double MaxLat, double MaxLon)? LastOverlayBBox
        {
            get => LastRadarBBox ?? LastTemperatureBBox;
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
                Console.WriteLine($"[WeatherOverlay] Generating temperature grid for bbox: {bbox.MinLat:F2},{bbox.MinLon:F2} to {bbox.MaxLat:F2},{bbox.MaxLon:F2}");
                
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
                                Console.WriteLine($"[WeatherOverlay] Temperature at {lat:F2},{lon:F2}: {weatherData.Current.Temperature_2m.Value:F1}°C");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WeatherOverlay] Failed to fetch temp at {lat:F2},{lon:F2}: {ex.Message}");
                        }
                    }
                }

                Console.WriteLine($"[WeatherOverlay] Collected {tempPoints.Count} temperature points");
                
                if (tempPoints.Count == 0)
                    return null;

                // Render temperature overlay
                return RenderTemperatureOverlay(tempPoints, bbox, width, height, mapZoom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] Temperature grid generation failed: {ex.Message}");
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
            // Calculate based on viewport bounds, not radius
            // At zoom level, the world is 2^zoom * 256 pixels wide
            // Viewportextends width/2 pixels from center
            double worldPixels = 256.0 * Math.Pow(2, zoom);
            double kmPerPixel = 40075.0 / worldPixels; // Earth circumference at equator
            double pixelRadius = Math.Max(width, height) / 2.0;
            
            // Use reasonable radius based on viewport (not globe-spanning)
            double radiusKm = pixelRadius * kmPerPixel;
            
            // Clamp to reasonable values (max ~1000km for most views)
            return Math.Min(radiusKm, 1000.0);
        }

        /// <summary>Over-fetch multiplier: fetch overlays for a wider area than the viewport
        /// so the user can pan without seeing cutoff edges.</summary>
        private const double OVERLAY_OVERFETCH = 2.0;

        private (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBoundingBox(
            double centerLat, double centerLon, int zoom, int width, int height)
        {
            // Over-fetch: request 2× the viewport so panning doesn't immediately show cutoff
            double fetchWidth = width * OVERLAY_OVERFETCH;
            double fetchHeight = height * OVERLAY_OVERFETCH;

            // Web Mercator calculations for proper viewport bounds
            // At this zoom level, world is 2^zoom * 256 pixels
            double worldPixels = 256.0 * Math.Pow(2, zoom);
            
            // Convert center to pixel coordinates
            double centerPixelX = (centerLon + 180.0) / 360.0 * worldPixels;
            double latRad = centerLat * Math.PI / 180.0;
            double centerPixelY = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * worldPixels;
            
            // Calculate viewport bounds in pixels (using over-fetched dimensions)
            double minPixelX = centerPixelX - fetchWidth / 2.0;
            double maxPixelX = centerPixelX + fetchWidth / 2.0;
            double minPixelY = centerPixelY - fetchHeight / 2.0;
            double maxPixelY = centerPixelY + fetchHeight / 2.0;
            
            // Convert back to lat/lon
            double minLon = (minPixelX / worldPixels) * 360.0 - 180.0;
            double maxLon = (maxPixelX / worldPixels) * 360.0 - 180.0;
            
            // Convert Y pixels back to latitude (inverse mercator)
            double minLatRad = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * maxPixelY / worldPixels)));
            double maxLatRad = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * minPixelY / worldPixels)));
            double minLat = minLatRad * 180.0 / Math.PI;
            double maxLat = maxLatRad * 180.0 / Math.PI;

            // Cap lat span to avoid absurdly large WMS requests
            double latSpan = maxLat - minLat;
            if (latSpan > 80.0)
            {
                double mid = (minLat + maxLat) / 2.0;
                minLat = mid - 40.0;
                maxLat = mid + 40.0;
            }

            return (minLat, minLon, maxLat, maxLon);
        }
        
        /// <summary>
        /// Calculate bounding box from center point and fixed radius (simple, proven approach)
        /// </summary>
        private (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBoundingBoxFromRadius(
            double centerLat, 
            double centerLon, 
            double radiusKm)
        {
            // Approximate conversion (1 degree ≈ 111 km at equator)
            const double kmPerDegreeLat = 111.0;
            double kmPerDegreeLon = 111.0 * Math.Cos(centerLat * Math.PI / 180.0);

            double latOffset = radiusKm / kmPerDegreeLat;
            double lonOffset = radiusKm / kmPerDegreeLon;

            return (
                MinLat: centerLat - latOffset,
                MinLon: centerLon - lonOffset,
                MaxLat: centerLat + latOffset,
                MaxLon: centerLon + lonOffset
            );
        }

        // ─── Radar Animation Support ───────────────────────────────────────

        private const string ECCC_GEOMET_WMS = "https://geo.weather.gc.ca/geomet";

        /// <summary>
        /// Fetches available radar timestamps from ECCC WMS GetCapabilities.
        /// Returns ISO8601 timestamps in chronological order.
        /// </summary>
        public async Task<List<string>> FetchRadarTimestampsAsync(int numFrames = 8)
        {
            try
            {
                var capsUrl = $"{ECCC_GEOMET_WMS}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities&LAYERS={Uri.EscapeDataString(RadarLayer)}";
                Console.WriteLine($"[WeatherOverlay] Fetching radar timestamps for {RadarLayer}...");
                var xml = await _httpClient.GetStringAsync(capsUrl);
                var doc = XDocument.Parse(xml);
                var ns = doc.Root?.GetDefaultNamespace();

                if (ns == null)
                {
                    Console.WriteLine("[WeatherOverlay] Failed to parse GetCapabilities XML");
                    return GenerateFallbackTimestamps(numFrames, 6);
                }

                var dim = doc.Descendants(ns + "Dimension")
                             .FirstOrDefault(d => (string?)d.Attribute("name") == "time");

                if (dim != null)
                {
                    var content = dim.Value.Trim();

                    // Format: start/end/period (e.g., 2024-01-01T00:00:00Z/2024-01-01T12:00:00Z/PT6M)
                    if (content.Contains('/') && content.Contains("PT"))
                    {
                        var parts = content.Split('/');
                        if (parts.Length >= 3 &&
                            DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime start) &&
                            DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime end))
                        {
                            var step = ParseIso8601Period(parts[2]);

                            if (step.TotalSeconds > 0)
                            {
                                var times = new List<string>();
                                var t = end.ToUniversalTime();

                                for (int i = 0; i < numFrames; i++)
                                {
                                    times.Add(t.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                                    t = t.Subtract(step);
                                    if (t < start) break;
                                }

                                times.Reverse(); // Chronological order
                                Console.WriteLine($"[WeatherOverlay] Found {times.Count} radar timestamps");
                                return times;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] Failed to fetch timestamps: {ex.Message}");
            }

            return GenerateFallbackTimestamps(numFrames, 6);
        }

        /// <summary>
        /// Fetches multiple radar frames for animation.
        /// Returns list of transparent PNG byte arrays and their timestamps.
        /// </summary>
        public async Task<(List<byte[]> Frames, List<string> Timestamps)> FetchMultipleRadarFramesAsync(
            double centerLat, double centerLon, int width, int height, int mapZoom,
            List<string> timestamps)
        {
            var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);
            LastRadarBBox = bbox;

            var frames = new List<byte[]>();
            var validTimestamps = new List<string>();

            Console.WriteLine($"[WeatherOverlay] Fetching {timestamps.Count} radar frames for animation...");

            foreach (var time in timestamps)
            {
                try
                {
                    var data = await _radarService.FetchRadarOverlayOnlyAsync(
                        (bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon),
                        width, height, RadarLayer, RadarWmsStyle, time);

                    if (data != null && data.Length > 0)
                    {
                        frames.Add(data);
                        validTimestamps.Add(time);
                        Console.WriteLine($"[WeatherOverlay] Frame {frames.Count}/{timestamps.Count}: {time} ({data.Length} bytes)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WeatherOverlay] Failed to fetch frame for {time}: {ex.Message}");
                }
            }

            Console.WriteLine($"[WeatherOverlay] Animation frames loaded: {frames.Count}/{timestamps.Count}");
            return (frames, validTimestamps);
        }

        private List<string> GenerateFallbackTimestamps(int numFrames, int stepMinutes)
        {
            var times = new List<string>();
            var now = DateTime.UtcNow;

            for (int i = numFrames - 1; i >= 0; i--)
            {
                var t = now.Subtract(TimeSpan.FromMinutes(i * stepMinutes));
                times.Add(t.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }

            return times;
        }

        private TimeSpan ParseIso8601Period(string period)
        {
            try
            {
                if (string.IsNullOrEmpty(period) || !period.StartsWith("PT"))
                    return TimeSpan.Zero;

                var value = period.Substring(2).TrimEnd('M', 'H', 'S');

                if (period.EndsWith("M"))
                    return TimeSpan.FromMinutes(int.Parse(value));
                else if (period.EndsWith("H"))
                    return TimeSpan.FromHours(int.Parse(value));
                else if (period.EndsWith("S"))
                    return TimeSpan.FromSeconds(int.Parse(value));
            }
            catch { }

            return TimeSpan.Zero;
        }

        public void Dispose()
        {
            // Cleanup
        }
    }
}
