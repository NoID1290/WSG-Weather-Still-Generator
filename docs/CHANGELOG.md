# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.8.5.0204] - 2026-02-04

- Update helper executable test
- Add updater utility and integrate into update process

## [1.8.4.0204] - 2026-02-04

- Another Auto-Test update
- Implement fallback mechanism for applying pending updates during startup

## [1.8.3.0204] - 2026-02-04

- Auto-Update test

## [1.8.2.0204] - 2026-02-04

- fix auto-update process with deferred file replacement for locked files

## [1.8.1.0203] - 2026-02-03

- Add Piper TTS support and settings to the application
- Improve process handling and error reporting in AlertToneGenerator; enhance HttpClient usage in MainForm
- feat: Enhance Emergency Alert Generation with Video Support
- Add Alert Tone Generation and Testing Functionality
- Fix label widths in SettingsForm that overlaping

## [1.8.0.0202] - 2026-02-02

- Implement feature X to enhance user experience and optimize performance
- Add legal attribution overlays for OSM and weather data in multiple services
- Refactor button enable/disable logic in MainForm for improved visual feedback and maintainability
- Add cache summary logging and improve cache hit/miss tracking in MapOverlayService
- Add optional logging for cache and download activity in MapOverlayService
- Implement tile caching mechanism in MapOverlayService and update SettingsForm for cache directory configuration
- Implement silent mode for configuration saves to reduce log spam during UI state changes
- Enhance MainForm UI with modern styling and improved status indicators
- Refactor MainForm layout for improved organization and aesthetics
- Implement persistent UI state management for tab selection, splitter position, and window size
- Refactor MainForm layout for improved compactness and readability
- Enhance logging functionality with message throttling and timestamp formatting
- Add WSG auto-update
- Update version badge from 1.6.3 to 1.7.3

## [1.7.3.0201] - 2026-02-01

- Fix button alignment for WebUI in MainForm
- Update ECCC API URLs and enhance error logging Fetching optimization Fixing call for ECCC
- Enhance Web UI: Update weather data display, add refresh button, and improve layout for current weather and forecast sections
- fixing duplicate function that crash webgui

## [1.7.2.0128] - 2026-01-28

- Implement location management API
- Enhance weather data handling: request hourly fields and improve diagnostics for missing hourly data

## [1.7.1.0127] - 2026-01-27

- Add WebUI button in MainForm

## [1.7.0.0126] - 2026-01-26

- Beta integration WebUI accesing by open ports
- Update Web UI to display dynamic version information and improve status reporting

## [1.6.8.0125] - 2026-01-25

- feat: Implement Web UI service integration and event handling for improved user interaction

## [1.6.7.0125] - 2026-01-25

- feat: Add Enhanced Web UI with comprehensive control and settings management
- Enable Web UI and update configuration settings for improved server management
- Add Web UI for Weather Image Generator with API integration

## [1.6.6.0125] - 2026-01-25

- Update control label text for clarity in MainForm
- Clean up CHANGELOG by removing outdated version entry

## [1.6.5.0125] - 2026-01-25

- Update OpenMap project version to 1.3.3.0125 and include in auto-push script
- Update README.md

## [1.6.3.0125] - 2026-01-25

- Refactor Video Tab layout and enhance Alert Settings with improved controls and grouping
- replace PlayRadarAnimationTwiceOnAlert with PlayRadarAnimationCountOnAlert and add AlertDisplayDurationSeconds
- Add settings for skipping detailed weather and duplicating radar animation on alerts
- Update font family in appsettings to 'Lato Heavy' and add font preview functionality in SettingsForm
- Add font family selector to settings
- Bump version to 1.3.2.0124 in project file

## [1.6.2.0124] - 2026-01-24

- Bump version to 1.3.1.0123 in project file

## [1.6.1.0123] - 2026-01-23

- Refactor code structure and fix ECCC weather alert fetch
- Refactor alert deduplication and improve image generation text wrapping for better display
- Enhance alert processing and display: extract alert details from URLs, normalize titles, and improve summary handling in image generation
- Update alert fetching logs and enhance alert category checks
- chore: Update version to 1.3.0.0123 in OpenMap project file

## [1.6.0.0123] - 2026-01-23

- Remove broken radar options label from settings form
- Add dark mode support for terrain maps and update settings
- feat: Integrate OpenMap service for enhanced radar image generation and update configuration settings
- feat: Implement OpenMap configuration system with customizable map settings

## [1.5.1.0122] - 2026-01-22

- improve alert filtering for image generation by excluding ended alerts
- enhance ECCC alert detection with additional category checks and add test utility
- add Windows startup configuration options and functionality
- disable fade controls until xfade issue is resolved
- feat: update version to 1.2.0.0121 in OpenMap project
- Update version badge to 1.5.0

## [1.5.0.0121] - 2026-01-21

- Version bump

## [1.4.0.0121] - 2026-01-21

