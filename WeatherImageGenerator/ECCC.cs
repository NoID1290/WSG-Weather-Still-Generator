using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using System.Diagnostics;
using WeatherImageGenerator;

namespace QuebecWeatherAlertMonitor
{
    // A simple model to hold alert data
    public class AlertEntry
    {
        public string City { get; set; } = "";
        public string Type { get; set; } = ""; // WARNING, WATCH, STATEMENT
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string SeverityColor { get; set; } = "Gray"; // Red, Yellow, Gray
    }

    public static class ECCC
    {
        // Call this method from Program.cs
        public static async Task<List<AlertEntry>> FetchAllAlerts(HttpClient client)
        {
            var config = ConfigManager.LoadConfig();
            var ecccConfig = config.ECCC ?? new ECCCSettings();
            var cityFeeds = ecccConfig.CityFeeds ?? new Dictionary<string, string>();

            List<AlertEntry> allAlerts = new List<AlertEntry>();

            // Ensure we have a User-Agent or ECCC will block the request
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", ecccConfig.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }

            foreach (var city in cityFeeds)
            {
                try
                {
                    var cityAlerts = await CheckFeed(client, city.Key, city.Value);
                    allAlerts.AddRange(cityAlerts);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ECCC] Error fetching {city.Key}: {ex.Message}", ConsoleColor.Red);
                }
                
                // Be polite to the server
                await Task.Delay(ecccConfig.DelayBetweenRequestsMs); 
            }

            return allAlerts;
        }

