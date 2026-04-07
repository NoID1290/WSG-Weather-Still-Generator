using System.Collections.ObjectModel;
using WSG.Mobile.Models;
using WSG.Mobile.Services;
using WSG.Mobile.ViewModels;

namespace WSG.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly WeatherAppState _state;
    private readonly SettingsService _settingsService;
    private readonly LocationStorageService _locationStorage;
    private readonly GpsLocationService _gpsService;
    private readonly ThemeService _themeService;
    private readonly VersionService _versionService;
    private readonly ObservableCollection<SavedLocation> _locations = new();
    private bool _isInitialized;

    public SettingsPage(
        WeatherAppState state,
        SettingsService settingsService,
        LocationStorageService locationStorage,
        GpsLocationService gpsService,
        ThemeService themeService,
        VersionService versionService)
    {
        InitializeComponent();
        _state = state;
        _settingsService = settingsService;
        _locationStorage = locationStorage;
        _gpsService = gpsService;
        _themeService = themeService;
        _versionService = versionService;
        BindingContext = _state;

        LocationList.ItemsSource = _locations;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadSettingsToUI();
        _isInitialized = true;
    }

    private void LoadSettingsToUI()
    {
        var settings = _settingsService.Load();

        // Locations
        _locations.Clear();
        foreach (var loc in _locationStorage.LoadLocations())
            _locations.Add(loc);

        // GPS
        GpsSwitch.IsToggled = _gpsService.IsTracking || _locationStorage.GetFollowingLocation() is not null;

        // Theme
        ThemePicker.ItemsSource = (System.Collections.IList)_themeService.AvailableThemes;
        ThemePicker.SelectedItem = _themeService.CurrentTheme;

        // Display
        TempUnitPicker.SelectedItem = settings.TemperatureUnit;
        WindUnitPicker.SelectedItem = settings.WindSpeedUnit;
        TimeFormatPicker.SelectedItem = settings.TimeFormat;

        // Radar
        RadarFramesPicker.SelectedItem = settings.RadarFrameCount.ToString();
        RadarSpeedPicker.SelectedItem = settings.RadarAnimationSpeed;
        RadarOpacitySlider.Value = settings.RadarOpacityPercent;

        // Notifications
        NotifSwitch.IsToggled = settings.NotificationsEnabled;
        PollIntervalSection.IsVisible = settings.NotificationsEnabled;
        PollIntervalPicker.SelectedItem = settings.AlertPollIntervalMinutes switch
        {
            15 => "15 min",
            60 => "60 min",
            _ => "30 min"
        };

        // Thresholds
        TempThresholdSwitch.IsToggled = settings.TempThresholdEnabled;
        TempThresholdRange.IsVisible = settings.TempThresholdEnabled;
        TempMinEntry.Text = settings.TempThresholdMin.ToString("F0");
        TempMaxEntry.Text = settings.TempThresholdMax.ToString("F0");

        WindThresholdSwitch.IsToggled = settings.WindThresholdEnabled;
        WindMaxEntry.IsVisible = settings.WindThresholdEnabled;
        WindMaxEntry.Text = settings.WindThresholdMax.ToString("F0");

        PrecipThresholdSwitch.IsToggled = settings.PrecipThresholdEnabled;
        PrecipMaxEntry.IsVisible = settings.PrecipThresholdEnabled;
        PrecipMaxEntry.Text = settings.PrecipThresholdMax.ToString("F0");

        // Wire up threshold switch visibility
        TempThresholdSwitch.Toggled += (_, e) => TempThresholdRange.IsVisible = e.Value;
        WindThresholdSwitch.Toggled += (_, e) => WindMaxEntry.IsVisible = e.Value;
        PrecipThresholdSwitch.Toggled += (_, e) => PrecipMaxEntry.IsVisible = e.Value;

        // Version
        VersionLabel.Text = $"WSG Mobile v{_versionService.GetCurrentVersion()}";
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!_isInitialized || ThemePicker.SelectedItem is not string theme)
            return;
        _themeService.ApplyTheme(theme);
    }

    private void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        PollIntervalSection.IsVisible = e.Value;
    }

    private async void OnGpsToggled(object? sender, ToggledEventArgs e)
    {
        if (e.Value)
        {
            var loc = await _gpsService.GetCurrentLocationAsync();
            if (loc is not null)
            {
                _locationStorage.SetFollowingLocation(loc);
                _gpsService.StartTracking(_locationStorage, () =>
                {
                    MainThread.BeginInvokeOnMainThread(RefreshLocationList);
                });
                RefreshLocationList();
            }
            else
            {
                GpsSwitch.IsToggled = false;
                await DisplayAlert("Location", "Could not get your location. Please enable location services.", "OK");
            }
        }
        else
        {
            _gpsService.StopTracking();
            _locationStorage.SetFollowingLocation(null);
            RefreshLocationList();
        }
    }

    private async void OnAddLocation(object? sender, EventArgs e)
    {
        var name = NewLocationEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var location = new SavedLocation { Name = name, Region = _state.SelectedAlertRegion ?? "Canada" };

        // Geocode to get lat/lon
        try
        {
            var client = new OpenMeteo.OpenMeteoClient();
            var coords = await client.GetLocationLatitudeLongitudeAsync(name);
            if (coords.HasValue)
            {
                location.Latitude = coords.Value.latitude;
                location.Longitude = coords.Value.longitude;
            }
        }
        catch
        {
            // Proceed without coordinates — the aggregator will fall back to name-based geocoding
        }

        if (_locationStorage.AddLocation(location))
        {
            NewLocationEntry.Text = string.Empty;
            RefreshLocationList();
        }
        else
        {
            await DisplayAlert("Limit Reached", "You can save up to 4 locations. Remove one first.", "OK");
        }
    }

    private async void OnDeleteLocationClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is SavedLocation loc)
        {
            var confirmed = await DisplayAlert(
                "Delete Location",
                $"Remove \"{loc.DisplayName}\" from saved locations?",
                "Delete", "Cancel");

            if (!confirmed)
                return;

            var locations = _locationStorage.LoadLocations();
            var index = locations.FindIndex(l => l.Name == loc.Name && l.Order == loc.Order);
            if (index >= 0)
            {
                _locationStorage.RemoveLocation(index);
                RefreshLocationList();
            }
        }
    }

    private void RefreshLocationList()
    {
        _locations.Clear();
        foreach (var loc in _locationStorage.LoadLocations())
            _locations.Add(loc);
    }

    private async void OnSaveAndRefreshClicked(object? sender, EventArgs e)
    {
        SaveAllSettings();
        await _state.RefreshAsync();
    }

    private void SaveAllSettings()
    {
        var settings = _settingsService.Load();

        settings.TemperatureUnit = TempUnitPicker.SelectedItem as string ?? "°C";
        settings.WindSpeedUnit = WindUnitPicker.SelectedItem as string ?? "km/h";
        settings.TimeFormat = TimeFormatPicker.SelectedItem as string ?? "24h";

        if (int.TryParse(RadarFramesPicker.SelectedItem as string, out var frames))
            settings.RadarFrameCount = frames;
        settings.RadarAnimationSpeed = RadarSpeedPicker.SelectedItem as string ?? "Normal";
        settings.RadarOpacityPercent = (int)RadarOpacitySlider.Value;

        settings.AlertRegion = _state.SelectedAlertRegion ?? "Canada";
        settings.HighRiskOnly = _state.HighRiskOnly;
        settings.NotificationsEnabled = NotifSwitch.IsToggled;
        settings.AlertPollIntervalMinutes = (PollIntervalPicker.SelectedItem as string) switch
        {
            "15 min" => 15,
            "60 min" => 60,
            _ => 30
        };

        settings.TempThresholdEnabled = TempThresholdSwitch.IsToggled;
        if (float.TryParse(TempMinEntry.Text, out var tMin)) settings.TempThresholdMin = tMin;
        if (float.TryParse(TempMaxEntry.Text, out var tMax)) settings.TempThresholdMax = tMax;

        settings.WindThresholdEnabled = WindThresholdSwitch.IsToggled;
        if (float.TryParse(WindMaxEntry.Text, out var wMax)) settings.WindThresholdMax = wMax;

        settings.PrecipThresholdEnabled = PrecipThresholdSwitch.IsToggled;
        if (float.TryParse(PrecipMaxEntry.Text, out var pMax)) settings.PrecipThresholdMax = pMax;

        _settingsService.Save(settings);
        _state.SaveSettings();
    }

    private async void OnCheckUpdates(object? sender, EventArgs e)
    {
        var (hasUpdate, latestVersion, releaseUrl) = await _versionService.CheckForUpdateAsync();
        if (hasUpdate && releaseUrl is not null)
        {
            var openBrowser = await DisplayAlert(
                "Update Available",
                $"Version {latestVersion} is available. You are running v{_versionService.GetCurrentVersion()}.",
                "Open Download",
                "Later");

            if (openBrowser)
            {
                await Launcher.Default.OpenAsync(new Uri(releaseUrl));
            }
        }
        else
        {
            await DisplayAlert("Up to Date", $"You are running the latest version (v{_versionService.GetCurrentVersion()}).", "OK");
        }
    }
}
