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
using Grib2.Models;
using OpenMap;
using OpenMeteo;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator.Rendering.Common
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
        public bool ShowTemperatureLabels { get; set; } = true;
        public float RadarOpacity { get; set; } = 0.75f;
        public float TemperatureOpacity { get; set; } = 0.6f;

        // ═══ GRIB2 forecast overlay ═══
        private byte[]? _grib2Overlay;
        private DateTime _lastGrib2Update = DateTime.MinValue;
        private double _lastGrib2Lat;
        private double _lastGrib2Lon;
        private int _lastGrib2Zoom;
        private Grib2FieldType _lastGrib2FieldType;
        private int _lastGrib2ForecastHour;
        private readonly TimeSpan _grib2UpdateInterval = TimeSpan.FromMinutes(30);

        private Grib2DataService? _grib2DataService;
        private readonly Grib2OverlayRenderer _grib2Renderer = new();

        public bool Grib2Enabled { get; set; } = false;
        public float Grib2Opacity { get; set; } = 0.6f;
        public Grib2FieldType Grib2FieldType { get; set; } = Grib2FieldType.Temperature;
        public Grib2ModelSource Grib2Model { get; set; } = Grib2ModelSource.GDPS;
        public int Grib2ForecastHour { get; set; } = 0;
        public bool Grib2ShowLabels { get => _grib2Renderer.ShowLabels; set => _grib2Renderer.ShowLabels = value; }
        public bool Grib2ShowWindBarbs { get => _grib2Renderer.ShowWindBarbs; set => _grib2Renderer.ShowWindBarbs = value; }
        public bool Grib2ShowIsobars { get => _grib2Renderer.ShowIsobars; set => _grib2Renderer.ShowIsobars = value; }
        public (double MinLat, double MinLon, double MaxLat, double MaxLon)? LastGrib2BBox { get; private set; }

        // Configurable radar layer and WMS style
        public string RadarLayer { get; set; } = "RADAR_1KM_RRAI";
        public string? RadarWmsStyle { get; set; } = "RADARURPPRECIPR14-LINEAR";

        /// <summary>
        /// Fired when an overlay fetch completes but the result is empty (all transparent).
        /// The string parameter is a user-friendly status message.
        /// </summary>
        public event Action<string>? OverlayStatusChanged;

        /// <summary>
        /// Returns the recommended WMS style for a given ECCC radar layer.
        /// Each layer has its own palette; using the wrong style produces blank results.
        /// </summary>
        public static string? GetDefaultStyleForLayer(string layer)
        {
            return layer switch
            {
                "RADAR_1KM_RRAI" => "RADARURPPRECIPR14-LINEAR",   // Rain rate
                "RADAR_1KM_RSNO" => "RADARURPPRECIPS14-LINEAR",   // Snow rate
                "RADAR_1KM_RDBR" => null,                          // Combined reflectivity (server default)
                "RADAR_COVERAGE_RRAI.INV" => null,                 // Coverage (server default)
                _ => "RADARURPPRECIPR14-LINEAR"
            };
        }

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
        /// Invalidates the cached temperature overlay so it will be regenerated on next fetch.
        /// Useful when toggling temperature labels on/off without changing position.
        /// </summary>
        public void InvalidateTemperatureCache()
        {
            _temperatureOverlay = null;
            _lastTemperatureUpdate = DateTime.MinValue;
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
                    // Detect empty overlays (all-transparent PNGs = no precipitation in area)
                    bool isEmpty = IsOverlayEmpty(radarData);
                    if (isEmpty)
                    {
                        string layerName = RadarLayer switch
                        {
                            "RADAR_1KM_RRAI" => "Rain",
                            "RADAR_1KM_RSNO" => "Snow",
                            "RADAR_1KM_RDBR" => "Rain/Snow",
                            "RADAR_COVERAGE_RRAI.INV" => "Coverage",
                            _ => "Radar"
                        };
                        Console.WriteLine($"[WeatherOverlay] {layerName} layer returned empty data — no precipitation detected in this area");
                        OverlayStatusChanged?.Invoke($"No {layerName.ToLower()} data available for this area");
                    }
                    else
                    {
                        // Precipitation data is present — clear any "no data" HUD message immediately
                        OverlayStatusChanged?.Invoke("");
                    }

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
        /// Generates a temperature grid overlay with bilinear-interpolated heatmap
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
                
                // 6×6 grid gives 36 sample points – good balance of density vs API calls
                int gridSize = 6;
                // Store results in a 2-D array for bilinear interpolation
                float?[,] tempGrid = new float?[gridSize, gridSize];
                var tempPoints = new List<(double lat, double lon, float temp, string location)>();

                double latStep = (bbox.MaxLat - bbox.MinLat) / (gridSize - 1);
                double lonStep = (bbox.MaxLon - bbox.MinLon) / (gridSize - 1);

                // Fire all API calls concurrently (throttled via SemaphoreSlim)
                var sem = new System.Threading.SemaphoreSlim(8);
                var tasks = new List<System.Threading.Tasks.Task>();

                for (int i = 0; i < gridSize; i++)
                {
                    for (int j = 0; j < gridSize; j++)
                    {
                        int ci = i, cj = j;
                        double lat = bbox.MinLat + (ci * latStep);
                        double lon = bbox.MinLon + (cj * lonStep);

                        tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                        {
                            await sem.WaitAsync();
                            try
                            {
                                var options = new WeatherForecastOptions
                                {
                                    Current = new CurrentOptions(CurrentOptionsParameter.temperature_2m),
                                    Latitude = (float)lat,
                                    Longitude = (float)lon
                                };
                                var weatherData = await _openMeteoClient.QueryAsync(options);

                                if (weatherData?.Current?.Temperature_2m != null)
                                {
                                    float t = weatherData.Current.Temperature_2m.Value;
                                    tempGrid[ci, cj] = t;
                                    lock (tempPoints)
                                        tempPoints.Add((lat, lon, t, $"{lat:F2},{lon:F2}"));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[WeatherOverlay] Failed to fetch temp at {lat:F2},{lon:F2}: {ex.Message}");
                            }
                            finally { sem.Release(); }
                        }));
                    }
                }

                await System.Threading.Tasks.Task.WhenAll(tasks);

                Console.WriteLine($"[WeatherOverlay] Collected {tempPoints.Count} temperature points");
                
                if (tempPoints.Count == 0) return null;

                // Fill any missing grid cells with nearest-neighbor
                FillMissingGridCells(tempGrid, gridSize);

                // Render bilinear-interpolated heatmap + labels
                return RenderTemperatureOverlay(tempPoints, tempGrid, gridSize, bbox, width, height, mapZoom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] Temperature grid generation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Fill null cells with nearest available value so bilinear interpolation is complete.</summary>
        private static void FillMissingGridCells(float?[,] grid, int size)
        {
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    if (grid[i, j].HasValue) continue;
                    // spiral search for nearest
                    float val = 0;
                    bool found = false;
                    for (int r = 1; r < size && !found; r++)
                    {
                        for (int di = -r; di <= r && !found; di++)
                        {
                            for (int dj = -r; dj <= r && !found; dj++)
                            {
                                int ni = i + di, nj = j + dj;
                                if (ni >= 0 && ni < size && nj >= 0 && nj < size && grid[ni, nj].HasValue)
                                {
                                    val = grid[ni, nj]!.Value;
                                    found = true;
                                }
                            }
                        }
                    }
                    grid[i, j] = val;
                }
            }
        }

        /// <summary>
        /// Renders a bilinear-interpolated temperature heatmap with pill-shaped labels
        /// </summary>
        private byte[]? RenderTemperatureOverlay(
            List<(double lat, double lon, float temp, string location)> points,
            float?[,] tempGrid,
            int gridSize,
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
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // ── Render bilinear-interpolated heatmap raster ──
                // Downsample to small raster, then stretch with bilinear filtering
                int rasterW = Math.Max(gridSize * 8, 64);
                int rasterH = Math.Max(gridSize * 8, 64);
                using var heatRaster = new Bitmap(rasterW, rasterH, PixelFormat.Format32bppArgb);
                for (int py = 0; py < rasterH; py++)
                {
                    for (int px = 0; px < rasterW; px++)
                    {
                        // Map pixel to grid coordinate (fractional)
                        float gx = (float)px / rasterW * (gridSize - 1);
                        float gy = (float)(rasterH - 1 - py) / rasterH * (gridSize - 1); // flip Y

                        // Bilinear sample
                        int x0 = Math.Min((int)gx, gridSize - 2);
                        int y0 = Math.Min((int)gy, gridSize - 2);
                        float fx = gx - x0;
                        float fy = gy - y0;

                        float t00 = tempGrid[y0, x0] ?? 0;
                        float t10 = tempGrid[y0, x0 + 1] ?? 0;
                        float t01 = tempGrid[y0 + 1, x0] ?? 0;
                        float t11 = tempGrid[y0 + 1, x0 + 1] ?? 0;

                        float temp = t00 * (1 - fx) * (1 - fy) +
                                     t10 * fx * (1 - fy) +
                                     t01 * (1 - fx) * fy +
                                     t11 * fx * fy;

                        var col = GetTemperatureColor(temp);
                        heatRaster.SetPixel(px, py, Color.FromArgb(100, col));
                    }
                }

                // Draw heatmap stretched to full viewport
                g.DrawImage(heatRaster, 0, 0, width, height);

                // ── Draw temperature labels with pill badge ──
                if (ShowTemperatureLabels)
                {
                using var font = new Font("Segoe UI", 12, FontStyle.Bold);
                using var smallFont = new Font("Segoe UI", 8);

                foreach (var pt in points)
                {
                    var pixelPos = LatLonToPixel(pt.lat, pt.lon, bbox, width, height);
                    if (pixelPos.x < -20 || pixelPos.x > width + 20 || pixelPos.y < -20 || pixelPos.y > height + 20) continue;

                    string tempText = $"{pt.temp:F1}°";
                    var textSize = g.MeasureString(tempText, font);

                    float badgeW = textSize.Width + 14;
                    float badgeH = textSize.Height + 6;
                    float bx = pixelPos.x - badgeW / 2;
                    float by = pixelPos.y - badgeH / 2;

                    // Pill background
                    var badgeColor = GetTemperatureColor(pt.temp);
                    using var badgePath = CreateRoundRectPath(bx, by, badgeW, badgeH, badgeH / 2);
                    using var bgBrush = new SolidBrush(Color.FromArgb(200, badgeColor));
                    using var borderPen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f);
                    g.FillPath(bgBrush, badgePath);
                    g.DrawPath(borderPen, badgePath);

                    // Temperature text (white with shadow)
                    float tx = bx + 7;
                    float ty = by + 3;
                    g.DrawString(tempText, font, Brushes.Black, tx + 1, ty + 1);
                    g.DrawString(tempText, font, Brushes.White, tx, ty);
                }
                } // end if ShowTemperatureLabels

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Creates a rounded rectangle GraphicsPath.</summary>
        private static GraphicsPath CreateRoundRectPath(float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w, h) / 2);
            var path = new GraphicsPath();
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
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
        /// Gets color for temperature using smooth linear interpolation across a 10-stop palette.
        /// </summary>
        private Color GetTemperatureColor(float tempC)
        {
            // (temperature °C, R, G, B)
            (float t, int r, int g, int b)[] stops =
            {
                (-40, 40,   0, 100),   // deep purple
                (-30,  0,   0, 180),   // dark blue
                (-20,  0,  80, 220),   // blue
                (-10, 60, 160, 240),   // sky blue
                (  0,130, 210, 255),   // light cyan
                ( 10,120, 220, 120),   // green
                ( 20,240, 230,  50),   // yellow
                ( 30,255, 160,  20),   // orange
                ( 40,220,  30,  20),   // red
                ( 50,140,   0,  50),   // dark crimson
            };

            if (tempC <= stops[0].t) return Color.FromArgb(stops[0].r, stops[0].g, stops[0].b);
            if (tempC >= stops[^1].t) return Color.FromArgb(stops[^1].r, stops[^1].g, stops[^1].b);

            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (tempC >= stops[i].t && tempC <= stops[i + 1].t)
                {
                    float f = (tempC - stops[i].t) / (stops[i + 1].t - stops[i].t);
                    int cr = (int)(stops[i].r + (stops[i + 1].r - stops[i].r) * f);
                    int cg = (int)(stops[i].g + (stops[i + 1].g - stops[i].g) * f);
                    int cb = (int)(stops[i].b + (stops[i + 1].b - stops[i].b) * f);
                    return Color.FromArgb(
                        Math.Clamp(cr, 0, 255),
                        Math.Clamp(cg, 0, 255),
                        Math.Clamp(cb, 0, 255));
                }
            }
            return Color.Gray;
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
                        // Validate that the data is a decodable image before adding
                        try
                        {
                            using var ms = new MemoryStream(data);
                            using var testBmp = new Bitmap(ms);
                            _ = testBmp.Width; // Force header decode
                        }
                        catch
                        {
                            Console.WriteLine($"[WeatherOverlay] Frame for {time} contains invalid image data ({data.Length} bytes), skipping");
                            continue;
                        }

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

        /// <summary>
        /// Fetches GRIB2 forecast data from ECCC Datamart and renders it as a positioned overlay.
        /// </summary>
        public async Task<byte[]?> UpdateGrib2OverlayAsync(
            double centerLat,
            double centerLon,
            int width,
            int height,
            int mapZoom)
        {
            if (!Grib2Enabled)
                return null;

            bool positionChanged = Math.Abs(centerLat - _lastGrib2Lat) > 0.5 || Math.Abs(centerLon - _lastGrib2Lon) > 0.5;
            bool zoomChanged = mapZoom != _lastGrib2Zoom;
            bool fieldChanged = Grib2FieldType != _lastGrib2FieldType;
            bool hourChanged = Grib2ForecastHour != _lastGrib2ForecastHour;
            bool cacheExpired = DateTime.UtcNow - _lastGrib2Update >= _grib2UpdateInterval;

            if (_grib2Overlay != null && !positionChanged && !zoomChanged && !fieldChanged && !hourChanged && !cacheExpired)
                return _grib2Overlay;

            if (positionChanged || zoomChanged || fieldChanged || hourChanged)
            {
                _grib2Overlay = null;
                Console.WriteLine($"[WeatherOverlay] GRIB2 cache invalidated: pos={positionChanged}, zoom={zoomChanged}, field={fieldChanged}, hour={hourChanged}");
            }

            try
            {
                // Lazy-init the data service on first use
                if (_grib2DataService == null)
                {
                    var cacheDir = Path.Combine(
                        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                        "MapCache", "Grib2");
                    _grib2DataService = new Grib2DataService(cacheDir);
                }

                await _grib2DataService.SetModelAsync(Grib2Model);

                // Clamp forecast hour to model's maximum
                int forecastHour = Math.Min(Grib2ForecastHour, _grib2DataService.MaxForecastHours);

                var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);
                LastGrib2BBox = bbox;

                byte[]? overlayPng = null;

                if (Grib2FieldType == Models.Grib2FieldType.Wind)
                {
                    // Wind needs U + V components
                    var (uField, vField) = await _grib2DataService.FetchWindComponentsAsync(forecastHour);
                    if (uField != null && vField != null)
                    {
                        LogGrib2Diagnostics("Wind-U", uField);
                        LogGrib2Diagnostics("Wind-V", vField);
                        overlayPng = _grib2Renderer.RenderWindOverlay(uField, vField, bbox, width, height);
                    }
                }
                else
                {
                    var field = await _grib2DataService.FetchFieldAsync(Grib2FieldType, forecastHour);
                    if (field != null)
                    {
                        LogGrib2Diagnostics(Grib2FieldType.ToString(), field);
                        overlayPng = _grib2Renderer.RenderOverlay(field, Grib2FieldType, bbox, width, height);
                    }
                }

                if (overlayPng != null && !IsOverlayEmpty(overlayPng))
                {
                    _grib2Overlay = overlayPng;
                    _lastGrib2Update = DateTime.UtcNow;
                    _lastGrib2Lat = centerLat;
                    _lastGrib2Lon = centerLon;
                    _lastGrib2Zoom = mapZoom;
                    _lastGrib2FieldType = Grib2FieldType;
                    _lastGrib2ForecastHour = Grib2ForecastHour;
                    OverlayStatusChanged?.Invoke($"GRIB2 {Grib2FieldType} +{Grib2ForecastHour}h loaded");
                }

                return _grib2Overlay;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] GRIB2 update error: {ex.Message}");
                return _grib2Overlay;
            }
        }

        /// <summary>
        /// Invalidates the GRIB2 overlay cache, forcing a re-fetch on the next update.
        /// </summary>
        public void InvalidateGrib2Cache()
        {
            _grib2Overlay = null;
            _lastGrib2Update = DateTime.MinValue;
        }

        /// <summary>
        /// Fetches GRIB2 data and produces a GPU-ready render package (raw float grid + palette).
        /// Used by the GPU shader pipeline instead of the CPU-rendered PNG path.
        /// </summary>
        public async Task<Grib2GpuRenderData?> GetGrib2GpuDataAsync(
            double centerLat,
            double centerLon,
            int width,
            int height,
            int mapZoom)
        {
            if (!Grib2Enabled)
                return null;

            try
            {
                if (_grib2DataService == null)
                {
                    var cacheDir = Path.Combine(
                        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                        "MapCache", "Grib2");
                    _grib2DataService = new Grib2DataService(cacheDir);
                }

                await _grib2DataService.SetModelAsync(Grib2Model);
                int forecastHour = Math.Min(Grib2ForecastHour, _grib2DataService.MaxForecastHours);
                var bbox = CalculateBoundingBox(centerLat, centerLon, mapZoom, width, height);
                LastGrib2BBox = bbox;

                var palette = new Grib2ShaderPalette();
                var uploader = new Grib2TextureUploader(palette);
                string fieldName = Grib2FieldType.ToString();
                var paletteData = palette.GetPalette(fieldName);
                var (dataMin, dataMax) = Grib2ShaderPalette.GetNormalizationRange(fieldName);

                if (Grib2FieldType == Models.Grib2FieldType.Wind)
                {
                    var (uField, vField) = await _grib2DataService.FetchWindComponentsAsync(forecastHour);
                    if (uField == null || vField == null) return null;

                    var windData = uploader.PrepareWindUpload(uField, vField);
                    if (windData == null) return null;

                    return new Grib2GpuRenderData
                    {
                        GridData = windData.SpeedData,
                        GridWidth = windData.GridWidth,
                        GridHeight = windData.GridHeight,
                        PaletteData = paletteData,
                        DataMin = dataMin,
                        DataMax = dataMax,
                        FieldType = Grib2FieldType,
                        MinLat = bbox.MinLat,
                        MinLon = bbox.MinLon,
                        MaxLat = bbox.MaxLat,
                        MaxLon = bbox.MaxLon,
                        WindU = windData.UComponentData,
                        WindV = windData.VComponentData,
                        Opacity = Grib2Opacity,
                        EnableGlow = true
                    };
                }
                else
                {
                    var field = await _grib2DataService.FetchFieldAsync(Grib2FieldType, forecastHour);
                    if (field == null) return null;

                    var gpuData = uploader.PrepareForUpload(field, Grib2FieldType);
                    if (gpuData == null) return null;

                    return new Grib2GpuRenderData
                    {
                        GridData = gpuData.GridData,
                        GridWidth = gpuData.GridWidth,
                        GridHeight = gpuData.GridHeight,
                        PaletteData = paletteData,
                        DataMin = gpuData.DataMin,
                        DataMax = gpuData.DataMax,
                        FieldType = Grib2FieldType,
                        MinLat = bbox.MinLat,
                        MinLon = bbox.MinLon,
                        MaxLat = bbox.MaxLat,
                        MaxLon = bbox.MaxLon,
                        Opacity = Grib2Opacity,
                        EnableGlow = true
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherOverlay] GRIB2 GPU data error: {ex.Message}");
                return null;
            }
        }

        private static void LogGrib2Diagnostics(string label, Grib2.Models.Grib2Message msg)
        {
            var grid = msg.Grid;
            var field = msg.Field;
            var vals = field?.Values;
            Console.WriteLine($"[GRIB2 Diag] === {label} ===");
            Console.WriteLine($"  Grid: Template={grid?.TemplateNumber}, {grid?.Ni}x{grid?.Nj}, " +
                $"FirstLat={grid?.FirstLatitude:F4}, FirstLon={grid?.FirstLongitude:F4}, " +
                $"LastLat={grid?.LastLatitude:F4}, LastLon={grid?.LastLongitude:F4}, " +
                $"Di={grid?.DiDegrees:F6}, Dj={grid?.DjDegrees:F6}, ScanMode=0x{grid?.ScanningMode:X2}");
            if (grid?.RotatedPoleLat.HasValue == true)
                Console.WriteLine($"  Rotated: PoleLat={grid.RotatedPoleLat:F4}, PoleLon={grid.RotatedPoleLon:F4}, Angle={grid.RotationAngle:F4}");
            Console.WriteLine($"  Field: PackingTemplate={field?.PackingTemplateNumber}, BitsPerValue={field?.BitsPerValue}, " +
                $"R={field?.ReferenceValue}, E={field?.BinaryScaleFactor}, D={field?.DecimalScaleFactor}");
            if (vals != null && vals.Length > 0)
            {
                float min = float.MaxValue, max = float.MinValue;
                int nanCount = 0;
                double sum = 0;
                int validCount = 0;
                for (int i = 0; i < vals.Length; i++)
                {
                    if (float.IsNaN(vals[i])) { nanCount++; continue; }
                    if (vals[i] < min) min = vals[i];
                    if (vals[i] > max) max = vals[i];
                    sum += vals[i];
                    validCount++;
                }
                Console.WriteLine($"  Data: Length={vals.Length}, Valid={validCount}, NaN={nanCount}, " +
                    $"Min={min:F4}, Max={max:F4}, Avg={(validCount > 0 ? sum / validCount : 0):F4}");
                Console.WriteLine($"  Sample[0..4]: {string.Join(", ", vals.Take(5).Select(v => v.ToString("F4")))}");
            }
            else
            {
                Console.WriteLine($"  Data: EMPTY or null");
            }
        }

        public void Dispose()
        {
            _grib2DataService?.Dispose();
        }

        /// <summary>
        /// Checks if an overlay image is effectively empty (all pixels fully transparent).
        /// Samples a grid of pixels for performance — avoids scanning every pixel.
        /// </summary>
        private static bool IsOverlayEmpty(byte[] pngData)
        {
            try
            {
                using var ms = new MemoryStream(pngData);
                using var bmp = new Bitmap(ms);
                if (!Image.IsAlphaPixelFormat(bmp.PixelFormat)) return false;

                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bits.Stride;
                    int stepX = Math.Max(1, bmp.Width / 32);  // sample ~32x32 grid
                    int stepY = Math.Max(1, bmp.Height / 32);
                    for (int y = 0; y < bmp.Height; y += stepY)
                    {
                        for (int x = 0; x < bmp.Width; x += stepX)
                        {
                            int idx = y * stride + x * 4 + 3; // alpha byte (BGRA)
                            if (idx < stride * bmp.Height)
                            {
                                byte alpha = System.Runtime.InteropServices.Marshal.ReadByte(bits.Scan0, idx);
                                if (alpha > 10) return false; // non-transparent pixel found
                            }
                        }
                    }
                    return true; // all sampled pixels are transparent
                }
                finally { bmp.UnlockBits(bits); }
            }
            catch { return false; }
        }
    }
}
