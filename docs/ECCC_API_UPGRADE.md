# ECCC Official API Integration

## Overview

The ECCC library now supports fetching weather data from Environment and Climate Change Canada's **official OGC API** at https://api.weather.gc.ca, which provides significantly better data quality compared to RSS feeds.

## Why Use the Official API?

### RSS Feeds (Old Method)
- **Format**: XML-based, designed for human consumption
- **Data Quality**: Text summaries that require parsing
- **Structure**: Limited numeric data
- **Reliability**: Can change format unexpectedly
- **Coverage**: City weather only

### Official API (New Method)
- **Format**: JSON-based, machine-readable
- **Data Quality**: Exact numeric values with units
- **Structure**: Comprehensive data structure with:
  - `currentConditions`: Temperature, humidity, wind speed, pressure, dewpoint
  - `forecastGroup`: 7-10 day daily forecast with highs/lows
  - `hourlyForecastGroup`: 24-hour detailed hourly forecast
  - Sunrise/sunset times, regional normals
- **Reliability**: Official government API with stable structure
- **Coverage**: 844 Canadian cities

## API Endpoint

**Base URL**: `https://api.weather.gc.ca`

**Collection**: `citypageweather-realtime`

**Query Example**:
```
https://api.weather.gc.ca/collections/citypageweather-realtime/items?f=json&identifier=qc-147&limit=1
```

Where `qc-147` is the city identifier (in this case, Montreal, Quebec).

## Sample Response Structure

```json
{
  "type": "FeatureCollection",
  "features": [{
    "properties": {
      "currentConditions": {
        "temperature": {"value": -12.3, "unitCode": "degC"},
        "humidity": {"value": 84, "unitCode": "percent"},
        "windSpeed": {"value": 5, "unitCode": "km/h"},
        "pressure": {"value": 102.4, "unitCode": "kPa"},
        "dewpoint": {"value": -14.5, "unitCode": "degC"}
      },
      "forecastGroup": {
        "forecast": [
          {
            "period": {"textForecastName": "Tonight"},
            "temperatures": {"temperature": {"value": -15, "class": "low"}},
            "abbreviatedForecast": {"textSummary": "Cloudy"}
          }
        ]
      },
      "hourlyForecastGroup": {
        "hourlyForecast": [
          {
            "dateTimeUTC": "2024-01-09T00:00:00Z",
            "temperature": {"value": -12},
            "windSpeed": {"value": 5}
          }
        ]
      }
    }
  }]
}
```

## Code Usage

### Method 1: Direct API Call with City ID

```csharp
using WeatherImageGenerator.Services;

var client = new HttpClient();

// Fetch weather for Montreal (qc-147)
var forecast = await ECCC.FetchWeatherFromApiAsync(client, "qc-147", "f");

if (forecast != null)
{
    Console.WriteLine($"Current temperature: {forecast.Current?.Temperature_2m}°C");
    Console.WriteLine($"Humidity: {forecast.Current?.Relativehumidity_2m}%");
    Console.WriteLine($"Wind speed: {forecast.Current?.Windspeed_10m} km/h");
}
```

### Method 2: Automatic API Fallback

The `FetchWeatherForecastByCityAsync` method now **automatically** tries the API first if it can extract the city ID from the configured RSS feed URL:

```csharp
// In appsettings.json:
{
  "ECCC": {
    "CityFeeds": {
      "Montréal": "https://weather.gc.ca/rss/city/qc-147_f.xml"
    }
  }
}

// In code - it will automatically try the API first:
var forecast = await ECCC.FetchWeatherForecastByCityAsync(client, "Montréal");
// 1. Extracts "qc-147" from the RSS URL
// 2. Tries FetchWeatherFromApiAsync("qc-147") first
// 3. Falls back to RSS parsing if API fails
```

## City Identifiers

City identifiers follow the format: `{province-code}-{city-number}`

