# WeatherImageGenerator Configuration Guide

## Overview

The WeatherImageGenerator now uses a centralized **`appsettings.json`** configuration file for all editable settings. This allows you to modify application behavior **without recompiling** the project.

## Configuration File Location

```
WeatherImageGenerator/appsettings.json
```

## Configuration Sections

### 1. **Locations** - Weather Query Locations

Define the 7 locations for weather data fetching.

```json
"Locations": {
  "Location0": "Montréal",
  "Location1": "Saint-Rémi",
  "Location2": "Québec",
  "Location3": "Amos",
  "Location4": "Sherbrooke",
  "Location5": "Trois-Rivières",
  "Location6": "Gatineau"
}
```

- **Default:** 7 Quebec locations
- **Usage:** Change city names to query different locations
- **Note:** Location0 is considered the primary location

### 2. **RefreshTimeMinutes** - Update Interval

How often the application fetches new weather data.

```json
"RefreshTimeMinutes": 10
```

- **Default:** 10 minutes
- **Valid Range:** Positive integers only
- **Usage:** Set to 15 for 15-minute updates, 5 for 5-minute updates

### 3. **ImageGeneration** - Image Output Settings

Controls image dimensions and rendering properties.

```json
"ImageGeneration": {
  "OutputDirectory": "WeatherImages",
  "ImageWidth": 1920,
  "ImageHeight": 1080,
  "MarginPixels": 50,
  "FontFamily": "Arial"
}
```

| Setting | Default | Notes |
|---------|---------|-------|
| OutputDirectory | `WeatherImages` | Folder where images are saved (relative to working directory) |
| ImageWidth | `1920` | Image width in pixels |
| ImageHeight | `1080` | Image height in pixels |
| MarginPixels | `50` | Padding around image content |
| FontFamily | `Arial` | Font used for all text (must be installed on system) |

### 4. **Video** - Video Generation Settings

Configuration for MP4 slideshow generation.

```json
"Video": {
  "OutputFileName": "slideshow_v3.mp4",
  "MusicFileName": "music.mp3",
  "StaticDurationSeconds": 8,
  "FadeDurationSeconds": 0.5,
  "FrameRate": 30,
  "ResolutionMode": "Mode1080p",
  "EnableFadeTransitions": false
}
```

| Setting | Default | Valid Values | Notes |
|---------|---------|--------------|-------|
| OutputFileName | `slideshow_v3.mp4` | Any valid filename | Output video file name |
| MusicFileName | `music.mp3` | Any valid filename | Background music file |
| StaticDurationSeconds | `8` | Positive numbers | How long each image displays |
| FadeDurationSeconds | `0.5` | Positive numbers | Fade transition duration |
| FrameRate | `30` | 24, 30, 60 | Output video frame rate |
| ResolutionMode | `Mode1080p` | `Mode1080p`, `Mode4K`, `ModeVertical` | Output resolution |
| EnableFadeTransitions | `false` | `true`, `false` | Enable fade effects between images |

### 5. **Alerts** - Weather Alerts Display

Customize how Environment Canada weather alerts appear.

```json
"Alerts": {
  "HeaderText": "⚠️ Environment Canada Alerts",
  "NoAlertsText": "No Active Warnings or Watches",
  "HeaderFontSize": 48,
  "CityFontSize": 28,
  "TypeFontSize": 28,
  "DetailsFontSize": 22,
  "AlertFilename": "6_WeatherAlerts.png"
}
```

| Setting | Default | Notes |
|---------|---------|-------|
| HeaderText | Alert header text | Main title for alerts image |
| NoAlertsText | Text when no alerts | Displayed when no active alerts |
| HeaderFontSize | `48` | Font size in points |
| CityFontSize | `28` | Font size for city names |
| TypeFontSize | `28` | Font size for alert types |
| DetailsFontSize | `22` | Font size for alert details |
| AlertFilename | `6_WeatherAlerts.png` | Output filename |

### 6. **WeatherImages** - Image Filenames

Output filenames for each generated image.

```json
"WeatherImages": {
  "CurrentWeatherFilename": "1_CurrentWeather.png",
  "DailyForecastFilename": "2_DailyForecast.png",
  "DetailedWeatherFilename": "3_DetailedWeather.png",
  "WeatherMapsFilename": "4_WeatherMaps.png",
  "TemperatureWatermarkFilename": "temp_watermark_alpha.png.IGNORE",
  "StaticMapFilename": "STATIC_MAP.IGNORE"
}
```

- Rename any image to customize output filenames
- `.IGNORE` files are temporary/overlay images not displayed in the main slideshow

### 7. **MapLocations** - City Position Coordinates

Pixel coordinates for where cities appear on the weather map image.

```json
"MapLocations": {
  "Location0": {
    "CityPositionX": 1131,
    "CityPositionY": 900,
    "TemperaturePositionX": 1131,
    "TemperaturePositionY": 950
  },
  ...
}
```

