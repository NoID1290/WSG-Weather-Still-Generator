using Android.Opengl;
using System.Net.Http;
using AndroidLog = global::Android.Util.Log;
using BitmapFactory = global::Android.Graphics.BitmapFactory;
using AndroidBitmap = global::Android.Graphics.Bitmap;
using GLUtils = global::Android.Opengl.GLUtils;

namespace WSG.Mobile.Platforms.Android.Rendering;

/// <summary>
/// Manages the 15 GL textures that back radar animation frames.
/// Downloads WMS PNG images in parallel and uploads them to the GPU.
/// Must be disposed when the GL context is torn down.
/// </summary>
internal sealed class RadarFrameBuffer : IDisposable
{
    public const int MaxFrames = 15;

    private readonly int[] _textures = new int[MaxFrames];
    private readonly string[] _labels = new string[MaxFrames];
    private int _count;
    private bool _loading;

    private readonly HttpClient _http;
    private readonly global::Android.Opengl.GLSurfaceView _glSurface;

    public int Count => _count;
    public bool IsLoading => _loading;

    /// <summary>Callback invoked on the GL thread when all frames are ready.</summary>
    public Action? OnFramesLoaded;

    public RadarFrameBuffer(HttpClient http, global::Android.Opengl.GLSurfaceView glSurface)
    {
        _http = http;
        _glSurface = glSurface;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Returns the GL texture ID for frame index (0 = oldest), or 0 if not loaded.</summary>
    public int TextureAt(int index) => (index >= 0 && index < _count) ? _textures[index] : 0;

    /// <summary>Returns the HH:mm display label for this frame.</summary>
    public string LabelAt(int index) => (index >= 0 && index < _count) ? (_labels[index] ?? "") : "";

    /// <summary>
    /// Download up to MaxFrames WMS images from the provided URLs and upload them to the GPU.
    /// Deletes any previously loaded textures first.
    /// This method returns immediately; the upload happens asynchronously on background + GL threads.
    /// </summary>
    public async Task LoadAsync(IReadOnlyList<WSG.Mobile.Controls.RadarFrameSource> frames)
    {
        if (_loading) return;
        _loading = true;

        // Free old textures on GL thread first
        _glSurface.QueueEvent(() =>
        {
            FreeTextures();
            _count = 0;
        });

        int toLoad = Math.Min(frames.Count, MaxFrames);

        // Download all frames concurrently (max 4 at a time)
        using var sem = new SemaphoreSlim(4, 4);
        var downloadTasks = new Task<(int idx, byte[]? data)>[toLoad];

        for (int i = 0; i < toLoad; i++)
        {
            int idx = i;
            string url = frames[i].WmsUrl;
            string label = frames[i].DisplayLabel;
            _labels[idx] = label;

            downloadTasks[i] = Task.Run(async () =>
            {
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    byte[] data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                    return (idx, data.Length > 0 ? data : null);
                }
                catch
                {
                    return (idx, (byte[]?)null);
                }
                finally
                {
                    sem.Release();
                }
            });
        }

        var results = await Task.WhenAll(downloadTasks).ConfigureAwait(false);

        // Upload to GPU on GL thread
        _glSurface.QueueEvent(() =>
        {
            int loaded = 0;
            foreach (var (idx, data) in results)
            {
                int tex = 0;
                if (data != null) tex = UploadPng(data);
                _textures[idx] = tex;
                if (tex != 0) loaded++;
            }
            _count = toLoad;
            _loading = false;
            AndroidLog.Debug("GLRadarRenderer", $"[RadarFrameBuffer] {loaded}/{toLoad} frames uploaded to GPU");
            OnFramesLoaded?.Invoke();
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // GL helpers — must be called on the GL thread
    // ─────────────────────────────────────────────────────────────────────

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

    private void FreeTextures()
    {
        for (int i = 0; i < MaxFrames; i++)
        {
            if (_textures[i] != 0)
            {
                GLES30.GlDeleteTextures(1, new[] { _textures[i] }, 0);
                _textures[i] = 0;
            }
        }
    }

    public void Dispose() => FreeTextures();
}
