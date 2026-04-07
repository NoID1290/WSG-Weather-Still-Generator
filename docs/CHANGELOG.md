# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.20.49.0407] - 2026-04-07

- Add radar functionality, weather widget, and alert services
- Implement feature X to enhance user experience and optimize performance

## [1.20.48.0406] - 2026-04-06

- Refactor app structure to use dependency injection for pages and improve AppShell initialization
- Add initial implementation of weather aggregation and UI styles

## [1.20.47.0406] - 2026-04-06

- Introducing procedural FX BETA TEST

## [1.19.47.0406] - 2026-04-06

- Increase main app windows by default

## [1.19.46.0406] - 2026-04-06

- Add BZTG project file paths to version management scripts

## [1.19.45.0406] - 2026-04-06

- Move UI controls for opacity settings into viewport Update procedural effects to be disabled by default

## [1.19.44.0403] - 2026-04-03

- Add per-step cloud opacity settings and UI controls for enhanced cloud rendering
- Enhance cloud rendering with improved edge effects and dynamic color gradients based on storm intensity
- Refine radar sampling and cloud rendering logic for improved precipitation visualization
- Adjust cloud rendering parameters for improved visual quality

## [1.19.43.0402] - 2026-04-02

- Add procedural shaders for weather effects: clouds, lightning, and rain
- Testing procedural weather effects and data handling for GPU rendering

## [1.19.42.0330] - 2026-03-30

- Enhance GPU composite
- Add .idea directory to .gitignore for local IDE settings

## [1.19.41.0330] - 2026-03-30

- Update radar layer options and labels for surface precipitation types

## [1.19.40.0330] - 2026-03-30

- Add WMS legend graphic support

## [1.19.39.0330] - 2026-03-30

- Enhance temperature data fetching logic

## [1.19.38.0330] - 2026-03-30

- Fix radar overlay been miscaculated

## [1.19.37.0329] - 2026-03-29

- Fix OSM tile caching problem and clean up changelog entries
- Update CHANGELOG with legal notices and bug fixes

## [1.19.36.0329] - 2026-03-29

- Expand Credits and License sections with legal notices for Silk.NET, ILGPU, CSJ2K, Earthquakes Canada, and Blitzortung

## [1.19.35.0329] - 2026-03-29

- Fix Map style dosen't been saved at close
- Fix OSM tile caching problem GRIB2 and Lightning got their indepndent panels Fixed opacity not loaded propely
- Fix OSM tile caching problem
- GRIB2 and Lightning got their indepndent panels
- Fixed opacity not loaded propely

## [1.19.34.0326] - 2026-03-26

- Introducing Lightning Feature
- Enhance lightning strike rendering with new properties and polling interval settings
- Implement lightning flash boost feature with decay effect
- Integrate Blitzortung lightning data API

## [1.18.34.0316] - 2026-03-16

- feat: Add lightning detection overlay and rendering support

## [1.18.33.0315] - 2026-03-15

- Add video generation options for EAS alert

## [1.18.32.0315] - 2026-03-15

- Update versioning logic to include EKCA

## [1.18.31.0315] - 2026-03-15

- fix the marker shader compile failure by removing non-ASCII characters from the GLSL comments
- Add LeftCenter anchor option for HUD panels and update legend anchor position

## [1.18.30.0314] - 2026-03-14

- Add GPU rendering for station and epicenter markers
- Increase waveform panel height and enhance event handling in SeismogramMapControl

## [1.18.29.0313] - 2026-03-13

- Enhance Atom feed parsing and improve SeismogramMapControl UI elements

## [1.18.28.0313] - 2026-03-13

- Introducing Seismograph Viewer
- Remove build_result.txt file

## [1.17.28.0313] - 2026-03-13

- Add [SupportedOSPlatform("windows")] attribute and improve null checks in various classes

## [1.17.27.0312] - 2026-03-12

- Testing StreamPipe

## [1.17.26.0311] - 2026-03-11

- Enhance git path resolution in push script

## [1.17.25.0311] - 2026-03-11

-  Puts Grib2/Grib2.csproj in the same canonical set as the other managed projects
- Fixing UI element overlaping updating progess
- Update version to 1.0.17.0311 in Grib2 project

## [1.17.24.0311] - 2026-03-11

- Enhance versioning script to include Grib2 project and improve version update logic
- Fix duplicate entry in CHANGELOG

## [1.17.23.0311] - 2026-03-11

