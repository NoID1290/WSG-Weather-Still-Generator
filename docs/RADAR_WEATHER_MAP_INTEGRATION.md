# 🌧️ Radar Animation & Weather Map Integration

## Overview

This document describes the integration of ECCC radar animation with OpenStreetMap overlays and global weather maps with temperature displays into the WeatherImageGenerator video workflow.

## Features

### 1. **Radar Animation with OpenMap Overlay**

Automatically fetches the latest radar animation frames from Environment Canada (ECCC) and overlays them on high-quality OpenStreetMap backgrounds.

**Key Features:**
- ✅ Fetches 8 frames of radar data (configurable)
- ✅ Overlays radar on terrain-style OpenStreetMap
- ✅ Automatic timestamp detection from ECCC WMS
- ✅ Configurable center point and zoom level
- ✅ 75% opacity radar overlay for optimal visibility
- ✅ High-quality rendering with anti-aliasing

**Service:** `RadarAnimationService.cs`

### 2. **Global Weather Map with Temperatures**

Generates a global or regional weather map showing all configured locations with current temperatures overlaid.

**Key Features:**
- ✅ OpenStreetMap background (standard or terrain style)
- ✅ Temperature display for multiple locations
- ✅ Weather icons for each location
- ✅ Configurable position for each city/temperature
- ✅ Professional styling with shadows
- ✅ Timestamp header

**Service:** `GlobalWeatherMapService.cs`

### 3. **Video Generator Integration**

The existing `VideoGenerator` automatically handles radar animation frames and composites them with weather stills during video generation.

## Configuration

Add these settings to your `appsettings.json`:

### ImageGeneration Section

```json
"ImageGeneration": {
  "OutputDirectory": "WeatherImages",
  "ImageWidth": 1920,
  "ImageHeight": 1080,
  "ImageFormat": "png",
  "MarginPixels": 50,
  "FontFamily": "Arial",
  "EnableWeatherMaps": true,
  "EnableRadarAnimation": true,
  "EnableGlobalWeatherMap": true
}
```

**Settings:**
- `EnableWeatherMaps`: Master switch for all map generation (default: `true`)
- `EnableRadarAnimation`: Enable radar animation with OpenMap overlay (default: `true`)
- `EnableGlobalWeatherMap`: Enable global weather map with temperatures (default: `true`)

### ECCC Section

```json
"ECCC": {
  "EnableProvinceRadar": true,
  "UseGeoMetWms": true,
  "ProvinceRadarLayer": "RADAR_1KM_RRAI",
  "ProvinceFrames": 8,
  "ProvinceImageWidth": 1920,
  "ProvinceImageHeight": 1080,
  "ProvinceFrameStepMinutes": 6,
  "ProvincePaddingDegrees": 1.0,
  "ProvinceEnsureCities": [
    "Montreal",
    "Quebec City",
    "Amos"
  ],
  "DelayBetweenRequestsMs": 200
}
```

**Radar Settings:**
- `EnableProvinceRadar`: Enable province-level radar fetching (default: `true`)
- `UseGeoMetWms`: Use ECCC GeoMet WMS service (default: `true`)
- `ProvinceRadarLayer`: WMS layer name (default: `"RADAR_1KM_RRAI"`)
- `ProvinceFrames`: Number of animation frames to fetch (default: `8`)
- `ProvinceImageWidth`: Frame width in pixels (default: `1920`)
- `ProvinceImageHeight`: Frame height in pixels (default: `1080`)
- `ProvinceFrameStepMinutes`: Time between frames in minutes (default: `6`)
- `ProvinceEnsureCities`: Cities to include in view (used to calculate center point)
- `ProvincePaddingDegrees`: Extra padding around bounding box (default: `1.0`)

### MapLocations Section

Configure where city names and temperatures appear on the weather map:

```json
"MapLocations": {
  "Location0": {
    "CityPositionX": 850,
    "CityPositionY": 450,
    "TemperaturePositionX": 850,
    "TemperaturePositionY": 500
  },
  "Location1": {
    "CityPositionX": 1200,
    "CityPositionY": 600,
    "TemperaturePositionX": 1200,
    "TemperaturePositionY": 650
  }
}
```

**Position Settings:**
- `CityPositionX/Y`: Pixel coordinates for city name
- `TemperaturePositionX/Y`: Pixel coordinates for temperature display

## Usage

### Automatic Integration

When `EnableWeatherMaps` is `true`, the system automatically:

1. **Fetches radar frames** from ECCC with timestamps
2. **Generates OpenMap backgrounds** for each frame
3. **Composites radar over maps** with proper opacity
4. **Saves frames** to `WeatherImages/province_frames/`
5. **Generates global weather map** with temperatures
6. **Integrates with video** during generation

### Manual Usage

#### Generate Radar Animation

```csharp
var radarService = new RadarAnimationService(httpClient, 1920, 1080);

var frames = await radarService.GenerateRadarAnimationWithMapAsync(
    centerLat: 48.5,
    centerLon: -71.0,
    outputDir: "WeatherImages",
    numFrames: 8,
    frameStepMinutes: 6,
    width: 1920,
    height: 1080,
    radarLayer: "RADAR_1KM_RRAI",
    zoomLevel: 7);

// frames contains list of generated frame paths
```

#### Generate Weather Map

```csharp
var weatherMapService = new GlobalWeatherMapService(1920, 1080);

await weatherMapService.GenerateWeatherMapAsync(
    weatherData: allForecasts,
    locationNames: locations,
    outputPath: "WeatherImages/00_WeatherMaps.png",
    centerLat: 48.5,
    centerLon: -71.0,
    zoomLevel: 6);
```