        private static async Task<List<AlertEntry>> CheckFeed(HttpClient client, string cityName, string url)
        {
            List<AlertEntry> foundAlerts = new List<AlertEntry>();

            string xmlContent = await client.GetStringAsync(url);
            XDocument doc = XDocument.Parse(xmlContent);
            XNamespace atom = "http://www.w3.org/2005/Atom";

            var entries = doc.Root.Elements(atom + "entry");

            foreach (var entry in entries)
            {
                string title = entry.Element(atom + "title")?.Value ?? "";
                string summary = entry.Element(atom + "summary")?.Value ?? "";
                string category = entry.Element(atom + "category")?.Attribute("term")?.Value ?? "";

                // Filter for Warnings/Watches/Statements
                if (category.Equals("Veilles et avertissements", StringComparison.OrdinalIgnoreCase) || 
                    category.Equals("Warnings and Watches", StringComparison.OrdinalIgnoreCase))
                {
                    // Skip "No watches or warnings"
                    if (title.Contains("Aucune veille ou alerte", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("No watches or warnings", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AlertEntry newAlert = new AlertEntry
                    {
                        City = cityName,
                        Title = title,
                        Summary = CleanSummary(summary)
                    };

                    // Determine Type and Color Logic
                    if (title.Contains("avertissement", StringComparison.OrdinalIgnoreCase) || 
                        title.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        newAlert.Type = "WARNING";
                        newAlert.SeverityColor = "Red";
                    }
                    else if (title.Contains("veille", StringComparison.OrdinalIgnoreCase) || 
                             title.Contains("watch", StringComparison.OrdinalIgnoreCase))
                    {
                        newAlert.Type = "WATCH";
                        newAlert.SeverityColor = "Yellow";
                    }
                    else
                    {
                        newAlert.Type = "STATEMENT";
                        newAlert.SeverityColor = "Gray";
                    }

                    foundAlerts.Add(newAlert);
                }
            }

            return foundAlerts;
        }

        private static string CleanSummary(string summary)
        {
            return summary.Replace("<br/>", "\n")
                          .Replace("<b>", "")
                          .Replace("</b>", "")
                          .Trim();
        }

        /// <summary>
        /// Downloads radar images configured in appsettings.json and saves them to the output directory
        /// </summary>
        public static async Task FetchRadarImages(HttpClient client, string outputDir)
        {
            var config = ConfigManager.LoadConfig();
            var ecccConfig = config.ECCC ?? new ECCCSettings();
            var radarFeeds = ecccConfig.RadarFeeds ?? new Dictionary<string, string>();

            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            // Ensure a User-Agent header is present
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", ecccConfig.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }

            // 1) City-level radar: if a direct URL is provided, use it; otherwise if UseGeoMetWms is true, use GeoMet WMS GetMap
            foreach (var city in ecccConfig.CityFeeds ?? new Dictionary<string,string>())
            {
                string cityName = city.Key;
                string directUrl = (radarFeeds.TryGetValue(cityName, out var rv) ? rv : null) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(directUrl))
                {
                    // Direct download
                    try
                    {
                        var resp = await client.GetAsync(directUrl);
                        if (!resp.IsSuccessStatusCode)
                        {
                            Logger.Log($"[ECCC] Radar fetch failed for {cityName}: HTTP {(int)resp.StatusCode}");
                        }
                        else
                        {
                            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                            string ext = GetExtensionFromContentTypeOrUrl(contentType, directUrl);
                            string safeName = SanitizeFileName(cityName);
                            string outPath = Path.Combine(outputDir, $"radar_{safeName}{ext}");
                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            await File.WriteAllBytesAsync(outPath, bytes);
                            Logger.Log($"✓ Downloaded radar for {cityName} -> {outPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ECCC] Radar {cityName} fetch failed: {ex.Message}", ConsoleColor.Yellow);
                    }

                    await Task.Delay(ecccConfig.DelayBetweenRequestsMs);
                    continue;
                }

                if (ecccConfig.UseGeoMetWms)
                {
                    try
                    {
                        // Build a small bbox around the city coordinates
                        if (CityCoordinates.TryGetValue(cityName, out var coord))
                        {
                            var bbox = BuildBoundingBox(coord.lat, coord.lon, 0.6);
                            string layer = ecccConfig.CityRadarLayer ?? "RADAR_1KM_RRAI";
                            string url = BuildGeoMetGetMapUrl(layer, bbox, 400, 200, "image/png", null);

                            var resp = await client.GetAsync(url);
                            if (resp.IsSuccessStatusCode)
                            {
                                string ext = ".png";
                                string safeName = SanitizeFileName(cityName);
                                string outPath = Path.Combine(outputDir, $"radar_{safeName}{ext}");
                                var bytes = await resp.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(outPath, bytes);
                                Logger.Log($"✓ Fetched GeoMet WMS radar for {cityName} -> {outPath}");
                            }
                            else
                            {
                                Logger.Log($"[ECCC] GeoMet WMS radar fetch failed for {cityName}: HTTP {(int)resp.StatusCode}");
                            }
                        }
                        else
                        {
                            Logger.Log($"[ECCC] No known coordinates for {cityName}; skipping GeoMet WMS thumbnail.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[ECCC] GeoMet WMS fetch failed for {cityName}: {ex.Message}", ConsoleColor.Yellow);
                    }

                    await Task.Delay(ecccConfig.DelayBetweenRequestsMs);
                }
            }

            // 2) Province-level animated radar using GeoMet WMS frames
            // If explicit ProvinceRadarUrl provided, use it as a fallback; otherwise attempt to build animation from GeoMet WMS
            bool provinceCreated = false;
            if (!string.IsNullOrWhiteSpace(ecccConfig.ProvinceRadarUrl))
            {
                try
                {
                    var resp = await client.GetAsync(ecccConfig.ProvinceRadarUrl);
                    if (resp.IsSuccessStatusCode)
                    {
                        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                        string ext = GetExtensionFromContentTypeOrUrl(contentType, ecccConfig.ProvinceRadarUrl ?? "");
                        string outPath = Path.Combine(outputDir, $"00_ProvinceRadar{ext}");
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(outPath, bytes);
                        Logger.Log($"✓ Downloaded province radar -> {outPath}");
                        provinceCreated = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ECCC] Province radar fetch failed: {ex.Message}", ConsoleColor.Yellow);
                }

                await Task.Delay(ecccConfig.DelayBetweenRequestsMs);
            }

            if (!provinceCreated && ecccConfig.UseGeoMetWms)
            {
                try
                {
                    await CreateProvinceRadarAnimation(client, outputDir, ecccConfig);
                    provinceCreated = true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ECCC] Failed to create province animation: {ex.Message}", ConsoleColor.Yellow);
                }
            }
        }

        // Small built-in list of city coordinates used for bbox construction
        private static readonly Dictionary<string, (double lat, double lon)> CityCoordinates = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase)
        {
            { "Montreal", (45.50884, -73.58781) },
            { "Quebec City", (46.813878, -71.207981) },
            { "Amos", (48.574, -78.116) }
        };

        private static (double minY, double minX, double maxY, double maxX) BuildBoundingBox(double lat, double lon, double deltaDegrees)
        {
            double minY = lat - deltaDegrees;
            double maxY = lat + deltaDegrees;
            double minX = lon - deltaDegrees;
            double maxX = lon + deltaDegrees;
            return (minY, minX, maxY, maxX);
        }

        private static string BuildGeoMetGetMapUrl(string layer, (double minY, double minX, double maxY, double maxX) bbox, int width, int height, string format = "image/png", string? time = null)
        {
            // WMS 1.3.0 requires axis order lat,lon for EPSG:4326
            string bboxStr = $"{bbox.minY},{bbox.minX},{bbox.maxY},{bbox.maxX}";
            var sb = new System.Text.StringBuilder();
            sb.Append("https://geo.weather.gc.ca/geomet?");
            sb.Append("SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap");
            sb.Append($"&LAYERS={Uri.EscapeDataString(layer)}");
            sb.Append("&CRS=EPSG:4326");
            sb.Append($"&BBOX={bboxStr}");
            sb.Append($"&WIDTH={width}&HEIGHT={height}");
            sb.Append($"&FORMAT={Uri.EscapeDataString(format)}");
            if (!string.IsNullOrWhiteSpace(time)) sb.Append($"&TIME={Uri.EscapeDataString(time)}");
            return sb.ToString();
        }

        private static async Task CreateProvinceRadarAnimation(HttpClient client, string outputDir, ECCCSettings ecccConfig)
        {
            string layer = ecccConfig.ProvinceRadarLayer ?? "RADAR_1KM_RRAI";
            int frames = Math.Max(2, ecccConfig.ProvinceFrames);

            // 1) Ask GetCapabilities for time dimension for the layer
            string capsUrl = $"https://geo.weather.gc.ca/geomet?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities&LAYERS={Uri.EscapeDataString(layer)}";
            string xml = await client.GetStringAsync(capsUrl);
            XDocument doc = XDocument.Parse(xml);
            XNamespace ns = doc.Root.GetDefaultNamespace();

            var dim = doc.Descendants(ns + "Dimension")
                         .Where(d => (string)d.Attribute("name") == "time")
                         .FirstOrDefault();

            List<string> times = new List<string>();
            if (dim != null)
            {
                // Format: start/end/period (ISO8601)
                var content = dim.Value.Trim();
                // If range like start/end/PT6M - parse end and step
                if (content.Contains('/') && content.Contains("PT"))
                {
                    var parts = content.Split('/');
                    if (parts.Length >= 3)
                    {
                        if (DateTime.TryParse(parts[0], out DateTime start) && DateTime.TryParse(parts[1], out DateTime end))
                        {
                            var period = parts[2];
                            var step = ParseIso8601PeriodToTimeSpan(period);
                            if (step.TotalSeconds > 0)
                            {
                                // Collect last N frames spaced by step
                                var t = end.ToUniversalTime();
                                for (int i = 0; i < frames; i++)
                                {
                                    times.Add(t.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                                    t = t.Subtract(step);
                                    if (t < start) break;
                                }
                                times.Reverse(); // chronological order
                            }
                        }
                    }
                }
                else
                {
                    // If dimension lists discrete times separated by comma
                    var parts = content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var partList = parts.ToList();
                    if (partList.Count > frames) partList = partList.Skip(Math.Max(0, partList.Count - frames)).ToList();
                    times = partList;
                }
            }

            if (times.Count == 0)
            {
                // Fallback to requesting the default time only
                Logger.Log("[ECCC] No times found in WMS capabilities; requesting latest single frame for province");
                times.Add(null);
            }

            // Province bounding box for Quebec (rough) lat,long: minY,minX,maxY,maxX
            var provinceBbox = (minY: 45.0, minX: -80.5, maxY: 53.5, maxX: -57.0);

            // Create temp frames
            string tempDir = Path.Combine(outputDir, "province_frames");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            int idx = 0;
            var frameFiles = new List<string>();
            foreach (var t in times)
            {
                string url = BuildGeoMetGetMapUrl(layer, provinceBbox, 1920, 1080, "image/png", t);
                try
                {
                    var resp = await client.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        string framePath = Path.Combine(tempDir, $"frame_{idx:03}.png");
                        var bytes = await resp.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(framePath, bytes);
                        frameFiles.Add(framePath);
                        Logger.Log($"✓ Province frame {idx} downloaded -> {framePath}");
                    }
                    else
                    {
                        Logger.Log($"[ECCC] Province frame fetch failed: HTTP {(int)resp.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ECCC] Province frame fetch error: {ex.Message}", ConsoleColor.Yellow);
                }

                idx++;
                await Task.Delay(ecccConfig.DelayBetweenRequestsMs);
            }

            if (frameFiles.Count > 0)
            {
                // Try to stitch into an animated GIF using ffmpeg (if available)
                string gifOut = Path.Combine(outputDir, "00_ProvinceRadar.gif");
                string mp4Out = Path.Combine(outputDir, "00_ProvinceRadar.mp4");

                try
                {
                    // Build ffmpeg command to create GIF
                    // Use fps=5 and loop
                    string args = $"-y -framerate 5 -i \"{Path.Combine(tempDir, "frame_%03d.png")}\" -vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2\" -loop 0 \"{gifOut}\"";
                    RunExternalProcess("ffmpeg", args, outputDir);

                    // Also build MP4 copy (use libx264)
                    string args2 = $"-y -framerate 5 -i \"{Path.Combine(tempDir, "frame_%03d.png")}\" -c:v libx264 -pix_fmt yuv420p -vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2\" \"{mp4Out}\"";
                    RunExternalProcess("ffmpeg", args2, outputDir);

                    Logger.Log($"✓ Province animation created: {gifOut} and {mp4Out}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ECCC] Failed to create animated GIF/MP4: {ex.Message}", ConsoleColor.Yellow);
                }
            }
        }

        private static TimeSpan ParseIso8601PeriodToTimeSpan(string period)
        {
            // Very limited parser for durations like PT6M, PT5M, PT1H
            if (string.IsNullOrWhiteSpace(period)) return TimeSpan.Zero;
            if (!period.StartsWith("P", StringComparison.OrdinalIgnoreCase)) return TimeSpan.Zero;

            // Only handling time portion e.g. PT6M, PT1H
            if (period.StartsWith("PT", StringComparison.OrdinalIgnoreCase))
            {
                var p = period.Substring(2).ToUpperInvariant();
                if (p.EndsWith("S")) { if (int.TryParse(p.TrimEnd('S'), out int s)) return TimeSpan.FromSeconds(s); }
                if (p.EndsWith("M")) { if (int.TryParse(p.TrimEnd('M'), out int m)) return TimeSpan.FromMinutes(m); }
                if (p.EndsWith("H")) { if (int.TryParse(p.TrimEnd('H'), out int h)) return TimeSpan.FromHours(h); }
            }
            return TimeSpan.Zero;
        }

        private static void RunExternalProcess(string exe, string args, string workingDir)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir
            };

            using (var p = Process.Start(psi))
            {
                p?.WaitForExit();
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return clean.Replace(' ', '_');
        }

        private static string GetExtensionFromContentTypeOrUrl(string contentType, string url)
        {
            contentType = contentType?.ToLowerInvariant() ?? string.Empty;
            if (contentType.Contains("gif")) return ".gif";
            if (contentType.Contains("png")) return ".png";
            if (contentType.Contains("jpeg") || contentType.Contains("jpg")) return ".jpg";

            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (!string.IsNullOrWhiteSpace(ext)) return ext;
            }
            catch { }

            return ".png";
        }
    }
}