- Introduced an interpolation factor to enable smooth animations by generating synthetic frames between real radar frames. -
- Updated radar animation HUD elements

## [1.16.23.0310] - 2026-03-10

- Add CI workflow for building Weather Still Generator on push and pull request

## [1.16.22.0310] - 2026-03-10

- Refactor button colors in MainForm for improved semantic clarity

## [1.16.21.0310] - 2026-03-10

- Enhance MainForm layout
- Update CHANGELOG.md for version 1.16.20.0310: document cycle control settings and UI enhancements
- Add cycle control settings and UI for automatic update configuration

## [1.16.20.0310] - 2026-03-10

Add cycle control settings and UI for automatic update configuration

- Introduced CycleControlSettings to manage update cycle options.
- Added CycleControlForm for user interface to configure cycle steps.
- Updated MainForm to include a button for accessing cycle control settings.
- Enhanced Program.cs to utilize cycle control settings during data fetch and image generation processes.
- Removed unused settings from SettingsForm.

## [1.15.20.0309] - 2026-03-09

- Make permanant notify icon visible in the system tray

## [1.15.19.0309] - 2026-03-09

- Fix OpenGL pipeline
- Enhance GRIB2 data handling with Mercator projection support; update shaders and rendering logic for improved alignment and performance

## [1.14.19.0308] - 2026-03-08

- Refactor code structure for improved readability and maintainability

## [1.14.18.0307] - 2026-03-07

- Enhance logging in RadarImageService and WeatherOverlayManager; add diagnostics for GRIB2 data handling
- Fix grid boundary checks to prevent arc artifacts and improve bilinear interpolation clamping
- Update CHANGELOG.md

## [1.14.17.0306] - 2026-03-06

- Introducing GRIB2 Forecast

## [1.13.17.0305] - 2026-03-05

- Enhance GRIB2 handling with step size support for sliders and update packing templates for JPEG2000 and PNG
- Add GRIB2 Overlay Renderer and Integration Guide
- Add GRIB2 decoder with ILGPU support and various packing templates
- Enhance documentation and project structure for Grib2 library integration
- Add initial Grib2 library project structure and documentation

## [1.13.16.0303] - 2026-03-03

- Fixing DirectX11 back buffer issues
- Update README.md for improve feature descriptions

## [1.13.15.0303] - 2026-03-03

- Fix animation being slow with DirectX on AMD GPU Fix bad windows cursor on OpenGL

## [1.13.14.0303] - 2026-03-03

- Refactor release check in push.ps1 to simplify GitHub release verification
- Add developer shortcut to unlock Stream Pipe tab in SettingsForm Refreactor push script for futur version

## [1.13.13.0302] - 2026-03-02

- Testing Streampipe over proxy and HLS Injection
- Enhance HLS alert injection in StreamPipeService
- Refactor StreamPipeService to support single shared TCP listener and HTTP path routing for channels
- Add HLS Alert Injection feature for direct integration with Tunarr's stream cache
- Refactor Stream Proxy to Stream Pipe: Introduce lightweight TCP byte pipe for MPEG-TS streaming
- Add Auto-Detect Channels feature to SettingsForm for Tunarr integration
- Add StreamProxyService and MpegTsHelper for MPEG-TS streaming and alert splicing
- Update CHANGELOG.md by removing version bump

## [1.13.12.0228] - 2026-02-28

- Add session-only animation frame count controls and update UI interactions
- Add screenshot functionality and radar animation toggle to UI Fix zoom UI not working
- Update changelog to reflect radar frame configuration enhancements and validation logic

## [1.13.9.0227] - 2026-02-27

- Add missing radar frame configuration entries for frames 2-5 and update validation logic to check all frames
- Now the user can configure up to 30 radar frames (ECCC)

## [1.13.8.0227] - 2026-02-27

- Fix bump

## [1.13.7.0226] - 2026-02-26

- Enhance radar frame configuration and validation

## [1.13.6.0226] - 2026-02-26

- Refactor shader to use embedded resources across DirectX, OpenGL, and Vulkan renderers

## [1.13.5.0226] - 2026-02-26

- Embed application icon as a resource and update icon loading logic in MainForm

## [1.13.4.0226] - 2026-02-26

- Enhance HUD controls: add progress line rendering, improve slider tick marks, and implement button disable states
- Refactor mouse event handling to update map position only if dragging occurred
- Add loading overlay functionality to HUD system for radar frame updates
- Enhance HUD layout and controls: adjust panel dimensions, add timeline slider, and improve glyph rendering
- Adjust panel title height and spacing in HUD layout for improved consistency
- Refactor CHANGELOG to remove duplicate entries

