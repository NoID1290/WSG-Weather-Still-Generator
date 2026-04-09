using WSG.Mobile.Controls;
using WSG.Mobile.Services;

namespace WSG.Mobile.Pages;

/// <summary>
/// Radar map page backed by the native OpenGL ES renderer (GLRadarRenderer).
/// Provides: radar layer selector, map style switcher, lightning controls,
/// timeline scrubber with smooth slider, retry on error.
/// </summary>
public partial class RadarPage : ContentPage
{
    private readonly RadarService _radarService;
    private readonly LocationStorageService _locationStorage;
    private readonly SettingsService _settingsService;

    private bool _isPlaying;
    private bool _radarVisible = true;
    private bool _framesLoaded;
    private int  _totalFrames;
    private bool _sliderDragging;          // suppress feedback loop while dragging timeline
    private CancellationTokenSource? _coordBadgeCts;

    // ── Radar layer list (populated from RadarService.RadarLayers) ──────────
    private readonly List<string> _layerIds = new();

    // ── Map style buttons (generated at runtime) ────────────────────────────
    private readonly List<Button> _styleButtons = new();
    private string _activeMapStyle = "Dark";

    // ── Lightning window options ─────────────────────────────────────────────
    private static readonly (int Minutes, string Label)[] LightningWindows =
    [
        (5,  "5 min"),
        (10, "10 min"),
        (20, "20 min"),
        (30, "30 min"),
        (60, "60 min"),
    ];

