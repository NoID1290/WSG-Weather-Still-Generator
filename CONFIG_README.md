# Configuration (short)

Edit `WeatherImageGenerator/appsettings.json` to control locations, refresh interval, image size, and optional video settings.

Common keys
- `Locations` — city names used for weather queries
- `RefreshTimeMinutes` — update frequency (minutes)
- `ImageGeneration.ImageWidth` / `ImageGeneration.ImageHeight` — output size (px)
- `Video.StaticDurationSeconds` — per-image duration in slideshow (0 to disable)
- `ECCC.CityFeeds` — RSS feeds for Environment Canada alerts

For more examples and details see: `WeatherImageGenerator/CONFIG_README.md`
