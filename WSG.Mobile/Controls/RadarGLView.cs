using System.Collections.Generic;

namespace WSG.Mobile.Controls;

// ─────────────────────────────────────────────────────────
// Event args
// ─────────────────────────────────────────────────────────

public sealed class RadarFrameEventArgs : EventArgs
{
    public int FrameIndex { get; }
    public string TimeLabel { get; }
    public RadarFrameEventArgs(int index, string label) { FrameIndex = index; TimeLabel = label; }
}

public sealed class GeoCoordEventArgs : EventArgs
{
    public double Latitude { get; }
    public double Longitude { get; }
    public GeoCoordEventArgs(double lat, double lon) { Latitude = lat; Longitude = lon; }
}

// ─────────────────────────────────────────────────────────
// Radar frame source (WMS URL + display label)
// ─────────────────────────────────────────────────────────

public sealed class RadarFrameSource
{
    public string WmsUrl { get; init; } = string.Empty;
    public string DisplayLabel { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────
// Internal interface implemented by the platform renderer.
// Set by RadarGLViewHandler when the surface is ready.
// ─────────────────────────────────────────────────────────

internal interface IRadarGLRenderer
{
    void SetCenter(double lat, double lon, int zoom);
    void SetShowRadar(bool show);
    void SetRadarOpacity(float opacity);
    void SetUserMarker(double lat, double lon, bool show);
    void PlayAnimation();
    void StopAnimation();
    void LoadFrames(IReadOnlyList<RadarFrameSource> frames);
    void SetAnimationSpeed(int millisPerFrame);
    void SetFrameIndex(int index);
    void SetRadarBbox(double minLat, double minLon, double maxLat, double maxLon);
    // ── New in v0.0.11 ────────────────────────────────────
    void SetMapStyle(string style);
    void SetLightningEnabled(bool enabled);
    void SetLightningCg(bool enabled);
    void SetLightningIc(bool enabled);
    void SetLightningWindowMinutes(int minutes);
    void SetLightningPollIntervalSeconds(int seconds);
    void ReloadRadarFrames(IReadOnlyList<RadarFrameSource> frames);
}

// ─────────────────────────────────────────────────────────
// RadarGLView — MAUI virtual view for the OpenGL ES radar map
// ─────────────────────────────────────────────────────────

public sealed class RadarGLView : View
{
    // The platform renderer is injected by the handler after surface creation.
    internal IRadarGLRenderer? PlatformRenderer;

    // ── Bindable properties ───────────────────────────────

    public static readonly BindableProperty CenterLatProperty =
        BindableProperty.Create(nameof(CenterLat), typeof(double), typeof(RadarGLView), 56.13,
            propertyChanged: (b, _, n) => ((RadarGLView)b).PlatformRenderer?.SetCenter(
                (double)n, ((RadarGLView)b).CenterLon, ((RadarGLView)b).ZoomLevel));

    public static readonly BindableProperty CenterLonProperty =
        BindableProperty.Create(nameof(CenterLon), typeof(double), typeof(RadarGLView), -106.35,
            propertyChanged: (b, _, n) => ((RadarGLView)b).PlatformRenderer?.SetCenter(
                ((RadarGLView)b).CenterLat, (double)n, ((RadarGLView)b).ZoomLevel));

    public static readonly BindableProperty ZoomLevelProperty =
        BindableProperty.Create(nameof(ZoomLevel), typeof(int), typeof(RadarGLView), 8,
            propertyChanged: (b, _, n) => ((RadarGLView)b).PlatformRenderer?.SetCenter(
                ((RadarGLView)b).CenterLat, ((RadarGLView)b).CenterLon, (int)n));

    public static readonly BindableProperty ShowRadarProperty =
        BindableProperty.Create(nameof(ShowRadar), typeof(bool), typeof(RadarGLView), true,
            propertyChanged: (b, _, n) => ((RadarGLView)b).PlatformRenderer?.SetShowRadar((bool)n));

    public static readonly BindableProperty RadarOpacityProperty =
        BindableProperty.Create(nameof(RadarOpacity), typeof(float), typeof(RadarGLView), 0.7f,
            propertyChanged: (b, _, n) => ((RadarGLView)b).PlatformRenderer?.SetRadarOpacity((float)n));

    public static readonly BindableProperty ShowUserMarkerProperty =
        BindableProperty.Create(nameof(ShowUserMarker), typeof(bool), typeof(RadarGLView), false,
            propertyChanged: (b, _, _2) =>
            {
                var v = (RadarGLView)b;
                v.PlatformRenderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker);
            });

    public static readonly BindableProperty UserLatProperty =
        BindableProperty.Create(nameof(UserLat), typeof(double), typeof(RadarGLView), 0.0,
            propertyChanged: (b, _, _2) =>
            {
                var v = (RadarGLView)b;
                v.PlatformRenderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker);
            });

