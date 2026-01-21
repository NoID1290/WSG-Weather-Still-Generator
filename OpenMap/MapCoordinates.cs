namespace OpenMap;

/// <summary>
/// Coordinate and bounds utilities for map operations
/// </summary>
public static class MapCoordinates
{
    /// <summary>
    /// Common map areas for Canadian weather regions
    /// </summary>
    public static class Canada
    {
        // Major cities
        public static readonly Coordinate Toronto = new(43.6532, -79.3832);
        public static readonly Coordinate Vancouver = new(49.2827, -123.1207);
        public static readonly Coordinate Montreal = new(45.5017, -73.5673);
        public static readonly Coordinate Calgary = new(51.0447, -114.0719);
        public static readonly Coordinate Ottawa = new(45.4215, -75.6972);
        public static readonly Coordinate Edmonton = new(53.5461, -113.4938);
        public static readonly Coordinate Winnipeg = new(49.8951, -97.1384);
        public static readonly Coordinate QuebecCity = new(46.8139, -71.2080);
        public static readonly Coordinate Halifax = new(44.6488, -63.5752);
        public static readonly Coordinate Victoria = new(48.4284, -123.3656);

        // Province-level bounding boxes
        public static readonly BoundingBox Ontario = new(41.7, -95.2, 56.9, -74.3);
        public static readonly BoundingBox Quebec = new(45.0, -79.8, 62.6, -57.1);
        public static readonly BoundingBox BritishColumbia = new(48.3, -139.1, 60.0, -114.0);
        public static readonly BoundingBox Alberta = new(49.0, -120.0, 60.0, -110.0);
        public static readonly BoundingBox Saskatchewan = new(49.0, -110.0, 60.0, -101.4);
        public static readonly BoundingBox Manitoba = new(49.0, -102.0, 60.0, -89.0);
        
        // Entire Canada
        public static readonly BoundingBox EntireCountry = new(41.7, -141.0, 83.1, -52.6);
    }

    /// <summary>
    /// Calculate appropriate zoom level to fit a bounding box in a given viewport size
    /// </summary>
    public static int CalculateZoomLevel(BoundingBox bounds, int viewportWidth, int viewportHeight)
    {
        // Calculate the span in degrees
        var latSpan = Math.Abs(bounds.MaxLat - bounds.MinLat);
        var lonSpan = Math.Abs(bounds.MaxLon - bounds.MinLon);

        // Determine zoom based on spans (approximate)
        // OSM zoom levels range from 0 (whole world) to 18 (street level)
        var latZoom = (int)Math.Floor(Math.Log(360.0 / latSpan) / Math.Log(2));
        var lonZoom = (int)Math.Floor(Math.Log(360.0 / lonSpan) / Math.Log(2));

        // Take the minimum to ensure everything fits
        var zoom = Math.Min(latZoom, lonZoom);
        
        // Clamp to valid range
        return Math.Clamp(zoom, 0, 18);
    }

    /// <summary>
    /// Calculate center point of a bounding box
    /// </summary>
    public static Coordinate CalculateCenter(BoundingBox bounds)
    {
        var centerLat = (bounds.MinLat + bounds.MaxLat) / 2.0;
        var centerLon = (bounds.MinLon + bounds.MaxLon) / 2.0;
        return new Coordinate(centerLat, centerLon);
    }

    /// <summary>
    /// Calculate distance between two coordinates (in kilometers) using Haversine formula
    /// </summary>
    public static double CalculateDistance(Coordinate point1, Coordinate point2)
    {
        const double earthRadiusKm = 6371.0;

        var lat1Rad = DegreesToRadians(point1.Latitude);
        var lat2Rad = DegreesToRadians(point2.Latitude);
        var deltaLatRad = DegreesToRadians(point2.Latitude - point1.Latitude);
        var deltaLonRad = DegreesToRadians(point2.Longitude - point1.Longitude);

        var a = Math.Sin(deltaLatRad / 2) * Math.Sin(deltaLatRad / 2) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLonRad / 2) * Math.Sin(deltaLonRad / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

/// <summary>
/// Represents a geographic coordinate (latitude, longitude)
/// </summary>
public record Coordinate(double Latitude, double Longitude)
{
    public override string ToString() => $"{Latitude:F4}, {Longitude:F4}";
}

/// <summary>
/// Represents a geographic bounding box
/// </summary>
public record BoundingBox(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    /// <summary>
    /// Expand the bounding box by a percentage
    /// </summary>
    public BoundingBox Expand(double percentage)
    {
        var latPadding = (MaxLat - MinLat) * percentage;
        var lonPadding = (MaxLon - MinLon) * percentage;
        
        return new BoundingBox(
            MinLat - latPadding,
            MinLon - lonPadding,
            MaxLat + latPadding,
            MaxLon + lonPadding
        );
    }

    public override string ToString() => $"[{MinLat:F2},{MinLon:F2}] to [{MaxLat:F2},{MaxLon:F2}]";
}
