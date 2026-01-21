# OpenMap - OpenStreetMap Integration Library

OpenMap is a lightweight .NET library for integrating OpenStreetMap tiles into your applications. It provides simple APIs for generating map backgrounds, overlays, and composite images with transparent layers.

## Features

- 🗺️ **Multiple Map Styles**: Standard, Terrain (OpenTopoMap), Satellite (ESRI), Minimal
- 🎨 **Layer Composition**: Overlay transparent images (weather data, markers) on maps
- 📍 **Coordinate Support**: Easy-to-use coordinate presets for major Canadian cities
- 🔄 **Async Operations**: Non-blocking tile downloads and image generation
- ⚡ **Caching**: Built-in tile caching with HTTP header compliance
- 🎯 **Precise Positioning**: Sub-tile pixel-accurate map centering

## Legal Requirements ⚖️

**IMPORTANT**: This library uses OpenStreetMap tiles. You **MUST**:

✅ Display attribution: `© OpenStreetMap contributors` (with link)  
✅ Comply with [OSM Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/)  
✅ Cache tiles properly (7-day minimum)  
❌ NO bulk downloading or offline prefetching

**See [LEGAL.md](LEGAL.md) for complete legal requirements and attribution guidelines.**

## Installation

Add OpenMap as a project reference:

```xml
<ProjectReference Include="..\OpenMap\OpenMap.csproj" />
```

## Quick Start

### Basic Map Generation

```csharp
using OpenMap;

// Create service
var mapService = new MapOverlayService(width: 1920, height: 1080);

// Generate map background for Toronto
var map = await mapService.GenerateMapBackgroundAsync(
    latitude: 43.6532,
    longitude: -79.3832,
    zoomLevel: 10,
    style: MapStyle.Standard
);

// Save to file
map.Save("toronto_map.png");

// IMPORTANT: Display attribution on your map!
// © OpenStreetMap contributors
```

### Using Coordinate Presets

```csharp
using OpenMap;

// Use predefined coordinates
var coords = MapCoordinates.Toronto; // (43.6532, -79.3832)

var map = await mapService.GenerateMapBackgroundAsync(
    coords.Latitude,
    coords.Longitude,
    zoomLevel: 8
);
```

### Map with Overlay (e.g., Weather Radar)

```csharp
using OpenMap;

// Generate base map
var baseMap = await mapService.GenerateMapBackgroundAsync(
    43.6532, -79.3832, zoomLevel: 8, style: MapStyle.Terrain
);

// Load radar image
var radarImage = Image.FromFile("radar.png");

// Composite with transparency
var compositor = new LayerCompositor();
var final = compositor.CompositeLayers(
    baseMap,
    new[] {
        new ImageLayer {
            Image = radarImage,
            Opacity = 0.7f,
            Alignment = LayerAlignment.Fill
        }
    }
);

final.Save("radar_on_map.png");

// Remember attribution: © OpenStreetMap contributors
```

## Map Styles

```csharp
public enum MapStyle
{
    Standard,   // OpenStreetMap Carto style
    Terrain,    // OpenTopoMap (topographic)
    Satellite,  // ESRI World Imagery
    Minimal     // Simple, minimal styling
}
```

**Note**: Each style has different attribution requirements. See [LEGAL.md](LEGAL.md).

## Canadian City Presets

Predefined coordinates available in `MapCoordinates`:

- **Major Cities**: Toronto, Vancouver, Montreal, Calgary, Edmonton, Ottawa, QuebecCity, Winnipeg, Halifax
- **Provincial Bounds**: Ontario, Quebec, BritishColumbia, Alberta (bounding boxes)

```csharp
var toronto = MapCoordinates.Toronto;
var ontario = MapCoordinates.Ontario;

// Calculate distance
double km = MapCoordinates.CalculateDistance(
    MapCoordinates.Toronto.Latitude,
    MapCoordinates.Toronto.Longitude,
    MapCoordinates.Vancouver.Latitude,
    MapCoordinates.Vancouver.Longitude
);
```

## Zoom Levels Guide

| Zoom | Coverage | Use Case |
|------|----------|----------|
| 0-2  | World    | Global view |
| 3-5  | Continent/Country | National weather |
| 6-8  | Province/State | Regional weather |
| 9-11 | City/Metro | City weather |
| 12-14| Neighborhood | Detailed maps |
| 15-18| Street-level | Very detailed |

**Recommended for weather radar**: Zoom 7-9

## Layer Composition

```csharp
var compositor = new LayerCompositor();

var result = compositor.CompositeLayers(
    baseImage: mapBackground,
    layers: new[] {
        new ImageLayer {
            Image = radarImage,
            Opacity = 0.7f,
            Alignment = LayerAlignment.Fill
        },
        new ImageLayer {
            Image = legendImage,
            Opacity = 1.0f,
            Alignment = LayerAlignment.TopRight
        }
    }
);
```