Examples:
- **Montreal**: `qc-147`
- **Toronto**: `on-143`
- **Vancouver**: `bc-74`
- **Calgary**: `ab-52`
- **Fort Albany**: `on-173`

You can discover available cities by querying:
```
https://api.weather.gc.ca/collections/citypageweather-realtime/items?f=json&limit=1000
```

## Implementation Details

### New Methods in ECCC.cs

#### `FetchWeatherFromApiAsync()`
- Fetches weather from the official API using a city identifier
- Returns `OpenMeteo.WeatherForecast` object for compatibility
- Supports both English ("en") and French ("f") languages

#### `ParseEcccApiJson()`
- Parses the JSON response from the API
- Extracts current conditions, daily forecasts, and hourly forecasts
- Converts to OpenMeteo-compatible format for existing code

#### `FetchWeatherForecastByCityAsync()` (Enhanced)
- Now tries API first if city ID can be extracted from feed URL
- Falls back to RSS parsing if API fails
- Maintains backward compatibility

## Data Mapping

| ECCC API Field | OpenMeteo Property | Notes |
|----------------|-------------------|-------|
| `currentConditions.temperature.value` | `Current.Temperature_2m` | In °C |
| `currentConditions.humidity.value` | `Current.Relativehumidity_2m` | Percentage |
| `currentConditions.windSpeed.value` | `Current.Windspeed_10m` | In km/h |
| `currentConditions.pressure.value` | `Current.Surface_pressure` | Converted from kPa to hPa (×10) |
| `forecastGroup.forecast[].temperatures.temperature.value` | `Daily.Temperature_2m_max/min` | Daily highs/lows |
| `hourlyForecastGroup.hourlyForecast[].temperature.value` | `Hourly.Temperature_2m` | Hourly temps |
| `hourlyForecastGroup.hourlyForecast[].windSpeed.value` | `Hourly.Windspeed_10m` | Hourly wind |

## Additional Resources

### GeoMet Services
ECCC also provides **GeoMet** for weather maps and layers:
- **WMS** (Web Map Service): Weather radar, precipitation, temperature layers
- **WCS** (Web Coverage Service): Gridded data
- **WFS** (Web Feature Service): Alerts and warnings

**Base URL**: `https://geo.weather.gc.ca/geomet`

Example WMS radar layer:
```
https://geo.weather.gc.ca/geomet?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=RADAR_1KM_RRAI&...
```

### MSC Datamart
For raw GRIB2 data files:
- **Base URL**: `https://dd.weather.gc.ca`
- Contains numerical weather prediction model outputs
- Requires specialized parsing (GRIB2 format)

## Migration Guide

### For Existing Code

No changes required! The enhanced `FetchWeatherForecastByCityAsync()` automatically uses the API when possible and falls back to RSS feeds.

### For New Code

Prefer using the API directly:

```csharp
// Old way (RSS)
var forecast = await ECCC.FetchWeatherForecastAsync(client, "qc", "147", "f");

// New way (API)
var forecast = await ECCC.FetchWeatherFromApiAsync(client, "qc-147", "f");
```

## Benefits Summary

✅ **Better Data Quality**: Exact numeric values instead of text parsing  
✅ **More Data**: Current + daily + hourly forecasts in one call  
✅ **Faster**: Single JSON request vs multiple RSS feed fetches  
✅ **More Reliable**: Official government API with stable structure  
✅ **Backward Compatible**: Existing code continues to work  
✅ **Automatic Fallback**: RSS feeds still available if API unavailable  

## Future Enhancements

Potential improvements:
- Add GeoMet integration for weather radar imagery
- Support for weather alerts via the API
- City search/discovery using the API
- Historical weather data access
- Marine forecasts and aviation weather

## Documentation

Official ECCC API documentation:
- **Main API**: https://api.weather.gc.ca/
- **GeoMet**: https://eccc-msc.github.io/open-data/msc-geomet/readme_en/
- **MSC Datamart**: https://eccc-msc.github.io/open-data/msc-datamart/readme_en/
