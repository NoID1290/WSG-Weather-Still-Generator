#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BZTG.Services;
using WeatherShared;

namespace BZTG
{
    /// <summary>
    /// Top-level façade for the Blitzortung.org lightning data API.
    /// Mirrors the call pattern used by ECCCApi for easy substitution.
    /// Attribution: Lightning data provided by Blitzortung.org contributors.
    /// </summary>
    public static class BZTGApi
    {
        /// <summary>
        /// Fetches real-time lightning flash data from Blitzortung.org for a
        /// geographic bounding box and time range.
        /// </summary>
        /// <param name="httpClient">Shared HttpClient instance.</param>
        /// <param name="bbox">Bounding box (minLat, minLon, maxLat, maxLon).</param>
        /// <param name="from">Start of time window (UTC).</param>
        /// <param name="to">End of time window (UTC).</param>
        /// <param name="limit">Maximum number of flashes to return.</param>
        public static async Task<List<LightningFlash>> GetLightningStrikesAsync(
            HttpClient httpClient,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) bbox,
            DateTime from,
            DateTime to,
            int limit = 5000)
        {
            try
            {
                Console.WriteLine($"[BZTG API] Fetching lightning: {from:u} → {to:u}");
                var svc     = new BlitzortungService(httpClient);
                var flashes = await svc.FetchLightningStrikesAsync(bbox, from, to, limit);
                Console.WriteLine($"[BZTG API] ✓ {flashes.Count} lightning flashes received");
                return flashes;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BZTG API] Error fetching lightning: {ex.Message}");
                return new List<LightningFlash>();
            }
        }
    }
}
