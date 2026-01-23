# OpenMap Settings Configuration Guide

## Overview

The OpenMap settings allow you to customize map appearance, styling, colors, and behavior for all map-based weather visualizations in the Weather Still Generator.

## Configuration Location

Settings are located in `appsettings.json` under the `"OpenMap"` section:

```json
{
  "OpenMap": {
    "DefaultMapStyle": "Standard",
    "DefaultZoomLevel": 10,
    "BackgroundColor": "#D3D3D3",
    "OverlayOpacity": 0.7,
    "TileDownloadTimeoutSeconds": 30,
    "EnableTileCache": true,
    "TileCacheDirectory": "MapCache",
    "CacheDurationHours": 168,
    "StylePresets": { ... },
    "ColorOverrides": null
  }
}
```

---

## Settings Reference

### Core Settings

#### `DefaultMapStyle`
- **Type:** String
- **Default:** `"Standard"`
- **Options:** `"Standard"`, `"Minimal"`, `"Terrain"`, `"Satellite"`
- **Description:** The default map style to use when generating maps

**Map Style Examples:**
- **Standard**: Traditional OpenStreetMap style with roads, cities, and detailed features
- **Minimal**: Clean, simplified style from OpenStreetMap HOT (Humanitarian)
- **Terrain**: Topographic style with elevation contours from OpenTopoMap
- **Satellite**: High-resolution satellite imagery from Esri

#### `DefaultZoomLevel`
- **Type:** Integer (0-18)
- **Default:** `10`
- **Description:** Default zoom level for map generation
- **Zoom Guide:**
  - `0-3`: World/Continental view
  - `4-6`: Country/Region view
  - `7-10`: State/Province view (good for weather radar)
  - `11-13`: City view
  - `14-16`: Neighborhood view
  - `17-18`: Street level view

#### `BackgroundColor`
- **Type:** String (Hex color)
- **Default:** `"#D3D3D3"` (Light gray)
- **Description:** Background color for map tiles (shown where tiles are loading or unavailable)
- **Examples:**
  - `"#E8F4F8"` - Light blue (water-like)
  - `"#F0F0F0"` - Off-white
  - `"#FFFFFF"` - Pure white
  - `"#2C3E50"` - Dark slate (for dark themes)

#### `OverlayOpacity`
- **Type:** Float (0.0 to 1.0)
- **Default:** `0.7`
- **Description:** Default transparency for weather overlays (radar, forecast layers)
- **Guide:**
  - `0.5` - Very transparent (50% see-through)
  - `0.7` - Balanced visibility
  - `0.85` - Mostly opaque
  - `1.0` - Fully opaque (no map visible underneath)

### Performance Settings

#### `TileDownloadTimeoutSeconds`
- **Type:** Integer
- **Default:** `30`
- **Description:** Maximum time to wait for each map tile to download
- **Recommendation:** Increase to 60 if you have slow internet

#### `EnableTileCache`
- **Type:** Boolean
- **Default:** `true`
- **Description:** Enable local caching of map tiles to reduce bandwidth and improve performance
- **Note:** Highly recommended to keep enabled

#### `TileCacheDirectory`
- **Type:** String
- **Default:** `"MapCache"`
- **Description:** Directory name for storing cached tiles (relative to application directory)

#### `CacheDurationHours`
- **Type:** Integer
- **Default:** `168` (7 days)
- **Description:** How long to keep cached tiles before re-downloading
- **Note:** Map tiles rarely change, so longer durations are fine

---

## Style Presets

Style presets allow you to define different map configurations for specific use cases. Each preset can override the default settings.

### Preset Structure

```json
"StylePresets": {
  "PresetName": {
    "Style": "Standard|Minimal|Terrain|Satellite",
    "ZoomLevel": 10,
    "BackgroundColor": "#RRGGBB",
    "OverlayOpacity": 0.7
  }
}
```

### Default Presets

#### Weather Preset
```json
"Weather": {
  "Style": "Standard",
  "ZoomLevel": 10,
  "BackgroundColor": "#E8F4F8",
  "OverlayOpacity": 0.75
}
```
- **Use Case:** General weather forecasts and current conditions
- **Features:** Clear city labels, roads visible, light blue background

#### Radar Preset
```json
"Radar": {
  "Style": "Terrain",
  "ZoomLevel": 8,
  "BackgroundColor": "#F0F0F0",
  "OverlayOpacity": 0.8
}
```
- **Use Case:** Weather radar overlays
- **Features:** Topographic features help understand storm movement, higher opacity for radar visibility

