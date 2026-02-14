using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WeatherImageGenerator.OpenGL;
using OpenMap;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// Generates a tile pyramid for a geographic bounding box and stores tiles in
    /// - BinaryTileCache (map_cache)
    /// - optional BinaryTileCache for TileProvider (tilecache)
    /// - optional local z/x/y.png folder (LocalTilesRoot)
    ///
    /// Designed to be invoked from the UI to pre-populate local caches for offline / snappy map rendering.
    /// </summary>
    public static class TilePyramidGenerator
    {
        public class ProgressState
        {
            public int Completed { get; set; }
            public int Total { get; set; }
            public int Fetched { get; set; }
            public string Message { get; set; } = "";
        }

        /// <summary>
        /// Generate tiles for the bounding box between minLat/minLon and maxLat/maxLon for zooms in [minZoom,maxZoom].
        /// Writes tiles into provided BinaryTileCache(s) and optionally into a local tiles folder (z/x/y.png layout).
        /// </summary>
        public static async Task<ProgressState> GenerateAsync(
            BinaryTileCache mapCache,
            MapOverlayService mapService,
            int minZoom,
            int maxZoom,
            double minLat,
            double minLon,
            double maxLat,
            double maxLon,
            BinaryTileCache? tileProviderCache = null,
            string? localTilesRoot = null,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellation = default)
        {
            if (mapCache == null) throw new ArgumentNullException(nameof(mapCache));
            if (mapService == null) throw new ArgumentNullException(nameof(mapService));
            if (minZoom < 0) minZoom = 0;
            if (maxZoom < minZoom) maxZoom = minZoom;

            // Calculate total tiles to process
            int total = 0;
            for (int z = minZoom; z <= maxZoom; z++)
            {
                var (x0, x1, y0, y1) = TileBoundsForBBox(minLat, minLon, maxLat, maxLon, z);
                if (x1 >= x0 && y1 >= y0)
                    total += (x1 - x0 + 1) * (y1 - y0 + 1);
            }

            var state = new ProgressState { Completed = 0, Total = total, Fetched = 0 };
            progress?.Report(state);

            // Ensure local folder exists if requested
            if (!string.IsNullOrEmpty(localTilesRoot))
            {
                Directory.CreateDirectory(localTilesRoot!);
            }

            // Limit parallelism to be polite to tile servers
            int maxParallel = 8;
            using var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = new List<Task>();

            for (int z = minZoom; z <= maxZoom; z++)
            {
                var (xMin, xMax, yMin, yMax) = TileBoundsForBBox(minLat, minLon, maxLat, maxLon, z);
                int wrap = 1 << z;

                for (int tx = xMin; tx <= xMax; tx++)
                {
                    for (int ty = yMin; ty <= yMax; ty++)
                    {
                        cancellation.ThrowIfCancellationRequested();

                        // Normalize x (wrap horizontally)
                        int wrappedX = ((tx % wrap) + wrap) % wrap;
                        int tz = z; int txWrapped = wrappedX; int tyv = ty;

                        // Skip if already present in BOTH caches / local file
                        bool existsInMapCache = mapCache.HasTile(tz, txWrapped, tyv);
                        bool existsInTileProviderCache = tileProviderCache?.HasTile(tz, txWrapped, tyv) ?? false;
                        bool existsLocalFile = false;
                        if (!string.IsNullOrEmpty(localTilesRoot))
                        {
                            var p = Path.Combine(localTilesRoot!, z.ToString(), wrappedX.ToString(), tyv + ".png");
                            existsLocalFile = File.Exists(p);
                        }

                        if (existsInMapCache && (tileProviderCache == null || existsInTileProviderCache) && (string.IsNullOrEmpty(localTilesRoot) || existsLocalFile))
                        {
                            state.Completed++;
                            progress?.Report(state);
                            continue;
                        }

                        await semaphore.WaitAsync(cancellation);
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                // Use MapOverlayService to fetch (it will respect existing MapCache files when available)
                                var (bytes, httpStatus) = await mapService.FetchTileBytesAsync(wrappedX, ty, z, MapStyle.Standard);
                                if (bytes != null && bytes.Length > 0)
                                {
                                    // store in binary caches
                                    try { await mapCache.PutTileAsync(z, wrappedX, ty, bytes); } catch { }
                                    if (tileProviderCache != null)
                                    {
                                        try { await tileProviderCache.PutTileAsync(z, wrappedX, ty, bytes); } catch { }
                                    }

                                    // write canonical local file (z/x/y.png) if requested
                                    if (!string.IsNullOrEmpty(localTilesRoot))
                                    {
                                        try
                                        {
                                            var dir = Path.Combine(localTilesRoot!, z.ToString(), wrappedX.ToString());
                                            Directory.CreateDirectory(dir);
                                            var path = Path.Combine(dir, ty + ".png");
                                            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                                        }
                                        catch { }
                                    }

                                    state.Fetched++;
                                }
                            }
                            catch
                            {
                                // ignore single-tile failures
                            }
                            finally
                            {
                                state.Completed++;
                                state.Message = $"z={z} x={wrappedX} y={ty} (fetched={state.Fetched})";
                                progress?.Report(state);
                                semaphore.Release();
                            }
                        }, cancellation);

                        tasks.Add(task);

                        // Throttle queued tasks to avoid excessive memory usage
                        if (tasks.Count > 1024)
                        {
                            var t = await Task.WhenAny(tasks);
                            tasks.Remove(t);
                        }
                    }
                }
            }

            // Wait for remaining tasks
            await Task.WhenAll(tasks);
            state.Message = "Complete";
            progress?.Report(state);
            return state;
        }

        private static (int x0, int x1, int y0, int y1) TileBoundsForBBox(double minLat, double minLon, double maxLat, double maxLon, int zoom)
        {
            // Clamp latitudes to Mercator usable range
            minLat = Math.Max(minLat, -85.05112878);
            maxLat = Math.Min(maxLat, 85.05112878);

            int x0 = LonToTileX(minLon, zoom);
            int x1 = LonToTileX(maxLon, zoom);
            int y0 = LatToTileY(maxLat, zoom); // top (min Y)
            int y1 = LatToTileY(minLat, zoom); // bottom (max Y)

            // Ensure within tile grid
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
    }
}
