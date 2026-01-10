# WSG - Weather Still Generator

> Automated weather image and video generation system for displaying current conditions, forecasts, and weather alerts.

![Build Status](https://img.shields.io/badge/build-passing-brightgreen) ![License](https://img.shields.io/badge/license-MIT-blue) ![.NET Version](https://img.shields.io/badge/.NET-8.0-purple)

##  Overview

**WSG (Weather Still Generator)** is a comprehensive weather visualization application that automatically generates high-quality still images and MP4 videos displaying current weather conditions, forecasts, and alerts. Perfect for digital signage, weather stations, streaming overlays, or information displays.

The system continuously fetches weather data, processes it into beautiful images, and can compile them into slideshow videos with configurable transitions and background music.

##  Features

- ** Multiple Image Types**
  - Current weather conditions (temperature, humidity, wind speed)
  - Daily forecasts (high/low temperatures, conditions)
  - Detailed weather analysis (pressure, wind direction)
  - Location-based weather maps with city overlays
  - Temperature watermark overlays
  - Weather alerts from Environment Canada

- ** Video Generation**
  - MP4 slideshow creation with FFmpeg
  - Configurable fade transitions
  - Custom static duration per image
  - Multiple resolution modes (1080p, 4K, Vertical)
  - Background music integration
  - Customizable frame rates
  - Hardware encoding support

- ** Alert System**
  - Real-time Environment Canada weather alerts
  - Color-coded severity (Red/Yellow/Gray)
  - Automatic alert categorization (Warning/Watch/Statement)
  - Multi-city alert aggregation

- ** Highly Configurable**
  - JSON-based configuration (no recompile needed)
  - Customizable locations, fonts, colors
  - Adjustable refresh intervals
  - Dynamic image dimensions
  - Flexible output paths

- ** Automated Updates**
  - Continuous operation with configurable update cycles
  - Reliable error handling and retry logic
  - Console logging for monitoring
  - Cross-platform compatibility (Windows/.NET 8.0)

##  Documentation

- [Configuration Guide](CONFIG_README.md) - Detailed guide on ppsettings.json
- [Push Script Guide](PUSH_SCRIPT_GUIDE.md) - How to use the auto-push script
- [Changelog](docs/CHANGELOG.md) - Version history

##  Project Structure

`
WSG-Weather-Still-Generator/
 OpenMeteo/                 # Weather data library
 OpenMeteoTests/            # Unit tests
 WeatherImageGenerator/     # Main application
    Forms/                 # GUI Forms
    Services/              # Core logic (Image, Video, Config)
    Utilities/             # Helper classes
    appsettings.json       # Configuration file
 build.ps1                  # Build script
 push.ps1                   # Deployment script
 update_version.ps1         # Versioning script
`

## Notes

- **logs/** — consolidated runtime and build logs moved to `logs/`. These are ignored via `.gitignore`.
- **docs/** — configuration and guides moved to `docs/` for clarity.
- **WeatherImageGenerator/logs/** — module-specific logs moved here as well.

##  Getting Started

1. **Build the project:**
   `powershell
   ./build.ps1
   `

2. **Configure settings:**
   Edit WeatherImageGenerator/appsettings.json to set your locations and preferences.

3. **Run the application:**
   Start WeatherImageGenerator.exe from the build output.

##  Development Scripts

- **uild.ps1**: Compiles the solution.
- **push.ps1**: Automates versioning, git commit, and push.
- **update_version.ps1**: Increments version numbers in project files.

##  License

This project is licensed under the MIT License.