- **Usage:** Update X/Y coordinates to reposition city labels on map
- **Note:** Only Locations 0, 2, 3, and 6 have default positions
- **Coordinates:** Measured in pixels from top-left corner (0,0)

### 8. **ECCC** - Environment Canada Settings

Configuration for fetching weather alerts from Environment and Climate Change Canada.

```json
"ECCC": {
  "CityFeeds": {
    "Montreal": "https://weather.gc.ca/rss/city/qc-147_f.xml",
    "Quebec City": "https://weather.gc.ca/rss/city/qc-133_f.xml",
    "Gatineau": "https://weather.gc.ca/rss/city/qc-59_f.xml",
    "Amos": "https://weather.gc.ca/rss/alerts/48.574_-78.116_f.xml",
    "Saint-Rémi": "https://weather.gc.ca/rss/alerts/45.263_-73.620_f.xml"
  },
  "RadarFeeds": {
    "Montreal": "https://<your-eccc-radar-url>/montreal.gif",
    "Quebec City": "https://<your-eccc-radar-url>/quebeccity.gif",
    "Amos": "https://<your-eccc-radar-url>/amos.gif"
  },
  "ProvinceRadarUrl": "https://<your-eccc-radar-url>/quebec_province.gif",
  "DelayBetweenRequestsMs": 200,
  "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
}

| Setting | Default | Notes |
|---------|---------|-------|
| CityFeeds | Dictionary of cities and RSS URLs | Add/remove cities or update feed URLs |
| RadarFeeds | Dictionary of cities and radar image URLs | Add radar image URLs (ECCC or other) to show per-city thumbnails on the maps image |
| UseGeoMetWms | boolean | When true (default) the application will request city and province radar images from the MSC GeoMet WMS if no direct `RadarFeeds` entry is provided |
| CityRadarLayer | string | Which GeoMet `LAYERS` name to use for city thumbnails (default `RADAR_1KM_RRAI`) |
| ProvinceRadarLayer | string | Which GeoMet `LAYERS` name to use to build a province animation (default `RADAR_1KM_RRAI`) |
| ProvinceFrames | integer | Number of frames to request when building the province animation from GeoMet WMS (default `8`) |
| ProvinceRadarUrl | string | URL to province-wide animated radar GIF (saved as `00_ProvinceRadar.gif` and used fullscreen on maps image) |
| DelayBetweenRequestsMs | `200` | Milliseconds to wait between requests (politeness setting) |
| UserAgent | Standard Mozilla UA | HTTP User-Agent header for requests |
```

| Setting | Default | Notes |
|---------|---------|-------|
| CityFeeds | Dictionary of cities and RSS URLs | Add/remove cities or update feed URLs |
| DelayBetweenRequestsMs | `200` | Milliseconds to wait between requests (politeness setting) |
| UserAgent | Standard Mozilla UA | HTTP User-Agent header for requests |

## How to Edit Configuration

1. **Open** `WeatherImageGenerator/appsettings.json` in your editor
2. **Modify** any values (ensure valid JSON syntax)
3. **Save** the file
4. **Restart** the application to apply changes
5. **No recompilation** needed!

## Example: Common Changes

### Change Update Interval to 5 Minutes
```json
"RefreshTimeMinutes": 5
```

### Add a New Location
```json
"Locations": {
  "Location0": "Montréal",
  "Location1": "Toronto",  // Changed
  "Location2": "Québec",
  ...
}
```

### Increase Image Resolution to 4K
```json
"ImageGeneration": {
  "ImageWidth": 3840,
  "ImageHeight": 2160,
  ...
}
```

### Disable Video Generation by Changing Output
```json
"Video": {
  "StaticDurationSeconds": 0,  // Skip video processing
  ...
}
```

## Validation

- **JSON Format:** Must be valid JSON (use a JSON validator if unsure)
- **Required Fields:** All fields in the template should be present
- **Data Types:** Follow the specified types (numbers, strings, booleans)
- **Strings:** Always use double quotes (`"value"`)
- **Numbers:** No quotes needed (`123`)
- **Booleans:** Use `true` or `false` (lowercase)

## Troubleshooting

### Config File Not Loading
- Ensure `appsettings.json` is in the application's working directory
- Check JSON syntax for errors (missing commas, quotes, braces)
- Verify file name is exactly `appsettings.json`

### Changes Not Applied
- **Restart the application** after editing the config
- Verify changes were saved to the file
- Check file is not read-only

### Application Crashes on Startup
- Check the JSON is valid (use an online JSON validator)
- Verify all required fields are present
- Look for special characters that need escaping (use `\\` for backslashes)

## Default Configuration

If `appsettings.json` is missing, the application will use built-in defaults and log an error. Restore the original config file to resume normal operation.

## Advanced: Multiple Configurations

You can create different config files and switch between them:

```powershell
# Backup current config
Copy-Item appsettings.json appsettings.backup.json

# Use a different config
Copy-Item appsettings.test.json appsettings.json
```

---

**Version:** 1.0  
**Last Updated:** December 12, 2025
