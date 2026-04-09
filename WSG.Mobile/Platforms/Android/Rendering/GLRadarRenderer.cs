using Android.Opengl;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using BZTG;
using WeatherShared;
using WSG.Mobile.Controls;
using WSG.Mobile.Services;
using AndroidLog = global::Android.Util.Log;
using BitmapFactory = global::Android.Graphics.BitmapFactory;
using AndroidBitmap = global::Android.Graphics.Bitmap;
using GLUtils = global::Android.Opengl.GLUtils;

namespace WSG.Mobile.Platforms.Android.Rendering;

/// <summary>
/// OpenGL ES 3.0 radar map renderer.
/// Implements GLSurfaceView.IRenderer — called exclusively on the GL thread by Android.
/// Implements IRadarGLRenderer — called from the MAUI main thread (methods queue onto GL thread).
/// Mirrors the render loop architecture of Windows GLRadarControl.cs.
/// </summary>
internal sealed class GLRadarRenderer : Java.Lang.Object, GLSurfaceView.IRenderer, IRadarGLRenderer
{
    // ── Back-reference to virtual view for events ──────────────────────────
    private readonly RadarGLView _virtualView;

    // ── GL geometry ────────────────────────────────────────────────────────
    private int _vao, _vbo, _ebo;     // shared unit quad
    private int _fallbackTex;         // 2×2 gray tile placeholder

    // ── Shaders ────────────────────────────────────────────────────────────
    private GLESShader? _tileShader;
    private GLESShader? _radarShader;
    private GLESShader? _markerShader;
    private GLESShader? _lightningShader;

    // Cached uniform locations (tile shader — hot path)
    private int _tileTransformLoc = -1;
    private int _tileOpacityLoc = -1;
    private int _tileZoomNormLoc = -1;
    private int _tileSatLoc = -1;
    private int _tileContrastLoc = -1;
    private int _tileVignetteLoc = -1;

    // Cached uniform locations (radar overlay shader)
    private int _radTransformLoc = -1;
    private int _radOpacityLoc = -1;
    private int _radTimeLoc = -1;
    private int _radGlowLoc = -1;

    // Cached uniform locations (user marker shader)
    private int _mkNdcXLoc = -1, _mkNdcYLoc = -1;
    private int _mkHalfXLoc = -1, _mkHalfYLoc = -1;
    private int _mkTimeLoc = -1;

    // Cached uniform locations (lightning marker shader)
    private int _lmNdcXLoc = -1, _lmNdcYLoc = -1;
    private int _lmHalfXLoc = -1, _lmHalfYLoc = -1;
    private int _lmAgeLoc = -1, _lmIsCGLoc = -1;
    private int _lmFlashBoostLoc = -1, _lmIsNewLoc = -1;

    // ── Map state ──────────────────────────────────────────────────────────
    private double _centerLat = 56.13;
    private double _centerLon = -106.35;
    private int _mapZoom = 8;         // discrete tile zoom
    private float _smoothZoom = 1.0f; // sub-tile zoom multiplier (1.0 = native)
    private int _viewW, _viewH;

