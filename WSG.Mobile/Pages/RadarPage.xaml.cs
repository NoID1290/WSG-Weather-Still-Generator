using System.Text.Json;
using WSG.Mobile.Services;

namespace WSG.Mobile.Pages;

public partial class RadarPage : ContentPage
{
    private readonly RadarService _radarService;
    private readonly LocationStorageService _locationStorage;
    private readonly SettingsService _settingsService;
    private bool _isPlaying;
    private bool _radarVisible = true;
    private bool _mapLoaded;

    public RadarPage(
        RadarService radarService,
        LocationStorageService locationStorage,
        SettingsService settingsService)
    {
        InitializeComponent();
        _radarService = radarService;
        _locationStorage = locationStorage;
        _settingsService = settingsService;

        RadarWebView.Source = new HtmlWebViewSource
        {
            Html = string.Empty // Will load from raw resource
        };

        RadarWebView.Navigated += OnWebViewNavigated;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_mapLoaded)
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("radar_map.html");
                using var reader = new StreamReader(stream);
                var html = await reader.ReadToEndAsync();
                RadarWebView.Source = new HtmlWebViewSource { Html = html };
            }
            catch
            {
                RadarWebView.Source = new UrlWebViewSource { Url = "about:blank" };
            }
        }
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        _mapLoaded = true;
        RadarWebView.Navigated -= OnWebViewNavigated;

        // Center on active location
        await CenterOnActiveLocation();

        // Apply theme
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark ||
                     Application.Current?.UserAppTheme == AppTheme.Dark;
        await RadarWebView.EvaluateJavaScriptAsync($"setDarkMode({(isDark ? "true" : "false")})");

        // Set initial opacity
        var settings = _settingsService.Load();
        var opacity = settings.RadarOpacityPercent / 100.0;
        OpacitySlider.Value = opacity;
        await RadarWebView.EvaluateJavaScriptAsync($"setRadarOpacity({opacity:F2})");
    }

    private async Task CenterOnActiveLocation()
    {
        var location = _locationStorage.GetActiveLocation();
        var zoom = RadarService.EstimateZoomLevel(200);
        await RadarWebView.EvaluateJavaScriptAsync(
            $"setCenter({location.Latitude:F4}, {location.Longitude:F4}, {zoom})");
    }

    private async void OnCenterClicked(object? sender, EventArgs e)
    {
        await CenterOnActiveLocation();
    }

    private async void OnToggleRadarClicked(object? sender, EventArgs e)
    {
        _radarVisible = !_radarVisible;
        await RadarWebView.EvaluateJavaScriptAsync($"setRadarVisible({(_radarVisible ? "true" : "false")})");
        BtnToggleRadar.Text = _radarVisible ? "🛰️" : "🗺️";
    }

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (!_isPlaying)
        {
            // Load frames and start animation
            var settings = _settingsService.Load();
            var frames = _radarService.GetRadarFrames(settings.RadarFrameCount);
            var timestamps = frames.Select(f => f.TimeString).ToList();
            var json = JsonSerializer.Serialize(timestamps);

            await RadarWebView.EvaluateJavaScriptAsync($"loadFrames('{json.Replace("'", "\\'")}')");

            var speed = settings.RadarAnimationSpeed switch
            {
                "Slow" => 800,
                "Fast" => 300,
                _ => 500
            };

            await RadarWebView.EvaluateJavaScriptAsync($"animateRadar({speed})");
            _isPlaying = true;
            BtnPlayPause.Text = "⏸️";
        }
        else
        {
            await RadarWebView.EvaluateJavaScriptAsync("stopAnimation()");
            _isPlaying = false;
            BtnPlayPause.Text = "▶️";
        }
    }

    private async void OnOpacityChanged(object? sender, ValueChangedEventArgs e)
    {
        var opacity = e.NewValue;
        OpacityLabel.Text = $"{(int)(opacity * 100)}%";
        if (_mapLoaded)
        {
            await RadarWebView.EvaluateJavaScriptAsync($"setRadarOpacity({opacity:F2})");
        }
    }
}
