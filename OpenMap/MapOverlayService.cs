using SkiaSharp;
using System.Drawing;
using System.Drawing.Imaging;

namespace OpenMap;

/// <summary>
/// Service for generating map overlays with OpenStreetMap tiles
/// </summary>
public class MapOverlayService
{
    private readonly int _defaultWidth;
    private readonly int _defaultHeight;
    private readonly HttpClient _httpClient;
    private static readonly string _userAgent = "WeatherImageGenerator/1.0";

    public MapOverlayService(int width = 1920, int height = 1080)
    {
        _defaultWidth = width;
        _defaultHeight = height;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
    }

    /// <summary>
    /// Creates a map background image for a given location and zoom level
    /// </summary>
    public async Task<Bitmap> GenerateMapBackgroundAsync(
        double latitude,
        double longitude,
        int zoomLevel = 10,
        int? width = null,
        int? height = null,
        MapStyle style = MapStyle.Standard)
    {
        var w = width ?? _defaultWidth;
        var h = height ?? _defaultHeight;

        // Convert lat/lon to pixel coordinates at this zoom level
        var centerPixelX = LonToPixelX(longitude, zoomLevel);
        var centerPixelY = LatToPixelY(latitude, zoomLevel);

        // Calculate pixel bounds for the viewport
        var pixelMinX = centerPixelX - (w / 2.0);
        var pixelMinY = centerPixelY - (h / 2.0);
        var pixelMaxX = centerPixelX + (w / 2.0);
        var pixelMaxY = centerPixelY + (h / 2.0);

        // Convert pixel bounds to tile coordinates
        var tileMinX = (int)Math.Floor(pixelMinX / 256.0);
        var tileMinY = (int)Math.Floor(pixelMinY / 256.0);
        var tileMaxX = (int)Math.Floor(pixelMaxX / 256.0);
        var tileMaxY = (int)Math.Floor(pixelMaxY / 256.0);

        // Create a bitmap to hold the map
        var bitmap = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.LightGray);

