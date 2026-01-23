using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ECCC.Models;
using OpenMap;

namespace ECCC.Services
{
    /// <summary>
    /// Service for fetching and compositing radar images with base maps.
    /// Retrieves ECCC radar overlay and combines it with OpenStreetMap base layers.
    /// </summary>
    public class RadarImageService
    {
        private readonly HttpClient _httpClient;
        private readonly MapOverlayService? _mapService;
        private const string ECCC_GEOMET_WMS = "https://geo.weather.gc.ca/geomet";
        private const int DEFAULT_RADIUS_KM = 200;

        public RadarImageService(HttpClient httpClient, MapOverlayService? mapService = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _mapService = mapService;
        }

        /// <summary>
        /// Fetches a composite radar image for the specified location.
        /// Combines a base map with the ECCC radar overlay.
        /// </summary>
        /// <param name="lat">Latitude of the center point</param>
        /// <param name="lon">Longitude of the center point</param>
        /// <param name="width">Image width in pixels (default: 600)</param>
        /// <param name="height">Image height in pixels (default: 600)</param>
        /// <param name="radiusKm">Radius around center point in km (default: 200)</param>
        /// <returns>PNG image data as byte array, or null on failure</returns>
        public async Task<byte[]?> FetchRadarImageAsync(
            double lat, 
            double lon, 
            int width = 600, 
            int height = 600,
            double radiusKm = DEFAULT_RADIUS_KM)
        {
            try
            {
                Console.WriteLine($"[RadarImageService] Fetching radar for location: {lat}, {lon}");
                
                // Calculate bounding box based on radius
                var bbox = CalculateBoundingBox(lat, lon, radiusKm);
                Console.WriteLine($"[RadarImageService] Bounding box: {bbox}");

                // Fetch base map and radar overlay in parallel
                var baseMapTask = FetchBaseMapAsync(bbox, width, height);
                var radarTask = FetchRadarOverlayAsync(bbox, width, height);

                await Task.WhenAll(baseMapTask, radarTask);

                var baseMapData = await baseMapTask;
                var radarData = await radarTask;

                if (radarData == null)
                {
                    Console.WriteLine("[RadarImageService] Failed to fetch radar data");
                    return null;
                }

                // If we have a base map, composite them together
                if (baseMapData != null && baseMapData.Length > 0)
                {
                    Console.WriteLine("[RadarImageService] Compositing base map with radar overlay");
                    return CompositeImages(baseMapData, radarData, width, height);
                }

                // Return radar only if no base map
                Console.WriteLine("[RadarImageService] Returning radar overlay only");
                return radarData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RadarImageService] Error fetching radar image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetches the base map layer using OpenMap service or fallback.
        /// </summary>
        private async Task<byte[]?> FetchBaseMapAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height)
        {
            // Try OpenMap service first if available
            if (_mapService != null)
            {
                try
                {
                    Console.WriteLine("[RadarImageService] Using OpenMap service for base map");
                    
                    // Calculate center point from bounding box
                    double centerLat = (bbox.MinLat + bbox.MaxLat) / 2;
                    double centerLon = (bbox.MinLon + bbox.MaxLon) / 2;
                    
                    // Calculate appropriate zoom level based on bbox size
                    double latSpan = bbox.MaxLat - bbox.MinLat;
                    int zoomLevel = CalculateZoomLevel(latSpan);
                    
                    Console.WriteLine($"[RadarImageService] Generating map: center=({centerLat:F4}, {centerLon:F4}), zoom={zoomLevel}");
                    
                    using var mapBitmap = await _mapService.GenerateMapBackgroundAsync(
                        centerLat, 
                        centerLon, 
                        zoomLevel, 
                        width, 
                        height);
                    
                    if (mapBitmap != null)
                    {
                        // Convert Bitmap to byte array
                        using var ms = new MemoryStream();
                        mapBitmap.Save(ms, ImageFormat.Png);
                        var mapData = ms.ToArray();
                        
                        Console.WriteLine($"[RadarImageService] OpenMap base map generated: {mapData.Length} bytes");
                        return mapData;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RadarImageService] OpenMap service failed: {ex.Message}");
                }
            }

            // Fallback to detailed background if OpenMap unavailable
            Console.WriteLine("[RadarImageService] Using fallback background");
            return CreateDetailedBackground(width, height, bbox);
        }

