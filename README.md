# 🌦️ WSG — Weather Still Generator

<div align="center">

**Automated weather image and video generation for digital signage, streaming overlays, and weather displays**

[![Version](https://img.shields.io/badge/version-1.7.3-blue?style=for-the-badge)](docs/CHANGELOG.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

---

[📖 Documentation](#-documentation) • [🚀 Quick Start](#-quick-start) • [⚙️ Configuration](#️-configuration) • [📦 Downloads](#-downloads)

</div>

---

## 📋 Overview

**WSG (Weather Still Generator)** is a powerful Windows application that automatically generates beautiful weather images and MP4 slideshow videos. It fetches real-time weather data, processes it into high-quality visuals, and can compile them into videos with transitions and background music.

### ✨ Perfect For

- 📺 **Digital Signage** — Lobby displays, waiting rooms, public screens
- 🎬 **Streaming Overlays** — Twitch, YouTube, OBS integrations
- 📡 **Weather Stations** — Local TV, community channels
- 🖥️ **Information Displays** — Offices, schools, retail locations

---

## 🎯 Features

<table>
<tr>
<td width="50%">

### 🖼️ Image Generation

- **Current Weather** — Temperature, humidity, wind speed, conditions
- **Daily Forecasts** — High/low temps, weather predictions
- **Detailed Analysis** — Pressure, wind direction, UV index
- **Weather Maps** — Radar overlays with city markers
- **Alert Graphics** — Color-coded emergency alerts

</td>
<td width="50%">

### 🎬 Video Generation

- **MP4 Slideshows** — Automated FFmpeg encoding
- **Fade Transitions** — Smooth, configurable effects
- **Multiple Resolutions** — 1080p, 4K, Vertical modes
- **Background Music** — Custom audio tracks
- **Hardware Encoding** — NVIDIA GPU acceleration

</td>
</tr>
<tr>
<td>

### ⚠️ Alert Systems

- **Environment Canada (ECCC)** — Official weather alerts
- **NAAD/Alert Ready** — Emergency broadcast alerts (TCP stream)
- **Severity Levels** — Red (Warning), Yellow (Watch), Gray (Statement)
- **Multi-City** — Aggregate alerts from multiple locations

</td>
<td>

### 🌐 Data Sources

- **OpenMeteo API** — Global weather data, free & open
- **ECCC Official API** — Canadian forecasts & conditions
- **GeoMet WMS** — Weather radar imagery
- **Geocoding** — Automatic city coordinate lookup

</td>
</tr>
</table>

---

## 🚀 Quick Start

### Prerequisites

- Windows 10/11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [FFmpeg](https://ffmpeg.org/download.html) *(bundled or in PATH)*

### Installation

```powershell
# Clone the repository
git clone https://github.com/your-username/WSG.git
cd WSG

# Build the solution
.\build.ps1

# Run the application
.\WeatherImageGenerator\bin\Debug\net10.0-windows\WSG.exe
```

### Command-Line Options

```powershell
# Run with GUI (default)
WSG.exe

# Run headless (no GUI, continuous generation)
WSG.exe --nogui

# Generate icons only
WSG.exe --generate-icons

# Create video immediately
WSG.exe --make-video-now

# Test emergency alert system
WSG.exe --test-emergency-alerts
```

---

## ⚙️ Configuration

All settings are in `WeatherImageGenerator/appsettings.json` — **no recompile needed!**

### Key Settings

| Section | Description |
|---------|-------------|
| `Locations` | Up to 9 weather locations with API selection (OpenMeteo/ECCC) |
| `ImageGeneration` | Output dimensions, format, fonts, margins |
| `Video` | Codec, bitrate, transitions, duration, music |
| `Alerts` | Header text, font sizes, styling |
| `AlertReady` | NAAD TCP streams, jurisdictions, filters |
| `ECCC` | City feeds, radar URLs, WMS layers |

### Example: Add a Location

```json
{
  "Locations": {
    "Location0": "Montreal",
    "Location1": "Toronto",
    "Location0Api": 1,
    "Location1Api": 0
  }
}
```

> **API Options:** `0` = OpenMeteo (Global), `1` = ECCC (Canada)

📚 **Full guide:** [docs/CONFIG_README.md](docs/CONFIG_README.md)

---

## 📦 Downloads

Download the latest release from [GitHub Releases](../../releases/latest).

| Asset | Description |
|-------|-------------|
| `WSG-Weather-Still-Generator-x.x.x.zip` | Portable release (ready to run) |

---

## 🛠️ Development

### Project Structure

```
WSG/
├── 📁 WeatherImageGenerator/    # Main application
│   ├── Forms/                   # GUI windows (MainForm, SettingsForm, etc.)
│   ├── Services/                # Core logic (ImageGenerator, VideoGenerator)
│   ├── Utilities/               # Helpers (Logger, ConfigManager)
│   └── appsettings.json         # Configuration
├── 📁 OpenMeteo/                # Weather API library
├── 📁 ECCC/                     # Environment Canada API library
├── 📁 EAS/                      # Emergency Alert System library
├── 📁 WeatherShared/            # Shared models
├── 📁 OpenMeteoTests/           # Unit tests
└── 📁 docs/                     # Documentation
```

### Build Scripts

| Script | Description |
|--------|-------------|
| `build.ps1` | Compile the solution |
| `test.ps1` | Run unit tests |
| `push.ps1` | Version bump + Git push + GitHub release |
| `update_version.ps1` | Manual version update |

---

## 📤 Push Script — Automated Releases

The `push.ps1` script automates versioning, commits, and GitHub releases.

### Basic Usage

```powershell
# Quick fix (bumps patch version)
.\push.ps1

# Backend update (bumps minor version)
.\push.ps1 -Type backend -CommitMessage "Added new API endpoint"

# Frontend update (bumps major version)
.\push.ps1 -Type frontend -CommitMessage "New UI redesign"
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Type` | `fix` | Update type: `frontend`, `backend`, or `fix` |
| `-CommitMessage` | Auto-generated | Custom commit message |
| `-Branch` | `main` | Target branch |
| `-AttachAssets` | — | Build & attach zip to GitHub release |
| `-NoRelease` | — | Skip GitHub release creation |
| `-SkipVersion` | — | Skip version bump (docs-only commits) |

### Examples

```powershell
# Create release with downloadable zip
.\push.ps1 -Type fix -CommitMessage "Bug fix" -AttachAssets

# Push without creating a GitHub release
.\push.ps1 -NoRelease

# Update docs only (no version change)
.\push.ps1 -SkipVersion -CommitMessage "Updated README"

# Full release to develop branch
.\push.ps1 -Type backend -CommitMessage "New feature" -Branch develop -AttachAssets
```

### Version Format: `a.b.c.MMDD`

| Segment | Meaning | Triggered By |
|---------|---------|--------------|
| `a` | Frontend/GUI changes | `-Type frontend` |
| `b` | Backend changes | `-Type backend` |
| `c` | Bug fixes/patches | `-Type fix` (default) |
| `MMDD` | Auto-generated date | Today's date |

**Example:** `1.2.7.0116` = v1 frontend, 2 backend updates, 7 patches, pushed January 16th

---

## 📖 Documentation

| Document | Description |
|----------|-------------|
| [📋 Changelog](docs/CHANGELOG.md) | Version history and release notes |
| [⚙️ Configuration Guide](docs/CONFIG_README.md) | Complete appsettings.json reference |
| [🌧️ Radar & Weather Map Integration](docs/RADAR_WEATHER_MAP_INTEGRATION.md) | 🆕 Animated radar with OpenMap overlays |
| [🗺️ OpenMap Usage](docs/OPENMAP_USAGE.md) | OpenStreetMap integration guide |
| [🌐 ECCC API Upgrade](docs/ECCC_API_UPGRADE.md) | Environment Canada integration notes |
| [📡 Radar Feature](docs/RADAR_FEATURE.md) | Radar image integration details |
| [📤 Push Script Guide](docs/PUSH_SCRIPT_GUIDE.md) | Automated deployment workflow |

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

<div align="center">

**Made with ❤️ by [NoID Softwork](https://github.com/noidsoftwork)**

© 2020-2026

</div>
