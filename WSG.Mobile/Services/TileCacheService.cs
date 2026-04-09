using System.Collections.Concurrent;
using System.Net.Http;

namespace WSG.Mobile.Services;

/// <summary>
/// Three-tier OSM tile cache: in-memory LRU → file on CacheDir → HTTP download.
/// Thread-safe; designed to be called from background tasks in the GL renderer.
/// TTL matches the Windows BinaryTileCache: 7 days.
/// Supports multiple map styles via <see cref="SetMapStyle"/>.
/// </summary>
public sealed class TileCacheService : IDisposable
{
    private const int MaxMemoryTiles = 300;
    private const int MaxConcurrentDownloads = 6;
    private static readonly TimeSpan TileTtl = TimeSpan.FromDays(7);
    private const string UserAgent = "WSGMobile/1.0 (weather radar app; contact@noidsoftwork.com)";

    // ── Map style URL templates ────────────────────────────────────────────
    private static readonly Dictionary<string, string> StyleUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = "https://tile.openstreetmap.org/{0}/{1}/{2}.png",
        ["Dark"]      = "https://{s}.basemaps.cartocdn.com/dark_all/{0}/{1}/{2}.png",
        ["Terrain"]   = "https://tile.opentopomap.org/{0}/{1}/{2}.png",
        ["Satellite"] = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{0}/{1}/{2}",
    };
    private string _currentStyle = "Dark";
    private string _urlTemplate  = "https://{s}.basemaps.cartocdn.com/dark_all/{0}/{1}/{2}.png";
    // CartoDB is a multi-host CDN — cycle through a/b/c/d for distribution
    private int _cdnHostIndex;
    private static readonly string[] CartoHosts = ["a", "b", "c", "d"];

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _sem = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private readonly string _cacheRoot;

    // LRU in-memory cache: key → (pngBytes, accessTicks)
    // Key includes style so a style change does not serve stale tiles from a different provider.
    private readonly ConcurrentDictionary<(int z, int x, int y, string style), (byte[] Data, long Ticks)> _memCache = new();
    // Pending downloads — deduplicate concurrent requests for the same tile
    private readonly ConcurrentDictionary<(int z, int x, int y, string style), Task<byte[]?>> _pending = new();

    public TileCacheService()
    {
        _cacheRoot = Path.Combine(FileSystem.CacheDirectory, "tiles");
        Directory.CreateDirectory(_cacheRoot);

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Map style
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Available style names for display in the UI ("Dark", "Standard", "Terrain", "Satellite").
    /// </summary>
    public static IReadOnlyList<string> AvailableStyles => [.. StyleUrls.Keys];

    /// <summary>Currently active map style name.</summary>
    public string CurrentStyle => _currentStyle;

    /// <summary>
    /// Switch the tile source.  In-memory cache is cleared immediately; disk cache entries
    /// for the new style are served if present (each style writes to its own sub-folder).
    /// </summary>
    public void SetMapStyle(string style)
    {
        if (!StyleUrls.TryGetValue(style, out string? template)) return;
        if (string.Equals(_currentStyle, style, StringComparison.OrdinalIgnoreCase)) return;

        _currentStyle = style;
        _urlTemplate  = template;
        // Clear only memory cache; disk cache is keyed per style so it stays valid.
        _memCache.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Returns PNG bytes for the tile, fetching from cache or network as needed.</summary>
    public Task<byte[]?> GetTileAsync(int z, int x, int y)
    {
        var style = _currentStyle;
        var key   = (z, x, y, style);

        // 1. Hot path — memory cache
        if (_memCache.TryGetValue(key, out var cached))
        {
            _memCache[key] = (cached.Data, DateTime.UtcNow.Ticks); // refresh LRU
            return Task.FromResult<byte[]?>(cached.Data);
        }

        // 2. Deduplicate in-flight downloads
        return _pending.GetOrAdd(key, _ => FetchAsync(z, x, y, style))
                       .ContinueWith(t => { _pending.TryRemove(key, out _); return t.Result; },
                                     TaskScheduler.Default);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal fetch (disk → network)
    // ─────────────────────────────────────────────────────────────────────

    private async Task<byte[]?> FetchAsync(int z, int x, int y, string style)
    {
        // 1. Disk cache (style-namespaced)
        string path = TilePath(style, z, x, y);
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
                        AddToMemory((z, x, y, style), disk);
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
            string url = BuildUrl(style, z, x, y);
            byte[] data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (data.Length == 0) return null;

            // Persist to disk
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
            }
            catch { /* non-fatal */ }

            AddToMemory((z, x, y, style), data);
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

    private string BuildUrl(string style, int z, int x, int y)
    {
        string template = StyleUrls.GetValueOrDefault(style, StyleUrls["Dark"]);
        if (template.Contains("{s}"))
        {
            // CDN host rotation
            string host = CartoHosts[Interlocked.Increment(ref _cdnHostIndex) & 3];
            return string.Format(template.Replace("{s}", host), z, x, y);
        }
        return string.Format(template, z, x, y);
    }

    private string TilePath(string style, int z, int x, int y)
        => Path.Combine(_cacheRoot, style.ToLowerInvariant(), z.ToString(), x.ToString(), $"{y}.png");

    private void AddToMemory((int z, int x, int y, string style) key, byte[] data)
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
