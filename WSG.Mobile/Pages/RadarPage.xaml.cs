using WSG.Mobile.Controls;
using WSG.Mobile.Services;

namespace WSG.Mobile.Pages;

/// <summary>
/// Radar map page backed by the native OpenGL ES renderer (GLRadarRenderer).
/// All map interaction is handled by the RadarGLView control; this page manages
/// the toolbar, frame scrubber, settings panel, and coordinates badge.
/// </summary>
public partial class RadarPage : ContentPage
{
    private readonly RadarService _radarService;
    private readonly LocationStorageService _locationStorage;
    private readonly SettingsService _settingsService;

    private bool _isPlaying;
    private bool _radarVisible = true;
    private bool _framesLoaded;
    private CancellationTokenSource? _coordBadgeCts;

    // Frame scrubber backing list
    private readonly List<FrameChip> _chips = new();

    public RadarPage(
        RadarService radarService,
        LocationStorageService locationStorage,
        SettingsService settingsService)
    {
        InitializeComponent();
        _radarService = radarService;
        _locationStorage = locationStorage;
        _settingsService = settingsService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Page lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Wire events (safe to call multiple times — handler checks for duplicates)
        RadarView.FrameChanged       -= OnRendererFrameChanged;
        RadarView.FrameChanged       += OnRendererFrameChanged;
        RadarView.CoordinatesChanged -= OnRendererCoordsChanged;
        RadarView.CoordinatesChanged += OnRendererCoordsChanged;

        // Watch for the handler to be connected (fires after CreatePlatformView).
        // This is the reliable trigger for loading frames, because OnAppearing can
        // fire before the MAUI handler / PlatformRenderer is ready.
        RadarView.HandlerChanged -= OnRendererHandlerChanged;
        RadarView.HandlerChanged += OnRendererHandlerChanged;

        // Apply persisted settings
        var settings = _settingsService.Load();
        var opacity = settings.RadarOpacityPercent / 100f;
        OpacitySlider.Value = opacity;
        OpacityLabel.Text   = $"{settings.RadarOpacityPercent}%";

        var speedMs = AnimSpeedFromSetting(settings.RadarAnimationSpeed);
        SpeedSlider.Value = speedMs;
        SpeedLabel.Text   = $"{speedMs}ms";
        RadarView.RadarOpacity = opacity;

        // Center on active location then load frames
        CenterOnActiveLocation();

        // If the handler is already connected (e.g., returning to this tab), load now.
        // Otherwise OnRendererHandlerChanged will fire when handler connects.
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
        _isPlaying = false;
        BtnPlayPause.Text = "▶️";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler ready callback
    // ─────────────────────────────────────────────────────────────────────────

    private void OnRendererHandlerChanged(object? sender, EventArgs e)
    {
        // Fires when the MAUI handler (and therefore PlatformRenderer) is connected.
        // This is the earliest safe point to call LoadFrames.
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
        var location = _locationStorage.GetActiveLocation();
        var bbox = RadarService.CalculateBbox(location.Latitude, location.Longitude, 300);

        var radarFrames = _radarService.GetRadarFrames(
            Math.Clamp(settings.RadarFrameCount, 1, 15));

        var sources = radarFrames.Select(f => new RadarFrameSource
        {
            WmsUrl       = RadarService.BuildWmsImageUrl(bbox, f.TimeString, 512, 512),
            DisplayLabel = f.DisplayTime
        }).ToList();

        // Tell the GL renderer the geographic bounds of the WMS image
        RadarView.SetRadarBbox(bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon);

        // Build scrubber chips
        _chips.Clear();
        foreach (var f in radarFrames)
            _chips.Add(new FrameChip { Label = f.DisplayTime });
        FrameScrubber.ItemsSource = null;
        FrameScrubber.ItemsSource = _chips;

        // Show scrubber now that we have chips
        ScrubberPanel.IsVisible = true;

        // Show loading spinner while frames download
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;

        RadarView.LoadRadarFrames(sources);
        _framesLoaded = true;

        // The renderer will call FrameChanged when frames are ready, which hides the spinner
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Toolbar buttons
    // ─────────────────────────────────────────────────────────────────────────

    private void OnCenterClicked(object? sender, EventArgs e)
        => CenterOnActiveLocation();

    private void OnToggleRadarClicked(object? sender, EventArgs e)
    {
        _radarVisible = !_radarVisible;
        RadarView.ShowRadar   = _radarVisible;
        BtnToggleRadar.Text   = _radarVisible ? "🛰️" : "🗺️";
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

    private void OnSettingsClicked(object? sender, EventArgs e)
        => SettingsPanel.IsVisible = !SettingsPanel.IsVisible;

    // ─────────────────────────────────────────────────────────────────────────
    // Settings panel
    // ─────────────────────────────────────────────────────────────────────────

    private void OnOpacityChanged(object? sender, ValueChangedEventArgs e)
    {
        var pct = (int)(e.NewValue * 100);
        OpacityLabel.Text      = $"{pct}%";
        RadarView.RadarOpacity = (float)e.NewValue;
    }

    private void OnSpeedChanged(object? sender, ValueChangedEventArgs e)
    {
        int ms = (int)e.NewValue;
        SpeedLabel.Text = $"{ms}ms";
        RadarView.SetAnimationSpeed(ms);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Frame scrubber
    // ─────────────────────────────────────────────────────────────────────────

    private void OnFrameSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0) return;
        int idx = _chips.IndexOf((FrameChip)e.CurrentSelection[0]);
        if (idx >= 0) RadarView.SetFrameIndex(idx);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Renderer callbacks
    // ─────────────────────────────────────────────────────────────────────────

    private void OnRendererFrameChanged(object? sender, RadarFrameEventArgs e)
    {
        // Hide loading indicator on first frame — frames are ready
        if (LoadingIndicator.IsRunning)
        {
            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }

        // Update scrubber selection without triggering OnFrameSelected
        if (e.FrameIndex < _chips.Count)
        {
            FrameScrubber.SelectedItem = _chips[e.FrameIndex];
        }
    }

    private async void OnRendererCoordsChanged(object? sender, GeoCoordEventArgs e)
    {
        CoordLabel.Text = $"{e.Latitude:F4}°, {e.Longitude:F4}°";

        // Show badge, restart auto-hide timer
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
        catch (TaskCanceledException) { /* new pan started — badge stays visible */ }
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
        "Slow"  => 800,
        "Fast"  => 300,
        _       => 500
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// FrameChip — scrubber item model
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FrameChip
{
    public string Label { get; init; } = string.Empty;
}

