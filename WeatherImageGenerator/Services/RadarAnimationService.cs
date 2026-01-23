#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenMap;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Service for fetching ECCC radar animation frames and overlaying them on OpenStreetMap
    /// </summary>
    public class RadarAnimationService
    {
        private readonly HttpClient _httpClient;
        private readonly MapOverlayService _mapService;
        private const string ECCC_GEOMET_WMS = "https://geo.weather.gc.ca/geomet";
        private const string DEFAULT_RADAR_LAYER = "RADAR_1KM_RRAI";

        public RadarAnimationService(HttpClient httpClient, int width = 1920, int height = 1080)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            
            // Load OpenMap configuration
            var config = ConfigManager.LoadConfig();
            var openMapSettings = ConvertToOpenMapSettings(config.OpenMap);
            
            _mapService = new MapOverlayService(width, height, openMapSettings);
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
                CacheDurationHours = configSettings.CacheDurationHours,
                UseDarkMode = configSettings.UseDarkMode
            };
        }

        /// <summary>
        /// Fetches radar animation frames from ECCC and overlays them on an OpenStreetMap background
        /// </summary>
        /// <param name="centerLat">Center latitude</param>
        /// <param name="centerLon">Center longitude</param>
        /// <param name="outputDir">Output directory for frames</param>
        /// <param name="numFrames">Number of animation frames to fetch (default: 8)</param>
        /// <param name="frameStepMinutes">Minutes between each frame (default: 6)</param>
        /// <param name="width">Frame width (default: 1920)</param>
        /// <param name="height">Frame height (default: 1080)</param>
        /// <param name="radarLayer">ECCC radar layer to use (default: RADAR_1KM_RRAI)</param>
        /// <param name="zoomLevel">Map zoom level (default: 8)</param>
        /// <returns>List of generated frame file paths</returns>
        public async Task<List<string>> GenerateRadarAnimationWithMapAsync(
            double centerLat,
            double centerLon,
            string outputDir,
            int numFrames = 8,
            int frameStepMinutes = 6,
            int width = 1920,
            int height = 1080,
            string radarLayer = DEFAULT_RADAR_LAYER,
            int zoomLevel = 8)
        {
            Logger.Log($"[RadarAnimation] Generating radar animation with {numFrames} frames", ConsoleColor.Cyan);
            Logger.Log($"[RadarAnimation] Center: {centerLat}, {centerLon} | Zoom: {zoomLevel}", ConsoleColor.Cyan);

            // Get available radar times from ECCC WMS GetCapabilities
            var times = await FetchRadarTimestampsAsync(radarLayer, numFrames, frameStepMinutes);
            if (times.Count == 0)
            {
                Logger.Log("[RadarAnimation] No radar timestamps available", ConsoleColor.Yellow);
                return new List<string>();
            }

            Logger.Log($"[RadarAnimation] Found {times.Count} radar timestamps", ConsoleColor.Green);

            // Calculate bounding box
            var bbox = CalculateBoundingBox(centerLat, centerLon, zoomLevel, width, height);

            // Generate base map once (reuse for all frames)
            Logger.Log("[RadarAnimation] Generating base map...", ConsoleColor.Cyan);
            Bitmap? baseMap = null;
            try
            {
                baseMap = await _mapService.GenerateMapBackgroundAsync(
                    centerLat, centerLon, zoomLevel, width, height);
                Logger.Log("✓ Base map generated", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ Failed to generate base map: {ex.Message}", ConsoleColor.Red);
                return new List<string>();
            }

            var frameFiles = new List<string>();

            // Generate radar frames as individual numbered images (00, 01, 02, etc.)
            // Each frame becomes a separate image in the video sequence
            for (int i = 0; i < times.Count; i++)
            {
                try
                {
                    Logger.Log($"[RadarAnimation] Processing frame {i + 1}/{times.Count}...", ConsoleColor.Cyan);

                    // Fetch radar overlay for this timestamp
                    var radarData = await FetchRadarOverlayAsync(bbox, width, height, radarLayer, times[i]);
                    if (radarData == null || radarData.Length == 0)
                    {
                        Logger.Log($"  ⚠ No radar data for frame {i + 1}, skipping", ConsoleColor.Yellow);
                        continue;
                    }

                    // Convert radar data to bitmap
                    using var radarStream = new MemoryStream(radarData);
                    using var radarImage = new Bitmap(radarStream);

                    // Composite map + radar
                    var compositeImage = CompositeMapWithRadar(baseMap, radarImage, width, height);

                    // Save each frame as a numbered image (00_RadarFrame.png, 01_RadarFrame.png, etc.)
                    // These will be treated as regular images in the video with their own duration
                    var framePath = Path.Combine(outputDir, $"{i:D2}_RadarFrame.png");
                    compositeImage.Save(framePath, System.Drawing.Imaging.ImageFormat.Png);
                    compositeImage.Dispose();
                    frameFiles.Add(framePath);

                    Logger.Log($"  ✓ Frame {i + 1} saved: {Path.GetFileName(framePath)}", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    Logger.Log($"  ✗ Failed to process frame {i + 1}: {ex.Message}", ConsoleColor.Red);
                }
            }

            baseMap?.Dispose();

            Logger.Log($"✓ Radar animation complete: {frameFiles.Count} frames generated", ConsoleColor.Green);
            return frameFiles;
        }

        /// <summary>
        /// Fetches radar timestamps from ECCC WMS GetCapabilities
        /// </summary>
        private async Task<List<string>> FetchRadarTimestampsAsync(string layer, int numFrames, int frameStepMinutes)
        {
            try
            {
                var capsUrl = $"{ECCC_GEOMET_WMS}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities&LAYERS={Uri.EscapeDataString(layer)}";
                var xml = await _httpClient.GetStringAsync(capsUrl);
                var doc = XDocument.Parse(xml);
                var ns = doc.Root?.GetDefaultNamespace();

                if (ns == null)
                {
                    Logger.Log("[RadarAnimation] Failed to parse GetCapabilities XML", ConsoleColor.Yellow);
                    return GenerateFallbackTimestamps(numFrames, frameStepMinutes);
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
                            DateTime.TryParse(parts[0], out DateTime start) &&
                            DateTime.TryParse(parts[1], out DateTime end))
                        {
                            var period = parts[2];
                            var step = ParseIso8601Period(period);
                            
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
                                return times;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[RadarAnimation] Failed to fetch timestamps: {ex.Message}", ConsoleColor.Yellow);
            }

            // Fallback: generate timestamps manually
            return GenerateFallbackTimestamps(numFrames, frameStepMinutes);
        }

        /// <summary>
        /// Generates fallback timestamps when WMS GetCapabilities fails
        /// </summary>
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

        /// <summary>
        /// Parses ISO8601 duration (e.g., PT6M = 6 minutes)
        /// </summary>
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
            catch
            {
                // Ignore parse errors
            }
            
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Fetches radar overlay from ECCC WMS
        /// </summary>
        private async Task<byte[]?> FetchRadarOverlayAsync(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height,
            string layer,
            string time)
        {
            try
            {
                var url = BuildRadarWmsUrl(bbox, width, height, layer, time);
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    Logger.Log($"[RadarAnimation] Radar fetch failed: {response.StatusCode}", ConsoleColor.Yellow);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[RadarAnimation] Radar fetch error: {ex.Message}", ConsoleColor.Yellow);
            }
            
            return null;
        }

        /// <summary>
        /// Builds ECCC WMS URL for radar overlay
        /// </summary>
        private string BuildRadarWmsUrl(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height,
            string layer,
            string time)
        {
            // WMS 1.3.0 uses lat,lon order for EPSG:4326
            var bboxStr = $"{bbox.MinLat},{bbox.MinLon},{bbox.MaxLat},{bbox.MaxLon}";
            
            return $"{ECCC_GEOMET_WMS}?" +
                   $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap" +
                   $"&LAYERS={Uri.EscapeDataString(layer)}" +
                   $"&CRS=EPSG:4326" +
                   $"&BBOX={bboxStr}" +
                   $"&WIDTH={width}&HEIGHT={height}" +
                   $"&FORMAT=image/png" +
                   $"&TRANSPARENT=TRUE" +
                   $"&TIME={Uri.EscapeDataString(time)}";
        }

        /// <summary>
        /// Calculates bounding box based on center point and zoom level
        /// </summary>
        private (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBoundingBox(
            double centerLat,
            double centerLon,
            int zoomLevel,
            int width,
            int height)
        {
            // Calculate degrees per pixel at this zoom level
            double degreesPerPixel = 360.0 / (256.0 * Math.Pow(2, zoomLevel));
            
            // Calculate extent in degrees
            double latExtent = height * degreesPerPixel / 2.0;
            double lonExtent = width * degreesPerPixel / 2.0;
            
            return (
                MinLat: centerLat - latExtent,
                MinLon: centerLon - lonExtent,
                MaxLat: centerLat + latExtent,
                MaxLon: centerLon + lonExtent
            );
        }

        /// <summary>
        /// Composites map background with radar overlay
        /// </summary>
        private Bitmap CompositeMapWithRadar(Bitmap mapBackground, Bitmap radarOverlay, int width, int height)
        {
            var result = new Bitmap(width, height);
            
            using (var g = Graphics.FromImage(result))
            {
                // Enable high quality rendering
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // Draw map background
                g.DrawImage(mapBackground, 0, 0, width, height);

                // Draw radar overlay with transparency
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix
                {
                    Matrix33 = 0.75f // 75% opacity for radar overlay
                };
                var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
                imageAttributes.SetColorMatrix(colorMatrix);

                g.DrawImage(radarOverlay,
                    new Rectangle(0, 0, width, height),
                    0, 0, radarOverlay.Width, radarOverlay.Height,
                    GraphicsUnit.Pixel,
                    imageAttributes);
            }
            
            return result;
        }

        /// <summary>
        /// Generates a single static radar map with OpenMap overlay
        /// </summary>
        public async Task<string?> GenerateSingleRadarMapAsync(
            double centerLat,
            double centerLon,
            string outputPath,
            int width = 1920,
            int height = 1080,
            string radarLayer = DEFAULT_RADAR_LAYER,
            int zoomLevel = 8)
        {
            try
            {
                Logger.Log($"[RadarAnimation] Generating single radar map", ConsoleColor.Cyan);

                // Generate base map
                var baseMap = await _mapService.GenerateMapBackgroundAsync(
                    centerLat, centerLon, zoomLevel, width, height);

                // Calculate bounding box
                var bbox = CalculateBoundingBox(centerLat, centerLon, zoomLevel, width, height);

                // Fetch latest radar overlay
                var times = await FetchRadarTimestampsAsync(radarLayer, 1, 6);
                if (times.Count == 0)
                {
                    Logger.Log("[RadarAnimation] No radar timestamps available", ConsoleColor.Yellow);
                    baseMap.Dispose();
                    return null;
                }

                var radarData = await FetchRadarOverlayAsync(bbox, width, height, radarLayer, times[0]);
                if (radarData == null || radarData.Length == 0)
                {
                    Logger.Log("[RadarAnimation] No radar data available", ConsoleColor.Yellow);
                    baseMap.Dispose();
                    return null;
                }

                // Convert radar data to bitmap
                using var radarStream = new MemoryStream(radarData);
                using var radarImage = new Bitmap(radarStream);

                // Composite
                var compositeImage = CompositeMapWithRadar(baseMap, radarImage, width, height);

                // Save
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                compositeImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                compositeImage.Dispose();
                baseMap.Dispose();

                Logger.Log($"✓ Radar map saved: {outputPath}", ConsoleColor.Green);
                return outputPath;
            }
            catch (Exception ex)
            {
                Logger.Log($"✗ Failed to generate radar map: {ex.Message}", ConsoleColor.Red);
                return null;
            }
        }
    }
}