## [1.13.3.0225] - 2026-02-25

- Enhancing HUD layout in radar map
- Add Vulkan shaders for weather overlay and UI rendering
- - Introduced ShowStatusBar and ShowRuler properties in IMapRenderer interface. - Implemented corresponding properties in DirectX, OpenGL, and Vulkan map renderers. - Updated WeatherMapControl to include checkboxes for status bar and ruler visibility. - Added opacity settings for status bar and animation bar in shaders panel. - Enhanced configuration management to save and load new settings for status bar and ruler.
- Add invisible cursor for crosshair-as-mouse mode in DirectX and Vulkan renderers
- Implement deferred auto-update check on startup and remove update controls from SettingsForm
- Refactor single-instance guard for improved UX handling
- Update CHANGELOG for version 1.13.0.0223

## [1.13.0.0223] - 2026-02-23

- Refactor Settings Forms
- Fix HUD text symbol
- Fix DirectX rendering overlay
- Implement DirectX and Vulkan rendering pipeline DirectX show his first output Vulkan still no output
- Update CHANGELOG.md
- Rendering API Abstraction with Vulkan & DirectX Backends

## [1.12.1.0221] - 2026-02-21

- Fixed a bug when fetching data on French windows installation cause an error of parameter not be valid.

## [1.12.0.0221] - 2026-02-21

- Update WSG.ico
- update status text formatting for clarity.
- Add periodic status bar refresh timer to update FPS and cache stats
- Add user location marker and loading status; enhance HUD status bar with detailed information
- Enhance crosshair functionality and HUD status bar; implement zoom motion blur in shaders
- Refactor license information in MainForm to improve clarity and organization
- Implement fullscreen toggle and enhance HUD controls with shader options
- Add GPU-rendered HUD overlay system for weather map viewport
- Remove obsolete test files and TODO list to clean up the repository
- Update CHANGELOG with new features and enhancements

## [1.11.0.0219] - 2026-02-19

- Enhance theme management: apply dynamic theming to WeatherMapForm and WeatherMapControl, update control styles for consistency
- Enhance theme management: apply dark/light title bar chrome, update button styles, and implement owner-drawn tabs
- Enhance UI and theme management: update form dimensions, improve color handling, and add third-party license information
- Refactor cache management to use %LOCALAPPDATA%/WSG for tile cache and application data; update related settings and checks
- Add single instance check to prevent multiple application instances
- Implement theme management and first-boot disclaimer
- Update System.Drawing.Common package version to 9.0.13
- Update OpenTK.Graphics package version to 4.9.4
- Added ThemeManager for centralized theme management with support for live theme switching.
- Integrated theme application in MediaViewerForm, MusicForm, SettingsForm, TestAlertSelectionForm, and WeatherDetailsForm.
- Introduced a DisclaimerDialog to display a first-boot disclaimer, requiring user acknowledgment before proceeding.
- Updated ConfigManager to include settings for first boot completion and UI preferences (crosshair, coordinates HUD, temperature labels).
- Enhanced GLRadarControl and WeatherMapControl to support new UI settings and improved overlay management.
- Added options for showing/hiding temperature labels and crosshair in the UI.
- Implemented persistent settings storage for user preferences.

## [1.10.2.0218] - 2026-02-18

- Fix ECCC triggering false emergency alert

## [1.10.1.0218] - 2026-02-18

- Add application manifest files Fix auto-start fail for reading path Fix auto-update

## [1.10.0.0217] - 2026-02-17

- Releasing Interactive Weather Map
- feat: Add Weather Map button to main form and remove dev-only map button from settings
- feat: Enhance font atlas with additional extended characters and improve UI component sizes
- Enhance Weather Overlay and UI Components

## [1.9.12.0216] - 2026-02-16

- Refactor and enhance OpenGL rendering components
- feat: Improve tile rendering with pixel overlap and refined color adjustments
- feat: Enhance weather overlay functionality with GPU compositing
- feat: Enhance WeatherMapControl with collapsible options and panel positioning
- feat: Add attribution overlay with dynamic text updates based on selected map style and overlays
- feat: Add checkbox to control animation fetching behavior during map movements
- feat: Implement debounced animation frame refresh after map interactions
- feat: Add configurable map styles and radar animation support

## [1.9.11.0215] - 2026-02-15

