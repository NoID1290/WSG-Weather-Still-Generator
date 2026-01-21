# 🌧 ECCC Radar Image Integration

## Overview

The Weather Still Generator now includes integrated radar imagery from Environment and Climate Change Canada (ECCC) in the detailed weather forecast view. This feature allows users to view real-time precipitation radar data centered on their selected location.

## Features

### 1. **Radar Image Service**
- **Location**: `ECCC/Services/RadarImageService.cs`
- Fetches radar images from ECCC's GeoMet WMS service
- Uses the `RADAR_1KM_RRAI` layer (1km Resolution Rain Rate Radar)
- Automatically calculates bounding box based on location coordinates
- Configurable radius, width, and height

### 2. **Weather Details Form Integration**
- **Location**: `WeatherImageGenerator/Forms/WeatherDetailsForm.cs`
- New "🌧 Radar" tab in the Weather Details dialog
- Displays radar image centered on the selected location
- Includes:
  - Location coordinates display
  - Radar layer description
  - Refresh button for updating the image
  - Loading status indicator
  - Error handling with user-friendly messages

### 3. **ECCCApi Extension**
- **Location**: `ECCC/ECCCApi.cs`
- New `GetRadarImageAsync()` method for easy radar image fetching
- Integrated logging support
- Consistent error handling

## Usage

### From Code

#### Fetch Radar Image Directly
```csharp
using System.Net.Http;
using ECCC;

var httpClient = new HttpClient();

// Fetch radar image for Montreal (45.5°N, 73.6°W)
var imageData = await ECCCApi.GetRadarImageAsync(
    httpClient,
    latitude: 45.5,
    longitude: -73.6,
    radiusKm: 150,    // Optional: 150km radius
    width: 800,        // Optional: 800px width
    height: 600        // Optional: 600px height
);

if (imageData != null)
{
    // Save to file
    await File.WriteAllBytesAsync("radar.png", imageData);
}
```

#### Use RadarImageService Directly
```csharp
using ECCC.Services;

var radarService = new RadarImageService(httpClient);
var imageData = await radarService.FetchRadarImageAsync(
    latitude: 45.5,
    longitude: -73.6,
    radiusKm: 100
);
```

### From UI

1. **Open Weather Details**:
   - Click on any location in the main window
   - Select "View Details" or double-click a location

2. **View Radar**:
   - Navigate to the "🌧 Radar" tab
   - The radar image loads automatically
   - Click "🔄 Refresh Radar" to update the image

## Technical Details

### WMS Parameters

The radar service uses the following WMS configuration:

- **Service**: WMS (Web Map Service)
- **Version**: 1.3.0
- **Request**: GetMap
- **Layer**: `RADAR_1KM_RRAI` (1km Resolution Rain Rate Radar)
- **CRS**: EPSG:4326 (WGS84 Geographic)
- **Format**: image/png
- **Transparent**: TRUE

### Bounding Box Calculation

The bounding box is calculated based on the location coordinates and radius:

```csharp
// Approximate: 1 degree latitude ≈ 111 km
double latDelta = radiusKm / 111.0;

// 1 degree longitude varies by latitude
double lonDelta = radiusKm / (111.0 * Math.Cos(latitude * Math.PI / 180.0));

var bbox = (
    MinLat: latitude - latDelta,
    MinLon: longitude - lonDelta,
    MaxLat: latitude + latDelta,
    MaxLon: longitude + lonDelta
);
```

### Default Values

- **Radius**: 150km (provides good regional coverage)
- **Width**: 800px
- **Height**: 600px
- **Layer**: RADAR_1KM_RRAI (rain rate)

## Data Source

All radar data is provided by:
- **Environment and Climate Change Canada (ECCC)**
- **Service**: GeoMet WMS
- **URL**: https://geo.weather.gc.ca/geomet
- **Documentation**: https://eccc-msc.github.io/open-data/msc-geomet/readme_en/

## Available Radar Layers

While the default layer is `RADAR_1KM_RRAI`, ECCC provides several other radar layers:

| Layer | Description |
|-------|-------------|
| `RADAR_1KM_RRAI` | 1km Rain Rate (default) |
| `RADAR_1KM_RSNO` | 1km Snow Rate |
| `RADAR_1KM_RDBR` | 1km Rain/Snow Rate |
| `RADAR_COVERAGE_RRAI.INV` | Radar Coverage |

To use a different layer, modify the `DefaultRadarLayer` constant in `RadarImageService.cs`.

## Error Handling

The radar feature includes comprehensive error handling:

1. **Network Errors**: Displays user-friendly message if the service is unavailable
2. **Invalid Coordinates**: Checks for valid latitude/longitude values
3. **Missing Data**: Handles cases where forecast data doesn't include coordinates
4. **Image Loading**: Catches and displays errors during image processing

## Future Enhancements

Potential improvements for future versions:

1. **Layer Selection**: Allow users to choose different radar layers
2. **Animation**: Fetch multiple time steps to create radar animation
3. **Overlay**: Combine radar with base map for better context
4. **Time Selection**: View historical radar data
5. **Auto-Refresh**: Automatically update radar image at intervals
6. **Caching**: Cache radar images to reduce network requests
7. **Export**: Save radar images to file

## Integration with 5-Day Forecast

The radar image is seamlessly integrated into the 5-day detailed weather forecast view:

- **Tab-based Interface**: Radar is one of several tabs (Current, Forecast, Hourly, Radar, Alerts)
- **Contextual Data**: Shows radar centered on the forecast location
- **Consistent Design**: Matches the styling of other weather detail panels
- **Real-time Updates**: Can be refreshed independently of other forecast data

## Performance Considerations

- **Lazy Loading**: Radar image is loaded only when the tab is viewed
- **Async Operations**: All network requests are asynchronous to prevent UI blocking
- **Memory Management**: Proper disposal of image resources
- **Error Recovery**: Graceful degradation when radar service is unavailable

## Dependencies

- **System.Net.Http**: For HTTP requests to ECCC WMS service
- **System.Drawing**: For image processing and display
- **ECCC.Models**: For ECCC data types and settings
- **System.Threading.Tasks**: For asynchronous operations

## Configuration

No additional configuration is required. The radar feature works out-of-the-box with any location that has valid coordinates in the forecast data.

To adjust default settings, modify the constants in `RadarImageService.cs`:

```csharp
private const string DefaultRadarLayer = "RADAR_1KM_RRAI";
private const int DefaultWidth = 800;
private const int DefaultHeight = 600;
private const double DefaultRadiusKm = 150;
```

## Troubleshooting

### Radar Image Not Loading

1. **Check Internet Connection**: Ensure network connectivity to ECCC services
2. **Verify Coordinates**: Confirm the location has valid lat/lon in forecast data
3. **Service Status**: Check if ECCC GeoMet service is operational
4. **Firewall**: Ensure access to `https://geo.weather.gc.ca/geomet`

### Image Quality Issues

- **Increase Resolution**: Adjust `width` and `height` parameters
- **Adjust Radius**: Modify `radiusKm` for better coverage or detail
- **Check Location**: Verify location is within ECCC radar coverage area (primarily Canada)

## License & Attribution

This feature uses public data from Environment and Climate Change Canada (ECCC). When using or redistributing, please include appropriate attribution:

"Radar data provided by Environment and Climate Change Canada (ECCC)"

## References

- [ECCC GeoMet Documentation](https://eccc-msc.github.io/open-data/msc-geomet/readme_en/)
- [WMS Specification](https://www.ogc.org/standards/wms)
- [ECCC Open Data Portal](https://weather.gc.ca/grib/index_e.html)
