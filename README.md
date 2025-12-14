# Weather Still Generator (WSG)

Lightweight tool that generates weather images and optional slideshow videos.

Prerequisites
- .NET 8.0 SDK
- (Optional) FFmpeg for video generation

Quick start
1. Clone the repo: `git clone <repo>`
2. Restore and build: `dotnet restore && dotnet build -c Release`
3. Run: `dotnet run --project WeatherImageGenerator`

Configuration
- Edit `WeatherImageGenerator/appsettings.json` for locations, refresh interval, image/video settings.
- See [CONFIG_README.md](./CONFIG_README.md) for a short reference.

Project layout
- `WeatherImageGenerator/` — main app and `appsettings.json`
- `OpenMeteo/` — weather client library
- `OpenMeteoTests/` — unit tests

Support & Contributing
- Report issues or open PRs. See `CONTRIBUTING.md` (if present).

License
- MIT

<!-- Changelog moved to CHANGELOG.md; project info intentionally brief -->
