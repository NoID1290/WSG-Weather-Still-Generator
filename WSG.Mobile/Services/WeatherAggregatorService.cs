using System.Globalization;
using EAS.AlertReady;
using EAS.NWS;
using Microsoft.Maui.Graphics;
using OpenMeteo;
using WeatherImageGenerator.Models;

namespace WSG.Mobile.Services;

public sealed class WeatherAggregatorService
{
    private readonly HttpClient _httpClient;
    private readonly OpenMeteoClient _weatherClient = new();

    public WeatherAggregatorService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<WeatherSnapshot> GetSnapshotAsync(
        string location,
        string alertRegion,
        bool highRiskOnly,
        CancellationToken cancellationToken = default)
    {
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? "Montreal, QC" : location.Trim();
        var forecast = await _weatherClient.QueryAsync(normalizedLocation);

        cancellationToken.ThrowIfCancellationRequested();

        if (forecast?.Current is null)
        {
            return WeatherSnapshot.Error(normalizedLocation, "Weather data could not be loaded for that location.");
        }

        var forecastDays = BuildForecastDays(forecast);
        var alerts = await FetchAlertsAsync(normalizedLocation, alertRegion, highRiskOnly, forecast, cancellationToken);
        var current = forecast.Current;
        var currentUnits = forecast.CurrentUnits;
        var condition = _weatherClient.WeathercodeToString(current.Weathercode ?? 0);

        return new WeatherSnapshot
        {
            LocationName = normalizedLocation,
            CurrentTemperatureDisplay = FormatValue(current.Temperature_2m, currentUnits?.Temperature_2m ?? "°C"),
            ConditionSummary = condition,
            FeelsLikeDisplay = FormatValue(current.Apparent_temperature, currentUnits?.Apparent_temperature ?? "°C"),
            HumidityDisplay = current.Relativehumidity_2m is int humidity ? $"{humidity}%" : "—",
            WindDisplay = FormatValue(current.Windspeed_10m, currentUnits?.Windspeed_10m ?? "km/h"),
            PrecipitationDisplay = FormatValue(current.Precipitation, currentUnits?.Precipitation ?? "mm"),
            ForecastDays = forecastDays,
            Alerts = alerts,
            RefreshedAt = DateTimeOffset.Now,
            StatusMessage = alerts.Count == 0
                ? $"Updated {forecastDays.Count}-day forecast. No active alerts right now."
                : $"Updated {forecastDays.Count}-day forecast with {alerts.Count} active alert(s)."
        };
    }