- feat: disable fade transition checkbox due to xfade issues
- feat: add functionality to clear existing images before generating new ones
- feat: update version to 1.0.6.0121 in OpenMap project

## [1.3.12.0121] - 2026-01-21

- feat: Update global weather map generation to use static Quebec cities with exact coordinates
- feat: Refactor video generation logic to group images into slides for improved timing calculations
- feat: Integrate radar animation and global weather map services
- feat: update version to 1.0.5.0121 in OpenMap project

## [1.3.11.0121] - 2026-01-21

- feat: add Text-to-Speech settings and integrate with Emergency Alert generation
- feat: update version to 1.0.4.0121 in OpenMap project

## [1.3.10.0121] - 2026-01-21

- Fix error when process still open when closing the app
- feat: enhance NAAD client management with proper disposal
- feat: update version to 1.0.3.0120 in OpenMap project

## [1.3.9.0120] - 2026-01-20

- feat: embed weather icons into the binary and update resource handling
- feat: update version to 1.0.2.0120 in OpenMap project

## [1.3.8.0120] - 2026-01-20

- feat: enhance radar image service with coordinate grid and attribution overlay
- feat: add map attribution information and requirements to credits section
- feat: update versioning to 1.0.1.0120 in OpenMap project

## [1.3.7.0120] - 2026-01-20

- feat: enhance legal compliance and attribution guidelines in OpenMap library
- feat: add OpenMap project file path and update versioning logic in push script
- feat: integrate map background generation with radar overlay in WeatherDetailsForm
- feat: integrate OpenMap library for enhanced map overlay functionality

## [1.3.6.0120] - 2026-01-20

- feat: enhance radar image loading UI and improve component dimensions
- feat: refactor radar image fetching logic and enhance UI for radar display
- feat: updated ECCC radar image functionality with UI enhancements and documentation
- Update version badge in README.md to 1.3.4

## [1.3.5.0120] - 2026-01-20

- feat: enhance radar image loading UI and improve component dimensions
- feat: refactor radar image fetching logic and enhance UI for radar display
- feat: updated ECCC radar image functionality with UI enhancements and documentation

## [1.3.4.0120] - 2026-01-20

- feat: update framework information and remove redundant text in About dialog
- feat: add download bundled FFmpeg functionality
- feat: enhance UI and layout for Forms with improved styles and functionality

## [1.3.3.0117] - 2026-01-17

- feat: enhance UI elements and improve layout in MainForm and SettingsForm
- refactor: streamline version update calls in push script

## [1.3.2.0117] - 2026-01-17

- feat: add FFmpeg configuration settings and UI controls for source selection
- fix: update copyright information in OpenMeteo.csproj

## [1.3.1.0117] - 2026-01-17

- feat: update project files for EAS and WeatherShared with additional metadata; modify push script to include WeatherShared
- fix: update .NET version in AboutForm and MainForm to 10.0; modify test alert button text
- refactor: remove OpenMeteoTests project from solution
- refactor: remove obsolete test files and project configuration
- Update .gitignore to exclude log files"
- Update CHANGELOG for version 1.3.0.0117

## [1.3.0.0117] - 2026-01-17

- ffmpeg bundle integration
- fix: Remove OpenMeteoTests project from solution file to streamline project structure
- feat: Update location data for release purpose
- docs: Update README.md for improved clarity and structure, enhancing feature descriptions and visual appeal
- fix: Improve CHANGELOG.md update process to ensure compliance and prevent multiple blank lines

## [1.2.7.0116] - 2026-01-16

- fix: Simplify changelog section generation in push script to avoid duplicate heading warnings
- feat: Enhance NAAD status panel layout
- fix: Correct logic for version updates in ECCC and EAS projects based on update type

## [1.2.6.0116] - 2026-01-16

- feat: Add EAS project path to push script and update versioning logic
- Enhance changelog update process in push.ps1
- refactor: Update nullable reference types and clean up code for better safety and readability
- chore: Update version to 2.1.5.0116 in ECCC project file

## [1.2.5.0116] - 2026-01-16

- EAS integration in progress/testing
- feat: Implement NAAD TCP stream listener and update app settings for AlertReady configuration
- chore: Update version to 2.1.4.0115 in ECCC project file

## [1.2.4.0115] - 2026-01-15

- Add test alert generation and emergency alert visualization
- feat: Add ExcludeWeatherAlerts option to filter out meteorological alerts
- feat: Enhance Alert Ready functionality with TCP stream support and new configuration options

## [1.2.3.0115] - 2026-01-15

- feat: Integrate Alert Ready functionality for enhanced weather alerts
- chore: Update version to 2.1.2.0111 in ECCC project file

## [1.2.2.0111] - 2026-01-11

- feat: Improve weather data retrieval with OpenMeteo retry logic and ECCC fallback
- chore: Update version to 2.1.1.0111 in ECCC project file

