using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using WSG.Mobile.Services;

namespace WSG.Mobile.ViewModels;

public sealed class WeatherAppState : INotifyPropertyChanged
{
    private const string LocationPreferenceKey = "settings.location";
    private const string RegionPreferenceKey = "settings.region";
    private const string HighRiskPreferenceKey = "settings.highRiskOnly";

    private readonly WeatherAggregatorService _weatherAggregator;

    private string _locationQuery;
    private string _selectedAlertRegion;
    private bool _highRiskOnly;
    private bool _isBusy;
    private string _statusMessage = "Ready to fetch weather.";
    private string _lastUpdatedText = "Not refreshed yet.";
    private string _currentTemperatureDisplay = "--";
    private string _conditionSummary = "Tap Refresh to load current conditions.";
    private string _feelsLikeDisplay = "—";
    private string _humidityDisplay = "—";
    private string _windDisplay = "—";
    private string _precipitationDisplay = "—";

    public WeatherAppState(WeatherAggregatorService weatherAggregator)
    {
        _weatherAggregator = weatherAggregator;
        _locationQuery = Preferences.Default.Get(LocationPreferenceKey, "Montreal, QC");
        _selectedAlertRegion = Preferences.Default.Get(RegionPreferenceKey, "Canada");
        _highRiskOnly = Preferences.Default.Get(HighRiskPreferenceKey, true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> AlertRegions { get; } = new[] { "Canada", "United States", "Both" };

    public ObservableCollection<ForecastDayItem> ForecastDays { get; } = new();

    public ObservableCollection<WeatherAlertItem> Alerts { get; } = new();

    public string LocationQuery
    {
        get => _locationQuery;
        set => SetProperty(ref _locationQuery, value);
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

    public string AlertSummary => Alerts.Count switch
    {
        0 => "No active alerts.",
        1 => "1 active alert.",
        _ => $"{Alerts.Count} active alerts."
    };

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        SaveSettings();

        try
        {
            IsBusy = true;
            StatusMessage = $"Refreshing {LocationQuery.Trim()}…";

            var snapshot = await _weatherAggregator.GetSnapshotAsync(LocationQuery, SelectedAlertRegion, HighRiskOnly, cancellationToken);

            CurrentTemperatureDisplay = snapshot.CurrentTemperatureDisplay;
            ConditionSummary = snapshot.ConditionSummary;
            FeelsLikeDisplay = snapshot.FeelsLikeDisplay;
            HumidityDisplay = snapshot.HumidityDisplay;
            WindDisplay = snapshot.WindDisplay;
            PrecipitationDisplay = snapshot.PrecipitationDisplay;
            LastUpdatedText = $"Updated {snapshot.RefreshedAt.LocalDateTime:g}";
            StatusMessage = snapshot.StatusMessage;

            ReplaceCollection(ForecastDays, snapshot.ForecastDays);
            ReplaceCollection(Alerts, snapshot.Alerts);
            OnPropertyChanged(nameof(AlertSummary));
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
        Preferences.Default.Set(LocationPreferenceKey, LocationQuery ?? string.Empty);
        Preferences.Default.Set(RegionPreferenceKey, SelectedAlertRegion ?? "Canada");
        Preferences.Default.Set(HighRiskPreferenceKey, HighRiskOnly);
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
