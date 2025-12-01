# weather-still-api

A minimal PowerShell project that fetches free weather data from Open-Meteo and creates three still PNG images showing current conditions, a 24-hour hourly chart, and a 3-day forecast.

## Features
- No API key required — uses Open-Meteo (https://open-meteo.com)
- Pure PowerShell script (Windows PowerShell 5.1 compatible)
- Produces `current.png`, `hourly.png`, `forecast.png` in the `images/` directory

## Quick start
Open PowerShell and run:

```powershell
# Example: run the generator for New York City
PowerShell -NoProfile -ExecutionPolicy Bypass -File ./src/Generate-WeatherImages.ps1 -Latitude 40.7128 -Longitude -74.0060 -Location "New York, NY" -OutputDir ./images -Width 1200 -Height 600
```

Or run the helper example:

```powershell
./examples/run-example.ps1
```

## Requirements
- Windows PowerShell 5.1 (the script uses System.Drawing from .NET Framework available on Windows)
- Internet access to reach Open-Meteo API

## Files
- `src/Generate-WeatherImages.ps1` - main script
- `examples/run-example.ps1` - quick runner example
- `images/` - output images (created by the script)

## Notes
- The visuals are intentionally simple, created with System.Drawing and basic drawing primitives so they work with default Windows PowerShell.
- If you want higher fidelity or more advanced styling consider converting the drawing code to use other libraries or generating SVG + converting to PNG.

## License
MIT