- feat: Enhance alert description and instructions rendering with improved layout and clipping logic
- feat: Implement cross-cycle alert deduplication and enhance alert display functionality

## [1.9.10.0215] - 2026-02-15

- feat: Add NWS alert support with customizable settings and enhanced UI integration
- feat: Implement NWS alert provider with modern API support and legacy CAP fallback
- feat: Enhance temperature overlay caching logic to improve refresh efficiency

## [1.9.9.0215] - 2026-02-15

- Enhance WeatherMapControl and OverlayManager for improved overlay handling and performance

## [1.9.8.0214] - 2026-02-14

- feat: Enhance map tile cache verification to include file-cache and binary-cache statistics
- Ignore local appsettings files and untrack existing appsettings.json

## [1.9.7.0214] - 2026-02-14

- feat: Remove radar map button from main form and add dev-only button in settings to open Weather Interactive Map
- feat: Add radar tile prefetching functionality and ECCC radar tile generator
- feat: Add prefetching and composite generation for map tiles

## [1.9.6.0212] - 2026-02-12

- Enhance overlay management with clear overlay functionality and improved tile handling

## [1.9.5.0212] - 2026-02-12

- feat: Implement Weather Overlay Manager and Binary Tile Cache
- Add map zoom functionality and background composite handling in radar services

## [1.9.4.0211] - 2026-02-11

- Enhance tile fetching and caching mechanism with status updates and fallback handling
- Add OpenGL radar control and tile provider functionality

## [1.9.3.0210] - 2026-02-10

- pre interactive radar map

## [1.9.2.0210] - 2026-02-10

- Update Test Alert Selection Form UI for improved usability and aesthetics
- SAME Header TONE implemented
- Add NWS SAME tone generator and test alert generation
- feat: Add configurable log line spacing and update related methods for RichTextBox
- Update version badge to 1.9.0

## [1.9.1.0208] - 2026-02-08

- Fix push script corrupting README.md
- fix(push): restore README.md and make Update-ReadmeVersionBadge safe (avoid literal , use lookaround)

## [1.9.0.0208] - 2026-02-08

- feat: Implement EAS-NWS provider and refactor Alert Ready
- Refactor code structure for improved readability and maintainability for logger console
- Add Hybrid weather API option and update related configurations
- Refactor weather code handling: improve error messages for unknown weather codes
- Enhance log visibility toggle: improve handling of splitter distance and use built-in properties for collapsing
- Add toggle functionality for logs visibility and persist state in configuration
- Enhance push.ps1: add function to update README.md version badge and include it in staging when AttachAssets is requested
- Update version badge in README.md to 1.8.16
- Enhance SettingsForm: add warning for application restart when enabling Remote Access

## [1.8.16.0207] - 2026-02-07

- Enhance WebUIService and BootChecks: implement WebUINetworkAccessCheck for remote access validation and streamline URL ACL and firewall rule registration
- Enhance WebUIService and SettingsForm: add IP address display for remote access and implement URL ACL registration for HttpListener
- Enhance WebUIService: add AllowRemoteAccess property and update Web UI initialization for remote access support

## [1.8.15.0206] - 2026-02-06

- Update project files and add AssemblyInfo for WSG.Updater: set version to 1.8.14.0206, include copyright, and adjust appsettings.json handling in publish
- Enhance boot checks: add AppUpdateCheck and NaadConnectionCheck, update UI layout for boot screen
- Add boot check system for application initialization
- Enhance audio file concatenation with FFmpeg: implement filter_complex for format handling, add fallback to concat demuxer, and improve logging for empty or invalid files.

## [1.8.14.0204] - 2026-02-04

- Fixing Auto-Update failing when file is lock

## [1.8.13.0204] - 2026-02-04

- Test
- Filter out updater files from self-update process and improve logging

## [1.8.12.0204] - 2026-02-04

- Fix dump

## [1.8.11.0204] - 2026-02-04

- Refactor updater to launch with only the current process ID and improve logging

## [1.8.10.0204] - 2026-02-04

- ANOTHER TESTTTT GODMAN

## [1.8.9.0204] - 2026-02-04

- Refactor updater to determine app directory from its own location and support optional directory override

## [1.8.8.0204] - 2026-02-04

- Enhance build process to include updater in release artifacts

## [1.8.7.0204] - 2026-02-04

- Version bump

## [1.8.6.0204] - 2026-02-04

- Another Auto-Test update 2
- Fix: Correct EXE filename in update staging logic (WSG.exe instead of WeatherImageGenerator.exe)

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