        /// <summary>
        /// Fetches the ECCC radar overlay (transparent).
        /// </summary>
        private async Task<byte[]?> FetchRadarOverlayAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height)
        {
            try
            {
                var radarUrl = BuildRadarUrl(bbox, width, height);
                Console.WriteLine($"[RadarImageService] Fetching radar overlay...");
                
                var response = await _httpClient.GetAsync(radarUrl);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadAsByteArrayAsync();
                    Console.WriteLine($"[RadarImageService] Radar overlay fetched: {data?.Length ?? 0} bytes");
                    return data;
                }

                Console.WriteLine($"[RadarImageService] Radar fetch failed: {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RadarImageService] Error fetching radar overlay: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Composites the base map and radar overlay into a single image.
        /// </summary>
        private byte[]? CompositeImages(byte[] baseMapData, byte[] radarData, int width, int height)
        {
            try
            {
                using (var baseStream = new MemoryStream(baseMapData))
                using (var radarStream = new MemoryStream(radarData))
                using (var baseImage = Image.FromStream(baseStream))
                using (var radarImage = Image.FromStream(radarStream))
                using (var result = new Bitmap(width, height))
                using (var graphics = Graphics.FromImage(result))
                using (var ms = new MemoryStream())
                {
                    // Set high quality rendering
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;

                    // Draw base map first
                    graphics.DrawImage(baseImage, 0, 0, width, height);
                    
                    // Draw radar overlay on top (transparency preserved)
                    graphics.DrawImage(radarImage, 0, 0, width, height);

                    // Save composite image
                    result.Save(ms, ImageFormat.Png);
                    Console.WriteLine($"[RadarImageService] Composite image created: {ms.Length} bytes");
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RadarImageService] Error compositing images: {ex.Message}");
                // Return radar only if composite fails
                return radarData;
            }
        }

        /// <summary>
        /// Creates a detailed background map with terrain-like appearance when external services fail.
        /// </summary>
        private byte[]? CreateDetailedBackground(int width, int height, (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox)
        {
            try
            {
                using (var bitmap = new Bitmap(width, height))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var ms = new MemoryStream())
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    
                    // Create a terrain-like gradient background
                    using (var brush = new LinearGradientBrush(
                        new Rectangle(0, 0, width, height),
                        Color.FromArgb(255, 220, 235, 220), // Light greenish
                        Color.FromArgb(255, 245, 245, 220), // Light tan
                        45f))
                    {
                        graphics.FillRectangle(brush, 0, 0, width, height);
                    }

                    // Add coordinate grid with labels
                    using (var gridPen = new Pen(Color.FromArgb(80, 100, 100, 100), 1))
                    using (var majorGridPen = new Pen(Color.FromArgb(120, 80, 80, 80), 2))
                    using (var font = new Font("Arial", 8))
                    using (var textBrush = new SolidBrush(Color.FromArgb(200, 60, 60, 60)))
                    {
                        int gridSpacing = 50;
                        int majorGridInterval = 5;
                        
                        // Draw vertical lines (longitude)
                        for (int i = 0; i <= width / gridSpacing; i++)
                        {
                            int x = i * gridSpacing;
                            var pen = (i % majorGridInterval == 0) ? majorGridPen : gridPen;
                            graphics.DrawLine(pen, x, 0, x, height);
                            
                            // Add longitude labels on major grid lines
                            if (i % majorGridInterval == 0 && x > 10 && x < width - 10)
                            {
                                double lon = bbox.MinLon + (bbox.MaxLon - bbox.MinLon) * i / (width / (double)gridSpacing);
                                graphics.DrawString($"{lon:F2}°", font, textBrush, x + 2, 5);
                            }
                        }
                        
                        // Draw horizontal lines (latitude)
                        for (int i = 0; i <= height / gridSpacing; i++)
                        {
                            int y = i * gridSpacing;
                            var pen = (i % majorGridInterval == 0) ? majorGridPen : gridPen;
                            graphics.DrawLine(pen, 0, y, width, y);
                            
                            // Add latitude labels on major grid lines
                            if (i % majorGridInterval == 0 && y > 10 && y < height - 20)
                            {
                                double lat = bbox.MaxLat - (bbox.MaxLat - bbox.MinLat) * i / (height / (double)gridSpacing);
                                graphics.DrawString($"{lat:F2}°", font, textBrush, 5, y + 2);
                            }
                        }
                    }

                    // Add center crosshair
                    using (var crosshairPen = new Pen(Color.FromArgb(150, 255, 0, 0), 2))
                    {
                        int centerX = width / 2;
                        int centerY = height / 2;
                        int crossSize = 20;
                        graphics.DrawLine(crosshairPen, centerX - crossSize, centerY, centerX + crossSize, centerY);
                        graphics.DrawLine(crosshairPen, centerX, centerY - crossSize, centerX, centerY + crossSize);
                    }

                    // Add coordinate grid info and attribution
                    using (var font = new Font("Arial", 9, FontStyle.Regular))
                    using (var textBrush = new SolidBrush(Color.FromArgb(200, 80, 80, 80)))
                    using (var bgBrush = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                    {
                        // Main message
                        string text = "Coordinate Grid Only - Map Integration Available";
                        var textSize = graphics.MeasureString(text, font);
                        float x = (width - textSize.Width) / 2;
                        float y = height - 30;
                        
                        // Draw semi-transparent background
                        graphics.FillRectangle(bgBrush, x - 5, y - 2, textSize.Width + 10, textSize.Height + 4);
                        
                        // Draw text
                        graphics.DrawString(text, font, textBrush, x, y);
                        
                        // Add attribution for radar data
                        string attrText = "Radar data: Environment and Climate Change Canada";
                        graphics.DrawString(attrText, font, textBrush, 10, height - 25);
                    }

                    bitmap.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RadarImageService] Error creating background: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the ECCC GeoMet WMS URL for radar data.
        /// </summary>
        private string BuildRadarUrl(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height)
        {
            return $"{ECCC_GEOMET_WMS}?" +
                   $"SERVICE=WMS&" +
                   $"VERSION=1.3.0&" +
                   $"REQUEST=GetMap&" +
                   $"LAYERS=RADAR_1KM_RRAI&" +
                   $"CRS=EPSG:4326&" +
                   $"BBOX={bbox.MinLat},{bbox.MinLon},{bbox.MaxLat},{bbox.MaxLon}&" +
                   $"WIDTH={width}&" +
                   $"HEIGHT={height}&" +
                   $"FORMAT=image/png&" +
                   $"TRANSPARENT=TRUE&" +
                   $"STYLES=RADARURPPRECIPR14-LINEAR";
        }

        /// <summary>
        /// Calculates appropriate zoom level based on latitude span.
        /// </summary>
        private int CalculateZoomLevel(double latSpan)
        {
            // Approximate zoom level calculation
            // Larger spans need lower zoom (more zoomed out)
            if (latSpan > 10) return 6;
            if (latSpan > 5) return 7;
            if (latSpan > 2.5) return 8;
            if (latSpan > 1.25) return 9;
            if (latSpan > 0.6) return 10;
            if (latSpan > 0.3) return 11;
            return 12;
        }

        /// <summary>
        /// Calculates a bounding box around a center point given a radius in kilometers.
        /// </summary>
        private (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBoundingBox(
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
    }
}