    // ── Tile management ────────────────────────────────────────────────────
    private readonly TileCacheService _tileCache;
    private readonly ConcurrentDictionary<(int z, int x, int y), int> _tileTextures = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), long> _tileLruTick = new();
    private readonly ConcurrentDictionary<(int z, int x, int y), Task> _pendingLoads = new();
    private const int MaxTileTextures = 500;
    private const int PrefetchRadius = 2;

    // ── Radar animation ────────────────────────────────────────────────────
    private readonly HttpClient _radarHttp;
    private RadarFrameBuffer? _frameBuffer;
    private int _activeFrame;
    private int _animSpeedMs = 500;
    private System.Threading.Timer? _animTimer;
    private bool _showRadar = true;
    private float _radarOpacity = 0.7f;
    // Radar overlay bbox (for geo-transform)
    private double _radMinLat, _radMinLon, _radMaxLat, _radMaxLon;
    private bool _hasRadarBbox;

    // ── User location marker ────────────────────────────────────────────────
    private double _userLat, _userLon;
    private bool _showUserMarker;

    // ── Lightning strikes (BZTG) ───────────────────────────────────────────
    private List<LightningFlash> _lightningFlashes = new();
    private readonly HashSet<string> _seenFlashIds = new();
    private readonly HashSet<string> _newFlashIds = new();
    private readonly object _lightningLock = new();
    private System.Threading.Timer? _lightningTimer;
    private float _elapsedSeconds;

    // ── Lightning settings ─────────────────────────────────────────────────
    private bool _lightningEnabled = false;
    private bool _lightningCgEnabled = true;
    private bool _lightningIcEnabled = true;
    private int  _lightningWindowMinutes = 30;
    private int  _lightningPollSeconds   = 60;

    // ── GLSurfaceView reference (for QueueEvent) ────────────────────────────
    private GLSurfaceView? _surface;

    // ── Elapsed time for shader animations ─────────────────────────────────
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public GLRadarRenderer(RadarGLView virtualView, TileCacheService tileCache)
    {
        _virtualView = virtualView;
        _tileCache = tileCache;

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _radarHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _radarHttp.DefaultRequestHeaders.Add("User-Agent",
            "WSGMobile/1.0 (weather radar; contact@noidsoftwork.com)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GLSurfaceView.IRenderer
    // ─────────────────────────────────────────────────────────────────────────

    public void OnSurfaceCreated(Javax.Microedition.Khronos.Opengles.IGL10? gl,
                                 Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        AndroidLog.Debug("GLRadarRenderer", "OnSurfaceCreated — GLES context ready");

        GLES30.GlClearColor(0.12f, 0.12f, 0.12f, 1f);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendFunc(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlDisable(GLES30.GlDepthTest);

        BuildQuadGeometry();
        _fallbackTex = CreateFallbackTexture();
        CompileAllShaders();

        AndroidLog.Debug("GLRadarRenderer", $"[GLRadarRenderer] Shaders compiled. tile={_tileShader?.Handle} radar={_radarShader?.Handle}");

        // Start lightning polling (every 60 s)
        _lightningTimer = new System.Threading.Timer(_ => PollLightningAsync(), null,
                                                     TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public void OnSurfaceChanged(Javax.Microedition.Khronos.Opengles.IGL10? gl, int width, int height)
    {
        _viewW = width;
        _viewH = height;
        GLES30.GlViewport(0, 0, width, height);
        AndroidLog.Debug("GLRadarRenderer", $"OnSurfaceChanged {width}×{height}");
    }

    public void OnDrawFrame(Javax.Microedition.Khronos.Opengles.IGL10? gl)
    {
        _elapsedSeconds = (float)_clock.Elapsed.TotalSeconds;

        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit);

        if (_tileShader == null || _viewW == 0) return;

        DrawMapTiles();

        if (_showRadar && _hasRadarBbox && _frameBuffer != null)
        {
            int tex = _frameBuffer.TextureAt(_activeFrame);
            if (tex != 0) DrawRadarOverlay(tex);
        }

        DrawLightningMarkers();

        if (_showUserMarker) DrawUserMarker();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IRadarGLRenderer — all methods safe to call from main thread
    // ─────────────────────────────────────────────────────────────────────────

    public void SetCenter(double lat, double lon, int zoom)
        => QueueGl(() =>
        {
            _centerLat = lat;
            _centerLon = Math.Max(-180, Math.Min(180, lon));
            _mapZoom = Math.Clamp(zoom, 1, 18);
            _smoothZoom = 1.0f;
            TriggerTilePrefetch();
        });

    public void SetShowRadar(bool show) => QueueGl(() => _showRadar = show);

    public void SetRadarOpacity(float opacity) => QueueGl(() => _radarOpacity = Math.Clamp(opacity, 0f, 1f));

    public void SetUserMarker(double lat, double lon, bool show)
        => QueueGl(() => { _userLat = lat; _userLon = lon; _showUserMarker = show; });

    public void PlayAnimation()
        => QueueGl(() =>
        {
            _animTimer?.Dispose();
            _animTimer = new System.Threading.Timer(_ => AdvanceFrame(), null, 0, _animSpeedMs);
        });

    public void StopAnimation()
        => QueueGl(() =>
        {
            _animTimer?.Dispose();
            _animTimer = null;
        });

    public void LoadFrames(IReadOnlyList<RadarFrameSource> frames)
    {
        if (_surface == null) return;
        _frameBuffer ??= new RadarFrameBuffer(_radarHttp, _surface);
        _frameBuffer.OnFramesLoaded = () => _virtualView.RaiseFrameChanged(0, _frameBuffer.LabelAt(0));

        _ = _frameBuffer.LoadAsync(frames);
    }

    public void SetAnimationSpeed(int millisPerFrame)
        => QueueGl(() =>
        {
            _animSpeedMs = Math.Max(100, millisPerFrame);
            // Restart timer if already playing
            if (_animTimer != null)
            {
                _animTimer.Dispose();
                _animTimer = new System.Threading.Timer(_ => AdvanceFrame(), null, 0, _animSpeedMs);
            }
        });

    public void SetFrameIndex(int index)
        => QueueGl(() =>
        {
            if (_frameBuffer == null) return;
            _activeFrame = Math.Clamp(index, 0, _frameBuffer.Count - 1);
            _virtualView.RaiseFrameChanged(_activeFrame, _frameBuffer.LabelAt(_activeFrame));
        });

    /// <summary>Call once from the handler after platform view is assigned.</summary>
    internal void AttachSurface(GLSurfaceView surface) => _surface = surface;

    // ─────────────────────────────────────────────────────────────────────────
    // Touch input — called by RadarGLSurface (on main/touch thread → pan/zoom via QueueEvent)
    // ─────────────────────────────────────────────────────────────────────────

    internal void Pan(float deltaX, float deltaY)
    {
        // Convert screen pixel delta to geo coordinate delta for the current zoom
        double centerPixX = LonToPixelX(_centerLon, _mapZoom);
        double centerPixY = LatToPixelY(_centerLat, _mapZoom);
        double newPixX = centerPixX - deltaX / _smoothZoom;
        double newPixY = centerPixY - deltaY / _smoothZoom;
        _centerLon = PixelXToLon(newPixX, _mapZoom);
        _centerLat = PixelYToLat(newPixY, _mapZoom);
        _centerLon = Math.Max(-180, Math.Min(180, _centerLon));
        _virtualView.RaiseCoordinatesChanged(_centerLat, _centerLon);
        TriggerTilePrefetch();
    }

    internal void AdjustZoom(float scaleFactor)
    {
        _smoothZoom = Math.Clamp(_smoothZoom * scaleFactor, 0.25f, 4f);
    }

    internal void SnapZoom()
    {
        if (_smoothZoom > 1.5f && _mapZoom < 18) { _mapZoom++; _smoothZoom = 1f; TriggerTilePrefetch(); }
        else if (_smoothZoom < 0.7f && _mapZoom > 1) { _mapZoom--; _smoothZoom = 1f; TriggerTilePrefetch(); }
        else _smoothZoom = 1f;
    }

    internal void ZoomIn() => QueueGl(() => { if (_mapZoom < 18) { _mapZoom++; TriggerTilePrefetch(); } });

    internal void PlaceUserMarker(float screenX, float screenY)
    {
        if (_viewW == 0) return;
        double pixX = LonToPixelX(_centerLon, _mapZoom) + (screenX - _viewW / 2.0) / _smoothZoom;
        double pixY = LatToPixelY(_centerLat, _mapZoom) + (screenY - _viewH / 2.0) / _smoothZoom;
        _userLon = PixelXToLon(pixX, _mapZoom);
        _userLat = PixelYToLat(pixY, _mapZoom);
        _showUserMarker = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw passes
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawMapTiles()
    {
        _tileShader!.Use();

        float zoomNorm = Math.Clamp(_mapZoom / 20f, 0f, 1f);
        GLESShader.SetFloatAt(_tileZoomNormLoc, zoomNorm);
        GLESShader.SetBoolAt(_tileSatLoc, true);
        GLESShader.SetBoolAt(_tileContrastLoc, true);
        GLESShader.SetBoolAt(_tileVignetteLoc, true);
        _tileShader.SetInt("uTexture", 0);

        int z = _mapZoom;
        double cx = LonToPixelX(_centerLon, z);
        double cy = LatToPixelY(_centerLat, z);
        int tilesWide = (int)Math.Ceiling(_viewW / 256.0) + 2;
        int tilesHigh = (int)Math.Ceiling(_viewH / 256.0) + 2;
        int centerTileX = (int)Math.Floor(cx / 256.0);
        int centerTileY = (int)Math.Floor(cy / 256.0);
        int wrap = 1 << z;

        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindVertexArray(_vao);

        for (int dx = -tilesWide / 2; dx <= tilesWide / 2; dx++)
        {
            for (int dy = -tilesHigh / 2; dy <= tilesHigh / 2; dy++)
            {
                int tileX = centerTileX + dx;
                int tileY = centerTileY + dy;
                if (tileY < 0 || tileY >= wrap) continue;
                int wx = ((tileX % wrap) + wrap) % wrap;
                var key = (z, wx, tileY);

                int tex = _fallbackTex;
                if (_tileTextures.TryGetValue(key, out int cached))
                {
                    tex = cached;
                    _tileLruTick[key] = DateTime.UtcNow.Ticks;
                }
                else
                {
                    _ = EnsureTileAsync(z, wx, tileY);
                }

                double tilePx = tileX * 256.0;
                double tilePy = tileY * 256.0;
                double screenCx = (tilePx - cx) + _viewW / 2.0 + 128.0;
                double screenCy = (tilePy - cy) + _viewH / 2.0 + 128.0;

                float tw = (float)(256.0 / (_viewW / 2.0)) * _smoothZoom;
                float th = (float)(256.0 / (_viewH / 2.0)) * _smoothZoom;
                float ndcX = ((float)(screenCx / (_viewW / 2.0) - 1.0)) * _smoothZoom;
                float ndcY = ((float)(1.0 - screenCy / (_viewH / 2.0))) * _smoothZoom;

                // Add 0.5px overlap to eliminate sub-pixel seams between tiles
                float sx = tw / 2f + _smoothZoom / _viewW;
                float sy = th / 2f + _smoothZoom / _viewH;

                float[] tmat = { sx, 0f, 0f, 0f, sy, 0f, ndcX, ndcY, 1f };
                GLESShader.SetMatrix3At(_tileTransformLoc, tmat);
                GLESShader.SetFloatAt(_tileOpacityLoc, 1f);

                GLES30.GlBindTexture(GLES30.GlTexture2d, tex);
                GLES30.GlDrawElements(GLES30.GlTriangles, 6, GLES30.GlUnsignedInt, 0);
            }
        }

        GLES30.GlBindVertexArray(0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
    }

    private void DrawRadarOverlay(int tex)
    {
        _radarShader!.Use();
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        _radarShader.SetInt("uTexture", 0);

        GLESShader.SetFloatAt(_radOpacityLoc, _radarOpacity);
        GLESShader.SetFloatAt(_radTimeLoc, _elapsedSeconds);
        GLESShader.SetBoolAt(_radGlowLoc, true);

        // Compute geo-transform from bbox
        float[] mat = GeoTransformMatrix(_radMinLat, _radMinLon, _radMaxLat, _radMaxLon);
        GLESShader.SetMatrix3At(_radTransformLoc, mat);

        GLES30.GlBindVertexArray(_vao);
        GLES30.GlBindTexture(GLES30.GlTexture2d, tex);
        GLES30.GlDrawElements(GLES30.GlTriangles, 6, GLES30.GlUnsignedInt, 0);
        GLES30.GlBindVertexArray(0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
    }

    private void DrawLightningMarkers()
    {
        if (_lightningShader == null) return;
        if (!_lightningEnabled) return;

        List<LightningFlash> flashes;
        HashSet<string> newIds;
        lock (_lightningLock)
        {
            flashes = _lightningFlashes;
            newIds = _newFlashIds;
        }
        if (flashes.Count == 0) return;

        _lightningShader.Use();
        GLES30.GlBindVertexArray(_vao);

        float markerHalfPx = 18f; // marker radius in screen pixels
        float halfX = markerHalfPx / (_viewW / 2f);
        float halfY = markerHalfPx / (_viewH / 2f);

        var now = DateTime.UtcNow;
        double windowMin = Math.Max(5, _lightningWindowMinutes);

        foreach (var flash in flashes)
        {
            // Respect CG/IC filter
            if (!_lightningCgEnabled && flash.StrikeType == LightningStrikeType.CloudToGround) continue;
            if (!_lightningIcEnabled && flash.StrikeType == LightningStrikeType.InCloud) continue;

            double age = (now - flash.Time).TotalMinutes / windowMin; // 0 = new, 1 = outside window
            if (age > 1.0) continue;

            (float ndcX, float ndcY) = GeoToNdc(flash.Latitude, flash.Longitude);
            // Cull markers outside viewport + small margin
            if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f) continue;

            float isCg = flash.StrikeType == LightningStrikeType.CloudToGround ? 1f : 0f;
            string id = $"{flash.Time.Ticks}_{flash.Latitude:F4}_{flash.Longitude:F4}";
            bool isNew = newIds.Contains(id);
            // flashBoost: 1 → 0 over first 3s of life
            float flashBoost = isNew ? Math.Max(0f, 1f - (float)(now - flash.Time).TotalSeconds / 3f) : 0f;

            GLESShader.SetFloatAt(_lmNdcXLoc, ndcX);
            GLESShader.SetFloatAt(_lmNdcYLoc, ndcY);
            GLESShader.SetFloatAt(_lmHalfXLoc, halfX);
            GLESShader.SetFloatAt(_lmHalfYLoc, halfY);
            GLESShader.SetFloatAt(_lmAgeLoc, (float)age);
            GLESShader.SetFloatAt(_lmIsCGLoc, isCg);
            GLESShader.SetFloatAt(_lmFlashBoostLoc, flashBoost);
            GLESShader.SetFloatAt(_lmIsNewLoc, isNew ? 1f : 0f);

            GLES30.GlDrawElements(GLES30.GlTriangles, 6, GLES30.GlUnsignedInt, 0);
        }

        GLES30.GlBindVertexArray(0);
    }

    private void DrawUserMarker()
    {
        if (_markerShader == null) return;

        (float ndcX, float ndcY) = GeoToNdc(_userLat, _userLon);
        if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f) return;

        float halfX = 20f / (_viewW / 2f);
        float halfY = 20f / (_viewH / 2f);

        _markerShader.Use();
        GLES30.GlBindVertexArray(_vao);
        GLESShader.SetFloatAt(_mkNdcXLoc, ndcX);
        GLESShader.SetFloatAt(_mkNdcYLoc, ndcY);
        GLESShader.SetFloatAt(_mkHalfXLoc, halfX);
        GLESShader.SetFloatAt(_mkHalfYLoc, halfY);
        GLESShader.SetFloatAt(_mkTimeLoc, _elapsedSeconds);
        GLES30.GlDrawElements(GLES30.GlTriangles, 6, GLES30.GlUnsignedInt, 0);
        GLES30.GlBindVertexArray(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tile loading pipeline
    // ─────────────────────────────────────────────────────────────────────────

    private Task EnsureTileAsync(int z, int x, int y)
    {
        var key = (z, x, y);
        return _pendingLoads.GetOrAdd(key, tileKey =>
        {
            var loadTask = LoadTileAsync(tileKey.Item1, tileKey.Item2, tileKey.Item3);
            return loadTask.ContinueWith(completed =>
            {
                Task dummy;
                _pendingLoads.TryRemove(tileKey, out dummy!);
            }, TaskScheduler.Default);
        });
    }

    private async Task LoadTileAsync(int z, int x, int y)
    {
        byte[]? data = await _tileCache.GetTileAsync(z, x, y).ConfigureAwait(false);
        if (data == null) return;

        _surface?.QueueEvent(() =>
        {
            int tex = UploadPng(data);
            if (tex == 0) return;
            _tileTextures[(z, x, y)] = tex;
            _tileLruTick[(z, x, y)] = DateTime.UtcNow.Ticks;
            EvictOldTiles();
        });
    }

    private static int UploadPng(byte[] pngBytes)
    {
        var opts = new BitmapFactory.Options
        {
            InPreferredConfig = AndroidBitmap.Config.Argb8888
        };
        var bmp = BitmapFactory.DecodeByteArray(pngBytes, 0, pngBytes.Length, opts);
        if (bmp == null) return 0;

        int[] texArr = new int[1];
        GLES30.GlGenTextures(1, texArr, 0);
        int tex = texArr[0];
        if (tex == 0) { bmp.Recycle(); return 0; }

        GLES30.GlBindTexture(GLES30.GlTexture2d, tex);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
        GLUtils.TexImage2D(GLES30.GlTexture2d, 0, bmp, 0);

        bmp.Recycle();
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        return tex;
    }

    private void EvictOldTiles()
    {
        if (_tileTextures.Count <= MaxTileTextures) return;
        var oldest = _tileLruTick.OrderBy(kv => kv.Value).Take(50).Select(kv => kv.Key).ToList();
        foreach (var k in oldest)
        {
            if (_tileTextures.TryRemove(k, out int tex))
                GLES30.GlDeleteTextures(1, new[] { tex }, 0);
            _tileLruTick.TryRemove(k, out _);
        }
    }

    private void TriggerTilePrefetch()
    {
        int z = _mapZoom;
        double cx = LonToPixelX(_centerLon, z);
        double cy = LatToPixelY(_centerLat, z);
        int cTileX = (int)Math.Floor(cx / 256.0);
        int cTileY = (int)Math.Floor(cy / 256.0);
        int wrap = 1 << z;

        for (int dx = -PrefetchRadius; dx <= PrefetchRadius; dx++)
            for (int dy = -PrefetchRadius; dy <= PrefetchRadius; dy++)
            {
                int ty = cTileY + dy;
                if (ty < 0 || ty >= wrap) continue;
                int tx = ((cTileX + dx) % wrap + wrap) % wrap;
                if (!_tileTextures.ContainsKey((z, tx, ty)))
                    _ = EnsureTileAsync(z, tx, ty);
            }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lightning polling
    // ─────────────────────────────────────────────────────────────────────────

    private async void PollLightningAsync()
    {
        if (!_lightningEnabled) return; // skip if disabled to save battery/network

        try
        {
            var bbox = CurrentBbox();
            var from = DateTime.UtcNow.AddMinutes(-Math.Max(5, _lightningWindowMinutes));
            var to   = DateTime.UtcNow;
            var flashes = await BZTGApi.GetLightningStrikesAsync(_radarHttp, bbox, from, to, 2000)
                                       .ConfigureAwait(false);

            lock (_lightningLock)
            {
                _newFlashIds.Clear();
                foreach (var f in flashes)
                {
                    string id = $"{f.Time.Ticks}_{f.Latitude:F4}_{f.Longitude:F4}";
                    if (!_seenFlashIds.Contains(id))
                    {
                        _seenFlashIds.Add(id);
                        _newFlashIds.Add(id);
                    }
                }
                // Evict IDs older than window+5 min to avoid unbounded growth
                double evictMins = _lightningWindowMinutes + 5;
                _seenFlashIds.RemoveWhere(id =>
                {
                    if (id.Length > 0 && long.TryParse(id.Split('_')[0], out long ticks))
                        return (DateTime.UtcNow - new DateTime(ticks)).TotalMinutes > evictMins;
                    return false;
                });
                _lightningFlashes = flashes;
            }
        }
        catch (Exception ex)
        {
            AndroidLog.Warn("GLRadarRenderer", $"Lightning poll failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Animation
    // ─────────────────────────────────────────────────────────────────────────

    private void AdvanceFrame()
    {
        _surface?.QueueEvent(() =>
        {
            if (_frameBuffer == null || _frameBuffer.Count == 0) return;
            _activeFrame = (_activeFrame + 1) % _frameBuffer.Count;
            _virtualView.RaiseFrameChanged(_activeFrame, _frameBuffer.LabelAt(_activeFrame));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Geo / Mercator math — mirrors Windows MapCoordinates.cs
    // ─────────────────────────────────────────────────────────────────────────

    private static double LonToPixelX(double lon, int zoom)
        => (lon + 180.0) / 360.0 * 256.0 * (1 << zoom);

    private static double LatToPixelY(double lat, int zoom)
    {
        double sinLat = Math.Sin(lat * Math.PI / 180.0);
        sinLat = Math.Clamp(sinLat, -0.9999, 0.9999);
        double y = 0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4 * Math.PI);
        return y * 256.0 * (1 << zoom);
    }

    private static double PixelXToLon(double pixX, int zoom)
        => pixX / (256.0 * (1 << zoom)) * 360.0 - 180.0;

    private static double PixelYToLat(double pixY, int zoom)
    {
        double n = Math.PI - 2 * Math.PI * pixY / (256.0 * (1 << zoom));
        return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
    }

    /// <summary>Returns NDC (-1..1) position of a geo coordinate relative to current view.</summary>
    private (float x, float y) GeoToNdc(double lat, double lon)
    {
        int z = _mapZoom;
        double cx = LonToPixelX(_centerLon, z);
        double cy = LatToPixelY(_centerLat, z);
        double px = LonToPixelX(lon, z);
        double py = LatToPixelY(lat, z);
        double screenX = (px - cx) + _viewW / 2.0;
        double screenY = (py - cy) + _viewH / 2.0;
        float ndcX = ((float)(screenX / (_viewW / 2.0) - 1.0)) * _smoothZoom;
        float ndcY = ((float)(1.0 - screenY / (_viewH / 2.0))) * _smoothZoom;
        return (ndcX, ndcY);
    }

    /// <summary>Build a 3×3 column-major transform matrix to map a geo bbox to NDC.</summary>
    private float[] GeoTransformMatrix(double minLat, double minLon, double maxLat, double maxLon)
    {
        (float nMinX, float nMinY) = GeoToNdc(minLat, minLon);
        (float nMaxX, float nMaxY) = GeoToNdc(maxLat, maxLon);
        float sx = (nMaxX - nMinX) / 2f;
        float sy = (nMaxY - nMinY) / 2f;
        float tx = (nMinX + nMaxX) / 2f;
        float ty = (nMinY + nMaxY) / 2f;
        return new float[] { sx, 0f, 0f, 0f, sy, 0f, tx, ty, 1f };
    }

    private (double MinLat, double MinLon, double MaxLat, double MaxLon) CurrentBbox()
    {
        double latOff = 200.0 / 111.32;
        double lonOff = 200.0 / (111.32 * Math.Cos(_centerLat * Math.PI / 180));
        return (_centerLat - latOff, _centerLon - lonOff, _centerLat + latOff, _centerLon + lonOff);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GL initialisation helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildQuadGeometry()
    {
        // Unit quad: positions [-1,1] and UVs [0,1]
        float[] vertices = {
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        };
        int[] indices = { 0, 1, 2, 2, 3, 0 };

        int[] vaoBuf = new int[1]; GLES30.GlGenVertexArrays(1, vaoBuf, 0); _vao = vaoBuf[0];
        int[] vboBuf = new int[1]; GLES30.GlGenBuffers(1, vboBuf, 0); _vbo = vboBuf[0];
        int[] eboBuf = new int[1]; GLES30.GlGenBuffers(1, eboBuf, 0); _ebo = eboBuf[0];

        GLES30.GlBindVertexArray(_vao);

        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vbo);
        using var vb = CreateFloatBuffer(vertices);
        GLES30.GlBufferData(GLES30.GlArrayBuffer, vertices.Length * 4, vb, GLES30.GlStaticDraw);

        GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, _ebo);
        using var ib = CreateIntBuffer(indices);
        GLES30.GlBufferData(GLES30.GlElementArrayBuffer, indices.Length * 4, ib, GLES30.GlStaticDraw);

        // aPos @ location 0 (vec2, stride 16 bytes, offset 0)
        GLES30.GlEnableVertexAttribArray(0);
        GLES30.GlVertexAttribPointer(0, 2, GLES30.GlFloat, false, 4 * 4, 0);
        // aTex @ location 1 (vec2, stride 16 bytes, offset 8)
        GLES30.GlEnableVertexAttribArray(1);
        GLES30.GlVertexAttribPointer(1, 2, GLES30.GlFloat, false, 4 * 4, 2 * 4);

        GLES30.GlBindVertexArray(0);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, 0);
        GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, 0);
    }

    private static int CreateFallbackTexture()
    {
        // 2×2 mid-gray placeholder while tiles load
        byte[] pixels = new byte[2 * 2 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 48; pixels[i + 1] = 48; pixels[i + 2] = 48; pixels[i + 3] = 255; }

        int[] texArr = new int[1];
        GLES30.GlGenTextures(1, texArr, 0);
        int tex = texArr[0];
        GLES30.GlBindTexture(GLES30.GlTexture2d, tex);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlNearest);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlNearest);

        using var bb = Java.Nio.ByteBuffer.Wrap(pixels)!;
        GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, 2, 2, 0,
                            GLES30.GlRgba, GLES30.GlUnsignedByte, bb);
        GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
        return tex;
    }

    private void CompileAllShaders()
    {
        // ── Tile shader ──────────────────────────────────────────────────────
        _tileShader = new GLESShader(TileVert, TileFrag);
        _tileTransformLoc = _tileShader.CacheLoc("uTransform");
        _tileOpacityLoc   = _tileShader.CacheLoc("uOpacity");
        _tileZoomNormLoc  = _tileShader.CacheLoc("uZoomNorm");
        _tileSatLoc       = _tileShader.CacheLoc("uEnableSaturation");
        _tileContrastLoc  = _tileShader.CacheLoc("uEnableContrast");
        _tileVignetteLoc  = _tileShader.CacheLoc("uEnableVignette");
        _tileShader.Use();
        _tileShader.SetInt("uTexture", 0);

        // ── Radar overlay shader ──────────────────────────────────────────────
        _radarShader = new GLESShader(RadarVert, RadarFrag);
        _radTransformLoc = _radarShader.CacheLoc("uTransform");
        _radOpacityLoc   = _radarShader.CacheLoc("uOpacity");
        _radTimeLoc      = _radarShader.CacheLoc("uTime");
        _radGlowLoc      = _radarShader.CacheLoc("uEnableGlow");
        _radarShader.Use();
        _radarShader.SetInt("uTexture", 0);

        // ── User location marker shader ───────────────────────────────────────
        _markerShader = new GLESShader(MarkerVert, MarkerFrag);
        _mkNdcXLoc  = _markerShader.CacheLoc("uNdcX");
        _mkNdcYLoc  = _markerShader.CacheLoc("uNdcY");
        _mkHalfXLoc = _markerShader.CacheLoc("uHalfSizeX");
        _mkHalfYLoc = _markerShader.CacheLoc("uHalfSizeY");
        _mkTimeLoc  = _markerShader.CacheLoc("uTime");

        // ── Lightning marker shader ───────────────────────────────────────────
        _lightningShader = new GLESShader(LightningVert, LightningFrag);
        _lmNdcXLoc      = _lightningShader.CacheLoc("uNdcX");
        _lmNdcYLoc      = _lightningShader.CacheLoc("uNdcY");
        _lmHalfXLoc     = _lightningShader.CacheLoc("uHalfSizeX");
        _lmHalfYLoc     = _lightningShader.CacheLoc("uHalfSizeY");
        _lmAgeLoc       = _lightningShader.CacheLoc("uAge");
        _lmIsCGLoc      = _lightningShader.CacheLoc("uIsCG");
        _lmFlashBoostLoc = _lightningShader.CacheLoc("uFlashBoost");
        _lmIsNewLoc     = _lightningShader.CacheLoc("uIsNew");
    }

    // ── NIO buffer helpers ────────────────────────────────────────────────────

    private static Java.Nio.FloatBuffer CreateFloatBuffer(float[] data)
    {
        var bb = Java.Nio.ByteBuffer.AllocateDirect(data.Length * 4)!;
        bb.Order(Java.Nio.ByteOrder.NativeOrder()!);
        var fb = bb.AsFloatBuffer()!;
        fb.Put(data);
        fb.Position(0);
        return fb;
    }

    private static Java.Nio.IntBuffer CreateIntBuffer(int[] data)
    {
        var bb = Java.Nio.ByteBuffer.AllocateDirect(data.Length * 4)!;
        bb.Order(Java.Nio.ByteOrder.NativeOrder()!);
        var ib = bb.AsIntBuffer()!;
        ib.Put(data);
        ib.Position(0);
        return ib;
    }

    // ── Queue onto GL thread ──────────────────────────────────────────────────

    private void QueueGl(Action action)
    {
        if (_surface != null) _surface.QueueEvent(action);
        else action(); // surface not yet assigned — run inline (rare, pre-init)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Radar bbox access for external callers (RadarPage sets this after load)
    // ─────────────────────────────────────────────────────────────────────────

    public void SetRadarBbox(double minLat, double minLon, double maxLat, double maxLon)
        => QueueGl(() =>
        {
            _radMinLat = minLat; _radMinLon = minLon;
            _radMaxLat = maxLat; _radMaxLon = maxLon;
            _hasRadarBbox = true;
        });

    // ─────────────────────────────────────────────────────────────────────────
    // Map style
    // ─────────────────────────────────────────────────────────────────────────

    public void SetMapStyle(string style)
    {
        // Tile cache style change is thread-safe; no GL work required.
        _tileCache.SetMapStyle(style);
        // Flush existing tile textures so the new style is fetched on next draw.
        QueueGl(() =>
        {
            foreach (var kv in _tileTextures)
                GLES30.GlDeleteTextures(1, new[] { kv.Value }, 0);
            _tileTextures.Clear();
            _tileLruTick.Clear();
            TriggerTilePrefetch();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lightning controls
    // ─────────────────────────────────────────────────────────────────────────

    public void SetLightningEnabled(bool enabled)
    {
        _lightningEnabled = enabled;
        if (!enabled)
        {
            lock (_lightningLock) { _lightningFlashes = []; _newFlashIds.Clear(); }
        }
        else
        {
            // Kick off an immediate poll when the user enables lightning.
            PollLightningAsync();
        }
    }

    public void SetLightningCg(bool enabled)
        => _lightningCgEnabled = enabled;

    public void SetLightningIc(bool enabled)
        => _lightningIcEnabled = enabled;

    public void SetLightningWindowMinutes(int minutes)
        => _lightningWindowMinutes = Math.Clamp(minutes, 5, 60);

    public void SetLightningPollIntervalSeconds(int seconds)
    {
        _lightningPollSeconds = Math.Max(15, seconds);
        // Restart the timer with the new interval.
        _lightningTimer?.Dispose();
        _lightningTimer = new System.Threading.Timer(
            _ => PollLightningAsync(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(_lightningPollSeconds));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reload (force re-fetch all frames — used when layer changes)
    // ─────────────────────────────────────────────────────────────────────────

    public void ReloadRadarFrames(IReadOnlyList<RadarFrameSource> frames)
    {
        QueueGl(() =>
        {
            // Dispose existing frame buffer so textures are freed.
            _frameBuffer = null;
            _activeFrame = 0;
            _hasRadarBbox = false;
        });
        LoadFrames(frames);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Embedded shader sources — GLSL ES 300
    // ─────────────────────────────────────────────────────────────────────────

    private const string TileVert = @"#version 300 es
precision mediump float;
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat3 uTransform;
out vec2 vTex;
out vec2 vScreenPos;
void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}";

    private const string TileFrag = @"#version 300 es
precision mediump float;
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uZoomNorm;
uniform bool uEnableSaturation;
uniform bool uEnableContrast;
uniform bool uEnableVignette;
void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    vec3 result = c.rgb;
    if (uEnableSaturation) {
        float luma = dot(result, vec3(0.2126, 0.7152, 0.0722));
        result = mix(vec3(luma), result, 1.12);
    }
    if (uEnableContrast) {
        result = clamp(result * 1.05 - 0.025, 0.0, 1.0);
    }
    if (uEnableVignette) {
        float dist = length(vScreenPos);
        float vignette = smoothstep(1.6, 0.4, dist);
        result *= mix(0.6, 1.0, vignette);
    }
    FragColor = vec4(result, c.a * opacity);
}";

    private const string RadarVert = @"#version 300 es
precision mediump float;
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat3 uTransform;
out vec2 vTex;
out vec2 vScreenPos;
void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
    vScreenPos = p.xy;
}";

    private const string RadarFrag = @"#version 300 es
precision mediump float;
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uTime;
uniform bool uEnableGlow;
void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    float border = 0.012;
    float edgeFade = smoothstep(0.0, border, uv.x) * smoothstep(0.0, border, 1.0 - uv.x)
                   * smoothstep(0.0, border, uv.y) * smoothstep(0.0, border, 1.0 - uv.y);
    vec3 finalColor = c.rgb;
    if (uEnableGlow) {
        vec2 ts = 1.0 / vec2(512.0);
        float b = 2.5;
        float s = dot(texture(uTexture, uv + vec2(ts.x*b, 0.0)).rgb, vec3(0.333))
                + dot(texture(uTexture, uv - vec2(ts.x*b, 0.0)).rgb, vec3(0.333))
                + dot(texture(uTexture, uv + vec2(0.0, ts.y*b)).rgb, vec3(0.333))
                + dot(texture(uTexture, uv - vec2(0.0, ts.y*b)).rgb, vec3(0.333));
        float g = smoothstep(0.3, 0.8, s * 0.25) * 0.15;
        finalColor = mix(c.rgb, c.rgb * (1.0 + g), step(0.05, dot(c.rgb, vec3(0.299, 0.587, 0.114))));
    }
    FragColor = vec4(finalColor, c.a * opacity * edgeFade);
}";

    // User location marker — blue pulsing SDF circle
    private const string MarkerVert = @"#version 300 es
precision highp float;
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform float uNdcX;
uniform float uNdcY;
uniform float uHalfSizeX;
uniform float uHalfSizeY;
out vec2 vUv;
void main() {
    vec2 uv = aTex * 2.0 - 1.0;
    vUv = uv;
    gl_Position = vec4(uNdcX + uv.x * uHalfSizeX, uNdcY + uv.y * uHalfSizeY, 0.0, 1.0);
}";

    private const string MarkerFrag = @"#version 300 es
precision highp float;
in vec2 vUv;
out vec4 FragColor;
uniform float uTime;
void main() {
    float r = length(vUv);
    // Outer pulse ring
    float pulse = 0.65 + 0.2 * sin(uTime * 3.0);
    float ring = smoothstep(pulse + 0.08, pulse + 0.02, r) * smoothstep(pulse - 0.08, pulse - 0.02, r);
    // Inner solid dot
    float dot_ = smoothstep(0.28, 0.20, r);
    // White outline
    float outline = smoothstep(0.30, 0.22, r) * (1.0 - smoothstep(0.22, 0.14, r));
    vec3 blue = vec3(0.15, 0.47, 0.96);
    vec3 col = mix(blue, vec3(1.0), outline);
    float alpha = clamp(dot_ + ring * 0.55 + outline * 0.8, 0.0, 1.0);
    if (alpha < 0.01) discard;
    FragColor = vec4(col, alpha);
}";

    // Lightning marker — direct ES 300 port of Windows lightning_marker shaders
    private const string LightningVert = @"#version 300 es
precision highp float;
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform float uNdcX;
uniform float uNdcY;
uniform float uHalfSizeX;
uniform float uHalfSizeY;
uniform float uAge;
uniform float uIsCG;
out vec2  vUv;
out float vAge;
out float vIsCG;
void main() {
    vec2 uv = aTex * 2.0 - 1.0;
    vUv   = uv;
    vAge  = uAge;
    vIsCG = uIsCG;
    gl_Position = vec4(uNdcX + uv.x * uHalfSizeX,
                       uNdcY + uv.y * uHalfSizeY,
                       0.0, 1.0);
}";

    private const string LightningFrag = @"#version 300 es
precision highp float;
in  vec2  vUv;
in  float vAge;
in  float vIsCG;
out vec4  FragColor;
uniform float uFlashBoost;
uniform float uIsNew;
void main() {
    float r = length(vUv);
    vec3 cgColor = vec3(1.00, 0.843, 0.251);
    vec3 icColor = vec3(0.251, 0.784, 1.00);
    vec3 baseColor = mix(icColor, cgColor, vIsCG);
    vec3 ageColor = mix(vec3(1.0), baseColor, smoothstep(0.0, 0.25, vAge));
    float flashAmt = uIsNew * uFlashBoost;
    float ageFactor = mix(1.0, 0.10, vAge) * (1.0 + flashAmt * (1.0 - vAge) * 3.0);
    float coreR = 0.22;
    float coreA = smoothstep(coreR + 0.06, coreR - 0.06, r);
    float spec  = smoothstep(0.14, 0.0, length(vUv - vec2(-0.07, 0.08))) * 0.45;
    float glowA = exp(-max(r - coreR, 0.0) * 6.5) * 0.70;
    float rayMask = 0.0;
    if (r > 0.01 && r < 0.85) {
        vec2 u = vUv / r;
        float ray = max(pow(abs(u.x), 18.0), pow(abs(u.y), 18.0));
        float fade = (1.0 - smoothstep(0.25, 0.80, r));
        rayMask = ray * fade * 0.55;
    }
    float alpha = clamp(max(coreA, glowA * 0.45) + rayMask, 0.0, 1.0);
    vec3 color  = min(vec3(1.0), ageColor + spec * 0.35);
    if (alpha * ageFactor < 0.01) discard;
    FragColor = vec4(color * ageFactor, alpha * ageFactor);
}";
}
