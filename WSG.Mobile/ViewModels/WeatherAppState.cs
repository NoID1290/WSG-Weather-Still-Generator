using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using WSG.Mobile.Models;
using WSG.Mobile.Services;

namespace WSG.Mobile.ViewModels;

public sealed class WeatherAppState : INotifyPropertyChanged
{
    private readonly WeatherAggregatorService _weatherAggregator;
    private readonly LocationStorageService _locationStorage;
    private readonly SettingsService _settingsService;
    private readonly WeatherIconService _iconService;

    private string _locationDisplay = "Montreal, QC";
    private string _selectedAlertRegion = "Canada";
    private bool _highRiskOnly = true;
    private bool _isBusy;
    private string _statusMessage = "Ready to fetch weather.";
    private string _lastUpdatedText = "Not refreshed yet.";
    private string _currentTemperatureDisplay = "--";
    private string _conditionSummary = "Tap Refresh to load current conditions.";
    private string _feelsLikeDisplay = "—";
    private string _humidityDisplay = "—";
    private string _windDisplay = "—";
    private string _precipitationDisplay = "—";
    private string _weatherIcon = "🌡️";
    private int _weatherCode;
    private SavedLocation? _activeLocation;

    public WeatherAppState(
        WeatherAggregatorService weatherAggregator,
        LocationStorageService locationStorage,
        SettingsService settingsService,
        WeatherIconService iconService)
    {
        _weatherAggregator = weatherAggregator;
        _locationStorage = locationStorage;
        _settingsService = settingsService;
        _iconService = iconService;

        // Load active location
        _activeLocation = _locationStorage.GetActiveLocation();
        _locationDisplay = _activeLocation.DisplayName;

        var settings = _settingsService.Load();
        _selectedAlertRegion = settings.AlertRegion;
        _highRiskOnly = settings.HighRiskOnly;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> AlertRegions { get; } = new[] { "Canada", "United States", "Both" };

    public ObservableCollection<ForecastDayItem> ForecastDays { get; } = new();

    public ObservableCollection<WeatherAlertItem> Alerts { get; } = new();

    public string LocationDisplay
    {
        get => _locationDisplay;
        set => SetProperty(ref _locationDisplay, value);
    }

    // Kept for backward compatibility with settings page
    public string LocationQuery
    {
        get => _activeLocation?.Name ?? _locationDisplay;
        set
        {
            _locationDisplay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LocationDisplay));
        }
    }

    public string SelectedAlertRegion
    {
        get => _selectedAlertRegion;
        set => SetProperty(ref _selectedAlertRegion, value);
    }

    public bool HighRiskOnly
    {
        get => _highRiskOnly;
        set => SetProperty(ref _highRiskOnly, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string CurrentTemperatureDisplay
    {
        get => _currentTemperatureDisplay;
        private set => SetProperty(ref _currentTemperatureDisplay, value);
    }

    public string ConditionSummary
    {
        get => _conditionSummary;
        private set => SetProperty(ref _conditionSummary, value);
    }

    public string FeelsLikeDisplay
    {
        get => _feelsLikeDisplay;
        private set => SetProperty(ref _feelsLikeDisplay, value);
    }

    public string HumidityDisplay
    {
        get => _humidityDisplay;
        private set => SetProperty(ref _humidityDisplay, value);
    }

    public string WindDisplay
    {
        get => _windDisplay;
        private set => SetProperty(ref _windDisplay, value);
    }

    public string PrecipitationDisplay
    {
        get => _precipitationDisplay;
        private set => SetProperty(ref _precipitationDisplay, value);
    }

    public string WeatherIcon
    {
        get => _weatherIcon;
        private set => SetProperty(ref _weatherIcon, value);
    }

    public int WeatherCode
    {
        get => _weatherCode;
        private set => SetProperty(ref _weatherCode, value);
    }

    public string AlertSummary => Alerts.Count switch
    {
        0 => "No active alerts.",
        1 => "1 active alert.",
        _ => $"{Alerts.Count} active alerts."
    };

    public void SwitchLocation(int index)
    {
        _locationStorage.SetActiveIndex(index);
        _activeLocation = _locationStorage.GetActiveLocation();
        LocationDisplay = _activeLocation.DisplayName;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        SaveSettings();

        // Get active location
        _activeLocation = _locationStorage.GetActiveLocation();
        var locationName = _activeLocation?.Name ?? "Montreal, QC";
        LocationDisplay = _activeLocation?.DisplayName ?? locationName;

        try
        {
            IsBusy = true;
            StatusMessage = $"Refreshing {locationName.Trim()}…";

            var snapshot = await _weatherAggregator.GetSnapshotAsync(
                locationName,
                _activeLocation?.Latitude,
                _activeLocation?.Longitude,
                SelectedAlertRegion,
                HighRiskOnly,
                cancellationToken);

            CurrentTemperatureDisplay = snapshot.CurrentTemperatureDisplay;
            ConditionSummary = snapshot.ConditionSummary;
            FeelsLikeDisplay = snapshot.FeelsLikeDisplay;
            HumidityDisplay = snapshot.HumidityDisplay;
            WindDisplay = snapshot.WindDisplay;
            PrecipitationDisplay = snapshot.PrecipitationDisplay;
            LastUpdatedText = $"Updated {snapshot.RefreshedAt.LocalDateTime:g}";
            StatusMessage = snapshot.StatusMessage;
            WeatherCode = snapshot.WeatherCode;
            WeatherIcon = _iconService.GetWeatherIcon(snapshot.WeatherCode);

            ReplaceCollection(ForecastDays, snapshot.ForecastDays);
            ReplaceCollection(Alerts, snapshot.Alerts);
            OnPropertyChanged(nameof(AlertSummary));

            // Push data to widget
            PushToWidget(locationName, snapshot.CurrentTemperatureDisplay, snapshot.ConditionSummary);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SaveSettings()
    {
        var settings = _settingsService.Load();
        settings.AlertRegion = SelectedAlertRegion ?? "Canada";
        settings.HighRiskOnly = HighRiskOnly;
        _settingsService.Save(settings);
    }

    private static void PushToWidget(string location, string temperature, string condition)
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            Platforms.Android.Widget.WeatherWidgetProvider.PushWeatherData(context, location, temperature, condition);
        }
        catch
        {
            // Widget update failure is non-critical
        }
#endif
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
        {
            return false;
        }

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
