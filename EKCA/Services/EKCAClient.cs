#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EKCA.Api;
using EKCA.Models;
using EKCA.Services;

namespace EKCA.Services
{
    /// <summary>
    /// Internal HTTP client and orchestration layer for Earthquakes Canada data.
    /// Wraps <see cref="UrlBuilder"/> calls, delegates parsing to the static parsers,
    /// and manages rate limiting.
    /// </summary>
    internal class EKCAClient
    {
        private readonly HttpClient _http;
        private readonly EKCASettings _settings;
        private DateTime _lastRequestTime = DateTime.MinValue;

        public EKCAClient(HttpClient http, EKCASettings? settings = null)
        {
            _http = http;
            _settings = settings ?? new EKCASettings();
        }

        // ---------------------------------------------------------------------------
        // Station list
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fetches and parses the current list of CNSN seismograph stations.
        /// Only active stations (open-ended EndTime) are included for display.
        /// </summary>
        public async Task<List<SeismicStation>> FetchStationsAsync(CancellationToken ct = default)
        {
            var url = UrlBuilder.BuildActiveStationsUrl(_settings.DefaultNetwork);
            var rawText = await GetStringAsync(url, ct).ConfigureAwait(false);
            if (rawText == null) return new List<SeismicStation>();

            var all = StationParser.ParseStationText(rawText);
            // Return only active stations for map display
            return all.Where(s => s.IsActive).ToList();
        }

        // ---------------------------------------------------------------------------
        // Earthquake events
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fetches and parses recent significant earthquakes from the Atom feed.
        /// </summary>
        public async Task<List<EarthquakeEvent>> FetchRecentEventsAsync(CancellationToken ct = default)
        {
            var url = UrlBuilder.BuildAtomFeedUrl();
            var xml = await GetStringAsync(url, ct).ConfigureAwait(false);
            if (xml == null) return new List<EarthquakeEvent>();
            return AtomFeedParser.ParseAtomFeed(xml);
        }

        // ---------------------------------------------------------------------------
        // Waveform / MiniSEED
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fetches MiniSEED waveform data for the specified station and time window.
        /// Tries the configured primary channel first, then falls back through
        /// <see cref="EKCASettings.FallbackChannels"/> until data is obtained.
        /// </summary>
        public async Task<SeismogramData?> FetchWaveformAsync(
            string station,
            DateTime startUtc,
            DateTime endUtc,
            string? channel = null,
            CancellationToken ct = default)
        {
            var channelsToTry = BuildChannelList(channel);

            foreach (var ch in channelsToTry)
            {
                var result = await TryFetchWaveformAsync(station, startUtc, endUtc, ch, ct)
                    .ConfigureAwait(false);
                if (result != null && result.HasData)
                    return result;
            }
            return null;
        }

        private async Task<SeismogramData?> TryFetchWaveformAsync(
            string station,
            DateTime startUtc,
            DateTime endUtc,
            string channel,
            CancellationToken ct)
        {
            var url = UrlBuilder.BuildWaveformUrl(
                _settings.DefaultNetwork, station, channel, startUtc, endUtc);

            var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
            if (bytes == null || bytes.Length < 48) return null;

            try
            {
                var records = MiniSeedParser.ParseRecords(bytes);
                var data = MiniSeedParser.ToSeismogramData(records, station, channel);
                return data.HasData ? data : null;
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------------------
        // HTTP helpers
        // ---------------------------------------------------------------------------

        private async Task<string?> GetStringAsync(string url, CancellationToken ct)
        {
            await RateLimit(ct).ConfigureAwait(false);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", _settings.UserAgent);
                var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    System.Console.WriteLine($"[EKCA] HTTP {(int)response.StatusCode} for {url}");
                    return null;
                }
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task<byte[]?> GetBytesAsync(string url, CancellationToken ct)
        {
            await RateLimit(ct).ConfigureAwait(false);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", _settings.UserAgent);
                var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private async Task RateLimit(CancellationToken ct)
        {
            var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
            int delay = _settings.DelayBetweenRequestsMs - (int)elapsed;
            if (delay > 0)
                await Task.Delay(delay, ct).ConfigureAwait(false);
            _lastRequestTime = DateTime.UtcNow;
        }

        private string[] BuildChannelList(string? preferred)
        {
            var list = new List<string>();
            list.Add(preferred ?? _settings.DefaultChannel);
            foreach (var fb in _settings.FallbackChannels)
                if (!list.Contains(fb)) list.Add(fb);
            return list.ToArray();
        }
    }
}