### Layer Alignments

- `Fill`: Stretch to fill entire canvas
- `Center`: Center on canvas (preserve aspect)
- `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`: Corner positioning
- `Fit`: Scale to fit (maintain aspect ratio)

## Advanced Usage

### Bounding Box Maps

```csharp
// Generate map covering a specific area
var map = await mapService.GenerateMapWithBoundingBoxAsync(
    minLat: 42.0,
    minLon: -81.0,
    maxLat: 45.0,
    maxLon: -78.0,
    zoomLevel: 8
);
```

### Custom Dimensions

```csharp
// Override default dimensions
var map = await mapService.GenerateMapBackgroundAsync(
    43.6532, -79.3832,
    zoomLevel: 10,
    width: 3840,  // 4K width
    height: 2160, // 4K height
    style: MapStyle.Terrain
);
```

## Attribution Display

**You MUST display attribution on all maps.** Example implementations:

### Windows Forms
```csharp
pictureBox.Paint += (s, e) => {
    var text = "© OpenStreetMap contributors";
    var font = new Font("Arial", 8);
    var brush = Brushes.Black;
    var point = new PointF(pictureBox.Width - 200, pictureBox.Height - 20);
    e.Graphics.DrawString(text, font, brush, point);
};
```

### Static Image Overlay
```csharp
using var g = Graphics.FromImage(map);
var text = "© OpenStreetMap contributors";
var font = new Font("Arial", 10);
var size = g.MeasureString(text, font);
var point = new PointF(map.Width - size.Width - 10, map.Height - size.Height - 5);

// Semi-transparent background
var bgRect = new RectangleF(point.X - 5, point.Y - 2, size.Width + 10, size.Height + 4);
g.FillRectangle(new SolidBrush(Color.FromArgb(180, 255, 255, 255)), bgRect);

// Text
g.DrawString(text, font, Brushes.Black, point);
```

## Best Practices

### Do ✅
- Display attribution prominently
- Cache tiles for at least 7 days
- Use appropriate zoom levels for your use case
- Handle tile download failures gracefully
- Respect HTTP cache headers

### Don't ❌
- Bulk download entire regions
- Implement "download for offline" features
- Hide or remove attribution
- Ignore tile server errors/rate limits
- Use for real-time high-frequency updates

## Performance Tips

1. **Reuse MapOverlayService**: Create once, use for multiple maps
2. **Cache Generated Maps**: Store final composited images
3. **Appropriate Zoom Levels**: Higher zoom = more tiles = slower
4. **Async Operations**: Use `await` properly, don't block
5. **Dispose Images**: Always dispose Bitmap objects when done

```csharp
using var map = await mapService.GenerateMapBackgroundAsync(...);
using var final = compositor.CompositeLayers(...);
// Auto-disposed at end of using block
```

## Troubleshooting

### Tiles Not Loading
- Check internet connection
- Verify User-Agent is set correctly
- Check OSM tile server status: https://status.openstreetmap.org/
- Review [tile usage policy](https://operations.osmfoundation.org/policies/tiles/)

### Map Positioning Inaccurate
- Verify latitude/longitude values are correct
- Check zoom level is appropriate for coverage area
- Ensure coordinates are in decimal degrees (not DMS)

### Rate Limiting / Blocked
- Reduce request frequency
- Implement proper caching
- Consider using alternative tile providers
- Review [LEGAL.md](LEGAL.md) for compliance

## Dependencies

- **.NET 10.0**: Target framework
- **SkiaSharp 2.88.8**: 2D graphics rendering
- **System.Drawing.Common 9.0.0**: Image manipulation

## Examples

See the main project for integration examples:
- `WeatherDetailsForm.cs`: Radar overlay on map
- `WeatherMapService.cs`: High-level weather visualization API

## Version History

- **1.0.0.0120** (2026-01-20): Initial release
  - OpenStreetMap tile integration
  - Multiple map styles
  - Layer composition
  - Canadian city presets
  - Full legal compliance

## Contributing

This library is part of the Weather Still API project. Contributions welcome!

## License

**OpenMap Library**: MIT License (Copyright © 2026 NoID Softwork)  
**OpenStreetMap Data**: ODbL (© OpenStreetMap contributors)

See [LEGAL.md](LEGAL.md) for complete licensing information.

## Support

- **Issues**: https://github.com/NoID-Softwork/weather-still-api/issues
- **OSM Help**: https://help.openstreetmap.org/
- **Map Issues**: https://www.openstreetmap.org/fixthemap

## Acknowledgments

- **OpenStreetMap Contributors**: For providing free, open map data
- **OpenStreetMap Foundation**: For tile infrastructure
- **OpenTopoMap**: For topographic map tiles
- **ESRI**: For satellite imagery tiles

---

**Remember**: Always display `© OpenStreetMap contributors` attribution when using this library!
