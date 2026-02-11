using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WeatherImageGenerator.OpenGL
{
    public enum TileFetchStatus { Ok, NotFound, Blocked, Error }

    public class TileProvider : IDisposable
    {
        private readonly HttpClient _client = new HttpClient();
        private readonly string _cacheRoot;
        private readonly string _urlTemplate;
        private readonly OpenMap.MapOverlayService? _mapService;

        /// <summary>
        /// Optional local tiles root folder. If set, tiles will be read from here first (path layout z/x/y.png).
        /// </summary>
        public string? LocalTilesRoot { get; set; }

        public TileProvider(string urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png", OpenMap.MapOverlayService? mapService = null)
        {
            _urlTemplate = urlTemplate;
            _cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "tilecache");
            Directory.CreateDirectory(_cacheRoot);

            // Provide a minimal User-Agent to be polite to tile servers
            try { _client.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-Radar/1.0 (+https://example.com)"); } catch { }

            // Use existing OpenMap map service if provided so we reuse its cache and timeouts
            _mapService = mapService;
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

                // Check cache
                var dir = Path.Combine(_cacheRoot, z.ToString(), x.ToString());
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, y + ".png");
                if (File.Exists(file))
                {
                    var b = await File.ReadAllBytesAsync(file);
                    // detect blocked image text
                    if (IsBlockedImage(b)) return (null, TileFetchStatus.Blocked);
                    return (b, TileFetchStatus.Ok);
                }

                // If OpenMap MapOverlayService is available, delegate tile fetching to it
                if (_mapService != null)
                {
                    try
                    {
                        // Use Standard style by default; MapOverlayService will handle cache and timeouts
                        var (bytes, httpStatus) = await _mapService.FetchTileBytesAsync(x, y, z, OpenMap.MapStyle.Standard);
                        if (httpStatus == 403 || httpStatus == 401) return (null, TileFetchStatus.Blocked);
                        if (httpStatus == 404) return (null, TileFetchStatus.NotFound);
                        if (httpStatus != 0 || bytes == null) return (null, TileFetchStatus.Error);

                        // Detect blocked image heuristics
                        if (IsBlockedImage(bytes)) return (null, TileFetchStatus.Blocked);

                        try { await File.WriteAllBytesAsync(file, bytes); } catch { }
                        return (bytes, TileFetchStatus.Ok);
                    }
                    catch
                    {
                        // fallback to direct network below if OpenMap fails
                    }
                }

                // Fetch from network (legacy fallback)
                var url = _urlTemplate.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
                var resp = await _client.GetAsync(url);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, TileFetchStatus.NotFound);
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return (null, TileFetchStatus.Blocked);
                if (!resp.IsSuccessStatusCode) return (null, TileFetchStatus.Error);

                var data = await resp.Content.ReadAsByteArrayAsync();
                // Check common blocked-image content
                if (IsBlockedImage(data)) return (null, TileFetchStatus.Blocked);

                try { await File.WriteAllBytesAsync(file, data); } catch { }
                return (data, TileFetchStatus.Ok);            }
            catch
            {
                return (null, TileFetchStatus.Error);
            }
        }

        private static bool IsBlockedImage(byte[] data)
        {
            // Simple heuristic: check if the bytes contain the ASCII text "Access blocked" or "access blocked"
            try
            {
                var s = System.Text.Encoding.ASCII.GetString(data);
                if (s.IndexOf("Access blocked", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (s.IndexOf("access blocked", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (s.IndexOf("Access blocked", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
