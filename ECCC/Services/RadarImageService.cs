#nullable enable
using System;
using System.Net.Http;
using System.Threading.Tasks;
using ECCC.Models;

namespace ECCC.Services
{
    /// <summary>
    /// Service for fetching and managing ECCC radar images.
    /// Provides radar imagery based on location coordinates.
    /// </summary>
    public class RadarImageService
    {
        private readonly HttpClient _httpClient;
        private readonly EcccSettings _settings;
        
        // Common radar layer for precipitation
        private const string DefaultRadarLayer = "RADAR_1KM_RRAI";
        
        public RadarImageService(HttpClient httpClient, EcccSettings? settings = null)
        {
            _httpClient = httpClient;
            _settings = settings ?? new EcccSettings();
        }

        /// <summary>
        /// Fetches a radar image for the specified location coordinates.
        /// Creates a bounding box centered on the location.
        /// </summary>
        /// <param name="latitude">Location latitude</param>
        /// <param name="longitude">Location longitude</param>
        /// <param name="radiusKm">Radius in kilometers for the bounding box (default: 100km)</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <returns>Radar image data or null if failed</returns>
        public async Task<byte[]?> FetchRadarImageAsync(
            double latitude, 
            double longitude, 
            double radiusKm = 100,
            int width = 800,
            int height = 600)
        {
            try
            {
                // Calculate bounding box based on radius
                // Approximate: 1 degree latitude = 111 km, 1 degree longitude varies by latitude
                double latDelta = radiusKm / 111.0;
                double lonDelta = radiusKm / (111.0 * Math.Cos(latitude * Math.PI / 180.0));

                var bbox = (
                    MinLat: latitude - latDelta,
                    MinLon: longitude - lonDelta,
                    MaxLat: latitude + latDelta,
                    MaxLon: longitude + lonDelta
                );

                var request = new EcccDataRequest
                {
                    DataType = EcccDataType.WmsLayer,
                    Layer = DefaultRadarLayer,
                    BoundingBox = bbox,
                    Width = width,
                    Height = height,
                    Format = "image/png",
                    Crs = "EPSG:4326"
                };

                // Use the ECCC service to fetch the WMS image
                var url = BuildRadarUrl(bbox, width, height);
                
                if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                        _settings.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                }

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RadarImageService] Error fetching radar image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the WMS URL for radar imagery.
        /// </summary>
        private string BuildRadarUrl(
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            int width,
            int height)
        {
            const string baseUrl = "https://geo.weather.gc.ca/geomet";
            
            return $"{baseUrl}?" +
                   $"SERVICE=WMS&" +
                   $"VERSION=1.3.0&" +
                   $"REQUEST=GetMap&" +
                   $"LAYERS={Uri.EscapeDataString(DefaultRadarLayer)}&" +
                   $"CRS=EPSG:4326&" +
                   $"BBOX={bbox.MinLat},{bbox.MinLon},{bbox.MaxLat},{bbox.MaxLon}&" +
                   $"WIDTH={width}&" +
                   $"HEIGHT={height}&" +
                   $"FORMAT=image/png&" +
                   $"TRANSPARENT=TRUE";
        }

        /// <summary>
        /// Gets the radar layer description.
        /// </summary>
        public static string GetRadarLayerDescription()
        {
            return "1km Resolution Rain Rate Radar (RADAR_1KM_RRAI)";
        }
    }
}
