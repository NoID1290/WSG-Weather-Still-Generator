using WSG.Mobile.Models;

namespace WSG.Mobile.Services;

public sealed class AlertPollingService : IDisposable
{
    private readonly WeatherAggregatorService _aggregator;
    private readonly SettingsService _settings;
    private readonly LocationStorageService _locationStorage;
    private readonly ThresholdMonitorService _thresholdMonitor;
    private IDispatcherTimer? _timer;
    private readonly HashSet<string> _seenAlertIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AlertPollingService(
        WeatherAggregatorService aggregator,
        SettingsService settings,
        LocationStorageService locationStorage,
        ThresholdMonitorService thresholdMonitor)
    {
        _aggregator = aggregator;
        _settings = settings;
        _locationStorage = locationStorage;
        _thresholdMonitor = thresholdMonitor;
    }

    public event Action<string, string>? NewAlertDetected;
    public event Action<string, string>? ThresholdBreached;

    public void Start(IDispatcher dispatcher)
    {
        Stop();

        var appSettings = _settings.Load();
        if (!appSettings.NotificationsEnabled)
            return;

        var interval = TimeSpan.FromMinutes(Math.Max(15, appSettings.AlertPollIntervalMinutes));
        _timer = dispatcher.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public async Task PollAsync()
    {
        try
        {
            var appSettings = _settings.Load();
            var location = _locationStorage.GetActiveLocation();

            var snapshot = await _aggregator.GetSnapshotAsync(
                location.Name,
                appSettings.AlertRegion,
                appSettings.HighRiskOnly);

            // Check for new alerts
            foreach (var alert in snapshot.Alerts)
            {
                var key = $"{alert.Provider}|{alert.Title}|{alert.Region}";
                if (_seenAlertIds.Add(key))
                {
                    NewAlertDetected?.Invoke(alert.Title, alert.Summary);
                }
            }

            // Trim seen alerts if too large
            if (_seenAlertIds.Count > 500)
                _seenAlertIds.Clear();

            // Check thresholds
            _thresholdMonitor.CheckThresholds(snapshot, appSettings, ThresholdBreached);
        }
        catch
        {
            // Polling failures are non-critical
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