#### Generate Single Static Radar Map

```csharp
var radarService = new RadarAnimationService(httpClient, 1920, 1080);

var mapPath = await radarService.GenerateSingleRadarMapAsync(
    centerLat: 45.5,
    centerLon: -73.6,
    outputPath: "radar_montreal.png",
    width: 1920,
    height: 1080,
    radarLayer: "RADAR_1KM_RRAI",
    zoomLevel: 8);
```

## Output Files

### Radar Animation Frames

Located in: `WeatherImages/province_frames/`

- `frame_000.png` - Oldest frame
- `frame_001.png`
- ...
- `frame_007.png` - Most recent frame

Each frame is a composite of:
1. OpenStreetMap terrain background
2. ECCC radar overlay (75% opacity)
3. High-quality rendering

### Weather Map

Located in: `WeatherImages/00_WeatherMaps.png`

Contains:
- OpenStreetMap background
- City names (configurable positions)
- Current temperatures (large font)
- Weather icons
- Header with title and timestamp

## Video Integration

The `VideoGenerator` automatically uses radar frames when found in `province_frames/` directory:

```csharp
var videoGenerator = new VideoGenerator(outputDir)
{
    UseOverlayMode = true,  // Enable radar overlay
    StaticMapPath = "path/to/static/map.png",  // Optional static base
    OverlayTargetFilename = "WeatherMaps"  // Target still for overlay
};

videoGenerator.Generate();
```

**Video Flow:**
1. Weather stills are generated
2. Radar frames are found in `province_frames/`
3. During video encoding, radar frames animate over specified still
4. Result: Animated radar on one frame, static weather on others

## Troubleshooting

### No Radar Frames Generated

**Check:**
1. `EnableWeatherMaps` is `true`
2. `EnableRadarAnimation` is `true`
3. `EnableProvinceRadar` is `true` in ECCC section
4. Internet connection is available
5. ECCC GeoMet service is accessible

**Logs:**
```
[RadarAnimation] Generating radar animation with 8 frames
[RadarAnimation] Found 8 radar timestamps
✓ Base map generated
[RadarAnimation] Processing frame 1/8...
✓ Frame 1 saved: frame_000.png
```

### Weather Map Shows No Temperatures

**Check:**
1. `MapLocations` is configured in appsettings.json
2. Positions are not (0,0)
3. Weather data was successfully fetched
4. `EnableGlobalWeatherMap` is `true`

### OpenMap Tiles Not Loading

**Check:**
1. Internet connection
2. OSM tile servers are accessible
3. User-Agent is properly set
4. Rate limiting is not triggered (200ms delay between requests)

### Radar Timestamps Not Found

**Fallback Behavior:**
- System generates timestamps automatically
- Uses current time minus frame intervals
- May result in missing data for some frames

## Performance

### Typical Generation Times

- **Radar animation (8 frames)**: 15-30 seconds
  - Base map: 3-5 seconds (generated once)
  - Per frame: 1-2 seconds (radar fetch + composite)
  
- **Weather map**: 5-10 seconds
  - Base map: 3-5 seconds
  - Overlay rendering: 1-2 seconds

### Optimization Tips

1. **Reduce frame count** if generation is slow
2. **Use smaller resolution** for faster testing
3. **Cache base maps** (already implemented)
4. **Adjust DelayBetweenRequestsMs** if rate limited

## API Credits & Attribution

### OpenStreetMap

When using this library, you MUST:
- ✅ Display attribution: "© OpenStreetMap contributors"
- ✅ Link to: https://www.openstreetmap.org/copyright
- ✅ Comply with OSM Tile Usage Policy
- ✅ Cache tiles appropriately
- ✅ NOT bulk download or offline prefetch

See: `OpenMap/LEGAL.md` for full legal requirements

### ECCC GeoMet

- Service: https://geo.weather.gc.ca/geomet
- No API key required
- Rate limiting: Respect 200ms delays
- Terms: https://eccc-msc.github.io/open-data/licence/readme_en/

## Advanced Configuration

### Custom Bounding Box

```csharp
await weatherMapService.GenerateWeatherMapWithBboxAsync(
    weatherData,
    locationNames,
    outputPath,
    minLat: 45.0,
    minLon: -80.0,
    maxLat: 52.0,
    maxLon: -65.0);
```

### Custom City Coordinates

Update `GetCityCoordinates()` in `Program.cs`:

```csharp
var cityCoords = new Dictionary<string, (double lat, double lon)>
{
    { "MyCity", (45.123, -73.456) },
    // ... more cities
};
```

### Different Radar Layers

Available ECCC layers:
- `RADAR_1KM_RRAI` - 1km Rain Rate (default)
- `RADAR_1KM_RSNO` - 1km Snow Rate
- `RADAR_COMPOSITE` - Composite radar
- See ECCC documentation for full list

## Future Enhancements

Potential improvements:
- [ ] Animated GIF output for radar
- [ ] Temperature gradients/heat maps
- [ ] Wind direction arrows
- [ ] Precipitation forecasts overlay
- [ ] Multiple radar layers composited
- [ ] Real-time streaming updates

## Support

For issues or questions:
1. Check logs in `logs/` directory
2. Verify configuration in `appsettings.json`
3. Test with `--generate-province-animation` flag
4. Review ECCC service status

## License

This integration uses:
- **OpenStreetMap data**: ODbL License
- **ECCC data**: Open Government License - Canada
- **Project code**: See repository LICENSE

---

**Last Updated:** January 2026  
**Version:** 1.0
