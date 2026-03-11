# 🌦️ WSG — Weather Still Generator

<div align="center">

**Automated weather image and video generation for digital signage, streaming overlays, and weather displays**

[![Version](https://img.shields.io/badge/version-1.17.25-blue?style=for-the-badge)](docs/CHANGELOG.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)

---

[🚀 Quick Start](#-quick-start) • [📖 Documentation](#-documentation) • [📦 Downloads](#-downloads)

</div>

---

## 📋 Overview

**WSG (Weather Still Generator)** is a Windows application that generates weather images and MP4 slideshow videos from real-time data. It supports multiple weather APIs, emergency alert systems, radar map overlays, and GPU-accelerated video encoding.

`Digital Signage` · `Streaming Overlays` · `Weather Stations` · `Information Displays`

---

## 🎯 Features

- **Image Generation** — Current conditions, daily forecasts, detailed analysis, weather maps, and alert graphics
- **Video Generation** — MP4 slideshows with fade transitions, background music, 1080p/4K/vertical, NVIDIA GPU encoding
- **Alert Systems** — Environment Canada (ECCC), NAAD/Alert Ready, color-coded severity levels, multi-city aggregation
- **Data Sources** — OpenMeteo API (global), ECCC Official API (Canada), GeoMet WMS radar, automatic geocoding
- **Web UI** — Built-in browser-based interface for monitoring and control
- **Weather Maps** — Animated radar overlays with OpenStreetMap base layers and city markers

---

## 🖼️ Screenshots

<!-- Add screenshots here -->
*Screenshots coming soon.*

---

## 🚀 Quick Start

### Prerequisites

- Windows 10/11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [FFmpeg](https://ffmpeg.org/download.html) *(bundled or in PATH)*

### Install & Run

```powershell
git clone https://github.com/NoID1290/WSG-Weather-Still-Generator.git
cd WSG-Weather-Still-Generator
.\build.ps1
.\WeatherImageGenerator\bin\Debug\net10.0-windows\WSG.exe
```

Run headless with `WSG.exe --nogui` for continuous generation without the GUI.

All settings live in `WeatherImageGenerator/appsettings.json` — no recompile needed. See the [Configuration Guide](docs/CONFIG_README.md) for details.

---

## 📦 Downloads

Download the latest portable release from [GitHub Releases](https://github.com/NoID1290/WSG-Weather-Still-Generator/releases/latest).

| Asset | Description |
|-------|-------------|
| `WSG-Weather-Still-Generator-x.x.x.zip` | Portable release (ready to run) |

---

## 📖 Documentation

| Document | Description |
|----------|-------------|
| [Changelog](docs/CHANGELOG.md) | Version history and release notes |
| [Configuration Guide](docs/CONFIG_README.md) | Complete `appsettings.json` reference |
| [Weather Maps & Radar](docs/WEATHER_MAP_GUIDE.md) | Map architecture, radar overlays, and rendering |
| [OpenMap Integration](docs/OPENMAP_USAGE.md) | OpenStreetMap layers and settings |
| [Web UI Guide](docs/WEB_UI_GUIDE.md) | Browser-based interface usage |
| [ECCC API](docs/ECCC_API_UPGRADE.md) | Environment Canada integration |
| [Push Script & Releases](docs/PUSH_SCRIPT_GUIDE.md) | Automated versioning and deployment |

---

## 🤝 Contributing

Contributions are welcome — feel free to open issues and pull requests.

---

## 📄 License

[MIT License](LICENSE) — © 2020-2026 [NoID Softwork](https://github.com/noidsoftwork)





