#### Satellite Preset
```json
"Satellite": {
  "Style": "Satellite",
  "ZoomLevel": 11,
  "OverlayOpacity": 0.65
}
```
- **Use Case:** Satellite imagery with weather overlays
- **Features:** Real imagery background, lower opacity to see both satellite and weather data

#### Minimal Preset
```json
"Minimal": {
  "Style": "Minimal",
  "ZoomLevel": 10,
  "BackgroundColor": "#FFFFFF",
  "OverlayOpacity": 0.7
}
```
- **Use Case:** Clean, simple weather maps without clutter
- **Features:** Minimal road/city details, white background, focus on weather data

---

## Color Overrides (Advanced)

The `ColorOverrides` feature is planned for future implementation to allow custom color schemes for map elements.

**Planned Features:**
```json
"ColorOverrides": {
  "Background": "#2C3E50",
  "Water": "#3498DB",
  "Land": "#ECF0F1",
  "Roads": "#95A5A6",
  "Urban": "#BDC3C7",
  "Borders": "#7F8C8D"
}
```

*Note: This feature requires custom tile rendering and is not yet implemented.*

---

## Usage in Code

### Using Configuration in Services

Services can access OpenMap settings through the ConfigManager:

```csharp
using WeatherImageGenerator.Services;
using OpenMap;

// Load configuration
var config = ConfigManager.LoadConfig();
var openMapSettings = config.OpenMap;

// Create MapOverlayService with settings
var mapService = new MapOverlayService(1920, 1080, openMapSettings);

// Generate map with configured defaults
var map = await mapService.GenerateMapBackgroundAsync(45.5017, -73.5673);
```

### Using Style Presets

Access presets programmatically:

```csharp
var radarPreset = openMapSettings?.StylePresets?["Radar"];
if (radarPreset != null)
{
    var presetSettings = new OpenMap.OpenMapSettings
    {
        DefaultMapStyle = radarPreset.Style ?? "Terrain",
        DefaultZoomLevel = radarPreset.ZoomLevel,
        BackgroundColor = radarPreset.BackgroundColor ?? "#F0F0F0",
        OverlayOpacity = radarPreset.OverlayOpacity ?? 0.8f
    };
    
    var mapService = new MapOverlayService(1920, 1080, presetSettings);
}
```

---

## Examples

### Example 1: Dark Theme Maps

For dark-themed weather displays:

```json
"OpenMap": {
  "DefaultMapStyle": "Satellite",
  "DefaultZoomLevel": 10,
  "BackgroundColor": "#1E1E1E",
  "OverlayOpacity": 0.8
}
```

### Example 2: High-Detail City View

For city-focused weather:

```json
"OpenMap": {
  "DefaultMapStyle": "Standard",
  "DefaultZoomLevel": 13,
  "BackgroundColor": "#FFFFFF",
  "OverlayOpacity": 0.65
}
```

### Example 3: Wide-Area Radar

For province/state-wide radar:

```json
"OpenMap": {
  "DefaultMapStyle": "Terrain",
  "DefaultZoomLevel": 7,
  "BackgroundColor": "#F5F5F5",
  "OverlayOpacity": 0.85
}
```

### Example 4: Minimal Bandwidth

For slower connections:

```json
"OpenMap": {
  "DefaultMapStyle": "Minimal",
  "EnableTileCache": true,
  "TileCacheDirectory": "MapCache",
  "CacheDurationHours": 720,
  "TileDownloadTimeoutSeconds": 60
}
```

---

## Legal & Attribution

All maps generated with OpenMap must display proper attribution:

- **Standard/Minimal/Terrain**: `© OpenStreetMap contributors`
- **Terrain**: `© OpenStreetMap contributors, SRTM | Style: © OpenTopoMap (CC-BY-SA)`
- **Satellite**: `Esri, Maxar, Earthstar Geographics`

See [OpenMap/LEGAL.md](../OpenMap/LEGAL.md) for complete attribution requirements.

---

## Troubleshooting

### Maps appear gray/blank
- Check your internet connection
- Verify `TileDownloadTimeoutSeconds` is sufficient
- Check that OpenStreetMap tile servers are accessible
- Clear tile cache directory and restart

### Slow map generation
- Enable `EnableTileCache` if disabled
- Reduce `DefaultZoomLevel` (lower numbers = fewer tiles)
- Check internet speed
- Consider using `Minimal` style (smaller tile sizes)

### Wrong colors displaying
- Verify `BackgroundColor` format is `#RRGGBB` (6 hex digits)
- Ensure hex values are valid (0-9, A-F)
- Check for typos in preset configurations

### Tiles not caching
- Verify `TileCacheDirectory` path exists and is writable
- Check available disk space
- Ensure `EnableTileCache` is `true`

---

**Last Updated:** January 22, 2026
