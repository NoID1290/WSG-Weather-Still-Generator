using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Globalization;
using System.Text;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services
{
    // Minimal, well-typed ECCC library implementation (extracted).
    public static class ECCC
    {
        public static async Task<List<AlertEntry>> FetchAllAlerts(HttpClient client, IEnumerable<string>? wantedCities = null, EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            var feeds = cfg.CityFeeds ?? new Dictionary<string, string>();
            // If caller supplied a list of desired cities, filter the feeds to only those cities (normalize names)
            if (wantedCities != null && wantedCities.Any())
            {
                var wantedSet = new HashSet<string>(wantedCities.Select(NormalizeCity));
                feeds = feeds.Where(kv => wantedSet.Contains(NormalizeCity(kv.Key)))
                             .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            var result = new List<AlertEntry>();
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", cfg.UserAgent ?? "Mozilla/5.0");

            foreach (var kv in feeds)
            {
                try
                {
                    var xml = await client.GetStringAsync(kv.Value);
                    var doc = XDocument.Parse(xml);
                    var atom = "http://www.w3.org/2005/Atom";
                    var entries = doc.Root?.Elements(XName.Get("entry", atom)) ?? Enumerable.Empty<XElement>();
                    foreach (var e in entries)
                    {
                        var title = e.Element(XName.Get("title", atom))?.Value ?? string.Empty;
                        var summary = e.Element(XName.Get("summary", atom))?.Value ?? string.Empty;
                        var category = e.Element(XName.Get("category", atom))?.Attribute("term")?.Value ?? string.Empty;
                        if (category.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("avertiss", StringComparison.OrdinalIgnoreCase) >= 0 || category.IndexOf("watch", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (title.IndexOf("no watches", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("aucune veille", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            result.Add(new AlertEntry { City = kv.Key, Title = title, Summary = summary });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ECCC] Error fetching {kv.Key}: {ex.Message}");
                }
                await Task.Delay(cfg.DelayBetweenRequestsMs);
            }
            return result;
        }

        public static async Task FetchRadarImages(HttpClient client, string outputDir, EcccSettings? settings = null)
        {
            var cfg = settings ?? LoadSettings();
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            // Minimal implementation: download any RadarFeeds direct URLs.
            foreach (var kv in cfg.RadarFeeds ?? new Dictionary<string,string>())
            {
                try
                {
                    var resp = await client.GetAsync(kv.Value);
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        var ext = GetExtFromContent(resp.Content.Headers.ContentType?.MediaType, kv.Value);
                        var path = Path.Combine(outputDir, $"radar_{Sanitize(kv.Key)}{ext}");
                        await File.WriteAllBytesAsync(path, bytes);
                        Console.WriteLine($"✓ Downloaded radar {kv.Key} -> {path}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ECCC] Radar fetch failed for {kv.Key}: {ex.Message}");
                }
            }
        }

        public static Task CreateProvinceRadarAnimation(HttpClient client, string outputDir, EcccSettings? settings = null)
        {
            // For quick extraction, keep a no-op that can be extended later.
            Console.WriteLine("ECCC.CreateProvinceRadarAnimation: not implemented in minimal library (no-op).");
            return Task.CompletedTask;
        }

        private static string GetExtFromContent(string? contentType, string url)
        {
            var ct = contentType?.ToLowerInvariant() ?? string.Empty;
            if (ct.Contains("png")) return ".png";
            if (ct.Contains("gif")) return ".gif";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return ".jpg";
            if (ct.Contains("mp4") || ct.Contains("video")) return ".mp4";
            try { var e = Path.GetExtension(new Uri(url).AbsolutePath); if (!string.IsNullOrWhiteSpace(e)) return e; } catch { }
            return ".png";
        }

        private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static EcccSettings LoadSettings()
        {
            try
            {
                var p = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(p)) return new EcccSettings();
                using var fs = File.OpenRead(p);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("ECCC", out var elem))
                {
                    return JsonSerializer.Deserialize<EcccSettings>(elem.GetRawText()) ?? new EcccSettings();
                }
            }
            catch (Exception ex) { Console.WriteLine($"[ECCC] Failed to load settings: {ex.Message}"); }
            return new EcccSettings();
        }

        // Normalize city names by removing diacritics, trimming and lowercasing to allow matching user-configured locations
        private static string NormalizeCity(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var normalized = s.Normalize(NormalizationForm.FormD);
            var filtered = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return filtered.Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
        }

        public class EcccSettings
        {
            public Dictionary<string,string>? CityFeeds { get; set; }
            public Dictionary<string,string>? RadarFeeds { get; set; }
            public int DelayBetweenRequestsMs { get; set; } = 200;
            public bool EnableCityRadar { get; set; } = false;
            public bool EnableProvinceRadar { get; set; } = true;
            public bool UseGeoMetWms { get; set; } = true;
            public string? UserAgent { get; set; }
            public string? ProvinceAnimationUrl { get; set; }
            public string? ProvinceRadarUrl { get; set; }
            public int ProvinceFrames { get; set; } = 8;
            public int ProvinceImageWidth { get; set; } = 1920;
            public int ProvinceImageHeight { get; set; } = 1080;
            public double ProvincePaddingDegrees { get; set; } = 0.5;
            public string[]? ProvinceEnsureCities { get; set; }
            public int ProvinceFrameStepMinutes { get; set; } = 6;
        }
    }
}
