using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WeatherImageGenerator.Rendering.Common
{
    public enum TileFetchStatus { Ok, NotFound, Blocked, Error }

    public class TileProvider : IDisposable
    {
        private readonly HttpClient _client = new HttpClient();
        private readonly string _cacheRoot;
        private readonly string _urlTemplate;
        private readonly OpenMap.MapOverlayService? _mapService;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BinaryTileCache> _styleCaches = new();
        private readonly bool _useBinaryCache;

        /// <summary>
        /// Optional local tiles root folder. If set, tiles will be read from here first (path layout z/x/y.png).
        /// </summary>
        public string? LocalTilesRoot { get; set; }

        /// <summary>
        /// Current map tile style. Changes which tile server URLs are used for fetching.
        /// </summary>
        public OpenMap.MapStyle CurrentStyle { get; set; } = OpenMap.MapStyle.Standard;

        public TileProvider(string urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png", OpenMap.MapOverlayService? mapService = null, bool useBinaryCache = true)
        {
            _urlTemplate = urlTemplate;
            _cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "tilecache");
            Directory.CreateDirectory(_cacheRoot);
            _useBinaryCache = useBinaryCache;

            // Provide a minimal User-Agent to be polite to tile servers
            try { _client.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-Radar/1.0 (+https://example.com)"); } catch { }

            // Use existing OpenMap map service if provided so we reuse its cache and timeouts
            _mapService = mapService;
        }

        private BinaryTileCache? GetCacheForCurrentStyle()
        {
            if (!_useBinaryCache) return null;
            var key = CurrentStyle.ToString().ToLowerInvariant();
            try
            {
                return _styleCaches.GetOrAdd(key, k =>
                {
                    var dir = Path.Combine(_cacheRoot, k);
                    Directory.CreateDirectory(dir);
                    return new BinaryTileCache(dir);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TileProvider] Binary cache init failed for style {key}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the tile URL for the specified coordinates using the current map style.
        /// </summary>
        private string GetTileUrlForCurrentStyle(int z, int x, int y)
        {
            return CurrentStyle switch
            {
                OpenMap.MapStyle.Standard => $"https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                OpenMap.MapStyle.Minimal => $"https://tile.openstreetmap.fr/hot/{z}/{x}/{y}.png",
                OpenMap.MapStyle.Terrain => $"https://tile.opentopomap.org/{z}/{x}/{y}.png",
                OpenMap.MapStyle.Satellite => $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                OpenMap.MapStyle.TerrainDark => $"https://cartodb-basemaps-a.global.ssl.fastly.net/dark_all/{z}/{x}/{y}.png",
                _ => $"https://tile.openstreetmap.org/{z}/{x}/{y}.png"
            };
        }

        /// <summary>
        /// Returns tile bytes and a status explaining the result. Detects common blocked tile images and treats them as Blocked.
        /// </summary>
        public async Task<(byte[]? Bytes, TileFetchStatus Status)> GetTileBytesAsync(int z, int x, int y)
        {
            try
            {
                // Check local tiles first (offline support)
                if (!string.IsNullOrEmpty(LocalTilesRoot))
                {
                    var localPath = Path.Combine(LocalTilesRoot, z.ToString(), x.ToString(), y + ".png");
                    if (File.Exists(localPath))
                    {
                        var b = await File.ReadAllBytesAsync(localPath);
                        return (b, TileFetchStatus.Ok);
                    }
                }

                var binaryCache = GetCacheForCurrentStyle();
                var styleDir = CurrentStyle.ToString().ToLowerInvariant();

                // Check binary cache first (fastest, style-specific)
                if (binaryCache != null)
                {
                    var cachedBytes = await binaryCache.GetTileAsync(z, x, y);
                    if (cachedBytes != null)
                    {
                        if (IsBlockedImage(cachedBytes)) return (null, TileFetchStatus.Blocked);
                        return (cachedBytes, TileFetchStatus.Ok);
                    }
                }

                // Check old file-based cache (fallback, style-prefixed)
                var dir = Path.Combine(_cacheRoot, styleDir, z.ToString(), x.ToString());
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, y + ".png");
                if (File.Exists(file))
                {
                    var b = await File.ReadAllBytesAsync(file);
                    if (IsBlockedImage(b)) return (null, TileFetchStatus.Blocked);
                    
                    if (binaryCache != null)
                    {
                        _ = binaryCache.PutTileAsync(z, x, y, b);
                    }
                    
                    return (b, TileFetchStatus.Ok);
                }

                // If OpenMap MapOverlayService is available, delegate tile fetching to it
                if (_mapService != null)
                {
                    try
                    {
                        var (bytes, httpStatus) = await _mapService.FetchTileBytesAsync(x, y, z, CurrentStyle);
                        if (httpStatus == 403 || httpStatus == 401) return (null, TileFetchStatus.Blocked);
                        if (httpStatus == 404) return (null, TileFetchStatus.NotFound);
                        if (httpStatus != 0 || bytes == null) return (null, TileFetchStatus.Error);

                        if (IsBlockedImage(bytes)) return (null, TileFetchStatus.Blocked);

                        if (binaryCache != null)
                        {
                            await binaryCache.PutTileAsync(z, x, y, bytes);
                        }
                        else
                        {
                            try { await File.WriteAllBytesAsync(file, bytes); } catch { }
                        }
                        
                        return (bytes, TileFetchStatus.Ok);
                    }
                    catch
                    {
                        // fallback to direct network below if OpenMap fails
                    }
                }

                // Fetch from network (style-aware)
                var url = GetTileUrlForCurrentStyle(z, x, y);
                var resp = await _client.GetAsync(url);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, TileFetchStatus.NotFound);
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return (null, TileFetchStatus.Blocked);
                if (!resp.IsSuccessStatusCode) return (null, TileFetchStatus.Error);

                var data = await resp.Content.ReadAsByteArrayAsync();
                if (IsBlockedImage(data)) return (null, TileFetchStatus.Blocked);

                if (binaryCache != null)
                {
                    await binaryCache.PutTileAsync(z, x, y, data);
                }
                else
                {
                    try { await File.WriteAllBytesAsync(file, data); } catch { }
                }
                
                return (data, TileFetchStatus.Ok);
            }
            catch
            {
                return (null, TileFetchStatus.Error);
            }
        }

        /// <summary>
        /// Returns aggregate cache stats across all tile style caches.
        /// </summary>
        public CacheStats GetAggregateStats()
        {
            var result = new CacheStats();
            foreach (var kvp in _styleCaches)
            {
                try
                {
                    var s = kvp.Value.GetStats();
                    result.TileCount += s.TileCount;
                    result.TotalSizeBytes += s.TotalSizeBytes;
                    result.RamCacheEntries += s.RamCacheEntries;
                    result.RamCacheBytes += s.RamCacheBytes;
                }
                catch { }
            }
            result.CacheFilePath = _cacheRoot;
            return result;
        }

        private static bool IsBlockedImage(byte[] data)
        {
            try
            {
                var s = System.Text.Encoding.ASCII.GetString(data);
                if (s.IndexOf("Access blocked", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            _client?.Dispose();
            foreach (var cache in _styleCaches.Values)
            {
                try { cache?.Dispose(); } catch { }
            }
            _styleCaches.Clear();
        }
    }
}
