# OpenMap Library Integration

## Overview

The **OpenMap** library provides map overlay functionality for the Weather Image Generator application. It allows you to generate map backgrounds using OpenStreetMap tiles and composite them with weather data (radar, forecasts, etc.).

## ⚠️ Legal Requirements

**IMPORTANT**: This library uses OpenStreetMap tile services. You **MUST** comply with:

- **Attribution**: Display `© OpenStreetMap contributors` on ALL maps (with link to https://www.openstreetmap.org/copyright)
- **OSM Tile Usage Policy**: https://operations.osmfoundation.org/policies/tiles/
- **Data License**: OpenStreetMap data is licensed under ODbL
- **No Bulk Downloads**: No prefetching or offline download features

**📄 See [OpenMap/LEGAL.md](../OpenMap/LEGAL.md) for complete legal requirements and attribution guidelines.**
**📖 See [OpenMap/README.md](../OpenMap/README.md) for detailed usage documentation.**

## Features

- 🗺️ **Map Background Generation**: Create map images for any location and zoom level
- 🌍 **Multiple Map Styles**: Standard, Minimal, Terrain, and Satellite views
- 📍 **Bounding Box Support**: Generate maps for specific geographic regions
- 🎨 **Layer Compositing**: Stack multiple images (map + radar + forecast) with transparency
- 🍁 **Canadian Presets**: Built-in coordinates and bounding boxes for Canadian cities and provinces
- ⚖️ **Legal Compliance**: Built-in OSM attribution and usage policy compliance

## Usage Examples

### 1. Basic Map Generation

```csharp
using OpenMap;

var mapService = new MapOverlayService(width: 1920, height: 1080);

// Generate a map centered on Toronto
var mapImage = await mapService.GenerateMapBackgroundAsync(
    latitude: 43.6532,
    longitude: -79.3832,
    zoomLevel: 10
);

mapImage.Save("toronto_map.png");
```

### 2. Overlay Weather Data on Map

```csharp
// Create map background
var mapBackground = await mapService.GenerateMapBackgroundAsync(43.6532, -79.3832, 10);

// Load your weather/radar image
var radarImage = new Bitmap("radar_data.png");

// Composite them together
var compositeImage = mapService.OverlayImageOnMap(
    mapBackground, 
    radarImage, 
    opacity: 0.7f // 70% opacity for overlay
);

compositeImage.Save("weather_with_map.png");
```

### 3. Regional Maps (Using Presets)

```csharp
// Generate map for entire province of Ontario
var bounds = MapCoordinates.Canada.Ontario;
var ontarioMap = await mapService.GenerateMapWithBoundingBoxAsync(
    bounds.MinLat, bounds.MinLon,
    bounds.MaxLat, bounds.MaxLon,
    style: MapStyle.Terrain
);

ontarioMap.Save("ontario_map.png");
```

### 4. Multi-Layer Compositing

```csharp
var compositor = new LayerCompositor();

var layers = new List<LayerInfo>
{
    new LayerInfo { Image = mapBackground, Opacity = 1.0f, Name = "Base Map" },
    new LayerInfo { Image = radarImage, Opacity = 0.7f, Name = "Radar" },
    new LayerInfo { Image = forecastImage, Opacity = 0.5f, Name = "Forecast" }
};

var finalImage = LayerCompositor.CompositeLayers(layers, 1920, 1080);
finalImage.Save("complete_weather_viz.png");
```

### 5. Using the Weather Map Service

The `WeatherMapService` class provides high-level methods for common weather visualization tasks:

```csharp
using WeatherImageGenerator.Services;

var weatherMapService = new WeatherMapService(outputDirectory: "WeatherImages");

// Generate weather still with map background
await weatherMapService.GenerateWeatherStillWithMapAsync(
    latitude: 43.6532,
    longitude: -79.3832,
    weatherImagePath: "current_weather.png",
    outputFileName: "weather_map_toronto.png",
    zoomLevel: 10
);

// Generate radar with map
await weatherMapService.GenerateRadarMapAsync(
    centerLat: 45.5017,
    centerLon: -73.5673,
    radarImagePath: "province_frames/frame_001.png",
    outputFileName: "radar_map_montreal.png",
    zoomLevel: 8
);

// Generate complete visualization with multiple layers
await weatherMapService.GenerateCompleteWeatherVisualizationAsync(
    latitude: 49.2827,
    longitude: -123.1207,
    overlayImagePaths: new List<string>
    {
        "radar_data.png",
        "forecast_data.png",
        "alert_overlay.png"
    },
    outputFileName: "complete_viz_vancouver.png",
    zoomLevel: 9
);
```

## Map Styles

The library supports multiple map styles:

- **Standard**: Default OpenStreetMap style
- **Minimal**: Light, clean humanitarian style
- **Terrain**: Topographic with elevation data (OpenTopoMap)
- **Satellite**: ESRI satellite imagery

## Canadian Geographic Presets

Built-in coordinates for major Canadian cities:

```csharp
MapCoordinates.Canada.Toronto
MapCoordinates.Canada.Vancouver
MapCoordinates.Canada.Montreal
MapCoordinates.Canada.Calgary
MapCoordinates.Canada.Ottawa
// ... and more
```

Built-in bounding boxes for provinces:

```csharp
MapCoordinates.Canada.Ontario
MapCoordinates.Canada.Quebec
MapCoordinates.Canada.BritishColumbia
MapCoordinates.Canada.Alberta
MapCoordinates.Canada.Saskatchewan
MapCoordinates.Canada.Manitoba
MapCoordinates.Canada.EntireCountry
```

## Integration with Existing Code

### Radar Images with Map Background

In your existing `ECCC.cs` radar generation code, you can now add map backgrounds:

```csharp
// After downloading radar frames, overlay them on a map
var mapService = new MapOverlayService();
var weatherMapService = new WeatherMapService(outputDirectory);

foreach (var radarFrame in radarFrames)
{
    await weatherMapService.GenerateRadarMapAsync(
        centerLat: locationData.Latitude,
        centerLon: locationData.Longitude,
        radarImagePath: radarFrame,
        outputFileName: Path.GetFileName(radarFrame).Replace(".png", "_with_map.png"),
        zoomLevel: 8
    );
}
```

### Video Generation with Map Backgrounds

When generating videos, you can now use map-overlaid frames as the base:

```csharp
// In VideoGenerator.cs
var mapService = new WeatherMapService(outputDirectory);

// Generate map background once for the location
var mapBackground = await mapService.GenerateRegionalMapAsync("ontario");

// Use this as StaticMapPath property
StaticMapPath = mapBackground;
```

## Performance Considerations

- Map tiles are downloaded from public servers; consider caching for better performance
- For video generation, generate the map background once and reuse it for all frames
- Use appropriate zoom levels: lower numbers (1-5) for regions, higher (10-15) for cities
- The library will automatically handle tile downloads and caching

## Attribution

When using map tiles, proper attribution is required:
- OpenStreetMap: © OpenStreetMap contributors
- OpenTopoMap: © OpenStreetMap contributors, SRTM
- ESRI Satellite: © ESRI

## Dependencies

The OpenMap library uses:
- **Mapsui**: Modern mapping library for .NET
- **SkiaSharp**: Cross-platform 2D graphics
- **BruTile**: Library for tile-based mapping
- **System.Drawing.Common**: Image manipulation

All dependencies are automatically included when you reference the OpenMap project.
