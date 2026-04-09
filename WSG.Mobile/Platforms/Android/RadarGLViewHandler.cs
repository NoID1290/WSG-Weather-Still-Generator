using Android.Content;
using Android.Views;
using Android.Opengl;
using Microsoft.Maui.Handlers;
using WSG.Mobile.Controls;
using WSG.Mobile.Platforms.Android.Rendering;
using WSG.Mobile.Services;

namespace WSG.Mobile.Platforms.Android;

/// <summary>
/// MAUI handler that bridges RadarGLView (virtual) ↔ RadarGLSurface (Android GLSurfaceView).
/// </summary>
public sealed class RadarGLViewHandler : ViewHandler<RadarGLView, RadarGLSurface>
{
    private GLRadarRenderer? _renderer;

    /// <summary>Exposes the platform renderer for direct calls (e.g. SetRadarBbox). Internal to the assembly.</summary>
    internal GLRadarRenderer? Renderer => _renderer;

    public static IPropertyMapper<RadarGLView, RadarGLViewHandler> PropertyMapper =
        new PropertyMapper<RadarGLView, RadarGLViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(RadarGLView.CenterLat)]     = static (h, v) => h._renderer?.SetCenter(v.CenterLat, v.CenterLon, v.ZoomLevel),
            [nameof(RadarGLView.CenterLon)]     = static (h, v) => h._renderer?.SetCenter(v.CenterLat, v.CenterLon, v.ZoomLevel),
            [nameof(RadarGLView.ZoomLevel)]     = static (h, v) => h._renderer?.SetCenter(v.CenterLat, v.CenterLon, v.ZoomLevel),
            [nameof(RadarGLView.ShowRadar)]     = static (h, v) => h._renderer?.SetShowRadar(v.ShowRadar),
            [nameof(RadarGLView.RadarOpacity)]  = static (h, v) => h._renderer?.SetRadarOpacity(v.RadarOpacity),
            [nameof(RadarGLView.ShowUserMarker)]= static (h, v) => h._renderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker),
            [nameof(RadarGLView.UserLat)]       = static (h, v) => h._renderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker),
            [nameof(RadarGLView.UserLon)]       = static (h, v) => h._renderer?.SetUserMarker(v.UserLat, v.UserLon, v.ShowUserMarker),
        };

    public RadarGLViewHandler() : base(PropertyMapper) { }

    protected override RadarGLSurface CreatePlatformView()
    {
        var tileCache = MauiContext!.Services.GetRequiredService<TileCacheService>();
        _renderer = new GLRadarRenderer(VirtualView, tileCache);

        var surface = new RadarGLSurface(Context!, _renderer);
        _renderer.AttachSurface(surface);
        VirtualView.PlatformRenderer = _renderer;

        return surface;
    }

    protected override void DisconnectHandler(RadarGLSurface platformView)
    {
        VirtualView.PlatformRenderer = null;
        _renderer = null;
        platformView.Dispose();
        base.DisconnectHandler(platformView);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RadarGLSurface — Android GLSurfaceView subclass with touch handling
// ─────────────────────────────────────────────────────────────────────────────

public sealed class RadarGLSurface : GLSurfaceView
{
    private readonly GLRadarRenderer _renderer;
    private readonly GestureDetector _gestureDetector;
    private readonly ScaleGestureDetector _scaleDetector;
    private bool _isScaling;

    internal RadarGLSurface(Context context, GLRadarRenderer renderer) : base(context)
    {
        _renderer = renderer;

        // Negotiate GLES 3.x (Pixel 9 Pro XL supports 3.2)
        SetEGLContextClientVersion(3);
        SetRenderer(renderer);
        RenderMode = Rendermode.Continuously;

        _gestureDetector = new GestureDetector(context, new PanDoubleTapListener(renderer, this));
        _gestureDetector.IsLongpressEnabled = true;
        _scaleDetector  = new ScaleGestureDetector(context, new PinchListener(renderer, this, () => _isScaling = false));
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return false;
        _scaleDetector.OnTouchEvent(e);
        if (e.PointerCount == 1 && !_isScaling)
            _gestureDetector.OnTouchEvent(e);
        return true;
    }

    // ── PanDoubleTapListener ──────────────────────────────────────────────────

    private sealed class PanDoubleTapListener
        : Java.Lang.Object, GestureDetector.IOnGestureListener, GestureDetector.IOnDoubleTapListener
    {
        private readonly GLRadarRenderer _r;
        private readonly RadarGLSurface _s;

        internal PanDoubleTapListener(GLRadarRenderer r, RadarGLSurface s) { _r = r; _s = s; }

        public bool OnDown(MotionEvent? e) => true;

        public bool OnScroll(MotionEvent? e1, MotionEvent e2, float distanceX, float distanceY)
        {
            _s.QueueEvent(() => _r.Pan(distanceX, distanceY));
            return true;
        }

        public bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY) => false;

        public void OnLongPress(MotionEvent e)
        {
            float x = e.GetX(), y = e.GetY();
            _s.QueueEvent(() => _r.PlaceUserMarker(x, y));
        }

        public void OnShowPress(MotionEvent? e) { }

        public bool OnSingleTapUp(MotionEvent? e) => false;

        public bool OnDoubleTap(MotionEvent e)
        {
            _s.QueueEvent(() => _r.ZoomIn());
            return true;
        }

        public bool OnDoubleTapEvent(MotionEvent? e) => false;

        public bool OnSingleTapConfirmed(MotionEvent? e) => false;
    }

    // ── PinchListener ─────────────────────────────────────────────────────────

    private sealed class PinchListener : Java.Lang.Object, ScaleGestureDetector.IOnScaleGestureListener
    {
        private readonly GLRadarRenderer _r;
        private readonly RadarGLSurface _s;
        private readonly Action _onEnd;

        internal PinchListener(GLRadarRenderer r, RadarGLSurface s, Action onEnd)
        {
            _r = r; _s = s; _onEnd = onEnd;
        }

        public bool OnScaleBegin(ScaleGestureDetector detector)
        {
            _s._isScaling = true;
            return true;
        }

        public bool OnScale(ScaleGestureDetector detector)
        {
            float factor = detector.ScaleFactor;
            _s.QueueEvent(() => _r.AdjustZoom(factor));
            return true;
        }

        public void OnScaleEnd(ScaleGestureDetector detector)
        {
            _s.QueueEvent(() => _r.SnapZoom());
            _onEnd();
        }
    }
}
