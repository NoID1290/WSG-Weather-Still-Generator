using System.Globalization;

namespace WSG.Mobile.Services;

public sealed class RadarService
{
    private const string BaseGeoMetUrl = "https://geo.weather.gc.ca/geomet";

    // ── Radar layer options (mirrors desktop WeatherOverlayManager) ────────
    public static readonly IReadOnlyList<(string Id, string DisplayName)> RadarLayers =
    [
        ("RADAR_1KM_RRAI",              "Rain (RRAI)"),
        ("RADAR_1KM_RSNO",              "Snow (RSNO)"),
        ("Radar_1km_SfcPrecipType",     "Precip Type"),
        ("RADAR_COVERAGE_RRAI.INV",     "Coverage"),
    ];

    // ── Style for each layer ───────────────────────────────────────────────
    private static string StyleForLayer(string layer) => layer switch
    {
        "RADAR_1KM_RRAI"          => "RADARURPPRECIPR14-LINEAR",
        "RADAR_1KM_RSNO"          => "RADARURPPRECIPS14-LINEAR",
        "Radar_1km_SfcPrecipType" => "",
        "RADAR_COVERAGE_RRAI.INV" => "",
        _                         => "RADARURPPRECIPR14-LINEAR"
    };

    /// <summary>
    /// Generates timestamped WMS URLs for radar animation frames.
    /// </summary>
    public List<RadarFrame> GetRadarFrames(int frameCount = 15, int intervalMinutes = 10)
    {
        var frames = new List<RadarFrame>();
        var now = DateTime.UtcNow;
        // Round down to nearest 10-minute mark
        var rounded = new DateTime(now.Year, now.Month, now.Day, now.Hour, (now.Minute / 10) * 10, 0, DateTimeKind.Utc);

        for (int i = frameCount - 1; i >= 0; i--)
        {
            var time = rounded.AddMinutes(-i * intervalMinutes);
            frames.Add(new RadarFrame
            {
                Timestamp = time,
                TimeString = time.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                DisplayTime = time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            });
        }

        return frames;
    }

    /// <summary>
    /// Builds a WMS GetMap URL for a specific bbox, time, layer and image size.
    /// Used by the OpenGL renderer to fetch geo-registered radar PNG images.
    /// </summary>
    public static string BuildWmsImageUrl(
        (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
        string isoTime,
        int width = 512,
        int height = 512,
        string? layer = null,
        string? style = null)
    {
        layer ??= "RADAR_1KM_RRAI";
        style ??= StyleForLayer(layer);

        // ECCC GeoMet uses EPSG:4326 with lat-lon axis order: BBOX=minLat,minLon,maxLat,maxLon
        var url = $"{BaseGeoMetUrl}?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap" +
                  $"&LAYERS={layer}" +
                  $"&CRS=EPSG:4326" +
                  $"&BBOX={bbox.MinLat:F6},{bbox.MinLon:F6},{bbox.MaxLat:F6},{bbox.MaxLon:F6}" +
                  $"&WIDTH={width}&HEIGHT={height}" +
                  $"&FORMAT=image/png&TRANSPARENT=true&time={isoTime}";

        if (!string.IsNullOrEmpty(style))
            url += $"&STYLES={style}";

        return url;
    }

    public static string GetWmsLayer() => "RADAR_1KM_RRAI";
    public static string GetWmsStyle() => "RADARURPPRECIPR14-LINEAR";

    /// <summary>
    /// Calculates a bounding box centered on lat/lon with a radius in km.
    /// </summary>
    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) CalculateBbox(
        double lat, double lon, double radiusKm = 200)
    {
        var latOffset = radiusKm / 111.32;
        var lonOffset = radiusKm / (111.32 * Math.Cos(lat * Math.PI / 180));
        return (lat - latOffset, lon - lonOffset, lat + latOffset, lon + lonOffset);
    }

    /// <summary>
    /// Estimates an appropriate Leaflet zoom level for a given radius.
    /// </summary>
    public static int EstimateZoomLevel(double radiusKm)
    {
        return radiusKm switch
        {
            <= 25 => 11,
            <= 50 => 10,
            <= 100 => 9,
            <= 200 => 8,
            <= 400 => 7,
            <= 800 => 6,
            _ => 5
        };
    }
}

public sealed class RadarFrame
{
    public DateTime Timestamp { get; init; }
    public string TimeString { get; init; } = string.Empty;
    public string DisplayTime { get; init; } = string.Empty;
}
