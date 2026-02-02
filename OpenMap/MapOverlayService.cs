using SkiaSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace OpenMap;

/// <summary>
/// Service for generating map overlays with OpenStreetMap tiles.
/// 
/// IMPORTANT: This library uses OpenStreetMap (OSM) tile services.
/// OSM data is licensed under the Open Data Commons Open Database License (ODbL).
/// When using this library, you MUST:
/// - Display proper attribution: "© OpenStreetMap contributors" with link to https://www.openstreetmap.org/copyright
/// - Comply with OSM Tile Usage Policy: https://operations.osmfoundation.org/policies/tiles/
/// - Cache tiles appropriately and honor HTTP caching headers
/// - NOT use for bulk downloading or offline prefetching
/// 
/// See OpenMap/LEGAL.md for full legal requirements and attribution guidelines.
/// </summary>
public class MapOverlayService
{
    private readonly int _defaultWidth;
    private readonly int _defaultHeight;
    private readonly HttpClient _httpClient;
    // User-Agent must identify the application and include contact info per OSM Tile Usage Policy
    private static readonly string _userAgent = "WeatherImageGenerator/1.0 (+https://github.com/NoID-Softwork/weather-still-api)";
    
    // Configuration
    private readonly string _backgroundColor;
    private readonly float _defaultOverlayOpacity;
    private readonly MapStyle _defaultMapStyle;
    private readonly int _defaultZoomLevel;
    private readonly bool _enableTileCache;
    private readonly string? _tileCacheDirectory;
    private readonly int _cacheDurationHours;
    private readonly bool _useDarkMode;

    /// <summary>
    /// Creates a new MapOverlayService with default settings
    /// </summary>
    public MapOverlayService(int width = 1920, int height = 1080)
        : this(width, height, null)
    {
    }

    /// <summary>
    /// Creates a new MapOverlayService with custom configuration
    /// </summary>
    /// <param name="width">Default map width in pixels</param>
    /// <param name="height">Default map height in pixels</param>
    /// <param name="settings">OpenMap configuration settings (optional)</param>
    public MapOverlayService(int width, int height, OpenMapSettings? settings)
    {
        _defaultWidth = width;
        _defaultHeight = height;
        
        // Apply configuration or use defaults
        _backgroundColor = settings?.BackgroundColor ?? "#D3D3D3";
        _defaultOverlayOpacity = settings?.OverlayOpacity ?? 0.7f;
        _defaultMapStyle = ParseMapStyle(settings?.DefaultMapStyle);
        _defaultZoomLevel = settings?.DefaultZoomLevel ?? 10;
        _enableTileCache = settings?.EnableTileCache ?? true;
        _tileCacheDirectory = settings?.TileCacheDirectory;
        _cacheDurationHours = settings?.CacheDurationHours ?? 168;
        _useDarkMode = settings?.UseDarkMode ?? false;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        // Set timeout from configuration
        _httpClient.Timeout = TimeSpan.FromSeconds(settings?.TileDownloadTimeoutSeconds ?? 30);
    }