    public RadarPage(
        RadarService radarService,
        LocationStorageService locationStorage,
        SettingsService settingsService)
    {
        InitializeComponent();
        _radarService   = radarService;
        _locationStorage = locationStorage;
        _settingsService = settingsService;

        BuildStaticControls();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One-time control population (called once from constructor)
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildStaticControls()
    {
        // Radar Layer Picker
        foreach (var (id, name) in RadarService.RadarLayers)
        {
            _layerIds.Add(id);
            RadarLayerPicker.Items.Add(name);
        }

        // Map Style buttons
        foreach (string style in TileCacheService.AvailableStyles)
        {
            var btn = new Button
            {
                Text            = style,
                FontSize        = 12,
                CornerRadius    = 8,
                Padding         = new Thickness(12, 6),
                HeightRequest   = 34,
                BackgroundColor = Colors.Transparent,
                TextColor       = Color.FromArgb("#94A3B8"),   // default dark-theme muted
                BorderColor     = Color.FromArgb("#334155"),
                BorderWidth     = 1,
            };
            btn.Clicked += (_, _) => OnMapStyleClicked(style);
            _styleButtons.Add(btn);
            MapStyleRow.Add(btn);
        }

        // Lightning window picker
        foreach (var (_, label) in LightningWindows)
            LightningWindowPicker.Items.Add(label);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Page lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();

        RadarView.FrameChanged       -= OnRendererFrameChanged;
        RadarView.FrameChanged       += OnRendererFrameChanged;
        RadarView.CoordinatesChanged -= OnRendererCoordsChanged;
        RadarView.CoordinatesChanged += OnRendererCoordsChanged;
        RadarView.HandlerChanged     -= OnRendererHandlerChanged;
        RadarView.HandlerChanged     += OnRendererHandlerChanged;

        var settings = _settingsService.Load();
        ApplySettings(settings);

        CenterOnActiveLocation();

        if (!_framesLoaded && RadarView.Handler != null)
            LoadAndShowFrames(settings);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        RadarView.HandlerChanged     -= OnRendererHandlerChanged;
        RadarView.FrameChanged       -= OnRendererFrameChanged;
        RadarView.CoordinatesChanged -= OnRendererCoordsChanged;

        if (_isPlaying) RadarView.StopAnimation();
        _isPlaying        = false;
        BtnPlayPause.Text = "▶️";
    }

    /// <summary>Apply persisted settings to all UI controls.</summary>
    private void ApplySettings(Models.AppSettings settings)
    {
        // Opacity
        var opacity = settings.RadarOpacityPercent / 100f;
        OpacitySlider.Value   = opacity;
        OpacityLabel.Text     = $"{settings.RadarOpacityPercent}%";
        RadarView.RadarOpacity = opacity;

        // Speed
        var speedMs = AnimSpeedFromSetting(settings.RadarAnimationSpeed);
        SpeedSlider.Value  = speedMs;
        SpeedLabel.Text    = $"{speedMs}ms";

        // Radar layer picker
        int layerIdx = _layerIds.IndexOf(settings.RadarLayer);
        RadarLayerPicker.SelectedIndex = Math.Max(0, layerIdx);

        // Map style
        _activeMapStyle = settings.MapStyle;
        RadarView.SetMapStyle(_activeMapStyle);
        UpdateMapStyleButtons();

        // Lightning
        LightningSwitch.IsToggled    = settings.LightningEnabled;
        LightningCgSwitch.IsToggled  = settings.LightningCgEnabled;
        LightningIcSwitch.IsToggled  = settings.LightningIcEnabled;
        LightningOptions.IsVisible   = settings.LightningEnabled;
        int windowIdx = Array.FindIndex(LightningWindows, w => w.Minutes == settings.LightningTimeWindowMinutes);
        LightningWindowPicker.SelectedIndex = Math.Max(0, windowIdx < 0 ? 3 : windowIdx); // default 30 min

        RadarView.SetLightningEnabled(settings.LightningEnabled);
        RadarView.SetLightningCg(settings.LightningCgEnabled);
        RadarView.SetLightningIc(settings.LightningIcEnabled);
        RadarView.SetLightningWindowMinutes(settings.LightningTimeWindowMinutes);
        RadarView.SetLightningPollIntervalSeconds(settings.LightningPollIntervalSeconds);

        // Lightning toolbar button highlight
        BtnLightning.BackgroundColor = settings.LightningEnabled
            ? Color.FromArgb("#2563EB")
            : Colors.Transparent;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler ready callback
    // ─────────────────────────────────────────────────────────────────────────

    private void OnRendererHandlerChanged(object? sender, EventArgs e)
    {
        if (RadarView.Handler != null && !_framesLoaded)
        {
            CenterOnActiveLocation();
            LoadAndShowFrames(_settingsService.Load());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frame loading
    // ─────────────────────────────────────────────────────────────────────────

    private void LoadAndShowFrames(Models.AppSettings settings)
    {
        var location   = _locationStorage.GetActiveLocation();
        var bbox       = RadarService.CalculateBbox(location.Latitude, location.Longitude, 300);
        string layer   = settings.RadarLayer;
        var radarFrames = _radarService.GetRadarFrames(Math.Clamp(settings.RadarFrameCount, 1, 15));

        var sources = radarFrames.Select(f => new RadarFrameSource
        {
            WmsUrl       = RadarService.BuildWmsImageUrl(bbox, f.TimeString, 512, 512, layer),
            DisplayLabel = f.DisplayTime
        }).ToList();

        RadarView.SetRadarBbox(bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon);

        _totalFrames = sources.Count;
        TimelineSlider.Maximum = Math.Max(1, _totalFrames - 1);
        TimelineSlider.Value   = 0;
        FrameCounterLabel.Text = $"1 / {_totalFrames}";

        TimelinePanel.IsVisible      = true;
        LoadingIndicator.IsVisible   = true;
        LoadingIndicator.IsRunning   = true;
        ErrorBanner.IsVisible        = false;

        RadarView.LoadRadarFrames(sources);
        _framesLoaded = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Toolbar buttons
    // ─────────────────────────────────────────────────────────────────────────

    private void OnCenterClicked(object? sender, EventArgs e)
        => CenterOnActiveLocation();

    private void OnToggleRadarClicked(object? sender, EventArgs e)
    {
        _radarVisible           = !_radarVisible;
        RadarView.ShowRadar     = _radarVisible;
        BtnToggleRadar.Text     = _radarVisible ? "🛰️" : "🗺️";
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (!_isPlaying)
        {
            RadarView.PlayAnimation();
            _isPlaying        = true;
            BtnPlayPause.Text = "⏸️";
        }
        else
        {
            RadarView.StopAnimation();
            _isPlaying        = false;
            BtnPlayPause.Text = "▶️";
        }
    }

    private void OnLightningToggleClicked(object? sender, EventArgs e)
    {
        // Mirror the setting-sheet switch from the toolbar button
        LightningSwitch.IsToggled = !LightningSwitch.IsToggled;
        // The switch's Toggled event handles the rest.
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        bool nowVisible = !SettingsPanel.IsVisible;
        SettingsPanel.IsVisible  = nowVisible;
        TimelinePanel.IsVisible  = !nowVisible || _framesLoaded;
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        _framesLoaded = false;
        ErrorBanner.IsVisible = false;
        LoadAndShowFrames(_settingsService.Load());
    }

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        _framesLoaded = false;
        LoadAndShowFrames(_settingsService.Load());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Settings panel controls
    // ─────────────────────────────────────────────────────────────────────────

    private void OnOpacityChanged(object? sender, ValueChangedEventArgs e)
    {
        var pct = (int)(e.NewValue * 100);
        OpacityLabel.Text      = $"{pct}%";
        RadarView.RadarOpacity = (float)e.NewValue;

        var s = _settingsService.Load();
        s.RadarOpacityPercent = pct;
        _settingsService.Save(s);
    }

    private void OnSpeedChanged(object? sender, ValueChangedEventArgs e)
    {
        int ms = (int)e.NewValue;
        SpeedLabel.Text = $"{ms}ms";
        RadarView.SetAnimationSpeed(ms);

        // Map back to coarse setting for persistence
        var s = _settingsService.Load();
        s.RadarAnimationSpeed = ms <= 350 ? "Fast" : ms <= 650 ? "Normal" : "Slow";
        _settingsService.Save(s);
    }

    private void OnRadarLayerChanged(object? sender, EventArgs e)
    {
        int idx = RadarLayerPicker.SelectedIndex;
        if (idx < 0 || idx >= _layerIds.Count) return;

        var s = _settingsService.Load();
        s.RadarLayer = _layerIds[idx];
        _settingsService.Save(s);

        if (_framesLoaded)
        {
            // Force a full reload with the new layer
            var location    = _locationStorage.GetActiveLocation();
            var bbox        = RadarService.CalculateBbox(location.Latitude, location.Longitude, 300);
            var radarFrames = _radarService.GetRadarFrames(Math.Clamp(s.RadarFrameCount, 1, 15));
            var sources     = radarFrames.Select(f => new RadarFrameSource
            {
                WmsUrl       = RadarService.BuildWmsImageUrl(bbox, f.TimeString, 512, 512, s.RadarLayer),
                DisplayLabel = f.DisplayTime
            }).ToList();

            RadarView.SetRadarBbox(bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon);
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            RadarView.ReloadRadarFrames(sources);
        }
    }

    // ── Map style ──────────────────────────────────────────────────────────

    private void OnMapStyleClicked(string style)
    {
        if (_activeMapStyle == style) return;

        _activeMapStyle = style;
        RadarView.SetMapStyle(style);
        UpdateMapStyleButtons();

        var s = _settingsService.Load();
        s.MapStyle = style;
        _settingsService.Save(s);

        // Update attribution
        AttributionLabel.Text = style switch
        {
            "Terrain"   => "© OpenTopoMap contributors",
            "Satellite" => "© Esri, Maxar, Earthstar Geographics",
            _           => "© OpenStreetMap contributors",
        };
    }

    private void UpdateMapStyleButtons()
    {
        foreach (var btn in _styleButtons)
        {
            bool active = string.Equals(btn.Text, _activeMapStyle, StringComparison.OrdinalIgnoreCase);
            btn.BackgroundColor = active ? Color.FromArgb("#2563EB") : Colors.Transparent;
            btn.TextColor       = active
                                    ? Colors.White
                                    : Color.FromArgb("#94A3B8");
        }
    }

    // ── Lightning controls ─────────────────────────────────────────────────

    private void OnLightningSwitchToggled(object? sender, ToggledEventArgs e)
    {
        bool on = e.Value;
        LightningOptions.IsVisible = on;
        RadarView.SetLightningEnabled(on);
        BtnLightning.BackgroundColor = on ? Color.FromArgb("#2563EB") : Colors.Transparent;

        var s = _settingsService.Load();
        s.LightningEnabled = on;
        _settingsService.Save(s);
    }

    private void OnLightningTypeChanged(object? sender, ToggledEventArgs e)
    {
        RadarView.SetLightningCg(LightningCgSwitch.IsToggled);
        RadarView.SetLightningIc(LightningIcSwitch.IsToggled);

        var s = _settingsService.Load();
        s.LightningCgEnabled = LightningCgSwitch.IsToggled;
        s.LightningIcEnabled = LightningIcSwitch.IsToggled;
        _settingsService.Save(s);
    }

    private void OnLightningWindowChanged(object? sender, EventArgs e)
    {
        int idx = LightningWindowPicker.SelectedIndex;
        if (idx < 0 || idx >= LightningWindows.Length) return;
        int minutes = LightningWindows[idx].Minutes;
        RadarView.SetLightningWindowMinutes(minutes);

        var s = _settingsService.Load();
        s.LightningTimeWindowMinutes = minutes;
        _settingsService.Save(s);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Timeline slider
    // ─────────────────────────────────────────────────────────────────────────

    private void OnTimelineSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_sliderDragging) return; // only act on user touch — renderer updates it on its own
        int idx = (int)Math.Round(e.NewValue);
        idx = Math.Clamp(idx, 0, _totalFrames - 1);
        RadarView.SetFrameIndex(idx);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Renderer callbacks
    // ─────────────────────────────────────────────────────────────────────────

    private void OnRendererFrameChanged(object? sender, RadarFrameEventArgs e)
    {
        // Hide loading indicator once the first real frame arrives
        if (LoadingIndicator.IsRunning)
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }

        int frameNum = e.FrameIndex + 1;
        TimeLabel.Text         = e.TimeLabel;
        FrameCounterLabel.Text = $"{frameNum} / {_totalFrames}";

        // Update the slider without triggering OnTimelineSliderChanged
        _sliderDragging = false;
        TimelineSlider.Value = Math.Clamp(e.FrameIndex, TimelineSlider.Minimum, TimelineSlider.Maximum);
    }

    private async void OnRendererCoordsChanged(object? sender, GeoCoordEventArgs e)
    {
        CoordLabel.Text = $"{e.Latitude:F4}°, {e.Longitude:F4}°";

        _coordBadgeCts?.Cancel();
        _coordBadgeCts = new CancellationTokenSource();
        var token = _coordBadgeCts.Token;

        await CoordBadge.FadeTo(1.0, 150);

        try
        {
            await Task.Delay(2000, token);
            if (!token.IsCancellationRequested)
                await CoordBadge.FadeTo(0.0, 400);
        }
        catch (TaskCanceledException) { /* new pan — badge stays visible */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void CenterOnActiveLocation()
    {
        var loc  = _locationStorage.GetActiveLocation();
        var zoom = RadarService.EstimateZoomLevel(200);
        RadarView.CenterOnLocation(loc.Latitude, loc.Longitude, zoom);
    }

    private static int AnimSpeedFromSetting(string speed) => speed switch
    {
        "Slow" => 800,
        "Fast" => 300,
        _      => 500
    };
}

