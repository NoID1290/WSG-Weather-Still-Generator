using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WeatherImageGenerator.OpenGL
{
    public class TileProvider : IDisposable
    {
        private readonly HttpClient _client = new HttpClient();
        private readonly string _cacheRoot;
        private readonly string _urlTemplate;

        /// <summary>
        /// Optional local tiles root folder. If set, tiles will be read from here first (path layout z/x/y.png).
        /// </summary>
        public string? LocalTilesRoot { get; set; }

        public TileProvider(string urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png")
        {
            _urlTemplate = urlTemplate;
            _cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "tilecache");
            Directory.CreateDirectory(_cacheRoot);

            // Provide a minimal User-Agent to be polite to tile servers
            try { _client.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-Radar/1.0 (+https://example.com)"); } catch { }
        }

        public async Task<byte[]?> GetTileBytesAsync(int z, int x, int y)
        {
            try
            {
                // Check local tiles first (offline support)
                if (!string.IsNullOrEmpty(LocalTilesRoot))
                {
                    var localPath = Path.Combine(LocalTilesRoot, z.ToString(), x.ToString(), y + ".png");
                    if (File.Exists(localPath))
                    {
                        return await File.ReadAllBytesAsync(localPath);
                    }
                }

                // Check cache
                var dir = Path.Combine(_cacheRoot, z.ToString(), x.ToString());
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, y + ".png");
                if (File.Exists(file))
                {
                    return await File.ReadAllBytesAsync(file);
                }

                // Fetch from network
                var url = _urlTemplate.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
                var resp = await _client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return null;
                var data = await resp.Content.ReadAsByteArrayAsync();
                try { await File.WriteAllBytesAsync(file, data); } catch { }
                return data;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
