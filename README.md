# WSG - Weather Still Generator

WSG is a Windows weather graphics generator for digital signage, live streams, information displays, and local weather channels. It builds still images, radar/map scenes, alert graphics, and MP4 slideshow videos from live weather feeds.

## What it does

- Generates current conditions, forecast, detailed weather, and alert images
- Builds MP4 slideshows with music, fade transitions, and multiple output resolutions
- Pulls data from Open-Meteo and Environment Canada sources
- Supports Canadian alert feeds plus NOAA/NWS-related alert tooling in the repo
- Renders radar and weather maps with OpenStreetMap-based layers
- Includes a built-in Web UI for monitoring and control
- Supports headless runs for unattended automation

## Quick start

```powershell
git clone https://github.com/NoID1290/WSG-Weather-Still-Generator.git
cd WSG-Weather-Still-Generator
.\build.ps1
.\WeatherImageGenerator\bin\Release\net10.0-windows10.0.17763.0\WSG.exe
```

For unattended operation:

```powershell
.\WeatherImageGenerator\bin\Release\net10.0-windows10.0.17763.0\WSG.exe --nogui
```

## Solution structure

The repository is a multi-project .NET solution centered on `WSG.sln`.

| Project | Purpose |
|---|---|
| `WeatherImageGenerator` | Main Windows Forms desktop app (`WSG.exe`) |
| `OpenMeteo` | Open-Meteo client and weather data integration |
| `ECCC` | Environment Canada feeds, radar, and related weather data |
| `EAS` | Emergency alert support, including Alert Ready and NWS-related components |
| `EKCA` | Additional weather/alert integration used by the app |
| `OpenMap` | Map tile and overlay support |
| `grib2` | GRIB2 data handling and overlays |
| `WeatherShared` | Shared models and helpers |
| `WeatherImageGenerator.Updater` | Update helper executable |
| `BZTG` | Additional supporting library in the solution |
| `WSG.Mobile` | Mobile companion project |

## Main features

### Weather graphics

- Current weather panels
- Multi-location forecasts
- Detailed weather graphics
- Alert summary graphics
- Weather map and radar output

### Video output

- MP4 slideshow generation
- Fade transitions
- Optional background music
- 1080p, 4K, and vertical output modes
- FFmpeg-based encoding with hardware-encoding options in config

### Mapping and radar

- OpenStreetMap-backed map rendering
- Environment Canada radar integration
- Province radar and city radar options
- Global weather map support

### Web UI

- Local or remote browser access
- Status and weather endpoints
- Image listing/download endpoints
- Configurable port and remote-access toggle

### Operating modes

- Normal desktop GUI
- `--nogui` unattended mode
- Utility and smoke-test command-line switches for specific tasks

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK to build from source
- FFmpeg available to the app for video generation

The main app targets `net10.0-windows10.0.17763.0` and uses Windows Forms.

## Build

From the repository root:

```powershell
.\build.ps1
```

Build the full solution instead of only the main app:

```powershell
.\build.ps1 -All
```

### Baseline validation note

The existing build script was run in this task's Linux sandbox and failed with `NETSDK1100` because this repository targets Windows desktop frameworks. Build and runtime validation should be done on Windows or with Windows targeting explicitly enabled.

## Run

Typical executable location after the default release build:

```powershell
.\WeatherImageGenerator\bin\Debug\net10.0-windows10.0.17763.0\WSG.exe
```

Useful command-line switches found in `Program.cs`:

```powershell
WSG.exe --nogui
WSG.exe --create-test-images
WSG.exe --generate-icons
WSG.exe --make-video-now
WSG.exe --generate-province-animation
WSG.exe --test-emergency-alerts
WSG.exe --smoke-gui
WSG.exe --smoke-make-video
WSG.exe --smoke-save-config
WSG.exe --test-weather-map
```

## Configuration

WSG uses `appsettings.json` for editable runtime settings.

- The app loads the file from the executable directory
- If the file is missing or invalid, the app can regenerate it with defaults
- Settings are written back by the app and Web UI

Important configuration areas documented in the codebase include:

- Locations and refresh interval
- Image output dimensions and directories
- Video output, codecs, durations, and music
- Environment Canada radar options
- Weather map toggles
- Web UI port and remote access
- Alert-related display settings

See [`docs/CONFIG_README.md`](docs/CONFIG_README.md) for the full settings reference.

## Web UI

The desktop app includes a built-in Web UI served by `WeatherImageGenerator/Services/WebUIService.cs`.

Documented endpoints and capabilities include:

- `/api/status`
- `/api/weather/current`
- `/api/weather/forecast`
- `/api/images/list`
- `/api/images/{filename}`
- `/api/settings/web`

Default settings in code include:

- Port `5000`
- Remote access disabled by default
- CORS enabled

See:

- [`docs/WEB_UI_GUIDE.md`](docs/WEB_UI_GUIDE.md)
- [`WEB_UI_SUMMARY.md`](WEB_UI_SUMMARY.md)

## Assets and output

Notable app assets:

- `WeatherImageGenerator/Music/` - bundled music/readme for slideshow audio
- `WeatherImageGenerator/wwwroot/` - static files for the Web UI
- `WeatherImageGenerator/Rendering/` - rendering backends and shaders

Generated files are typically written to the configured output directory, such as weather images and slideshow videos.

## Releases

Portable builds are published through GitHub Releases:

- <https://github.com/NoID1290/WSG-Weather-Still-Generator/releases/latest>

## Repository documentation

- `docs/CONFIG_README.md` - configuration reference
- `docs/WEATHER_MAP_GUIDE.md` - weather-map and radar documentation
- `docs/WEATHER_MAP_ARCHITECTURE.md` - map internals
- `docs/OPENMAP_USAGE.md` - map integration details
- `docs/WEB_UI_GUIDE.md` - Web UI usage
- `docs/ECCC_API_UPGRADE.md` - Environment Canada notes
- `docs/PUSH_SCRIPT_GUIDE.md` - release/versioning workflow
- `docs/CHANGELOG.md` - version history

## Development notes

- The build script defaults to building only `WeatherImageGenerator`
- The repo contains Windows desktop, web-serving, map, alert, and updater code in one solution
- `WeatherImageGenerator.csproj` references the supporting projects directly
- `appsettings.json` is treated as a runtime-generated/user-managed file rather than a committed release artifact

## License

MIT. See [`LICENSE`](LICENSE).
