using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ECCC.Api;

namespace ECCC.Services
{
    /// <summary>
    /// Convert ECCC GeoMet WMS frames into standard 256×256 XYZ tiles and store them
    /// under the local map cache directory using the layout: <base>/radar/<timestamp>/z/x/y.png
    ///
    /// - 'time' value of "latest" will omit the TIME parameter and fetch the most-recent frame.
    /// - This is intentionally file-based (timestamped namespace) so BinaryTileCache API does
    ///   not need to be changed for time-versioned radar tiles.
    /// </summary>
    public static class RadarTileGenerator
    {
        /// <summary>
        /// Generate radar tiles for the specified bounding box, zoom range and timestamps.
        /// </summary>
        public class RadarProgress
        {
            public int Completed { get; set; }
            public int Total { get; set; }
            public int Fetched { get; set; }
            public string Message { get; set; } = "";
        }

        public static async Task<RadarProgress> GenerateRadarTilesAsync(
            HttpClient httpClient,
            string layer,
            IEnumerable<string> times,
            int minZoom,
            int maxZoom,
            double minLat,
            double minLon,
            double maxLat,
            double maxLon,
            string? outputBaseDir = null,
            int parallelism = 4,
            int delayBetweenRequestsMs = 200,
            IProgress<RadarProgress>? progress = null,
            CancellationToken cancellation = default)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrWhiteSpace(layer)) layer = WmsLayers.Radar1KmRain;
            if (minZoom < 0) minZoom = 0;
            if (maxZoom < minZoom) maxZoom = minZoom;

            var timeList = (times == null || !times.Any()) ? new List<string> { "latest" } : times.ToList();