    private async Task<List<WeatherAlertItem>> FetchAlertsAsync(
        string location,
        string alertRegion,
        bool highRiskOnly,
        WeatherForecast forecast,
        CancellationToken cancellationToken)
    {
        var alertEntries = new List<AlertEntry>();
        var filters = BuildAreaFilters(location);
        var normalizedRegion = string.IsNullOrWhiteSpace(alertRegion) ? "Canada" : alertRegion.Trim();

        if (normalizedRegion is "United States" or "Both")
        {
            var point = string.Create(
                CultureInfo.InvariantCulture,
                $"{forecast.Latitude:0.####},{forecast.Longitude:0.####}");

            var nwsOptions = new NwsOptions
            {
                Enabled = true,
                Point = point,
                HighRiskOnly = highRiskOnly,
                FeedUrls = NwsOptions.GetDefaultFeedUrls(),
                UserAgent = "WSG Mobile/1.0"
            };

            using var nwsClient = new NwsClient(_httpClient, nwsOptions);
            alertEntries.AddRange(await nwsClient.FetchAlertsAsync());
        }

        if (normalizedRegion is "Canada" or "Both")
        {
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase)
                ? "fr-CA"
                : "en-CA";

            var alertReadyOptions = new AlertReadyOptions
            {
                Enabled = true,
                FeedUrls = AlertReadyOptions.GetDefaultHttpUrls(),
                AreaFilters = filters,
                PreferredLanguage = language,
                HighRiskOnly = highRiskOnly,
                ExcludeWeatherAlerts = false,
                Jurisdictions = new List<string> { "CA" }
            };

            using var alertReadyClient = new AlertReadyClient(_httpClient, alertReadyOptions);
            alertEntries.AddRange(await alertReadyClient.FetchAlertsAsync(filters));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return alertEntries
            .Where(alert => !string.IsNullOrWhiteSpace(alert.Title))
            .GroupBy(alert => BuildAlertKey(alert), StringComparer.OrdinalIgnoreCase)
            .Select(group => MapAlert(group.First()))
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => alert.Title)
            .ToList();
    }

    private List<ForecastDayItem> BuildForecastDays(WeatherForecast forecast)
    {
        var items = new List<ForecastDayItem>();
        var daily = forecast.Daily;
        if (daily?.Time is null)
        {
            return items;
        }

        var tempUnit = forecast.DailyUnits?.Temperature_2m_max ?? "°C";
        var windUnit = forecast.DailyUnits?.Windspeed_10m_max ?? "km/h";

        for (var index = 0; index < Math.Min(7, daily.Time.Length); index++)
        {
            var day = DateTime.TryParse(daily.Time[index], out var parsedDay)
                ? parsedDay
                : DateTime.Today.AddDays(index);
            var weatherCode = (int)(daily.Weathercode?.ElementAtOrDefault(index) ?? 0);
            var precipitation = daily.Precipitation_sum?.ElementAtOrDefault(index);
            var windSpeed = daily.Windspeed_10m_max?.ElementAtOrDefault(index);

            items.Add(new ForecastDayItem
            {
                DayLabel = index == 0 ? "Today" : day.ToString("ddd, MMM d"),
                Summary = _weatherClient.WeathercodeToString(weatherCode),
                TemperatureRange = $"{FormatValue(daily.Temperature_2m_max?.ElementAtOrDefault(index), tempUnit)} / {FormatValue(daily.Temperature_2m_min?.ElementAtOrDefault(index), tempUnit)}",
                Extras = $"Precip: {FormatValue(precipitation, "mm")} • Wind: {FormatValue(windSpeed, windUnit)}"
            });
        }

        return items;
    }

    private static List<string> BuildAreaFilters(string location) =>
        location
            .Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildAlertKey(AlertEntry alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.Identifier))
        {
            return alert.Identifier!;
        }

        return $"{alert.Provider}|{alert.Title}|{alert.Region}|{alert.City}";
    }

    private static WeatherAlertItem MapAlert(AlertEntry alert)
    {
        var severity = string.IsNullOrWhiteSpace(alert.Severity) ? alert.Type : alert.Severity;
        var region = string.IsNullOrWhiteSpace(alert.Region) ? alert.City : alert.Region;

        return new WeatherAlertItem
        {
            Title = alert.Title,
            Provider = alert.Provider ?? "Weather Alert",
            Severity = string.IsNullOrWhiteSpace(severity) ? "Alert" : severity,
            Region = string.IsNullOrWhiteSpace(region) ? "Local area" : region,
            Summary = string.IsNullOrWhiteSpace(alert.Summary) ? "No additional details provided." : alert.Summary,
            AccentColor = MapColor(alert.SeverityColor, alert.Severity)
        };
    }

    private static Color MapColor(string? severityColor, string? severity)
    {
        var normalized = $"{severityColor} {severity}".Trim().ToLowerInvariant();
        return normalized switch
        {
            var value when value.Contains("red") || value.Contains("extreme") || value.Contains("severe")
                => Color.FromArgb("#DC2626"),
            var value when value.Contains("yellow") || value.Contains("moderate")
                => Color.FromArgb("#CA8A04"),
            var value when value.Contains("orange")
                => Color.FromArgb("#EA580C"),
            _ => Color.FromArgb("#64748B")
        };
    }

    private static string FormatValue(float? value, string unit) =>
        value.HasValue ? $"{value.Value:0.#} {unit}" : "—";
}

public sealed class WeatherSnapshot
{
    public string LocationName { get; init; } = string.Empty;
    public string CurrentTemperatureDisplay { get; init; } = "--";
    public string ConditionSummary { get; init; } = "Weather data is not available yet.";
    public string FeelsLikeDisplay { get; init; } = "—";
    public string HumidityDisplay { get; init; } = "—";
    public string WindDisplay { get; init; } = "—";
    public string PrecipitationDisplay { get; init; } = "—";
    public DateTimeOffset RefreshedAt { get; init; } = DateTimeOffset.Now;
    public List<ForecastDayItem> ForecastDays { get; init; } = new();
    public List<WeatherAlertItem> Alerts { get; init; } = new();
    public string StatusMessage { get; init; } = "Ready";

    public static WeatherSnapshot Error(string locationName, string message) => new()
    {
        LocationName = locationName,
        ConditionSummary = message,
        StatusMessage = message,
        RefreshedAt = DateTimeOffset.Now
    };
}

public sealed class ForecastDayItem
{
    public string DayLabel { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string TemperatureRange { get; init; } = string.Empty;
    public string Extras { get; init; } = string.Empty;
}

public sealed class WeatherAlertItem
{
    public string Title { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public Color AccentColor { get; init; } = Colors.Gray;
}
