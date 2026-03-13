using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grib2.Decoder;
using Grib2.Models;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Service that downloads GRIB2 forecast data from ECCC Datamart (dd.weather.gc.ca),
    /// parses it with the Grib2 library, and exposes decoded grid data for overlay rendering.
    /// Supports GDPS (25 km global, 240 h) and HRDPS (2.5 km regional, 48 h).
    /// </summary>
    public class Grib2DataService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _cacheDir;
        private readonly TimeSpan _cacheExpiry;
        private readonly SemaphoreSlim _downloadSemaphore = new(4);
        private readonly ConcurrentDictionary<string, CachedGrib2Message> _memoryCache = new();

        // ECCC Datamart base URLs (date-prefixed structure: /{YYYYMMDD}/WXO-DD/...)
        private const string DD_BASE = "https://dd.weather.gc.ca";
        private const string GDPS_PATH = "WXO-DD/model_gem_global/15km/grib2/lat_lon";
        private const string HRDPS_PATH = "WXO-DD/model_hrdps/continental/2.5km";

        // Parameter → GRIB2 filename fragments per model
        private static readonly Dictionary<Grib2FieldType, GribFileSpec> GdpsSpecs = new()
        {
            [Grib2FieldType.Temperature]  = new("TMP",  "TMP_TGL_2",      null,             0, 0, 0),
            [Grib2FieldType.Wind]         = new("WIND", "UGRD_TGL_10",   "VGRD_TGL_10",   0, 2, 2),
            [Grib2FieldType.Precipitation]= new("APCP", "APCP_SFC_0",    null,             0, 1, 52),
            [Grib2FieldType.CloudCover]   = new("TCDC", "TCDC_SFC_0",    null,             0, 6, 1),
            [Grib2FieldType.Pressure]     = new("PRMSL","PRMSL_MSL_0",   null,             0, 3, 1),
            [Grib2FieldType.CAPE]         = new("CAPE", "CAPE_SFC_0",    null,             0, 7, 6),
        };

        private static readonly Dictionary<Grib2FieldType, GribFileSpec> HrdpsSpecs = new()
        {
            [Grib2FieldType.Temperature]  = new("TMP",  "TMP_AGL-2m",     null,              0, 0, 0),
            [Grib2FieldType.Wind]         = new("WIND", "UGRD_AGL-10m",   "VGRD_AGL-10m",   0, 2, 2),
            [Grib2FieldType.Precipitation]= new("APCP", "APCP_Sfc",       null,              0, 1, 52),
            [Grib2FieldType.CloudCover]   = new("TCDC", "TCDC_Sfc",       null,              0, 6, 1),
            [Grib2FieldType.Pressure]     = new("PRMSL","PRMSL_MSL",      null,              0, 3, 1),
            [Grib2FieldType.CAPE]         = new("CAPE", "CAPE_Sfc",       null,              0, 7, 6),
        };

        /// <summary>Latest GRIB2 run hour available (0, 6, 12, 18 for GDPS; 0, 6, 12, 18 for HRDPS)</summary>
        public string LatestRunHour { get; private set; } = "00";

        /// <summary>Date of the latest model run</summary>
        public DateTime LatestRunDate { get; private set; } = DateTime.UtcNow.Date;

        /// <summary>Maximum forecast hours for the current model</summary>
        public int MaxForecastHours => _currentModel == Grib2ModelSource.HRDPS ? 48 : 240;

        private Grib2ModelSource _currentModel = Grib2ModelSource.GDPS;

        public Grib2DataService(string? cacheDir = null, TimeSpan? cacheExpiry = null)
        {
            _httpClient = new HttpClient();
            _cacheDir = cacheDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSG", "grib2_cache");
            _cacheExpiry = cacheExpiry ?? TimeSpan.FromHours(6);

            Directory.CreateDirectory(_cacheDir);

            // Set longer timeout for large GRIB2 files
            if (_httpClient.Timeout < TimeSpan.FromMinutes(2))
            {
                try { _httpClient.Timeout = TimeSpan.FromMinutes(2); } catch { }
            }
        }

        /// <summary>
        /// Sets the active model source and auto-detects the latest available run.
        /// </summary>
        public async Task SetModelAsync(Grib2ModelSource model)
        {
            _currentModel = model;
            await DetectLatestRunAsync();
        }

        /// <summary>
        /// Gets the current model source.
        /// </summary>
        public Grib2ModelSource CurrentModel => _currentModel;

        /// <summary>
        /// Detects the latest available model run by probing ECCC Datamart.
        /// GDPS runs at 00/12 UTC; HRDPS runs at 00/06/12/18 UTC.
        /// </summary>
        public async Task DetectLatestRunAsync()
        {
            var now = DateTime.UtcNow;
            string[] runHours = _currentModel == Grib2ModelSource.HRDPS
                ? new[] { "18", "12", "06", "00" }
                : new[] { "12", "00" };

            // Try today first, then yesterday
            for (int dayOffset = 0; dayOffset <= 1; dayOffset++)
            {
                var date = now.Date.AddDays(-dayOffset);
                foreach (var run in runHours)
                {
                    int runH = int.Parse(run);
                    // Skip runs that haven't had time to be fully published (~4.5h delay)
                    if (dayOffset == 0 && now.Hour < runH + 5) continue;

                    string probeUrl = BuildUrl(Grib2FieldType.Temperature, int.Parse(run), 0, date);
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Head, probeUrl);
                        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        if (resp.IsSuccessStatusCode)
                        {
                            LatestRunHour = run;
                            LatestRunDate = date;
                            Logger.Log($"[Grib2DataService] Detected latest run: {date:yyyy-MM-dd} {run}Z ({_currentModel})", Logger.LogLevel.Info);
                            return;
                        }
                    }
                    catch { /* probe failed, try next */ }
                }
            }

            // Fallback: use earliest today
            LatestRunDate = now.Date;
            LatestRunHour = runHours[^1];
            Logger.Log($"[Grib2DataService] Using fallback run: {LatestRunDate:yyyy-MM-dd} {LatestRunHour}Z", Logger.LogLevel.Warning);
        }

        /// <summary>
        /// Downloads and parses a GRIB2 file for a specific field type and forecast hour.
        /// Returns the decoded messages containing grid data.
        /// </summary>
        public async Task<Grib2Message?> FetchFieldAsync(Grib2FieldType fieldType, int forecastHour, CancellationToken ct = default)
        {
            // Clamp and snap forecast hour to model's valid range/steps
            forecastHour = SnapForecastHour(forecastHour, fieldType);

            string cacheKey = $"{_currentModel}_{LatestRunDate:yyyyMMdd}_{LatestRunHour}_{fieldType}_{forecastHour:D3}";

            // Check memory cache first
            if (_memoryCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired(_cacheExpiry))
            {
                return cached.Message;
            }

            // Check disk cache
            string cachePath = Path.Combine(_cacheDir, $"{cacheKey}.grib2");
            byte[]? gribData = null;

            if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < _cacheExpiry)
            {
                gribData = await File.ReadAllBytesAsync(cachePath, ct);
                Logger.Log($"[Grib2DataService] Disk cache hit: {cacheKey}", Logger.LogLevel.Debug);
            }
            else
            {
                // Download from Datamart
                string url = BuildUrl(fieldType, int.Parse(LatestRunHour), forecastHour, LatestRunDate);
                await _downloadSemaphore.WaitAsync(ct);
                try
                {
                    Logger.Log($"[Grib2DataService] Downloading: {url}", Logger.LogLevel.Info);
                    gribData = await _httpClient.GetByteArrayAsync(url, ct);

                    // Save to disk cache
                    await File.WriteAllBytesAsync(cachePath, gribData, ct);
                    Logger.Log($"[Grib2DataService] Cached: {cachePath} ({gribData.Length:N0} bytes)", Logger.LogLevel.Debug);
                }
                catch (HttpRequestException ex)
                {
                    Logger.Log($"[Grib2DataService] Download failed: {ex.Message}", Logger.LogLevel.Warning);
                    return null;
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }

            if (gribData == null || gribData.Length == 0)
                return null;

            // Parse GRIB2
            try
            {
                var reader = new Grib2Reader(gribData);
                var messages = reader.ReadAllMessages();

                if (messages.Count == 0)
                {
                    Logger.Log($"[Grib2DataService] No messages in GRIB2 file: {cacheKey}", Logger.LogLevel.Warning);
                    return null;
                }

                // Find the matching message
                var specs = _currentModel == Grib2ModelSource.HRDPS ? HrdpsSpecs : GdpsSpecs;
                if (!specs.TryGetValue(fieldType, out var spec))
                    return null;

                var msg = messages.FirstOrDefault(m =>
                    m.Field != null &&
                    m.Field.Discipline == spec.Discipline &&
                    m.Field.ParameterCategory == spec.Category &&
                    m.Field.ParameterNumber == spec.Number);

                if (msg == null && messages.Count > 0)
                {
                    // Fallback: take the first message if exact match not found
                    msg = messages[0];
                }

                if (msg != null)
                {
                    _memoryCache[cacheKey] = new CachedGrib2Message(msg, DateTime.UtcNow);
                }

                return msg;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Grib2DataService] GRIB2 parse error: {ex.Message}", Logger.LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Fetches wind U and V components for wind speed/direction calculation.
        /// </summary>
        public async Task<(Grib2Message? U, Grib2Message? V)> FetchWindComponentsAsync(int forecastHour, CancellationToken ct = default)
        {
            // Clamp and snap forecast hour to model's valid range/steps
            forecastHour = SnapForecastHour(forecastHour);

            var specs = _currentModel == Grib2ModelSource.HRDPS ? HrdpsSpecs : GdpsSpecs;
            if (!specs.TryGetValue(Grib2FieldType.Wind, out var spec))
                return (null, null);

            string cacheKeyU = $"{_currentModel}_{LatestRunDate:yyyyMMdd}_{LatestRunHour}_UGRD_{forecastHour:D3}";
            string cacheKeyV = $"{_currentModel}_{LatestRunDate:yyyyMMdd}_{LatestRunHour}_VGRD_{forecastHour:D3}";

            // Try memory cache
            Grib2Message? uMsg = null, vMsg = null;
            if (_memoryCache.TryGetValue(cacheKeyU, out var cachedU) && !cachedU.IsExpired(_cacheExpiry))
                uMsg = cachedU.Message;
            if (_memoryCache.TryGetValue(cacheKeyV, out var cachedV) && !cachedV.IsExpired(_cacheExpiry))
                vMsg = cachedV.Message;

            if (uMsg != null && vMsg != null)
                return (uMsg, vMsg);

            // Download U component
            string urlU = BuildWindComponentUrl("UGRD", int.Parse(LatestRunHour), forecastHour, LatestRunDate);
            string urlV = BuildWindComponentUrl("VGRD", int.Parse(LatestRunHour), forecastHour, LatestRunDate);

            var tasks = new[]
            {
                DownloadAndParseAsync(urlU, $"{cacheKeyU}.grib2", ct),
                DownloadAndParseAsync(urlV, $"{cacheKeyV}.grib2", ct)
            };

            var results = await Task.WhenAll(tasks);

            if (results[0] != null)
            {
                uMsg = results[0];
                _memoryCache[cacheKeyU] = new CachedGrib2Message(uMsg!, DateTime.UtcNow);
            }
            if (results[1] != null)
            {
                vMsg = results[1];
                _memoryCache[cacheKeyV] = new CachedGrib2Message(vMsg!, DateTime.UtcNow);
            }

            return (uMsg, vMsg);
        }

        /// <summary>
        /// Gets the list of available forecast hours for the current model run.
        /// </summary>
        public int[] GetAvailableForecastHours()
        {
            if (_currentModel == Grib2ModelSource.HRDPS)
            {
                // HRDPS: hourly from 0 to 48
                return Enumerable.Range(0, 49).ToArray();
            }
            else
            {
                // GDPS: 3-hourly from 0 to 240
                var hours = new List<int>();
                for (int h = 0; h <= 240; h += 3)
                    hours.Add(h);
                return hours.ToArray();
            }
        }

        /// <summary>
        /// Cleans expired files from the disk cache.
        /// </summary>
        public void CleanDiskCache()
        {
            try
            {
                var expiry = DateTime.UtcNow - _cacheExpiry;
                foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.grib2"))
                {
                    if (File.GetLastWriteTimeUtc(file) < expiry)
                    {
                        File.Delete(file);
                        Logger.Log($"[Grib2DataService] Cleaned expired cache: {Path.GetFileName(file)}", Logger.LogLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Grib2DataService] Cache cleanup error: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        /// <summary>
        /// Clears all cached data (memory + disk).
        /// </summary>
        public void ClearCache()
        {
            _memoryCache.Clear();
            try
            {
                foreach (var file in Directory.EnumerateFiles(_cacheDir, "*.grib2"))
                    File.Delete(file);
            }
            catch { }
        }

        /// <summary>Forecast hour step size for the current model (1 for HRDPS, 3 for GDPS).</summary>
        public int ForecastHourStep => _currentModel == Grib2ModelSource.HRDPS ? 1 : 3;

        #region Private helpers

        /// <summary>Clamps and snaps a forecast hour to the nearest valid step for the current model.</summary>
        private int SnapForecastHour(int forecastHour, Grib2FieldType fieldType = Grib2FieldType.Temperature)
        {
            forecastHour = Math.Clamp(forecastHour, 0, MaxForecastHours);
            int step = ForecastHourStep;
            if (step > 1)
                forecastHour = (int)(Math.Round((double)forecastHour / step) * step);

            // APCP (Accumulated Precipitation) doesn't exist at hour 0 — ECCC doesn't publish it
            if (fieldType == Grib2FieldType.Precipitation && forecastHour < step)
                forecastHour = step;

            return Math.Clamp(forecastHour, 0, MaxForecastHours);
        }

        private string BuildUrl(Grib2FieldType fieldType, int runHour, int forecastHour, DateTime date)
        {
            var specs = _currentModel == Grib2ModelSource.HRDPS ? HrdpsSpecs : GdpsSpecs;
            if (!specs.TryGetValue(fieldType, out var spec))
                throw new ArgumentException($"Unsupported field type: {fieldType}");

            string dateStr = date.ToString("yyyyMMdd");
            string runStr = runHour.ToString("D2");
            string fhStr = forecastHour.ToString("D3");

            if (_currentModel == Grib2ModelSource.HRDPS)
            {
                // e.g. https://dd.weather.gc.ca/20260305/WXO-DD/model_hrdps/continental/2.5km/12/003/20260305T12Z_MSC_HRDPS_TMP_AGL-2m_RLatLon0.0225_PT003H.grib2
                return $"{DD_BASE}/{dateStr}/{HRDPS_PATH}/{runStr}/{fhStr}/{dateStr}T{runStr}Z_MSC_HRDPS_{spec.Primary}_RLatLon0.0225_PT{fhStr}H.grib2";
            }
            else
            {
                // e.g. https://dd.weather.gc.ca/20260305/WXO-DD/model_gem_global/15km/grib2/lat_lon/12/003/CMC_glb_TMP_TGL_2_latlon.15x.15_2026030512_P003.grib2
                return $"{DD_BASE}/{dateStr}/{GDPS_PATH}/{runStr}/{fhStr}/CMC_glb_{spec.Primary}_latlon.15x.15_{dateStr}{runStr}_P{fhStr}.grib2";
            }
        }

        private string BuildWindComponentUrl(string component, int runHour, int forecastHour, DateTime date)
        {
            string dateStr = date.ToString("yyyyMMdd");
            string runStr = runHour.ToString("D2");
            string fhStr = forecastHour.ToString("D3");

            if (_currentModel == Grib2ModelSource.HRDPS)
            {
                return $"{DD_BASE}/{dateStr}/{HRDPS_PATH}/{runStr}/{fhStr}/{dateStr}T{runStr}Z_MSC_HRDPS_{component}_AGL-10m_RLatLon0.0225_PT{fhStr}H.grib2";
            }
            else
            {
                return $"{DD_BASE}/{dateStr}/{GDPS_PATH}/{runStr}/{fhStr}/CMC_glb_{component}_TGL_10_latlon.15x.15_{dateStr}{runStr}_P{fhStr}.grib2";
            }
        }

        private async Task<Grib2Message?> DownloadAndParseAsync(string url, string cacheFileName, CancellationToken ct)
        {
            string cachePath = Path.Combine(_cacheDir, cacheFileName);
            byte[]? data = null;

            if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < _cacheExpiry)
            {
                data = await File.ReadAllBytesAsync(cachePath, ct);
            }
            else
            {
                await _downloadSemaphore.WaitAsync(ct);
                try
                {
                    data = await _httpClient.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(cachePath, data, ct);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Grib2DataService] Download failed: {url} — {ex.Message}", Logger.LogLevel.Warning);
                    return null;
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }

            if (data == null) return null;

            try
            {
                var reader = new Grib2Reader(data);
                var messages = reader.ReadAllMessages();
                return messages.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _downloadSemaphore.Dispose();
            _memoryCache.Clear();
        }

        #endregion

        #region Inner types

        private record GribFileSpec(string Abbrev, string Primary, string? Secondary, int Discipline, int Category, int Number);

        private class CachedGrib2Message
        {
            public Grib2Message Message { get; }
            public DateTime CachedAt { get; }

            public CachedGrib2Message(Grib2Message message, DateTime cachedAt)
            {
                Message = message;
                CachedAt = cachedAt;
            }

            public bool IsExpired(TimeSpan expiry) => DateTime.UtcNow - CachedAt > expiry;
        }

        #endregion
    }
}
