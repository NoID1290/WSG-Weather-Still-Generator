EAS-NWS / SAME Alert Provider

This folder contains the National Weather Service (NWS) alert provider implementation with support for:
- **CAP (Common Alerting Protocol)** feeds from NWS/NOAA
- **SAME (Specific Area Message Encoding)** header parsing and decoding
- **EAS (Emergency Alert System)** Attention Signal tone generation
- Test alert generation for integration testing

## Files

- `NwsClient.cs` - Main NWS CAP feed provider implementing `IAlertProvider`
- `NwsOptions.cs` - Configuration model for NWS feeds
- `NwsSameToneGenerator.cs` - Generates standard US EAS alert tones (853 Hz + 960 Hz)
- `TestNwsAlerts.cs` - Test CAP alert generator for various NWS event types

## Features

### Alert Parsing
- Parses CAP 1.2 alert XML from NWS feeds
- Extracts SAME headers when present
- Deduplicates alerts to prevent re-processing
- Supports geographic filtering by area name
- Maps alert severity to color codes

### SAME Header Support
- Extracts `<parameter><valueName>SAME</valueName><value>...</value></parameter>` from CAP alerts
- SAME format: `061741+0140-3612265-WRNWUS23 KLOT 142323 REC`
  - Area codes (6 digits)
  - Duration information
  - Event type codes
  - Official transmitter identifier

### EAS Alert Tone Generation
- Generates the official US Emergency Alert System Attention Signal
- Format: 16-bit PCM WAV, 44.1 kHz, Mono
- Duration: 8 seconds of alternating tone patterns
- Frequencies: 853 Hz + 960 Hz (primary), 853 Hz (secondary)
- Reference: FCC 47 CFR Part 11

## Configuration

Configure in `appsettings.json`:

```json
"EAS": {
  "Nws": {
    "Enabled": true,
    "HttpTimeoutSeconds": 30,
    "FeedUrls": [
      "https://alerts.weather.gov/cap/us.php?x=0",
      "https://alerts.weather.gov/cap/wwaatmget.php?x=1"
    ]
  }
}
```

## Usage

```csharp
var httpClient = new HttpClient();
var options = new NwsOptions { Enabled = true };
options.FeedUrls = NwsOptions.GetDefaultFeedUrls();

var nwsClient = new NwsClient(httpClient, options);
nwsClient.Log = Console.WriteLine;

// Fetch alerts
var alerts = await nwsClient.FetchAlertsAsync();
foreach (var alert in alerts)
{
    Console.WriteLine($"{alert.Title}: {alert.City}");
}

// Generate SAME alert tone
string? tonePath = NwsSameToneGenerator.GetOrGenerateSameTone();
if (tonePath != null)
{
    Console.WriteLine($"Alert tone: {tonePath}");
}

// Generate test alerts
var tornadoAlert = TestNwsAlerts.GenerateTornadoWarning();
var testAlerts = TestNwsAlerts.GetAllTestAlerts();
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

This will:
1. Generate the EAS SAME alert tone
2. Verify SAME header parsing
3. List all test alert types
4. Display sample SAME header format

## Event Handling

Subscribe to the `AlertReceived` event to be notified when new alerts are received:

```csharp
nwsClient.AlertReceived += (sender, args) => 
{
    Console.WriteLine($"Alert: {args.Alert.Title}");
};
```

## Notes

- Only processes alerts with `status=Actual` and `scope=Public`
- Automatically deduplicates by City|Title|Summary key
- Caches processed alert identifiers to prevent duplicates
- Supports timeout configuration for HTTP requests
- Thread-safe identifier caching with lock-based synchronization

## References

- [NWS CAP Feeds](https://alerts.weather.gov/)
- [FCC 47 CFR Part 11 - Emergency Alert System](https://www.fcc.gov/document/doc-89-104)
- [Common Alerting Protocol (CAP) v1.2](https://docs.oasis-open.org/emergency/cap/v1.2/CAP-v1.2-os.html)
- [SAME Encoding Standard](https://www.fcc.gov/oet/rfs/bulletin/oet94-105.pdf)