## [1.2.1.0111] - 2026-01-11

- feat: Enhance weather data handling by adding wind gusts and merging ECCC with OpenMeteo data
- chore: Update version to 2.1.0.0110 in ECCC project file

## [1.2.0.0110] - 2026-01-10

- feat: Integrate ECCC Official API for weather data retrieval
- Implement online city search using OpenMeteo API and enhance ECCC city feed URLs in appsettings
- Add city search functionality and ECCC feed URL handling in LocationsForm
- Add ECCC feed URL parsing and weather data fetching enhancements
- Implement dynamic URL generation and data fetching for ECCC weather services
- Add functionality to gather release notes from changelog and commits since last release
- Bump version to 1.0.5.0110 in ECCC project

## [1.1.7.0110] - 2026-01-10

- Add weather data fetching for multiple APIs and improve alert logging
- Add weather API selection and location management enhancements
- Prevent substring matches on empty strings in alert comparison logic
- Enhance ECCC alert fetching to support filtering by desired cities and update appsettings for location changes
- Update version numbers to 1.0.4.0110 in ECCC.csproj

## [1.1.6.0110] - 2026-01-10

- Cleanup fetch data/assets demo
- Update version numbers to 1.0.3.0110 in ECCC.csproj

## [1.1.5.0110] - 2026-01-10

- Refactor changelog categorization in push script to use a single generic section
- Fix copyright notice in ECCC.csproj to reflect correct years
- Update version to 1.0.2.0110 and enhance build script for cleaner output

## [1.1.4.0110] - 2026-01-10

- Enhance build process by suppressing PDB generation and cleaning up development files in release artifacts
- Update copyright notice and increment version to 1.0.1.0110 in ECCC.csproj

## [1.1.3.0110] - 2026-01-10

- Refactor project version update logic and enhance error handling in push script

## [1.1.2.0110] - 2026-01-10

- Update ECCC project metadata and enhance push script for solution path

## [1.1.1.0110] - 2026-01-10

- Update build and test scripts to reference new WSG solution

## [1.1.0.0110] - 2026-01-10

- Handle Dynamic Link and .NET10 update
- Update target framework to net10.0 across all project files
- Add WeatherShared and ECCC projects; refactor AlertEntry for shared use
- Refactor FFmpeg integration to use bundled binaries and improve initialization process
- Update changelog path in documentation and auto-push script

## [1.0.2.0109] - 2026-01-09

- Add experimental tab to settings form and enable tab control
- Disable CRF encoding option in video settings
- Add experimental features toggle and configuration option

## [1.0.1.0106] - 2026-01-06

- Add advanced video encoding options: CRF encoding, bitrate, buffer size, and encoder preset

## [1.0.0.0106] - 2026-01-06

- First Stable Release
- Add auto-start cycle

## [0.10.0.0105] - 2026-01-05

- Implement log archival feature to manage UI log size and enhance performance
- Throttle log messages and update progress during output file replacement retries
- Add safety buffer to clip duration for smoother transitions

## [0.9.3.0105] - 2026-01-05

- Increase timeout for hardware encoding checks for RTX

## [0.9.2.0105] - 2026-01-05

- Enhance video generation process with temporary output handling and retry logic for file replacement
- 2026 date update

## [0.9.1.1230] - 2025-12-30

- Add total duration feature for video generation with configurable settings

## [0.9.0.1228] - 2025-12-28

- Refactor GalleryForm to use a class-level top panel and improve theme application logic
- Add GalleryForm for displaying weather images and videos with refresh functionality
- `Added quality preset selection to settings form and updated config manager and appsettings.json accordingly`
- Adjust MainForm dimensions for improved layout

## [0.8.11.1228] - 2025-12-28

- Version bump

## [0.8.10.1228] - 2025-12-28

- `Improved memory management and cleanup in WeatherImageGenerator`

## [0.8.9.1228] - 2025-12-28

- Add video control buttons and time label to MediaViewerForm
- Fix changelog formatting for version 0.8.7.1228

## [0.8.8.1228] - 2025-12-28

- Enhance video playback handling with aspect ratio preservation and improved error logging
- `Updated appsettings.json with changes to VideoCodec and Theme`

## [0.8.6.1228] - 2025-12-28

- Updated .\push.ps1
- `Added GitHub release notes generation and changelog updates to push.ps1 script`
- `Updated AssemblyVersion, AssemblyFileVersion, and AssemblyInformationalVersion to 0.8.5.1228`

## [0.7.8.1226] - 2025-12-26

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

- Updated project to .NET 8.0.
- Improved error handling and retry logic for weather fetching.
- Refactored `WeatherImageGenerator` to use `appsettings.json` instead of hardcoded values.

- Various bug fixes and performance improvements.

## [0.6.15.1225] - 2025-12-25

- Initial release of the refactored Weather Still Generator.
- Basic image generation for current weather and forecasts.
- Alert system integration with Environment Canada.
