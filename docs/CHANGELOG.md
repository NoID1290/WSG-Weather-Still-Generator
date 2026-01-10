# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

















## [1.1.1.0110] - 2026-01-10

### Fixed
- Update build and test scripts to reference new WSG solution
## [1.1.0.0110] - 2026-01-10

### Changed
- Handle Dynamic Link and .NET10 update
- Update target framework to net10.0 across all project files
- Add WeatherShared and ECCC projects; refactor AlertEntry for shared use
- Refactor FFmpeg integration to use bundled binaries and improve initialization process
- Update changelog path in documentation and auto-push script
## [1.0.2.0109] - 2026-01-09

### Fixed
- Add experimental tab to settings form and enable tab control
- Disable CRF encoding option in video settings
- Add experimental features toggle and configuration option
## [1.0.1.0106] - 2026-01-06

### Fixed
- Add advanced video encoding options: CRF encoding, bitrate, buffer size, and encoder preset
## [1.0.0.0106] - 2026-01-06

### Added
- First Stable Release
- Add auto-start cycle
## [0.10.0.0105] - 2026-01-05

### Changed
- Implement log archival feature to manage UI log size and enhance performance
- Throttle log messages and update progress during output file replacement retries
- Add safety buffer to clip duration for smoother transitions
## [0.9.3.0105] - 2026-01-05

### Fixed
- Increase timeout for hardware encoding checks for RTX
## [0.9.2.0105] - 2026-01-05

### Fixed
- Enhance video generation process with temporary output handling and retry logic for file replacement
- 2026 date update
## [0.9.1.1230] - 2025-12-30

### Fixed
- Add total duration feature for video generation with configurable settings
## [0.9.0.1228] - 2025-12-28

### Changed
- Refactor GalleryForm to use a class-level top panel and improve theme application logic
- Add GalleryForm for displaying weather images and videos with refresh functionality
- `Added quality preset selection to settings form and updated config manager and appsettings.json accordingly`
- Adjust MainForm dimensions for improved layout
## [0.8.11.1228] - 2025-12-28

### Fixed
- Version bump
## [0.8.10.1228] - 2025-12-28

### Fixed
- `Improved memory management and cleanup in WeatherImageGenerator`
## [0.8.9.1228] - 2025-12-28

### Fixed
- Add video control buttons and time label to MediaViewerForm
- Fix changelog formatting for version 0.8.7.1228
## [0.8.8.1228] - 2025-12-28

### Fixed
- Enhance video playback handling with aspect ratio preservation and improved error logging


### Fixed
- `Updated appsettings.json with changes to VideoCodec and Theme`
## [0.8.6.1228] - 2025-12-28

### Fixed
- Updated .\push.ps1
- `Added GitHub release notes generation and changelog updates to push.ps1 script`
- `Updated AssemblyVersion, AssemblyFileVersion, and AssemblyInformationalVersion to 0.8.5.1228`
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
  - **New Locations Manager**: Added dedicated LocationsForm for managing weather fetch locations with add, edit, remove, and reorder capabilities.
- **Weather Data**:
  - Integration with OpenMeteo API.
  - Support for Air Quality, Daily, Hourly, and Minutely data.
- **User Interface**:
  - Added "Locations" button to main toolbar for easy access to location management.

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
















