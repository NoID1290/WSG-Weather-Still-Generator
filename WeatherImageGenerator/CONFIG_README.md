# WeatherImageGenerator — Configuration (short)

Edit `appsettings.json` to control what the generator does.

Key sections
- `Locations` — list of city names to query
- `RefreshTimeMinutes` — how often to refresh (minutes)
- `ImageGeneration` — `ImageWidth`, `ImageHeight`, `OutputDirectory`
- `Video` — `StaticDurationSeconds`, `FrameRate`, `OutputFileName`
- `ECCC` — `CityFeeds` to set RSS feeds for alerts

Quick examples
- Change update interval: `"RefreshTimeMinutes": 5`
- Disable video: `"Video": { "StaticDurationSeconds": 0 }`

Editing notes
- Save changes to `appsettings.json` and restart the app.
- Ensure valid JSON (double quotes, commas).

If you need details or full templates, check the `appsettings.json` file in this folder.
