EAS-NWS Provider

This folder contains a lightweight NWS (US National Weather Service) CAP feed provider (`EasnwsClient`) that implements `EAS.IAlertProvider`.

Usage

- Configure one or more CAP/Atom feed URLs in `EasnwsOptions.FeedUrls` (for example, in `appsettings.json`):

```json
"EAS": {
  "Nws": {
    "Enabled": true,
    "FeedUrls": [
      "https://alerts.weather.gov/cap/us.php?x=0",
      "https://alerts.weather.gov/cap/wwaatmget.php?x=1"
    ]
  }
}
```

- Create an `EasnwsClient` with an `HttpClient` and call `FetchAlertsAsync()` to retrieve `WeatherImageGenerator.Models.AlertEntry` objects.

Notes

- The implementation is intentionally small and conservative: it only processes `alert` CAP elements with `status=Actual` and `scope=Public`.
- Filtering by area is supported via the `filterAreas` parameter (case-insensitive substring match).
- Duplicate alerts are deduplicated by `(area|title|summary)` key.
- The provider currently does not stream TCP; NWS uses HTTP-based CAP feeds.

Next steps

- Add more robust handling for Atom-wrapped feeds, CAP 'info' languages, and area geocoding.
- Add unit tests and example configuration for common NWS feeds.
