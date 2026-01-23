using OpenMap;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services;

/// <summary>
/// Example service showing how to use OpenMap for weather visualization overlays
/// </summary>
public class WeatherMapService
{
    private readonly MapOverlayService _mapService;
    private readonly string _outputDirectory;

    public WeatherMapService(string outputDirectory, int width = 1920, int height = 1080)
    {
        // Load OpenMap configuration
        var config = ConfigManager.LoadConfig();
        var openMapSettings = ConvertToOpenMapSettings(config.OpenMap);
        
        _mapService = new MapOverlayService(width, height, openMapSettings);
        _outputDirectory = outputDirectory;
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
    /// Generate a weather still image with map background
    /// </summary>
    public async Task<string> GenerateWeatherStillWithMapAsync(
        double latitude,
        double longitude,
        string weatherImagePath,
        string outputFileName,
        int zoomLevel = 10,
        MapStyle mapStyle = MapStyle.Standard)
    {
        Logger.Log($"Generating weather image with map overlay for location: {latitude}, {longitude}", ConsoleColor.Cyan);

        // Generate the map background
        using var mapBackground = await _mapService.GenerateMapBackgroundAsync(
            latitude, longitude, zoomLevel);

        // Load the weather overlay image
        if (!File.Exists(weatherImagePath))
        {
            throw new FileNotFoundException($"Weather image not found: {weatherImagePath}");
        }

        using var weatherOverlay = new Bitmap(weatherImagePath);

        // Composite the weather data on top of the map
        using var compositeImage = _mapService.OverlayImageOnMap(mapBackground, weatherOverlay, opacity: 0.7f);

        // Save the result
        var outputPath = Path.Combine(_outputDirectory, outputFileName);
        compositeImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

        Logger.Log($"✓ Saved weather map to: {outputPath}", ConsoleColor.Green);
        return outputPath;
    }

    /// <summary>
    /// Generate a radar overlay with map background for a specific region
    /// </summary>
    public async Task<string> GenerateRadarMapAsync(
        double centerLat,
        double centerLon,
        string radarImagePath,
        string outputFileName,
        int zoomLevel = 8)
    {
        Logger.Log($"Generating radar map for region centered at: {centerLat}, {centerLon}", ConsoleColor.Cyan);

        // Generate map with terrain style (better for radar visualization)
        using var mapBackground = await _mapService.GenerateMapBackgroundAsync(
            centerLat, centerLon, zoomLevel);

        // Load and overlay the radar image
        if (File.Exists(radarImagePath))
        {
            using var radarImage = new Bitmap(radarImagePath);
            using var composite = _mapService.OverlayImageOnMap(mapBackground, radarImage, opacity: 0.75f);

            var outputPath = Path.Combine(_outputDirectory, outputFileName);
            composite.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

            Logger.Log($"✓ Saved radar map to: {outputPath}", ConsoleColor.Green);
            return outputPath;
        }
        else
        {
            // If radar image doesn't exist, just save the map
            var outputPath = Path.Combine(_outputDirectory, outputFileName);
            mapBackground.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            Logger.Log($"⚠ Radar image not found, saved map only: {outputPath}", ConsoleColor.Yellow);
            return outputPath;
        }
    }

    /// <summary>
    /// Generate a layered composite with map background, radar, and forecast data
    /// </summary>
    public async Task<string> GenerateCompleteWeatherVisualizationAsync(
        double latitude,
        double longitude,
        List<string> overlayImagePaths,
        string outputFileName,
        int zoomLevel = 9)
    {
        Logger.Log($"Generating complete weather visualization with {overlayImagePaths.Count} overlay layers", ConsoleColor.Cyan);

        // Generate base map
        using var mapBackground = await _mapService.GenerateMapBackgroundAsync(
            latitude, longitude, zoomLevel);

        // Create layers list
        var layers = new List<LayerInfo>
        {
            new LayerInfo
            {
                Image = (Bitmap)mapBackground.Clone(),
                Opacity = 1.0f,
                Alignment = LayerAlignment.Fill,
                Name = "Map Background"
            }
        };

        // Add all overlay images as layers
        float opacityStep = 0.7f / overlayImagePaths.Count; // Distribute opacity
        float currentOpacity = 0.7f;

        foreach (var overlayPath in overlayImagePaths)
        {
            if (File.Exists(overlayPath))
            {
                try
                {
                    var overlayImage = new Bitmap(overlayPath);
                    layers.Add(new LayerInfo
                    {
                        Image = overlayImage,
                        Opacity = currentOpacity,
                        Alignment = LayerAlignment.Fill,
                        Name = Path.GetFileNameWithoutExtension(overlayPath)
                    });
                    currentOpacity -= opacityStep;
                }
                catch (Exception ex)
                {
                    Logger.Log($"⚠ Failed to load overlay image {overlayPath}: {ex.Message}", ConsoleColor.Yellow);
                }
            }
        }

        // Composite all layers
        using var finalComposite = LayerCompositor.CompositeLayers(layers, mapBackground.Width, mapBackground.Height);

        // Save the result
        var outputPath = Path.Combine(_outputDirectory, outputFileName);
        
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        finalComposite.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

        // Dispose layer images
        foreach (var layer in layers)
        {
            layer.Dispose();
        }

        Logger.Log($"✓ Saved complete visualization to: {outputPath}", ConsoleColor.Green);
        return outputPath;
    }

    /// <summary>
    /// Generate map backgrounds for multiple locations (batch processing)
    /// </summary>
    public async Task<List<string>> GenerateMapsForLocationsAsync(
        List<(string name, double lat, double lon)> locations,
        int zoomLevel = 10,
        MapStyle style = MapStyle.Standard)
    {
        var outputPaths = new List<string>();

        Logger.Log($"Generating maps for {locations.Count} locations...", ConsoleColor.Cyan);

        foreach (var (name, lat, lon) in locations)
        {
            try
            {
                var fileName = $"map_{name.Replace(" ", "_").ToLower()}.png";
                var outputPath = Path.Combine(_outputDirectory, "maps", fileName);

                await _mapService.SaveMapAsPngAsync(outputPath, lat, lon, zoomLevel);
                outputPaths.Add(outputPath);

                Logger.Log($"  ✓ Generated map for {name}", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                Logger.Log($"  ✗ Failed to generate map for {name}: {ex.Message}", ConsoleColor.Red);
            }
        }

        return outputPaths;
    }

    /// <summary>
    /// Generate a provincial or regional map using predefined bounding boxes
    /// </summary>
    public async Task<string> GenerateRegionalMapAsync(
        string regionName,
        MapStyle style = MapStyle.Terrain)
    {
        Logger.Log($"Generating regional map for: {regionName}", ConsoleColor.Cyan);

        BoundingBox? bounds = regionName.ToLower() switch
        {
            "ontario" => MapCoordinates.Canada.Ontario,
            "quebec" => MapCoordinates.Canada.Quebec,
            "british columbia" or "bc" => MapCoordinates.Canada.BritishColumbia,
            "alberta" => MapCoordinates.Canada.Alberta,
            "saskatchewan" => MapCoordinates.Canada.Saskatchewan,
            "manitoba" => MapCoordinates.Canada.Manitoba,
            "canada" => MapCoordinates.Canada.EntireCountry,
            _ => null
        };

        if (bounds == null)
        {
            throw new ArgumentException($"Unknown region: {regionName}");
        }

        // Expand bounds slightly for padding
        bounds = bounds.Expand(0.05);

        using var mapImage = await _mapService.GenerateMapWithBoundingBoxAsync(
            bounds.MinLat, bounds.MinLon,
            bounds.MaxLat, bounds.MaxLon,
            style: style);

        var fileName = $"map_{regionName.Replace(" ", "_").ToLower()}.png";
        var outputPath = Path.Combine(_outputDirectory, "regional_maps", fileName);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        mapImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

        Logger.Log($"✓ Saved regional map to: {outputPath}", ConsoleColor.Green);
        return outputPath;
    }
}