    public static readonly BindableProperty UserLonProperty =
        BindableProperty.Create(nameof(UserLon), typeof(double), typeof(RadarGLView), 0.0,
            propertyChanged: (b, _, _2) =>
            {
                var v = (RadarGLView)b;
                v.PlatformRenderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker);
            });

    public static readonly BindableProperty IsAnimatingProperty =
        BindableProperty.Create(nameof(IsAnimating), typeof(bool), typeof(RadarGLView), false);

    // ── Properties ────────────────────────────────────────

    public double CenterLat
    {
        get => (double)GetValue(CenterLatProperty);
        set => SetValue(CenterLatProperty, value);
    }

    public double CenterLon
    {
        get => (double)GetValue(CenterLonProperty);
        set => SetValue(CenterLonProperty, value);
    }

    public int ZoomLevel
    {
        get => (int)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public bool ShowRadar
    {
        get => (bool)GetValue(ShowRadarProperty);
        set => SetValue(ShowRadarProperty, value);
    }

    public float RadarOpacity
    {
        get => (float)GetValue(RadarOpacityProperty);
        set => SetValue(RadarOpacityProperty, value);
    }

    public bool ShowUserMarker
    {
        get => (bool)GetValue(ShowUserMarkerProperty);
        set => SetValue(ShowUserMarkerProperty, value);
    }

    public double UserLat
    {
        get => (double)GetValue(UserLatProperty);
        set => SetValue(UserLatProperty, value);
    }

    public double UserLon
    {
        get => (double)GetValue(UserLonProperty);
        set => SetValue(UserLonProperty, value);
    }

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    // ── Events ────────────────────────────────────────────

    /// <summary>Fired on the main thread when the animation advances to the next frame.</summary>
    public event EventHandler<RadarFrameEventArgs>? FrameChanged;

    /// <summary>Fired on the main thread while the user is panning, with current center coords.</summary>
    public event EventHandler<GeoCoordEventArgs>? CoordinatesChanged;

    // ── Public command methods ────────────────────────────

    public void PlayAnimation()
    {
        IsAnimating = true;
        PlatformRenderer?.PlayAnimation();
    }

    public void StopAnimation()
    {
        IsAnimating = false;
        PlatformRenderer?.StopAnimation();
    }

    public void CenterOnLocation(double lat, double lon, int zoom = -1)
        => PlatformRenderer?.SetCenter(lat, lon, zoom < 0 ? ZoomLevel : zoom);

    public void LoadRadarFrames(IReadOnlyList<RadarFrameSource> frames)
        => PlatformRenderer?.LoadFrames(frames);

    public void SetAnimationSpeed(int millisPerFrame)
        => PlatformRenderer?.SetAnimationSpeed(millisPerFrame);

    public void SetFrameIndex(int index)
        => PlatformRenderer?.SetFrameIndex(index);

    public void SetRadarBbox(double minLat, double minLon, double maxLat, double maxLon)
        => PlatformRenderer?.SetRadarBbox(minLat, minLon, maxLat, maxLon);

    public void SetMapStyle(string style)
        => PlatformRenderer?.SetMapStyle(style);

    public void SetLightningEnabled(bool enabled)
        => PlatformRenderer?.SetLightningEnabled(enabled);

    public void SetLightningCg(bool enabled)
        => PlatformRenderer?.SetLightningCg(enabled);

    public void SetLightningIc(bool enabled)
        => PlatformRenderer?.SetLightningIc(enabled);

    public void SetLightningWindowMinutes(int minutes)
        => PlatformRenderer?.SetLightningWindowMinutes(minutes);

    public void SetLightningPollIntervalSeconds(int seconds)
        => PlatformRenderer?.SetLightningPollIntervalSeconds(seconds);

    public void ReloadRadarFrames(IReadOnlyList<RadarFrameSource> frames)
        => PlatformRenderer?.ReloadRadarFrames(frames);

    // ── Internal callbacks from renderer → MAUI (posted to main thread) ──

    internal void RaiseFrameChanged(int index, string label)
        => MainThread.BeginInvokeOnMainThread(() => FrameChanged?.Invoke(this, new RadarFrameEventArgs(index, label)));

    internal void RaiseCoordinatesChanged(double lat, double lon)
        => MainThread.BeginInvokeOnMainThread(() => CoordinatesChanged?.Invoke(this, new GeoCoordEventArgs(lat, lon)));
}
