using System.Globalization;

namespace WSG.Mobile.Services;

public sealed class RadarService
{
    private const string BaseGeoMetUrl = "https://geo.weather.gc.ca/geomet";
    private const string RadarLayer = "RADAR_1KM_RRAI";
    private const string RadarStyle = "RADARURPPRECIPR14-LINEAR";

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
    /// Builds a WMS tile URL for Leaflet's L.TileLayer.WMS.
    /// The tile layer handles BBOX automatically, so we return the base params.
    /// </summary>
    public static string GetWmsTileUrl()
    {
        return $"{BaseGeoMetUrl}";
    }

    public static string GetWmsLayer() => RadarLayer;
    public static string GetWmsStyle() => RadarStyle;

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