            // Download and draw each tile
            for (int tx = tileMinX; tx <= tileMaxX; tx++)
            {
                for (int ty = tileMinY; ty <= tileMaxY; ty++)
                {
                    // Skip invalid tiles
                    if (tx < 0 || ty < 0) continue;
                    var maxTile = (1 << zoomLevel);
                    if (tx >= maxTile || ty >= maxTile) continue;

                    try
                    {
                        var tileUrl = GetTileUrl(tx, ty, zoomLevel, style);
                        var tileImage = await DownloadTileAsync(tileUrl);
                        
                        if (tileImage != null)
                        {
                            // Calculate pixel position of this tile
                            var tilePixelX = tx * 256;
                            var tilePixelY = ty * 256;

                            // Calculate where to draw this tile in the viewport
                            var drawX = (int)(tilePixelX - pixelMinX);
                            var drawY = (int)(tilePixelY - pixelMinY);

                            g.DrawImage(tileImage, drawX, drawY, 256, 256);
                            tileImage.Dispose();
                        }
                    }
                    catch
                    {
                        // If tile download fails, just continue
                    }
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Creates a map overlay with custom styling (transparent background for layering)
    /// </summary>
    public async Task<Bitmap> GenerateMapOverlayAsync(
        double latitude,
        double longitude,
        int zoomLevel = 10,
        int? width = null,
        int? height = null,
        MapStyle style = MapStyle.Standard)
    {
        // Same as background but could apply transparency or different styling
        return await GenerateMapBackgroundAsync(latitude, longitude, zoomLevel, width, height, style);
    }

    /// <summary>
    /// Generates a map with a bounding box (useful for weather radar coverage areas)
    /// </summary>
    public async Task<Bitmap> GenerateMapWithBoundingBoxAsync(
        double minLat, double minLon,
        double maxLat, double maxLon,
        int? width = null,
        int? height = null,
        MapStyle style = MapStyle.Standard)
    {
        var w = width ?? _defaultWidth;
        var h = height ?? _defaultHeight;

        // Calculate center and appropriate zoom
        var centerLat = (minLat + maxLat) / 2.0;
        var centerLon = (minLon + maxLon) / 2.0;

        // Calculate appropriate zoom level to fit the bounding box
        var zoomLevel = CalculateZoomLevelForBounds(minLat, minLon, maxLat, maxLon, w, h);

        return await GenerateMapBackgroundAsync(centerLat, centerLon, zoomLevel, w, h, style);
    }

    /// <summary>
    /// Save a map as PNG file
    /// </summary>
    public async Task SaveMapAsPngAsync(
        string outputPath,
        double latitude,
        double longitude,
        int zoomLevel = 10,
        int? width = null,
        int? height = null,
        MapStyle style = MapStyle.Standard)
    {
        using var bitmap = await GenerateMapBackgroundAsync(latitude, longitude, zoomLevel, width, height, style);
        
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    /// <summary>
    /// Overlay a transparent image (like radar) on top of a map
    /// </summary>
    public Bitmap OverlayImageOnMap(Bitmap mapBackground, Bitmap overlayImage, float opacity = 0.7f)
    {
        var result = new Bitmap(mapBackground.Width, mapBackground.Height);
        
        using (var g = Graphics.FromImage(result))
        {
            // Draw the map background
            g.DrawImage(mapBackground, 0, 0);

            // Create color matrix for transparency
            var colorMatrix = new ColorMatrix
            {
                Matrix33 = opacity // Set alpha (transparency)
            };

            var imageAttributes = new ImageAttributes();
            imageAttributes.SetColorMatrix(colorMatrix);

            // Draw the overlay with transparency
            g.DrawImage(overlayImage,
                new Rectangle(0, 0, overlayImage.Width, overlayImage.Height),
                0, 0, overlayImage.Width, overlayImage.Height,
                GraphicsUnit.Pixel,
                imageAttributes);
        }

        return result;
    }

    #region Private Helper Methods

    private string GetTileUrl(int x, int y, int zoom, MapStyle style)
    {
        return style switch
        {
            MapStyle.Standard => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png",
            MapStyle.Minimal => $"https://tile.openstreetmap.fr/hot/{zoom}/{x}/{y}.png",
            MapStyle.Terrain => $"https://tile.opentopomap.org/{zoom}/{x}/{y}.png",
            MapStyle.Satellite => $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
            _ => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png"
        };
    }

    private async Task<Bitmap?> DownloadTileAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync();
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Ignore download failures
        }
        return null;
    }

    private int LonToTileX(double lon, int zoom)
    {
        return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
    }

    private int LatToTileY(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom));
    }

    private double LonToPixelX(double lon, int zoom)
    {
        return (lon + 180.0) / 360.0 * (256.0 * (1 << zoom));
    }

    private double LatToPixelY(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (256.0 * (1 << zoom));
    }

    private int CalculateZoomLevelForBounds(double minLat, double minLon, double maxLat, double maxLon, int width, int height)
    {
        // Simple calculation - could be improved
        var latSpan = Math.Abs(maxLat - minLat);
        var lonSpan = Math.Abs(maxLon - minLon);

        // Approximate zoom level
        for (int zoom = 18; zoom >= 0; zoom--)
        {
            var tilesX = LonToTileX(maxLon, zoom) - LonToTileX(minLon, zoom);
            var tilesY = LatToTileY(minLat, zoom) - LatToTileY(maxLat, zoom);

            if (tilesX * 256 <= width && tilesY * 256 <= height)
            {
                return Math.Max(0, zoom);
            }
        }

        return 5; // Default zoom if calculation fails
    }

    #endregion
}

/// <summary>
/// Map styling options
/// </summary>
public enum MapStyle
{
    Standard,   // Standard OpenStreetMap
    Minimal,    // Light/minimal style  
    Terrain,    // Topographic/terrain style
    Satellite   // Satellite imagery
}
