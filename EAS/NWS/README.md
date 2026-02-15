EAS-NWS / SAME Alert Provider

This folder contains the National Weather Service (NWS) alert provider implementation with support for:
- **Modern api.weather.gov JSON API** — primary alert source with state/zone/point filtering
- **CAP (Common Alerting Protocol)** legacy XML feeds as fallback
- **SAME (Specific Area Message Encoding)** header parsing and decoding
- **EAS (Emergency Alert System)** Attention Signal tone generation
- Test alert generation for integration testing

## Files

- `NwsClient.cs` - Main NWS alert provider implementing `IAlertProvider` (JSON API + CAP XML)
- `NwsOptions.cs` - Configuration model for NWS alerts
- `NwsSameToneGenerator.cs` - Generates standard US EAS alert tones (853 Hz + 960 Hz)
- `TestNwsAlerts.cs` - Test CAP alert generator for various NWS event types

## Features

### Modern API (api.weather.gov)
- Fetches active alerts from `api.weather.gov/alerts/active` (GeoJSON)
- Filter by US state codes (e.g. `IL`, `CA`, `TX`)
- Filter by NWS forecast zones (e.g. `ILZ014`)
- Filter by geographic point (lat,lon)
- Severity and high-risk filtering
- Proper `User-Agent` header (required by NWS API)
- Extracts SAME codes from API parameters

### Legacy CAP XML Parsing
- Parses CAP 1.2 alert XML from legacy NWS feeds
- Extracts SAME headers when present
- Supports geographic filtering by area name

### Common Features
- Deduplicates alerts to prevent re-processing
- Maps alert severity to color codes (Red/Yellow/Gray)
- Sets `Provider = "USA_NWS"` on all alerts
- Populates extended fields: IssuedAt, ExpiresAt, Description, Instructions, DetailUrl
- Max age filtering (default: 24 hours)

### SAME Header Support
- Extracts `SAME` codes from both API parameters and CAP XML
- SAME format: `061741+0140-3612265-WRNWUS23 KLOT 142323 REC`

### EAS Alert Tone Generation
- Generates the official US Emergency Alert System Attention Signal
- Format: 16-bit PCM WAV, 44.1 kHz, Mono
- Duration: 8 seconds of dual-tone (853 Hz + 960 Hz)
- Reference: FCC 47 CFR Part 11

## Configuration

Configure in `appsettings.json`:

```json
"Nws": {
  "Enabled": true,
  "ApiBaseUrl": "https://api.weather.gov",
  "States": ["IL", "CA"],
  "Zones": [],
  "Point": null,
  "MaxAgeHours": 24,
  "HighRiskOnly": false,
  "HttpTimeoutSeconds": 30,
  "UserAgent": "WSG-Weather-Still-Generator/1.0"
}
```

### Filtering Options

| Filter | Description | Example |
|--------|-------------|---------|
| `States` | Two-letter US state codes | `["IL", "CA", "TX"]` |
| `Zones` | NWS forecast zone IDs | `["ILZ014", "CAZ006"]` |
| `Point` | Lat,lon for point-based alerts | `"41.88,-87.63"` |
| `HighRiskOnly` | Only Extreme/Severe + Immediate/Expected | `true` |
| `SeverityFilter` | Specific severity levels | `["Extreme", "Severe"]` |

## Usage

```csharp
var httpClient = new HttpClient();
var options = new NwsOptions
{
    Enabled = true,
    States = new List<string> { "IL" }   // Filter to Illinois
};

var nwsClient = new NwsClient(httpClient, options);
nwsClient.Log = Console.WriteLine;

// Fetch alerts from api.weather.gov
var alerts = await nwsClient.FetchAlertsAsync();
foreach (var alert in alerts)
{
    Console.WriteLine($"[{alert.Provider}] {alert.Title}: {alert.City}");
    Console.WriteLine($"  Severity: {alert.SeverityColor}, Expires: {alert.ExpiresAt}");
}

// Subscribe to alert events
nwsClient.AlertReceived += (sender, args) =>
{
    Console.WriteLine($"New alert: {args.Alert?.Title}");
};

// Generate EAS SAME alert tone
string? tonePath = NwsSameToneGenerator.GetOrGenerateSameTone();
```

## Test Alert Types

- `GenerateTornadoWarning()` - Tornado Warning (Extreme severity)
- `GenerateSevereThunderstormWarning()` - Severe Thunderstorm Warning
- `GenerateWinterWeatherAdvisory()` - Winter Weather Advisory
- `GenerateFloodWarning()` - Flash Flood Warning
- `GenerateHeatAdvisory()` - Heat Advisory

## Testing

Run NWS integration tests:
```bash
dotnet run --project WeatherImageGenerator -- --test-nws
```

## Notes

- Only processes alerts with `status=Actual` (Test/Exercise alerts are skipped)
- Cancelled alerts (`msgType=Cancel`) are automatically filtered out
- The NWS API requires a `User-Agent` header — set via `UserAgent` config
- Alerts older than `MaxAgeHours` or past their `expires` time are discarded
- Provider is set to `"USA_NWS"` on all alerts for downstream rendering
- Automatically deduplicates by City|Title|Summary key
- Caches processed alert identifiers to prevent duplicates
- Supports timeout configuration for HTTP requests
- Thread-safe identifier caching with lock-based synchronization

## References

- [NWS CAP Feeds](https://alerts.weather.gov/)
- [FCC 47 CFR Part 11 - Emergency Alert System](https://www.fcc.gov/document/doc-89-104)
- [Common Alerting Protocol (CAP) v1.2](https://docs.oasis-open.org/emergency/cap/v1.2/CAP-v1.2-os.html)
- [SAME Encoding Standard](https://www.fcc.gov/oet/rfs/bulletin/oet94-105.pdf)