    /// <summary>
    /// Creates a map background image for a given location and zoom level
    /// </summary>
    public async Task<Bitmap> GenerateMapBackgroundAsync(
        double latitude,
        double longitude,
        int? zoomLevel = null,
        int? width = null,
        int? height = null,
        MapStyle? style = null)
    {
        var w = width ?? _defaultWidth;
        var h = height ?? _defaultHeight;
        var zoom = zoomLevel ?? _defaultZoomLevel;
        var mapStyle = style ?? _defaultMapStyle;
        
        // Apply dark mode if enabled and using Terrain
        if (_useDarkMode && mapStyle == MapStyle.Terrain)
        {
            mapStyle = MapStyle.TerrainDark;
        }

        // Convert lat/lon to pixel coordinates at this zoom level
        var centerPixelX = LonToPixelX(longitude, zoom);
        var centerPixelY = LatToPixelY(latitude, zoom);

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
            // Use configured background color
            var bgColor = ParseColor(_backgroundColor);
            g.Clear(bgColor);

            // Download and draw each tile
            for (int tx = tileMinX; tx <= tileMaxX; tx++)
            {
                for (int ty = tileMinY; ty <= tileMaxY; ty++)
                {
                    // Skip invalid tiles
                    if (tx < 0 || ty < 0) continue;
                    var maxTile = (1 << zoom);
                    if (tx >= maxTile || ty >= maxTile) continue;

                    try
                    {
                        var tileUrl = GetTileUrl(tx, ty, zoom, mapStyle);
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
        int? zoomLevel = null,
        int? width = null,
        int? height = null,
        MapStyle? style = null)
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
        MapStyle? style = null)
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
        int? zoomLevel = null,
        int? width = null,
        int? height = null,
        MapStyle? style = null)
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
    public Bitmap OverlayImageOnMap(Bitmap mapBackground, Bitmap overlayImage, float? opacity = null)
    {
        // Use provided opacity or configured default
        var effectiveOpacity = opacity ?? _defaultOverlayOpacity;
        
        var result = new Bitmap(mapBackground.Width, mapBackground.Height);
        
        using (var g = Graphics.FromImage(result))
        {
            // Draw the map background
            g.DrawImage(mapBackground, 0, 0);

            // Create color matrix for transparency
            var colorMatrix = new ColorMatrix
            {
                Matrix33 = effectiveOpacity // Set alpha (transparency)
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

    /// <summary>
    /// Gets the tile URL for the specified tile coordinates and style.
    /// 
    /// ATTRIBUTION REQUIREMENTS:
    /// - Standard/Minimal/Terrain: © OpenStreetMap contributors (ODbL license)
    /// - OpenTopoMap: © OpenStreetMap contributors, SRTM | Style: © OpenTopoMap (CC-BY-SA)
    /// - Satellite: Esri, Maxar, Earthstar Geographics (check ESRI terms)
    /// 
    /// See LEGAL.md for complete attribution requirements.
    /// </summary>
    private string GetTileUrl(int x, int y, int zoom, MapStyle style)
    {
        return style switch
        {
            // © OpenStreetMap contributors | ODbL license
            MapStyle.Standard => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png",
            // Humanitarian OSM Team (HOT) | © OpenStreetMap contributors | ODbL license
            MapStyle.Minimal => $"https://tile.openstreetmap.fr/hot/{zoom}/{x}/{y}.png",
            // © OpenStreetMap contributors, SRTM | Style: © OpenTopoMap (CC-BY-SA)
            MapStyle.Terrain => $"https://tile.opentopomap.org/{zoom}/{x}/{y}.png",
            // Esri, Maxar, Earthstar Geographics (proprietary)
            MapStyle.Satellite => $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{zoom}/{y}/{x}",
            // CartoDB Dark Matter | © OpenStreetMap contributors | © CARTO
            MapStyle.TerrainDark => $"https://cartodb-basemaps-a.global.ssl.fastly.net/dark_all/{zoom}/{x}/{y}.png",
            _ => $"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png"
        };
    }

    private async Task<Bitmap?> DownloadTileAsync(string url)
    {
        // Check cache first
        var cachedTile = GetCachedTile(url);
        if (cachedTile != null)
        {
            return cachedTile;
        }

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                
                // Save to cache
                SaveTileToCache(url, bytes);
                
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Ignore download failures
        }
        return null;
    }

    private string GetCacheFilePath(string url)
    {
        // Create a safe filename from the URL
        var hash = url.GetHashCode().ToString("X8");
        var uri = new Uri(url);
        var pathParts = uri.AbsolutePath.Trim('/').Replace("/", "_");
        var fileName = $"{pathParts}_{hash}.png";
        
        var cacheDir = _tileCacheDirectory ?? "MapCache";
        if (!Path.IsPathRooted(cacheDir))
        {
            cacheDir = Path.Combine(AppContext.BaseDirectory, cacheDir);
        }
        
        return Path.Combine(cacheDir, fileName);
    }

    private Bitmap? GetCachedTile(string url)
    {
        if (!_enableTileCache)
            return null;

        try
        {
            var cachePath = GetCacheFilePath(url);
            if (File.Exists(cachePath))
            {
                var fileInfo = new FileInfo(cachePath);
                var age = DateTime.Now - fileInfo.LastWriteTime;
                
                if (age.TotalHours <= _cacheDurationHours)
                {
                    // Load from cache
                    using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
                    return new Bitmap(stream);
                }
                else
                {
                    // Cache expired, delete it
                    File.Delete(cachePath);
                }
            }
        }
        catch
        {
            // Ignore cache read failures
        }
        return null;
    }

    private void SaveTileToCache(string url, byte[] data)
    {
        if (!_enableTileCache)
            return;

        try
        {
            var cachePath = GetCacheFilePath(url);
            var cacheDir = Path.GetDirectoryName(cachePath);
            
            if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            
            File.WriteAllBytes(cachePath, data);
        }
        catch
        {
            // Ignore cache write failures
        }
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

    /// <summary>
    /// Gets the required attribution text for the specified map style.
    /// This text MUST be displayed prominently on all maps.
    /// </summary>
    /// <param name="style">The map style being used</param>
    /// <returns>Attribution text that must be displayed</returns>
    public static string GetAttributionText(MapStyle style)
    {
        return style switch
        {
            MapStyle.Standard => "© OpenStreetMap contributors",
            MapStyle.Minimal => "© OpenStreetMap contributors",
            MapStyle.Terrain => "© OpenStreetMap contributors, SRTM | Style: © OpenTopoMap (CC-BY-SA)",
            MapStyle.Satellite => "Esri, Maxar, Earthstar Geographics",
            _ => "© OpenStreetMap contributors"
        };
    }

    /// <summary>
    /// Gets the attribution URL for the specified map style.
    /// This should be used as a hyperlink with the attribution text.
    /// </summary>
    /// <param name="style">The map style being used</param>
    /// <returns>URL for attribution link</returns>
    public static string GetAttributionUrl(MapStyle style)
    {
        return style switch
        {
            MapStyle.Standard => "https://www.openstreetmap.org/copyright",
            MapStyle.Minimal => "https://www.openstreetmap.org/copyright",
            MapStyle.Terrain => "https://opentopomap.org/about",
            MapStyle.Satellite => "https://www.esri.com/en-us/legal/terms/full-master-agreement",
            _ => "https://www.openstreetmap.org/copyright"
        };
    }

    /// <summary>
    /// Parses a hex color string to System.Drawing.Color
    /// </summary>
    private static Color ParseColor(string hexColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hexColor))
                return Color.LightGray;

            // Remove # if present
            hexColor = hexColor.TrimStart('#');

            if (hexColor.Length == 6)
            {
                int r = int.Parse(hexColor.Substring(0, 2), NumberStyles.HexNumber);
                int g = int.Parse(hexColor.Substring(2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hexColor.Substring(4, 2), NumberStyles.HexNumber);
                return Color.FromArgb(r, g, b);
            }
            else if (hexColor.Length == 8)
            {
                int a = int.Parse(hexColor.Substring(0, 2), NumberStyles.HexNumber);
                int r = int.Parse(hexColor.Substring(2, 2), NumberStyles.HexNumber);
                int g = int.Parse(hexColor.Substring(4, 2), NumberStyles.HexNumber);
                int b = int.Parse(hexColor.Substring(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
            // Return default on parse error
        }
        return Color.LightGray;
    }

    /// <summary>
    /// Parses a map style string to MapStyle enum
    /// </summary>
    private static MapStyle ParseMapStyle(string? styleString)
    {
        if (string.IsNullOrWhiteSpace(styleString))
            return MapStyle.Standard;

        return styleString.ToLowerInvariant() switch
        {
            "standard" => MapStyle.Standard,
            "minimal" => MapStyle.Minimal,
            "terrain" => MapStyle.Terrain,
            "satellite" => MapStyle.Satellite,
            "terraindark" => MapStyle.TerrainDark,
            _ => MapStyle.Standard
        };
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
    Satellite,  // Satellite imagery
    TerrainDark // Dark terrain/topographic style
}

/// <summary>
/// OpenMap configuration settings
/// </summary>
public class OpenMapSettings
{
    public string DefaultMapStyle { get; set; } = "Standard";
    public int DefaultZoomLevel { get; set; } = 10;
    public string BackgroundColor { get; set; } = "#D3D3D3";
    public float OverlayOpacity { get; set; } = 0.7f;
    public int TileDownloadTimeoutSeconds { get; set; } = 30;
    public bool EnableTileCache { get; set; } = true;
    public string? TileCacheDirectory { get; set; } = "MapCache";
    public int CacheDurationHours { get; set; } = 168;
    public bool UseDarkMode { get; set; } = false;
}
