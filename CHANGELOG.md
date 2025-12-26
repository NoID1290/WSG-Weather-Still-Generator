# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.7.8.1226] - 2025-12-26

### Added
- **Video Generation**:
  - Support for hardware encoding (`EnableHardwareEncoding`).
  - Configurable video codec and bitrate.
  - Option to show FFmpeg output in GUI.
  - Toggle for video generation (`doVideoGeneration`).
- **Configuration**:
  - Expanded location support to 9 locations (Location0 - Location8).
  - Centralized `appsettings.json` for all configuration.
- **Weather Data**:
  - Integration with OpenMeteo API.
  - Support for Air Quality, Daily, Hourly, and Minutely data.

### Changed
- Updated project to .NET 8.0.
- Improved error handling and retry logic for weather fetching.
- Refactored `WeatherImageGenerator` to use `appsettings.json` instead of hardcoded values.

### Fixed
- Various bug fixes and performance improvements.

## [0.6.15.1225] - 2025-12-25

### Added
- Initial release of the refactored Weather Still Generator.
- Basic image generation for current weather and forecasts.
- Alert system integration with Environment Canada.