            // Default cache base: %LOCALAPPDATA%/WSG/map_cache
            var baseDir = outputBaseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "map_cache");
            var radarRoot = Path.Combine(baseDir, "radar");
            Directory.CreateDirectory(radarRoot);

            // Calculate total tiles to process across zooms and timestamps
            int totalTiles = 0;
            for (int z = minZoom; z <= maxZoom; z++)
            {
                var (x0, x1, y0, y1) = TileBoundsForBBox(minLat, minLon, maxLat, maxLon, z);
                if (x1 >= x0 && y1 >= y0)
                    totalTiles += (x1 - x0 + 1) * (y1 - y0 + 1);
            }

            int total = Math.Max(0, totalTiles * Math.Max(1, timeList.Count));
            var state = new RadarProgress { Completed = 0, Total = total, Fetched = 0 };
            progress?.Report(state);

            // Use atomic counters for thread-safe increments (properties cannot be used with Interlocked)
            int fetchedCounter = 0;
            int completedCounter = 0;

            using var semaphore = new SemaphoreSlim(Math.Max(1, parallelism));
            var tasks = new List<Task>();

            foreach (var time in timeList)
            {
                // sanitize folder name for the timestamp (windows-safe)
                var safeTime = string.IsNullOrWhiteSpace(time) ? "latest" : time.Replace(':', '-');

                for (int z = minZoom; z <= maxZoom; z++)
                {
                    var (xMin, xMax, yMin, yMax) = TileBoundsForBBox(minLat, minLon, maxLat, maxLon, z);
                    int wrap = 1 << z;

                    for (int tx = xMin; tx <= xMax; tx++)
                    {
                        for (int ty = yMin; ty <= yMax; ty++)
                        {
                            cancellation.ThrowIfCancellationRequested();

                            int wrappedX = ((tx % wrap) + wrap) % wrap;
                            int tz = z; int txx = wrappedX; int tyy = ty;

                            await semaphore.WaitAsync(cancellation);

                            var task = Task.Run(async () =>
                            {
                                try
                                {
                                    var bbox = TileToBBox(tz, txx, tyy);

                                    // If the caller used the special "latest" token, omit TIME to get most recent
                                    string? wmsTime = (string.Equals(time, "latest", StringComparison.OrdinalIgnoreCase)) ? null : time;
                                    var url = UrlBuilder.BuildWmsUrl(layer, bbox, 256, 256, "image/png", wmsTime);

                                    try
                                    {
                                        var resp = await httpClient.GetAsync(url, cancellation);
                                        if (resp.IsSuccessStatusCode)
                                        {
                                            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellation);
                                            if (bytes != null && bytes.Length > 0)
                                            {
                                                var outDir = Path.Combine(radarRoot, safeTime, tz.ToString(), txx.ToString());
                                                Directory.CreateDirectory(outDir);
                                                var outPath = Path.Combine(outDir, tyy + ".png");

                                                // overwrite if exists (refresh)
                                                await File.WriteAllBytesAsync(outPath, bytes, cancellation);
                                                Interlocked.Increment(ref fetchedCounter);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // ignore single-tile failures
                                    }
                                    finally
                                    {
                                        Interlocked.Increment(ref completedCounter);
                                        state.Fetched = fetchedCounter;
                                        state.Completed = completedCounter;
                                        state.Message = $"z={tz} x={txx} y={tyy} (fetched={state.Fetched})";
                                        progress?.Report(state);

                                        // Respect throttling
                                        try { await Task.Delay(delayBetweenRequestsMs, cancellation); } catch { }
                                    }
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }, cancellation);

                            tasks.Add(task);

                            // keep queued tasks bounded
                            if (tasks.Count > 1024)
                            {
                                var t = await Task.WhenAny(tasks);
                                tasks.Remove(t);
                            }
                        }
                    }
                }
            }

            await Task.WhenAll(tasks);

            state.Message = "Complete";
            progress?.Report(state);
            return state;
        }

        #region Tile math (XYZ <-> EPSG:4326)
        private static (double MinLat, double MinLon, double MaxLat, double MaxLon) TileToBBox(int z, int x, int y)
        {
            double n = Math.Pow(2.0, z);
            double lonLeft = x / n * 360.0 - 180.0;
            double lonRight = (x + 1) / n * 360.0 - 180.0;

            double latTop = TileYToLat(y, z);
            double latBottom = TileYToLat(y + 1, z);

            // Return as (minLat, minLon, maxLat, maxLon) matching UrlBuilder expectations for EPSG:4326
            return (MinLat: latBottom, MinLon: lonLeft, MaxLat: latTop, MaxLon: lonRight);
        }

        private static double TileYToLat(int y, int z)
        {
            double n = Math.PI - (2.0 * Math.PI * y) / Math.Pow(2.0, z);
            double lat = (180.0 / Math.PI) * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
            return lat;
        }

        private static (int x0, int x1, int y0, int y1) TileBoundsForBBox(double minLat, double minLon, double maxLat, double maxLon, int zoom)
        {
            minLat = Math.Max(minLat, -85.05112878);
            maxLat = Math.Min(maxLat, 85.05112878);

            int x0 = LonToTileX(minLon, zoom);
            int x1 = LonToTileX(maxLon, zoom);
            int y0 = LatToTileY(maxLat, zoom);
            int y1 = LatToTileY(minLat, zoom);

            int maxIndex = (1 << zoom) - 1;
            x0 = Math.Max(0, Math.Min(maxIndex, x0));
            x1 = Math.Max(0, Math.Min(maxIndex, x1));
            y0 = Math.Max(0, Math.Min(maxIndex, y0));
            y1 = Math.Max(0, Math.Min(maxIndex, y1));

            return (x0, x1, y0, y1);
        }

        private static int LonToTileX(double lon, int z)
        {
            double n = Math.Pow(2.0, z);
            return (int)Math.Floor((lon + 180.0) / 360.0 * n);
        }

        private static int LatToTileY(double lat, int z)
        {
            var latRad = lat * Math.PI / 180.0;
            var n = Math.Pow(2.0, z);
            var y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
            return (int)Math.Floor(y);
        }
        #endregion
    }
}
