using System.Collections.Concurrent;
using System.Net.Http;

namespace WSG.Mobile.Services;

/// <summary>
/// Three-tier OSM tile cache: in-memory LRU → file on CacheDir → HTTP download.
/// Thread-safe; designed to be called from background tasks in the GL renderer.
/// TTL matches the Windows BinaryTileCache: 7 days.
/// </summary>
public sealed class TileCacheService : IDisposable
{
    private const int MaxMemoryTiles = 300;
    private const int MaxConcurrentDownloads = 6;
    private static readonly TimeSpan TileTtl = TimeSpan.FromDays(7);
    private const string OsmUrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
    private const string UserAgent = "WSGMobile/1.0 (weather radar app; contact@noidsoftwork.com)";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _sem = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private readonly string _cacheRoot;

    // LRU in-memory cache: key → (pngBytes, accessTicks)
    private readonly ConcurrentDictionary<(int z, int x, int y), (byte[] Data, long Ticks)> _memCache = new();
    // Pending downloads — deduplicate concurrent requests for the same tile
    private readonly ConcurrentDictionary<(int z, int x, int y), Task<byte[]?>> _pending = new();

    public TileCacheService()
    {
        _cacheRoot = Path.Combine(FileSystem.CacheDirectory, "tiles");
        Directory.CreateDirectory(_cacheRoot);

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Returns PNG bytes for the tile, fetching from cache or network as needed.</summary>
    public Task<byte[]?> GetTileAsync(int z, int x, int y)
    {
        var key = (z, x, y);

        // 1. Hot path — memory cache
        if (_memCache.TryGetValue(key, out var cached))
        {
            _memCache[key] = (cached.Data, DateTime.UtcNow.Ticks); // refresh LRU
            return Task.FromResult<byte[]?>(cached.Data);
        }

        // 2. Deduplicate in-flight downloads
        return _pending.GetOrAdd(key, _ => FetchAsync(z, x, y))
                       .ContinueWith(t => { _pending.TryRemove(key, out _); return t.Result; },
                                     TaskScheduler.Default);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal fetch (disk → network)
    // ─────────────────────────────────────────────────────────────────────

    private async Task<byte[]?> FetchAsync(int z, int x, int y)
    {
        // 1. Disk cache
        string path = TilePath(z, x, y);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (DateTime.UtcNow - info.LastWriteTimeUtc < TileTtl)
            {
                try
                {
                    byte[] disk = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    if (disk.Length > 0)
                    {
                        AddToMemory((z, x, y), disk);
                        return disk;
                    }
                }
                catch { /* fall through to network */ }
            }
        }

        // 2. Network download
        await _sem.WaitAsync().ConfigureAwait(false);
        try
        {
            string url = string.Format(OsmUrlTemplate, z, x, y);
            byte[] data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (data.Length == 0) return null;

            // Persist to disk
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
            }
            catch { /* non-fatal */ }

            AddToMemory((z, x, y), data);
            return data;
        }
        catch
        {
            return null;
        }
        finally
        {
            _sem.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private string TilePath(int z, int x, int y)
        => Path.Combine(_cacheRoot, z.ToString(), x.ToString(), $"{y}.png");

    private void AddToMemory((int z, int x, int y) key, byte[] data)
    {
        _memCache[key] = (data, DateTime.UtcNow.Ticks);
        if (_memCache.Count <= MaxMemoryTiles) return;

        // Evict the 20 least-recently-used entries
        var oldest = _memCache.OrderBy(kv => kv.Value.Ticks).Take(20).Select(kv => kv.Key).ToList();
        foreach (var k in oldest) _memCache.TryRemove(k, out _);
    }

    public void Dispose()
    {
        _http.Dispose();
        _sem.Dispose();
    }
}
