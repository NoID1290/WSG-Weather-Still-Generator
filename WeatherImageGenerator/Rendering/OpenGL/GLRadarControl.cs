using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Net.Http;
using System.Linq;
using OpenTK.WinForms;
using OpenTK.Graphics.OpenGL4;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.OpenGL
{
    public class GLRadarControl : GLControl, IMapRenderer
    {
        private int _vao;
        private int _vbo;
        private int _ebo;
        private int _texture = 0;
        private GLShader? _shader;

        // Overlay resources (crosshair, markers)
        private GLShader? _overlayShader;
        private int _overlayVao = 0;
        private int _overlayVbo = 0;

        // GL-native HUD text renderer for attribution + status
        private GLTextRenderer? _uiRenderer;
        private string _hudAttributionText = "";
        private string _hudStatusText = "";

        // Interactive HUD overlay system â€” all map controls rendered in-viewport
        private HudSystem? _hudSystem;

        // ═══ IMapRenderer implementation ═══

        /// <summary>The WinForms Control that hosts the OpenGL viewport.</summary>
        public Control HostControl => this;

        /// <summary>Request a repaint of the viewport.</summary>
        public void InvalidateView() => Invalidate();

        /// <summary>The interactive HUD overlay system for in-viewport controls</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HudSystem? HudSystem
        {
            get => _hudSystem;
            set { _hudSystem = value; Invalidate(); }
        }

        /// <summary>Whether to show the crosshair/aiming dot in the center of the viewport</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowCrosshair { get; set; } = true;

        /// <summary>When true, hides system cursor and renders green crosshair at mouse position with lat/lon tooltip</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool UseCrosshairAsMouse { get; set; } = true;

        /// <summary>Whether to show lat/lon coordinates near the crosshair</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowCoordinatesHUD { get; set; } = true;

        // Current mouse screen position for crosshair-as-mouse rendering
        private System.Drawing.Point _mouseScreenPos = new System.Drawing.Point(-1, -1);
        private bool _mouseInside = false;

        // Loading state â€” shown until first map tiles are rendered
        private bool _mapLoading = true;

        // User location marker
        private double _userMarkerLat = 0;
        private double _userMarkerLon = 0;
        private bool _showUserMarker = false;

        /// <summary>Latitude of user location marker</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double UserMarkerLat { get => _userMarkerLat; set { _userMarkerLat = value; Invalidate(); } }

        /// <summary>Longitude of user location marker</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double UserMarkerLon { get => _userMarkerLon; set { _userMarkerLon = value; Invalidate(); } }

        /// <summary>Whether to show the blue user location marker on the map</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowUserMarker { get => _showUserMarker; set { _showUserMarker = value; Invalidate(); } }



        // Invisible cursor for crosshair-as-mouse mode
        private static readonly Cursor _blankCursor = CreateBlankCursor();
        private static Cursor CreateBlankCursor()
        {
            var bmp = new System.Drawing.Bitmap(1, 1);
            bmp.SetPixel(0, 0, System.Drawing.Color.Transparent);
            return new Cursor(bmp.GetHicon());
        }

        // Always-visible status bar text (single row, bottom-right)
        private string _hudStatusBarText = "";
        /// <summary>Single-row status bar text rendered at bottom-right (always visible)</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string HudStatusBarText
        {
            get => _hudStatusBarText;
            set { _hudStatusBarText = value ?? ""; Invalidate(); }
        }

        /// <summary>Attribution text rendered as GL HUD in the bottom-left corner of the viewport</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string HudAttributionText
        {
            get => _hudAttributionText;
            set { _hudAttributionText = value ?? ""; Invalidate(); }
        }

        /// <summary>Status/frame info rendered as GL HUD in the bottom-center of the viewport</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string HudStatusText
        {
            get => _hudStatusText;
            set { _hudStatusText = value ?? ""; Invalidate(); }
        }

        // Map tile support
        public TileProvider? ActiveTileProvider => _tileProvider;
        /// <summary>Total number of GL textures currently allocated (tiles + overlays + frames + background)</summary>
        public int VramTextureCount
        {
            get
            {
                int count = _tileTextures.Count;
                if (_overlayTexture != 0) count++;
                if (_overlay2Texture != 0) count++;
                if (_texture != 0) count++;
                if (_fallbackTexture != 0) count++;
                count += _radarFrames.Count;
                return count;
            }
        }

        /// <summary>Estimated VRAM usage in bytes for all tracked GL textures</summary>
        public long VramEstimatedBytes
        {
            get
            {
                // Tiles: 256x256x4 = 256KB each
                long bytes = (long)_tileTextures.Count * 256 * 256 * 4;
                // Overlays and frames are variable-size; estimate from stored dimensions
                if (_overlayTexture != 0) bytes += 1024L * 1024 * 4; // ~4MB estimate for overlay
                if (_overlay2Texture != 0) bytes += 1024L * 1024 * 4;
                if (_texture != 0) bytes += (long)_bgPixelWidth * _bgPixelHeight * 4;
                if (_fallbackTexture != 0) bytes += 256L * 256 * 4;
                bytes += (long)_radarFrames.Count * 1024 * 1024 * 4; // estimate per frame
                return bytes;
            }
        }
        private TileProvider? _tileProvider;
        private string? _localTileFolder = null;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), int> _tileTextures = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), int>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), long> _tileLastUsed = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), long>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), DateTime> _blockedTiles = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), DateTime>();
        /// <summary>Limits concurrent tile HTTP requests to avoid hammering the server.</summary>
        private static readonly System.Threading.SemaphoreSlim _tileSemaphore = new System.Threading.SemaphoreSlim(14, 14);
        /// <summary>Blocked-tile TTL â€“ re-attempt after this interval.</summary>
        private static readonly TimeSpan BlockedTileTtl = TimeSpan.FromMinutes(2);
        private HttpClient _tileHttpClient = new HttpClient();
        private int _mapZoom = 4; // tile zoom (default to show Canada)
        private double _centerLat = 56.1304; // Canada centroid latitude
        private double _centerLon = -106.3468; // Canada centroid longitude
        private int _fallbackTexture = 0;

        // Background composite metadata (used so composite pans/zooms with map)
        private double _bgCenterLat = 0.0;
        private double _bgCenterLon = 0.0;
        private int _bgSourceZoom = 0;
        private int _bgPixelWidth = 0;
        private int _bgPixelHeight = 0;

        // Positioned overlay texture (transparent radar/weather that draws on top of tiles)
        private int _overlayTexture = 0;
        private double _overlayMinLat = 0.0;
        private double _overlayMinLon = 0.0;
        private double _overlayMaxLat = 0.0;
        private double _overlayMaxLon = 0.0;
        private int _overlayZoom = 0;
        private bool _hasPositionedOverlay = false;

        // Second overlay slot for GPU-side compositing (e.g. temperature on top of radar)
        // Each overlay has its own GL texture, bbox, opacity â€” composited via alpha blending on GPU
        private int _overlay2Texture = 0;
        private double _overlay2MinLat = 0.0;
        private double _overlay2MinLon = 0.0;
        private double _overlay2MaxLat = 0.0;
        private double _overlay2MaxLon = 0.0;
        private int _overlay2Zoom = 0;
        private bool _hasPositionedOverlay2 = false;
        private float _overlay2Opacity = 0.6f;

        /// <summary>Opacity for the second positioned overlay (0.0â€“1.0)</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Overlay2Opacity
        {
            get => _overlay2Opacity;
            set { _overlay2Opacity = Math.Max(0f, Math.Min(1f, value)); Invalidate(); }
        }

        // PBO pool for async texture uploads â€” avoids CPU stall during glTexImage2D
        private int _pboId = 0;
        private const int PBO_BUFFER_SIZE = 256 * 256 * 4; // one 256x256 RGBA tile

        // Render batching timer â€” coalesces rapid Invalidate() calls from tile loads
        private System.Threading.Timer? _renderBatchTimer;
        private volatile bool _renderPending = false;

        // Animation refresh timer for shader time-based effects (crosshair pulse, glow)
        private System.Threading.Timer? _animRefreshTimer;

        // Use Pixel Buffer Objects (PBO) for async texture uploads when available.
        // Enabled by default â€” can be toggled for diagnostics.
        private bool _usePboUploads = true;
        // Debug: draw overlay bounding box to help diagnose positioned overlay rendering
        private bool _debugOverlayBounds = false;
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DebugOverlayBounds { get => _debugOverlayBounds; set => _debugOverlayBounds = value; }
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool UsePboUploads { get => _usePboUploads; set => _usePboUploads = value; }

        // VRAM tile texture cache â€” modern GPUs can hold thousands of 256Ã—256 RGBA textures
        // (~256 KB each). 2000 textures â‰ˆ ~512 MB VRAM which is safe for mid-range GPUs.
        // Keeping more tiles in VRAM eliminates re-uploads when panning/zooming back to previously viewed areas.
        private const int MAX_TILE_TEXTURES = 2000;

        private const int PREFETCH_RADIUS = 3;

        // Dedicated overlay shader for weather data (no saturation/contrast/vignette)
        private GLShader? _weatherOverlayShader;

        // Radar frame buffer for ghosting/animation
        private readonly System.Collections.Generic.List<int> _radarFrames = new System.Collections.Generic.List<int>();
        private const int MAX_RADAR_FRAMES = 6;

        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.Zero;

        // Debounce timer for snapping tile zoom after smooth GL zoom
        private System.Threading.Timer? _zoomSnapTimer;
        /// <summary>True while smooth zoom is in progress (between mouse wheel and SnapTileZoom)</summary>
        public bool IsSmoothZooming { get; private set; } = false;
#pragma warning disable CS0414 // assigned but not yet read â€” reserved for future tile-snap logic
        private int _baseMapZoom = 4; // tile zoom before smooth zoom offset
#pragma warning restore CS0414

        // Cached uniform locations for optimization
        private int _tileShaderTransformLoc = -1;
        private int _tileShaderOpacityLoc = -1;
        private int _tileShaderTextureLoc = -1;
        private int _tileShaderZoomNormLoc = -1;
        private int _overlayShaderColorLoc = -1;
        private int _overlayShaderAlphaLoc = -1;
        private int _overlayShaderTimeLoc = -1;
        private int _overlayShaderOffsetLoc = -1;

        // Tile shader zoom blur uniform
        private int _tileShaderZoomBlurLoc = -1;

        // Weather overlay shader cached uniforms
        private int _woShaderTransformLoc = -1;
        private int _woShaderOpacityLoc = -1;
        private int _woShaderTimeLoc = -1;

        // Elapsed time for shader animations (crosshair pulse, overlay effects)
        private System.Diagnostics.Stopwatch _elapsedTimer = System.Diagnostics.Stopwatch.StartNew();

        // FPS tracking
        private int _frameCount = 0;
        private float _currentFps = 0f;
        private System.Diagnostics.Stopwatch _fpsTimer = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Current frames per second (updated every ~0.5 seconds)</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float CurrentFps => _currentFps;

        // â•â•â• Shader toggle properties (controlled from HUD Shaders panel) â•â•â•
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableTileSaturation { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableTileContrast { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableTileVignette { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableTileAtmosphere { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableRadarGlow { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EnableCrosshairPulse { get; set; } = true;

        // Cached uniform locations for shader toggles
        private int _tileShaderSaturationLoc = -1;
        private int _tileShaderContrastLoc = -1;
        private int _tileShaderVignetteLoc = -1;
        private int _tileShaderAtmosphereLoc = -1;
        private int _woShaderGlowLoc = -1;
        private int _overlayShaderPulseLoc = -1;

        // Tile loading deduplication
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), System.Threading.Tasks.Task> _pendingLoads 
            = new System.Collections.Concurrent.ConcurrentDictionary<(int z,int x,int y), System.Threading.Tasks.Task>();

        private readonly string _vertexSourceFallback = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aTex;
uniform mat3 uTransform;
out vec2 vTex;
void main() {
    vec3 p = uTransform * vec3(aPos, 1.0);
    gl_Position = vec4(p.xy, 0.0, 1.0);
    vTex = aTex;
}";

        private readonly string _fragmentSourceFallback = @"#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
void main() {
    FragColor = texture(uTexture, vTex);
}";

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Max(0.1f, Math.Min(10f, value));
                Invalidate();
            }
        }

        public GLRadarControl() : base()
        {
            // DoubleBuffer is enabled by GLControl base
            this.Load += GLRadarControl_Load;
            this.Paint += GLRadarControl_Paint;
            this.Resize += GLRadarControl_Resize;
            this.MouseWheel += GLRadarControl_MouseWheel;
            this.MouseDown += GLRadarControl_MouseDown;
            this.MouseMove += GLRadarControl_MouseMove;
            this.MouseUp += GLRadarControl_MouseUp;
            this.MouseEnter += (s, e) => { _mouseInside = true; UpdateCursorStyle(); };
            this.MouseLeave += (s, e) => { _mouseInside = false; this.Cursor = Cursors.Default; Invalidate(); };
        }

        private void GLRadarControl_Load(object? sender, EventArgs e)
        {
            MakeCurrent();
            GL.ClearColor(0.12f, 0.12f, 0.12f, 1.0f);

            // Build simple quad
            float[] vertices = new float[]
            {
                // positions    // tex
                -1f, -1f, 0f, 0f,
                 1f, -1f, 1f, 0f,
                 1f,  1f, 1f, 1f,
                -1f,  1f, 0f, 1f
            };
            uint[] indices = new uint[] { 0,1,2, 2,3,0 };

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            _ebo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            // Load shader from disk with fallback
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var vPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "vertex.glsl");
            var fPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "fragment.glsl");
            string vSrc, fSrc;
            try { vSrc = File.ReadAllText(vPath); } catch { vSrc = _vertexSourceFallback; }
            try { fSrc = File.ReadAllText(fPath); } catch { fSrc = _fragmentSourceFallback; }

            try
            {
                _shader = new GLShader(vSrc, fSrc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Primary shader compile failed: {ex.Message} â€” using fallback");
                _shader = new GLShader(_vertexSourceFallback, _fragmentSourceFallback);
            }
            _shader.Use();
            _shader.SetInt("uTexture", 0);
            _shader.SetFloat("uOpacity", 1.0f);

            // create fallback tile texture (neutral background) used when tiles are missing/blocked
            _fallbackTexture = CreateFallbackTexture(256, 256);

            // Tile shader (simple texture copy) - load from disk if present
            var tileVPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "tile.vert.glsl");
            var tileFPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "tile.frag.glsl");
            string tileV, tileF;
            try { tileV = File.ReadAllText(tileVPath); } catch { tileV = _vertexSourceFallback; }
            try { tileF = File.ReadAllText(tileFPath); } catch { tileF = "#version 330 core\nin vec2 vTex; out vec4 FragColor; uniform sampler2D uTexture; void main(){ FragColor = texture(uTexture, vTex); }"; }
            try
            {
                _tileShader = new GLShader(tileV, tileF);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Tile shader compile failed: {ex.Message} â€” using fallback");
                _tileShader = new GLShader(_vertexSourceFallback, "#version 330 core\nin vec2 vTex; out vec4 FragColor; uniform sampler2D uTexture; void main(){ FragColor = texture(uTexture, vTex); }");
            }

            // Enable alpha blending for radar overlays
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Prepare overlay (crosshair/markers)
            var overlayVPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "overlay.vert.glsl");
            var overlayFPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "overlay.frag.glsl");
            string ovSrcV, ovSrcF;
            try { ovSrcV = File.ReadAllText(overlayVPath); } catch { ovSrcV = "#version 330 core\nlayout(location=0) in vec2 aPos; layout(location=1) in float aLineEdge; out vec2 vLineCoord; void main(){ gl_Position = vec4(aPos,0,1); vLineCoord = vec2(aLineEdge, 0.0); }"; }
            try { ovSrcF = File.ReadAllText(overlayFPath); } catch { ovSrcF = "#version 330 core\nin vec2 vLineCoord; out vec4 FragColor; uniform vec3 uColor; uniform float uAlpha; uniform float uTime; void main(){ float pulse = 0.85 + 0.15 * sin(uTime * 2.5); float aa = 1.0 - smoothstep(0.4, 1.0, abs(vLineCoord.x)); FragColor = vec4(uColor, uAlpha * pulse * aa); }"; }

            try
            {
                _overlayShader = new GLShader(ovSrcV, ovSrcF);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Overlay shader compile failed: {ex.Message} â€” using fallback");
                _overlayShader = new GLShader(
                    "#version 330 core\nlayout(location=0) in vec2 aPos; layout(location=1) in float aLineEdge; out vec2 vLineCoord; void main(){ gl_Position = vec4(aPos,0,1); vLineCoord = vec2(aLineEdge, 0.0); }",
                    "#version 330 core\nin vec2 vLineCoord; out vec4 FragColor; uniform vec3 uColor; uniform float uAlpha; uniform float uTime; void main(){ float pulse = 0.85 + 0.15 * sin(uTime * 2.5); float aa = 1.0 - smoothstep(0.4, 1.0, abs(vLineCoord.x)); FragColor = vec4(uColor, uAlpha * pulse * aa); }");
            }

            // Initialize tile provider
            _tileProvider = new TileProvider();

            // If caller set a local tile folder earlier, pass it through
            if (!string.IsNullOrEmpty(_localTileFolder)) _tileProvider.LocalTilesRoot = _localTileFolder;

            // Ensure tile shader has texture unit set
            _tileShader!.Use();
            _tileShader!.SetInt("uTexture", 0);
            _tileShader!.SetFloat("uOpacity", 1.0f);

            // Build dedicated weather overlay shader (pass-through, no tile effects)
            var overlayTexVPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "weather_overlay.vert.glsl");
            var overlayTexFPath = Path.Combine(baseDir, "Rendering", "OpenGL", "shaders", "weather_overlay.frag.glsl");
            string woV, woF;
            try { woV = File.ReadAllText(overlayTexVPath); } catch { woV = _vertexSourceFallback; }
            try { woF = File.ReadAllText(overlayTexFPath); } catch {
                // Inline fallback: clean pass-through with opacity and edge blend
                woF = @"#version 330 core
in vec2 vTex;
in vec2 vScreenPos;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uTime;
void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    float edgeFade = smoothstep(0.0, 0.015, uv.x) * smoothstep(0.0, 0.015, 1.0 - uv.x) * smoothstep(0.0, 0.015, uv.y) * smoothstep(0.0, 0.015, 1.0 - uv.y);
    FragColor = vec4(c.rgb, c.a * opacity * edgeFade);
}";
            }
            try
            {
                _weatherOverlayShader = new GLShader(woV, woF);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Weather overlay shader compile failed: {ex.Message} â€” using fallback");
                _weatherOverlayShader = new GLShader(_vertexSourceFallback, @"#version 330 core
in vec2 vTex;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
void main() {
    vec2 uv = vec2(vTex.x, 1.0 - vTex.y);
    vec4 c = texture(uTexture, uv);
    float opacity = uOpacity > 0.0 ? uOpacity : 1.0;
    float edgeFade = smoothstep(0.0, 0.015, uv.x) * smoothstep(0.0, 0.015, 1.0 - uv.x) * smoothstep(0.0, 0.015, uv.y) * smoothstep(0.0, 0.015, 1.0 - uv.y);
    FragColor = vec4(c.rgb, c.a * opacity * edgeFade);
}");
            }
            _weatherOverlayShader.Use();
            _weatherOverlayShader.SetInt("uTexture", 0);
            _weatherOverlayShader.SetFloat("uOpacity", 1.0f);

            // Cache uniform locations for optimization
            _tileShaderTransformLoc = GL.GetUniformLocation(_tileShader.Handle, "uTransform");
            _tileShaderOpacityLoc = GL.GetUniformLocation(_tileShader.Handle, "uOpacity");
            _tileShaderTextureLoc = GL.GetUniformLocation(_tileShader.Handle, "uTexture");
            _tileShaderZoomNormLoc = GL.GetUniformLocation(_tileShader.Handle, "uZoomNorm");
            _overlayShaderColorLoc = GL.GetUniformLocation(_overlayShader.Handle, "uColor");
            _overlayShaderAlphaLoc = GL.GetUniformLocation(_overlayShader.Handle, "uAlpha");
            _overlayShaderTimeLoc = GL.GetUniformLocation(_overlayShader.Handle, "uTime");
            _overlayShaderOffsetLoc = GL.GetUniformLocation(_overlayShader.Handle, "uOffset");

            // Cache weather overlay shader uniforms
            _woShaderTransformLoc = GL.GetUniformLocation(_weatherOverlayShader.Handle, "uTransform");
            _woShaderOpacityLoc = GL.GetUniformLocation(_weatherOverlayShader.Handle, "uOpacity");
            _woShaderTimeLoc = GL.GetUniformLocation(_weatherOverlayShader.Handle, "uTime");

            // Cache tile shader zoom blur uniform
            _tileShaderZoomBlurLoc = GL.GetUniformLocation(_tileShader.Handle, "uZoomBlur");

            // Cache shader toggle uniform locations
            _tileShaderSaturationLoc = GL.GetUniformLocation(_tileShader.Handle, "uEnableSaturation");
            _tileShaderContrastLoc = GL.GetUniformLocation(_tileShader.Handle, "uEnableContrast");
            _tileShaderVignetteLoc = GL.GetUniformLocation(_tileShader.Handle, "uEnableVignette");
            _tileShaderAtmosphereLoc = GL.GetUniformLocation(_tileShader.Handle, "uEnableAtmosphere");
            _woShaderGlowLoc = GL.GetUniformLocation(_weatherOverlayShader.Handle, "uEnableGlow");
            _overlayShaderPulseLoc = GL.GetUniformLocation(_overlayShader.Handle, "uEnablePulse");

            // Setup overlay buffers (we'll fill data on resize)\n            // Format: [x, y, lineEdge] per vertex â€” lineEdge is 0 at center, 1 at edge (for AA)
            _overlayVao = GL.GenVertexArray();
            _overlayVbo = GL.GenBuffer();
            GL.BindVertexArray(_overlayVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _overlayVbo);
            // Allocate for crosshair quad-lines: 4 quads * 6 verts * 3 floats + center dot
            GL.BufferData(BufferTarget.ArrayBuffer, 128 * 3 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0); // aPos
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1); // aLineEdge
            GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, 3 * sizeof(float), 2 * sizeof(float));

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

            // Initialize GL-native HUD text renderer
            try
            {
                _uiRenderer = new GLTextRenderer();
                _uiRenderer.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] GLTextRenderer init failed: {ex.Message}");
                _uiRenderer = null;
            }

            // Initialize PBO for async texture uploads
            if (_usePboUploads)
            {
                try
                {
                    _pboId = GL.GenBuffer();
                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pboId);
                    GL.BufferData(BufferTarget.PixelUnpackBuffer, PBO_BUFFER_SIZE, IntPtr.Zero, BufferUsageHint.StreamDraw);
                    GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                    Console.WriteLine($"[GLRadarControl] PBO initialized: id={_pboId}, size={PBO_BUFFER_SIZE} bytes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GLRadarControl] PBO init failed, falling back to direct upload: {ex.Message}");
                    _usePboUploads = false;
                    _pboId = 0;
                }
            }

            // Animation refresh timer â€” triggers repaints at ~60fps for crosshair pulse
            // and shader time-based effects. Low overhead (just Invalidate, no work until Paint).
            _animRefreshTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (this.IsHandleCreated)
                        this.BeginInvoke(new Action(() => Invalidate()));
                }
                catch { }
            }, null, 16, 16); // ~60fps
        }

        private void GLRadarControl_Resize(object? sender, EventArgs e)
        {
            MakeCurrent();
            GL.Viewport(0, 0, Width, Height);
            UpdateOverlayVertices();
            Invalidate();
        }

        private int _crosshairVertexCount = 0;

        private void UpdateOverlayVertices()
        {
            // Build anti-aliased crosshair using quad-strip lines.
            // Each line segment is a screen-aligned quad with lineEdge = 0 at center, Â±1 at edges.
            // The fragment shader uses lineEdge for smooth anti-aliasing + black outline.
            int lenPx = 20;
            float halfW = 3.5f; // half-width of the line in pixels (wider for smooth anti-aliased black outline)
            float hx = (lenPx * 2f) / Math.Max(1, Width);
            float hy = (lenPx * 2f) / Math.Max(1, Height);
            float wx = halfW * 2f / Math.Max(1, Width);  // line width in NDC X
            float wy = halfW * 2f / Math.Max(1, Height); // line width in NDC Y

            // Each line = 2 triangles = 6 vertices. 2 lines (H + V) = 12 vertices.
            // Format: x, y, lineEdge
            var verts = new System.Collections.Generic.List<float>();

            // Horizontal line quad: from (-hx, 0) to (hx, 0), width = wy
            // Triangle 1: top-left, bottom-left, top-right
            verts.AddRange(new float[] { -hx, +wy, -1f });  // top-left (edge)
            verts.AddRange(new float[] { -hx, -wy, +1f });  // bottom-left (edge)
            verts.AddRange(new float[] { +hx, +wy, -1f });  // top-right (edge)
            // Triangle 2: top-right, bottom-left, bottom-right
            verts.AddRange(new float[] { +hx, +wy, -1f });
            verts.AddRange(new float[] { -hx, -wy, +1f });
            verts.AddRange(new float[] { +hx, -wy, +1f });

            // Vertical line quad: from (0, -hy) to (0, hy), width = wx
            verts.AddRange(new float[] { -wx, -hy, -1f });
            verts.AddRange(new float[] { +wx, -hy, +1f });
            verts.AddRange(new float[] { -wx, +hy, -1f });
            verts.AddRange(new float[] { -wx, +hy, -1f });
            verts.AddRange(new float[] { +wx, -hy, +1f });
            verts.AddRange(new float[] { +wx, +hy, +1f });

            // Center dot (small quad, all edges = 0 for full opacity)
            float dotR = 3f / Math.Max(1, Width);
            float dotRy = 3f / Math.Max(1, Height);
            verts.AddRange(new float[] { -dotR, +dotRy, 0f });
            verts.AddRange(new float[] { -dotR, -dotRy, 0f });
            verts.AddRange(new float[] { +dotR, +dotRy, 0f });
            verts.AddRange(new float[] { +dotR, +dotRy, 0f });
            verts.AddRange(new float[] { -dotR, -dotRy, 0f });
            verts.AddRange(new float[] { +dotR, -dotRy, 0f });

            _crosshairVertexCount = verts.Count / 3;
            float[] arr = verts.ToArray();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _overlayVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, arr.Length * sizeof(float), arr);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private void GLRadarControl_Paint(object? sender, PaintEventArgs e)
        {
            if (DesignMode) return;
            MakeCurrent();

            // FPS tracking
            _frameCount++;
            double elapsed = _fpsTimer.Elapsed.TotalSeconds;
            if (elapsed >= 0.5)
            {
                _currentFps = (float)(_frameCount / elapsed);
                _frameCount = 0;
                _fpsTimer.Restart();
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // All geometry is at z=0; disable depth testing so overlays draw on top of tiles
            GL.Disable(EnableCap.DepthTest);

            if (_shader == null)
            {
                SwapBuffers();
                return;
            }

            // Draw map tiles using tile shader.
            // If a pre-composited background (`_texture`) is present, draw it full-screen instead
            // of iterating map tiles â€” the background was produced by RadarImageService and
            // should not be recoloured by the radar palette shader.
            if (_texture != 0 && _hasBackgroundTexture && _bgSourceZoom != 0)
            {
                // Draw the composite background anchored to world coordinates so it will pan/zoom with tiles.
                _tileShader.Use();
                GL.ActiveTexture(TextureUnit.Texture0);

                // compute center pixel for composite at current map zoom (convert lon/lat -> pixel at _mapZoom)
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                // image center at requested background center, computed at current zoom (so it moves with map)
                double imgCenterPx = LonToPixelX(_bgCenterLon, z);
                double imgCenterPy = LatToPixelY(_bgCenterLat, z);

                // account for difference between source zoom and current zoom when scaling image size
                double scaleFactor = Math.Pow(2.0, z - _bgSourceZoom);
                double imgWidthMapPx = _bgPixelWidth * scaleFactor;
                double imgHeightMapPx = _bgPixelHeight * scaleFactor;

                // screen position of image center in pixels
                double screenCenterX = (imgCenterPx - cx) + Width / 2.0;
                double screenCenterY = (imgCenterPy - cy) + Height / 2.0;

                // image size in NDC
                float imgWnd = (float)(imgWidthMapPx / (Width / 2.0));
                float imgHnd = (float)(imgHeightMapPx / (Height / 2.0));

                float tileSx = imgWnd / 2f * _zoom;
                float tileSy = imgHnd / 2f * _zoom;
                float centerNdcX = (float)(screenCenterX / (Width / 2.0) - 1.0) * _zoom + _pan.X;
                float centerNdcY = (float)(1.0 - screenCenterY / (Height / 2.0)) * _zoom + _pan.Y;

                float[] tmat = new float[] { tileSx, 0f, 0f, 0f, tileSy, 0f, centerNdcX, centerNdcY, 1f };
                if (_tileShaderTransformLoc >= 0)
                    GL.UniformMatrix3(_tileShaderTransformLoc, 1, false, tmat);
                else
                    _tileShader.SetMatrix3("uTransform", tmat);

                GL.BindTexture(TextureTarget.Texture2D, _texture);
                GL.BindVertexArray(_vao);
                GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
            else
            {
                _tileShader.Use();
                GL.ActiveTexture(TextureUnit.Texture0);

                // Pass zoom normalization to tile shader for atmospheric effects
                // Normalize: zoom 0-20 â†’ 0.0-1.0
                float zoomNorm = Math.Clamp(_mapZoom / 20.0f, 0f, 1f);
                if (_tileShaderZoomNormLoc >= 0)
                    GL.Uniform1(_tileShaderZoomNormLoc, zoomNorm);

                // Pass shader toggle uniforms
                if (_tileShaderSaturationLoc >= 0)
                    GL.Uniform1(_tileShaderSaturationLoc, EnableTileSaturation ? 1 : 0);
                if (_tileShaderContrastLoc >= 0)
                    GL.Uniform1(_tileShaderContrastLoc, EnableTileContrast ? 1 : 0);
                if (_tileShaderVignetteLoc >= 0)
                    GL.Uniform1(_tileShaderVignetteLoc, EnableTileVignette ? 1 : 0);
                if (_tileShaderAtmosphereLoc >= 0)
                    GL.Uniform1(_tileShaderAtmosphereLoc, EnableTileAtmosphere ? 1 : 0);

                // Compute world/pixel metrics
                int z = _mapZoom;
                double n = Math.Pow(2.0, z);
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                // Expand visible range by inverse zoom to ensure coverage during smooth zoom-out
                int extraTiles = _zoom < 1.0f ? (int)Math.Ceiling(1.0 / _zoom) : 0;
                int tilesWide = (int)Math.Ceiling((double)Width / 256.0) + 2 + extraTiles * 2;
                int tilesHigh = (int)Math.Ceiling((double)Height / 256.0) + 2 + extraTiles * 2;

                // tile coordinates for center
                int centerTileX = (int)Math.Floor(cx / 256.0);
                int centerTileY = (int)Math.Floor(cy / 256.0);

                for (int dx = -tilesWide/2; dx <= tilesWide/2; dx++)
                {
                    for (int dy = -tilesHigh/2; dy <= tilesHigh/2; dy++)
                    {
                        int tileX = centerTileX + dx;
                        int tileY = centerTileY + dy;
                        int wrap = (int)Math.Pow(2, z);
                        int wrappedX = ((tileX % wrap) + wrap) % wrap;
                        if (tileY < 0 || tileY >= (1 << z)) continue; // out of lat range

                        var key = (z, wrappedX, tileY);
                        int texToBind = _fallbackTexture;
                        if (_tileTextures.TryGetValue(key, out int texId))
                        {
                            texToBind = texId;
                            _tileLastUsed[key] = DateTime.UtcNow.Ticks;
                        }
                        else if (_blockedTiles.ContainsKey(key))
                        {
                            // blocked tile known -> keep fallback and don't re-request frequently
                        }
                        else
                        {
                            // schedule download if not already queued
                            _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                        }

                        // compute tile's top-left pixel in global pixel space
                        double tilePx = tileX * 256.0;
                        double tilePy = tileY * 256.0;

                        // screen position of tile center
                        double screenCenterX = (tilePx - cx) + Width / 2.0 + 128.0;
                        double screenCenterY = (tilePy - cy) + Height / 2.0 + 128.0;

                        // tile size in NDC (incorporate smooth zoom)
                        float tileW = (float)((256.0) / (Width / 2.0)) * _zoom;
                        float tileH = (float)((256.0) / (Height / 2.0)) * _zoom;

                        // center NDC coords (incorporate smooth zoom + pan)
                        float centerNdcX = ((float)(screenCenterX / (Width / 2.0) - 1.0)) * _zoom + _pan.X;
                        float centerNdcY = ((float)(1.0 - screenCenterY / (Height / 2.0))) * _zoom + _pan.Y;

                        // tile transform: scale then translate
                        // Add 0.5 pixel overlap to eliminate sub-pixel seams between tiles
                        float halfPixelNdcX = _zoom / (float)Width;
                        float halfPixelNdcY = _zoom / (float)Height;
                        float tileSx = tileW / 2f + halfPixelNdcX;
                        float tileSy = tileH / 2f + halfPixelNdcY;
                        float txf = centerNdcX;
                        float tyf = centerNdcY;

                        float[] tmat = new float[] { tileSx, 0f, 0f, 0f, tileSy, 0f, txf, tyf, 1f };

                        if (_tileShaderTransformLoc >= 0)
                            GL.UniformMatrix3(_tileShaderTransformLoc, 1, false, tmat);
                        else
                            _tileShader.SetMatrix3("uTransform", tmat);

                        GL.BindTexture(TextureTarget.Texture2D, texToBind);
                        GL.BindVertexArray(_vao);
                        GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                        GL.BindVertexArray(0);
                    }
                }
            }

            // Mark loading complete once we have at least one real tile texture
            if (_mapLoading && _tileTextures.Count > 0)
                _mapLoading = false;

            // Draw positioned overlay (anchored to geographic bbox) if present
            if (_hasPositionedOverlay && _overlayTexture != 0)
            {
                // Use dedicated weather overlay shader (clean pass-through, no tile effects)
                GL.Enable(EnableCap.Blend);
                var ovShader = _weatherOverlayShader ?? _tileShader;
                ovShader.Use();
                int ovTransformLoc = _woShaderTransformLoc >= 0 ? _woShaderTransformLoc : GL.GetUniformLocation(ovShader.Handle, "uTransform");
                int ovOpacityLoc = _woShaderOpacityLoc >= 0 ? _woShaderOpacityLoc : GL.GetUniformLocation(ovShader.Handle, "uOpacity");
                int ovTimeLoc = _woShaderTimeLoc >= 0 ? _woShaderTimeLoc : GL.GetUniformLocation(ovShader.Handle, "uTime");
                if (ovOpacityLoc >= 0)
                    GL.Uniform1(ovOpacityLoc, _overlayOpacity);
                if (ovTimeLoc >= 0)
                    GL.Uniform1(ovTimeLoc, (float)_elapsedTimer.Elapsed.TotalSeconds);
                // Pass glow toggle to weather overlay shader
                if (_woShaderGlowLoc >= 0)
                    GL.Uniform1(_woShaderGlowLoc, EnableRadarGlow ? 1 : 0);
                GL.ActiveTexture(TextureUnit.Texture0);

                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                // Compute overlay bounds in global pixel space at current zoom
                double leftPx = LonToPixelX(_overlayMinLon, z);
                double rightPx = LonToPixelX(_overlayMaxLon, z);
                double topPy = LatToPixelY(_overlayMaxLat, z);
                double bottomPy = LatToPixelY(_overlayMinLat, z);

                double imgWidthMapPx = Math.Abs(rightPx - leftPx);
                double imgHeightMapPx = Math.Abs(bottomPy - topPy);
                double imgCenterPx = (leftPx + rightPx) / 2.0;
                double imgCenterPy = (topPy + bottomPy) / 2.0;

                // Screen-space center for the overlay
                double screenCenterX = (imgCenterPx - cx) + Width / 2.0;
                double screenCenterY = (imgCenterPy - cy) + Height / 2.0;

                // Size in NDC (matching tile/background transform math, with smooth zoom)
                float imgWnd = (float)(imgWidthMapPx / (Width / 2.0)) * _zoom;
                float imgHnd = (float)(imgHeightMapPx / (Height / 2.0)) * _zoom;

                float tileSx = imgWnd / 2f;
                float tileSy = imgHnd / 2f;
                float centerNdcX = ((float)(screenCenterX / (Width / 2.0) - 1.0)) * _zoom + _pan.X;
                float centerNdcY = ((float)(1.0 - screenCenterY / (Height / 2.0))) * _zoom + _pan.Y;

                float[] tmatOverlay = new float[] { tileSx, 0f, 0f, 0f, tileSy, 0f, centerNdcX, centerNdcY, 1f };
                if (ovTransformLoc >= 0)
                    GL.UniformMatrix3(ovTransformLoc, 1, false, tmatOverlay);
                else
                    ovShader.SetMatrix3("uTransform", tmatOverlay);

                GL.BindTexture(TextureTarget.Texture2D, _overlayTexture);
                GL.BindVertexArray(_vao);
                GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                // Reset opacity to 1.0 for subsequent draws
                if (ovOpacityLoc >= 0)
                    GL.Uniform1(ovOpacityLoc, 1.0f);

                // Debug: draw overlay bounding box so we can verify placement/size visually
                if (_debugOverlayBounds && _overlayShader != null)
                {
                    // compute NDC corners from transform parameters used above
                    float leftNdc = centerNdcX - tileSx;
                    float rightNdc = centerNdcX + tileSx;
                    float topNdc = centerNdcY + tileSy;
                    float bottomNdc = centerNdcY - tileSy;

                    // Debug quad with lineEdge = 0 (fully opaque, no AA needed for debug)
                    float[] quad = new float[]
                    {
                        leftNdc, topNdc, 0f,
                        rightNdc, topNdc, 0f,
                        rightNdc, bottomNdc, 0f,
                        leftNdc, bottomNdc, 0f
                    };

                    // Upload to overlay VBO (temporary) and draw a red outline
                    GL.BindBuffer(BufferTarget.ArrayBuffer, _overlayVbo);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, quad.Length * sizeof(float), quad);

                    _overlayShader.Use();
                    if (_overlayShaderTimeLoc >= 0)
                        GL.Uniform1(_overlayShaderTimeLoc, 0f); // no pulse for debug
                    if (_overlayShaderAlphaLoc >= 0)
                        GL.Uniform1(_overlayShaderAlphaLoc, 0.9f);
                    if (_overlayShaderColorLoc >= 0)
                        GL.Uniform3(_overlayShaderColorLoc, 1.0f, 0.2f, 0.2f);

                    GL.BindVertexArray(_overlayVao);
                    GL.LineWidth(2f);
                    GL.DrawArrays(PrimitiveType.LineLoop, 0, 4);
                    GL.BindVertexArray(0);

                    // restore crosshair vertices
                    UpdateOverlayVertices();
                }
            } // positioned overlay

            // Draw second positioned overlay (e.g. temperature) â€” GPU compositing via alpha blend
            if (_hasPositionedOverlay2 && _overlay2Texture != 0)
            {
                GL.Enable(EnableCap.Blend);
                var ov2Shader = _weatherOverlayShader ?? _tileShader;
                ov2Shader.Use();
                int ov2TransformLoc = _woShaderTransformLoc >= 0 ? _woShaderTransformLoc : GL.GetUniformLocation(ov2Shader.Handle, "uTransform");
                int ov2OpacityLoc = _woShaderOpacityLoc >= 0 ? _woShaderOpacityLoc : GL.GetUniformLocation(ov2Shader.Handle, "uOpacity");
                int ov2TimeLoc = _woShaderTimeLoc >= 0 ? _woShaderTimeLoc : GL.GetUniformLocation(ov2Shader.Handle, "uTime");
                if (ov2OpacityLoc >= 0)
                    GL.Uniform1(ov2OpacityLoc, _overlay2Opacity);
                if (ov2TimeLoc >= 0)
                    GL.Uniform1(ov2TimeLoc, (float)_elapsedTimer.Elapsed.TotalSeconds);
                // Pass glow toggle to overlay 2 shader
                if (_woShaderGlowLoc >= 0)
                    GL.Uniform1(_woShaderGlowLoc, EnableRadarGlow ? 1 : 0);
                GL.ActiveTexture(TextureUnit.Texture0);

                int z2 = _mapZoom;
                double cx2 = LonToPixelX(_centerLon, z2);
                double cy2 = LatToPixelY(_centerLat, z2);

                double leftPx2 = LonToPixelX(_overlay2MinLon, z2);
                double rightPx2 = LonToPixelX(_overlay2MaxLon, z2);
                double topPy2 = LatToPixelY(_overlay2MaxLat, z2);
                double bottomPy2 = LatToPixelY(_overlay2MinLat, z2);

                double imgW2 = Math.Abs(rightPx2 - leftPx2);
                double imgH2 = Math.Abs(bottomPy2 - topPy2);
                double imgCx2 = (leftPx2 + rightPx2) / 2.0;
                double imgCy2 = (topPy2 + bottomPy2) / 2.0;

                double sCx2 = (imgCx2 - cx2) + Width / 2.0;
                double sCy2 = (imgCy2 - cy2) + Height / 2.0;

                float wNdc2 = (float)(imgW2 / (Width / 2.0)) * _zoom;
                float hNdc2 = (float)(imgH2 / (Height / 2.0)) * _zoom;

                float sx2 = wNdc2 / 2f;
                float sy2 = hNdc2 / 2f;
                float ndcX2 = ((float)(sCx2 / (Width / 2.0) - 1.0)) * _zoom + _pan.X;
                float ndcY2 = ((float)(1.0 - sCy2 / (Height / 2.0))) * _zoom + _pan.Y;

                float[] tmat2 = { sx2, 0f, 0f, 0f, sy2, 0f, ndcX2, ndcY2, 1f };
                if (ov2TransformLoc >= 0)
                    GL.UniformMatrix3(ov2TransformLoc, 1, false, tmat2);
                else
                    ov2Shader.SetMatrix3("uTransform", tmat2);

                GL.BindTexture(TextureTarget.Texture2D, _overlay2Texture);
                GL.BindVertexArray(_vao);
                GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                if (ov2OpacityLoc >= 0)
                    GL.Uniform1(ov2OpacityLoc, 1.0f);
            } // positioned overlay 2

            // Draw radar frames (oldest first) with fading alpha
            if (_radarFrames.Count > 0)
            {
                GL.Enable(EnableCap.Blend);
                // Use weather overlay shader for clean pass-through (no tile contrast/vignette)
                var rfShader = _weatherOverlayShader ?? _tileShader;
                rfShader.Use();
                int rfTransformLoc = _woShaderTransformLoc >= 0 ? _woShaderTransformLoc : GL.GetUniformLocation(rfShader.Handle, "uTransform");
                int rfOpacityLoc = _woShaderOpacityLoc >= 0 ? _woShaderOpacityLoc : GL.GetUniformLocation(rfShader.Handle, "uOpacity");
                int rfTimeLoc = _woShaderTimeLoc >= 0 ? _woShaderTimeLoc : GL.GetUniformLocation(rfShader.Handle, "uTime");
                if (rfTimeLoc >= 0)
                    GL.Uniform1(rfTimeLoc, (float)_elapsedTimer.Elapsed.TotalSeconds);
                // Pass glow toggle to radar frames shader
                if (_woShaderGlowLoc >= 0)
                    GL.Uniform1(_woShaderGlowLoc, EnableRadarGlow ? 1 : 0);
                for (int i = 0; i < _radarFrames.Count; i++)
                {
                    int tex = _radarFrames[i];
                    // fading alpha for animation effect
                    float alpha = (float)(i + 1) / (_radarFrames.Count + 1);

                    if (rfOpacityLoc >= 0)
                        GL.Uniform1(rfOpacityLoc, alpha);
                    else
                        rfShader.SetFloat("uOpacity", alpha);
                    
                    // Apply smooth zoom to fullscreen radar frames too
                    float[] tmat = new float[] { _zoom, 0f, 0f, 0f, _zoom, 0f, _pan.X, _pan.Y, 1f };
                    if (rfTransformLoc >= 0)
                        GL.UniformMatrix3(rfTransformLoc, 1, false, tmat);
                    else
                        rfShader.SetMatrix3("uTransform", tmat);
                    
                    GL.BindTexture(TextureTarget.Texture2D, tex);
                    GL.BindVertexArray(_vao);
                    GL.DrawElements(BeginMode.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                    GL.BindVertexArray(0);
                }
            }

            // Draw overlay crosshair/marker in NDC with overlay shader (anti-aliased quads)
            // When UseCrosshairAsMouse=true, draw at mouse position; otherwise draw at center
            if (ShowCrosshair && _overlayShader != null && _crosshairVertexCount > 0)
            {
                bool drawAtMouse = UseCrosshairAsMouse && _mouseInside && _mouseScreenPos.X >= 0;
                // Only draw if we have a valid position
                if (!UseCrosshairAsMouse || drawAtMouse)
                {
                    _overlayShader.Use();
                    // Pass time for pulse animation
                    if (_overlayShaderTimeLoc >= 0)
                        GL.Uniform1(_overlayShaderTimeLoc, (float)_elapsedTimer.Elapsed.TotalSeconds);
                    // Pass pulse toggle (always pulse when crosshair-as-mouse)
                    if (_overlayShaderPulseLoc >= 0)
                        GL.Uniform1(_overlayShaderPulseLoc, (UseCrosshairAsMouse || EnableCrosshairPulse) ? 1 : 0);
                    if (_overlayShaderAlphaLoc >= 0)
                        GL.Uniform1(_overlayShaderAlphaLoc, 1.0f);
                    else
                        _overlayShader.SetFloat("uAlpha", 1.0f);
                    // draw crosshair in green
                    if (_overlayShaderColorLoc >= 0)
                        GL.Uniform3(_overlayShaderColorLoc, 0.6f, 1.0f, 0.2f);
                    else
                    {
                        var colorLoc2 = GL.GetUniformLocation(_overlayShader.Handle, "uColor");
                        GL.Uniform3(colorLoc2, 0.6f, 1.0f, 0.2f);
                    }

                    // Compute NDC offset for crosshair-as-mouse mode
                    if (drawAtMouse && Width > 0 && Height > 0)
                    {
                        float ndcX = (2f * _mouseScreenPos.X / Width) - 1f;
                        float ndcY = 1f - (2f * _mouseScreenPos.Y / Height);
                        if (_overlayShaderOffsetLoc >= 0)
                            GL.Uniform2(_overlayShaderOffsetLoc, ndcX, ndcY);
                    }
                    else
                    {
                        // Center of screen (no offset)
                        if (_overlayShaderOffsetLoc >= 0)
                            GL.Uniform2(_overlayShaderOffsetLoc, 0f, 0f);
                    }

                    GL.BindVertexArray(_overlayVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, _crosshairVertexCount);
                    GL.BindVertexArray(0);
                }
            }

            // --- User location marker (blue square with border) ---
            if (_showUserMarker && _uiRenderer != null && _uiRenderer.IsInitialized && Width > 0 && Height > 0)
            {
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);
                double markerPx = LonToPixelX(_userMarkerLon, z);
                double markerPy = LatToPixelY(_userMarkerLat, z);

                // Screen-space position with smooth zoom and pan
                double screenX = (markerPx - cx) * _zoom + Width / 2.0 + _pan.X * Width / 2.0;
                double screenY = (markerPy - cy) * _zoom + Height / 2.0 - _pan.Y * Height / 2.0;

                // Only draw if on screen
                if (screenX >= -20 && screenX <= Width + 20 && screenY >= -20 && screenY <= Height + 20)
                {
                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    GL.Disable(EnableCap.DepthTest);

                    _uiRenderer.BeginFrame(Width, Height);
                    float mx = (float)screenX;
                    float my = (float)screenY;
                    float markerSize = 10f;
                    float half = markerSize / 2f;
                    // Black border (slightly larger)
                    _uiRenderer.DrawRect(mx - half - 2f, my - half - 2f, markerSize + 4f, markerSize + 4f, 0f, 0f, 0f, 0.9f);
                    // Blue fill
                    _uiRenderer.DrawRect(mx - half, my - half, markerSize, markerSize, 0.2f, 0.45f, 1.0f, 0.9f);
                    // Small white center dot
                    _uiRenderer.DrawRect(mx - 1.5f, my - 1.5f, 3f, 3f, 1f, 1f, 1f, 0.9f);
                    _uiRenderer.EndFrame();
                }
            }

            // --- GL HUD rendering (attribution + status) ---
            if (_uiRenderer != null && _uiRenderer.IsInitialized)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.Disable(EnableCap.DepthTest);

                _uiRenderer.BeginFrame(Width, Height);

                // Attribution text: semi-transparent background bar at bottom-left
                if (!string.IsNullOrEmpty(_hudAttributionText))
                {
                    float textW = _uiRenderer.MeasureTextWidth(_hudAttributionText);
                    float lh = _uiRenderer.LineHeight;
                    float pad = 6f;
                    float barH = lh + pad * 2;
                    float barW = textW + pad * 2;
                    float barX = 0;
                    float barY = Height - barH;

                    _uiRenderer.DrawRect(barX, barY, barW, barH, 0f, 0f, 0f, 0.55f);
                    _uiRenderer.DrawText(_hudAttributionText, barX + pad, barY + pad, 0.82f, 0.82f, 0.82f, 0.85f);
                }

                // Status bar: single-row at bottom-right (mirrors attribution style)
                if (!string.IsNullOrEmpty(_hudStatusBarText))
                {
                    float textW = _uiRenderer.MeasureTextWidth(_hudStatusBarText);
                    float lh = _uiRenderer.LineHeight;
                    float pad = 6f;
                    float barH = lh + pad * 2;
                    float barW = textW + pad * 2;
                    float barX = Width - barW;
                    float barY = Height - barH;

                    _uiRenderer.DrawRect(barX, barY, barW, barH, 0f, 0f, 0f, 0.55f);
                    _uiRenderer.DrawText(_hudStatusBarText, barX + pad, barY + pad, 0.82f, 0.82f, 0.82f, 0.85f);
                }

                // (Old single-row status bar removed)

                // Status / frame info: centered near bottom
                if (!string.IsNullOrEmpty(_hudStatusText))
                {
                    float textW = _uiRenderer.MeasureTextWidth(_hudStatusText);
                    float lh = _uiRenderer.LineHeight;
                    float pad = 6f;
                    float barH = lh + pad * 2;
                    float barW = textW + pad * 2;
                    float barX = (Width - barW) / 2f;
                    float barY = Height - barH - 4f;

                    _uiRenderer.DrawRect(barX, barY, barW, barH, 0.05f, 0.05f, 0.08f, 0.85f);
                    _uiRenderer.DrawText(_hudStatusText, barX + pad, barY + pad, 1.0f, 1.0f, 1.0f, 1f);
                }

                // Coordinates HUD: show lat/lon near the crosshair
                // In crosshair-as-mouse mode, show lat/lon under mouse cursor; otherwise show center
                if (ShowCoordinatesHUD && ShowCrosshair)
                {
                    double coordLat, coordLon;
                    float anchorX, anchorY;
                    bool drawAtMouse2 = UseCrosshairAsMouse && _mouseInside && _mouseScreenPos.X >= 0;

                    if (drawAtMouse2 && Width > 0 && Height > 0)
                    {
                        // Compute lat/lon under mouse position
                        double cx2 = LonToPixelX(_centerLon, _mapZoom);
                        double cy2 = LatToPixelY(_centerLat, _mapZoom);
                        double mousePxX = cx2 + (_mouseScreenPos.X - Width / 2.0);
                        double mousePxY = cy2 + (_mouseScreenPos.Y - Height / 2.0);
                        coordLat = PixelYToLat(mousePxY, _mapZoom);
                        coordLon = PixelXToLon(mousePxX, _mapZoom);
                        anchorX = _mouseScreenPos.X + 20f;
                        anchorY = _mouseScreenPos.Y + 20f;
                    }
                    else
                    {
                        coordLat = _centerLat;
                        coordLon = _centerLon;
                        anchorX = (Width / 2f) + 20f;
                        anchorY = (Height / 2f) + 15f;
                    }

                    string latDir = coordLat >= 0 ? "N" : "S";
                    string lonDir = coordLon >= 0 ? "E" : "W";
                    string coordText = $"{Math.Abs(coordLat):F4}\u00b0{latDir}, {Math.Abs(coordLon):F4}\u00b0{lonDir}";
                    float coordW = _uiRenderer.MeasureTextWidth(coordText);
                    float lhc = _uiRenderer.LineHeight;
                    float padC = 5f;
                    float coordBarH = lhc + padC * 2;
                    float coordBarW = coordW + padC * 2;
                    float coordBarX = anchorX;
                    float coordBarY = anchorY;

                    // Keep on screen
                    if (coordBarX + coordBarW > Width - 5f) coordBarX = Width - coordBarW - 5f;
                    if (coordBarY + coordBarH > Height - 5f) coordBarY = Height - coordBarH - 5f;

                    _uiRenderer.DrawRect(coordBarX, coordBarY, coordBarW, coordBarH, 0.05f, 0.05f, 0.08f, 0.65f);
                    _uiRenderer.DrawText(coordText, coordBarX + padC, coordBarY + padC, 0.6f, 1.0f, 0.2f, 0.95f);
                }

                // Scale bar: rendered at bottom-right of viewport
                {
                    // Calculate meters per pixel using Mercator projection
                    double metersPerPixel = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0) / Math.Pow(2.0, _mapZoom);

                    // Choose a round-number distance that fits in ~100-200 pixels
                    double[] distances = { 5000000, 2000000, 1000000, 500000, 200000, 100000, 50000, 20000, 10000, 5000, 2000, 1000, 500, 200, 100, 50, 20, 10, 5 };
                    double chosenDist = 1000; // default 1km
                    foreach (var d in distances)
                    {
                        double pixels = d / metersPerPixel;
                        if (pixels >= 60 && pixels <= 250)
                        {
                            chosenDist = d;
                            break;
                        }
                    }

                    float barPixels = (float)(chosenDist / metersPerPixel);
                    string distLabel = chosenDist >= 1000 ? $"{chosenDist / 1000:F0} km" : $"{chosenDist:F0} m";

                    float scaleH = 6f;
                    float padS = 8f;

                    // Position scale bar at bottom-left, above the attribution bar
                    float attrBarH = _uiRenderer.LineHeight + 12f; // attribution bar height
                    float scaleY = Height - attrBarH - 16f;

                    // Background
                    float labelW = _uiRenderer.MeasureTextWidth(distLabel);
                    float bgW = Math.Max(barPixels, labelW) + padS * 2;
                    float bgH = scaleH + _uiRenderer.LineHeight + padS * 2 + 4f;
                    float bgX = 10f;
                    float bgY = scaleY - _uiRenderer.LineHeight - padS - 4f;
                    _uiRenderer.DrawRect(bgX, bgY, bgW, bgH, 0f, 0f, 0f, 0.5f);

                    // Scale bar line
                    float lineX = bgX + (bgW - barPixels) / 2f;
                    float lineY = scaleY;
                    _uiRenderer.DrawRect(lineX, lineY, barPixels, scaleH, 1f, 1f, 1f, 0.9f);
                    // End ticks
                    _uiRenderer.DrawRect(lineX, lineY - 4f, 2f, scaleH + 8f, 1f, 1f, 1f, 0.9f);
                    _uiRenderer.DrawRect(lineX + barPixels - 2f, lineY - 4f, 2f, scaleH + 8f, 1f, 1f, 1f, 0.9f);

                    // Distance label centered above bar
                    float labelX = lineX + (barPixels - labelW) / 2f;
                    float labelY = lineY - _uiRenderer.LineHeight - 4f;
                    _uiRenderer.DrawText(distLabel, labelX, labelY, 1f, 1f, 1f, 0.9f);
                }

                _uiRenderer.EndFrame();
            }

            // --- Interactive HUD overlay system (map controls) ---
            if (_hudSystem != null && _uiRenderer != null && _uiRenderer.IsInitialized)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.Disable(EnableCap.DepthTest);

                _uiRenderer.BeginFrame(Width, Height);
                _hudSystem.Render(_uiRenderer, Width, Height);
                _uiRenderer.EndFrame();
            }

            // --- Loading screen overlay (shown until first tiles render) ---
            if (_mapLoading && _uiRenderer != null && _uiRenderer.IsInitialized)
            {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.Disable(EnableCap.DepthTest);

                _uiRenderer.BeginFrame(Width, Height);

                // Semi-transparent dark overlay
                _uiRenderer.DrawRect(0, 0, Width, Height, 0.06f, 0.06f, 0.09f, 0.92f);

                // Animated loading text with pulsing dots
                float t = (float)_elapsedTimer.Elapsed.TotalSeconds;
                int dotCount = ((int)(t * 2.5f)) % 4;
                string dots = new string('.', dotCount);
                string loadingText = $"Loading Map{dots}";
                float textW = _uiRenderer.MeasureTextWidth(loadingText);
                float lh = _uiRenderer.LineHeight;
                float textX = (Width - textW) / 2f;
                float textY = (Height - lh) / 2f;

                // Subtle pulse on the text alpha
                float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 2.0);
                _uiRenderer.DrawText(loadingText, textX, textY, 0.75f, 0.82f, 0.95f, pulse);

                // Small subtitle
                string sub = "Fetching tiles...";
                float subW = _uiRenderer.MeasureTextWidth(sub);
                _uiRenderer.DrawText(sub, (Width - subW) / 2f, textY + lh + 8f, 0.5f, 0.5f, 0.6f, 0.6f);

                _uiRenderer.EndFrame();
            }

            // Check for any accumulated GL errors at end of frame
            {
                ErrorCode err = GL.GetError();
                if (err != ErrorCode.NoError)
                    Console.WriteLine($"[GLRadarControl] GL error after paint: {err}");
            }

            SwapBuffers();
        }

        private bool _dragging = false;
        private System.Drawing.Point _lastMousePos;

        private GLShader? _tileShader;

        private void GLRadarControl_MouseWheel(object? sender, MouseEventArgs e)
        {
            // Let HUD consume the event first (e.g., slider adjustments, panel scrolling)
            if (_hudSystem != null && _hudSystem.ProcessMouseWheel(e.X, e.Y, e.Delta))
            {
                Invalidate();
                return;
            }

            // Smooth GL zoom with cursor-centered pan adjustment
            var oldZoom = _zoom;
            var delta = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _zoom *= delta;
            _zoom = Math.Max(0.25f, Math.Min(8f, _zoom));

            // Adjust pan so the point under cursor stays under cursor
            if (Width > 0 && Height > 0)
            {
                var nx = (2f * e.X / Width) - 1f;
                var ny = 1f - (2f * e.Y / Height);
                _pan.X = (nx - (nx - _pan.X) * (oldZoom / _zoom));
                _pan.Y = (ny - (ny - _pan.Y) * (oldZoom / _zoom));
            }

            Invalidate();

            // Mark smooth zoom in progress
            IsSmoothZooming = true;

            // Debounced tile zoom snap after 300ms of no scrolling
            _zoomSnapTimer?.Dispose();
            _zoomSnapTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() => SnapTileZoom()));
                    }
                }
                catch { }
            }, null, 300, System.Threading.Timeout.Infinite);
        }

        private void SnapTileZoom()
        {
            // Compute target tile zoom from smooth zoom factor
            int zoomDelta = (int)Math.Round(Math.Log(_zoom) / Math.Log(2));
            if (zoomDelta == 0 && Math.Abs(_zoom - 1.0f) < 0.05f)
            {
                // Already at correct tile zoom, just reset smooth zoom
                _zoom = 1.0f;
                _pan = Vector2.Zero;
                IsSmoothZooming = false;
                Invalidate();
                return;
            }

            int targetZoom = Math.Max(0, Math.Min(20, _mapZoom + zoomDelta));
            
            // Reset smooth zoom
            _zoom = 1.0f;
            _pan = Vector2.Zero;
            IsSmoothZooming = false;
            
            if (targetZoom != _mapZoom)
            {
                SetMapZoom(targetZoom);
            }
            else
            {
                Invalidate();
            }
        }

        private void GLRadarControl_MouseDown(object? sender, MouseEventArgs e)
        {
            // Let HUD consume click first (buttons, checkboxes, dropdowns, sliders)
            if (e.Button == MouseButtons.Left && _hudSystem != null && _hudSystem.ProcessMouseDown(e.X, e.Y))
            {
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _lastMousePos = e.Location;
                // Show SizeAll during drag, even in crosshair-as-mouse mode
                this.Cursor = UseCrosshairAsMouse ? _blankCursor : Cursors.SizeAll;
            }
        }

        private void GLRadarControl_MouseMove(object? sender, MouseEventArgs e)
        {
            // Always track mouse position for crosshair rendering
            _mouseScreenPos = e.Location;

            // Let HUD process move (hover highlights, slider dragging)
            if (_hudSystem != null)
            {
                bool overHud = _hudSystem.ProcessMouseMove(e.X, e.Y);
                if (overHud && !_dragging)
                {
                    // Over HUD: show pointer cursor even in crosshair mode
                    this.Cursor = Cursors.Hand;
                    Invalidate();
                    return;
                }
                else if (!_dragging && !overHud)
                {
                    UpdateCursorStyle();
                }
                if (overHud) Invalidate();
            }

            // Repaint for crosshair tracking even when not dragging
            if (UseCrosshairAsMouse && ShowCrosshair && !_dragging)
                Invalidate();

            if (!_dragging) return;
            var dx = e.X - _lastMousePos.X;
            var dy = e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;

            // Instead of NDC pan, update map center based on pixel delta
            if (Width > 0 && Height > 0)
            {
                // pixel delta in screen
                double ndx = dx;
                double ndy = dy;

                // compute how many global pixels we moved
                // Positive dx means move center to left (lon decreases)
                double cx = LonToPixelX(_centerLon, _mapZoom);
                double cy = LatToPixelY(_centerLat, _mapZoom);

                cx -= ndx;
                cy -= ndy;

                _centerLon = PixelXToLon(cx, _mapZoom);
                _centerLat = PixelYToLat(cy, _mapZoom);

                // Only invalidate during drag - don't fetch tiles until drag ends
                Invalidate();
            }
        }

        private void GLRadarControl_MouseUp(object? sender, MouseEventArgs e)
        {
            // Let HUD process mouse-up (end slider drag, etc.)
            if (_hudSystem != null && _hudSystem.ProcessMouseUp(e.X, e.Y))
            {
                Invalidate();
                // Don't return â€” also need to end map drag if it was active
            }

            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                UpdateCursorStyle();

                // After dragging ends, refresh tiles for the new position
                UpdateTiles();

                // Notify listeners that map center changed (triggers overlay refresh)
                try { MapPositionChanged?.Invoke(_centerLat, _centerLon); } catch { }
            }
        }

        /// <summary>Set cursor style based on UseCrosshairAsMouse mode</summary>
        private void UpdateCursorStyle()
        {
            if (UseCrosshairAsMouse && ShowCrosshair && _mouseInside)
                this.Cursor = _blankCursor;
            else
                this.Cursor = Cursors.Hand;
        }

        // Backwards-compatible single-arg API - delegates to the metadata-aware variant with null metadata.
        public void SetImageBytes(byte[] data)
        {
            SetImageBytes(data, null, null, null);
        }

        // Metadata-aware overload â€” sourceCenter/zoom tell the control how to anchor the composite image
        public void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetImageBytes(data, sourceCenterLat, sourceCenterLon, sourceZoom)));
                return;
            }

            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                ProcessIncomingBitmap(bmp, sourceCenterLat, sourceCenterLon, sourceZoom);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[GLRadarControl] Invalid image data ({data?.Length ?? 0} bytes): {ex.Message}");
            }
        }
        
        // Bounding-box-aware overload â€” bbox defines geographic extent of overlay
        public void SetImageBytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetImageBytes(data, minLat, minLon, maxLat, maxLon, sourceZoom)));
                return;
            }

            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                ProcessIncomingBitmapWithBBox(bmp, minLat, minLon, maxLat, maxLon, sourceZoom);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[GLRadarControl] Invalid image data ({data?.Length ?? 0} bytes): {ex.Message}");
            }
        }


        /// <summary>
        /// Clears any overlay/background texture
        /// </summary>
        public void ClearOverlay()
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => ClearOverlay()));
                return;
            }

            MakeCurrent();
            
            // Clear background texture
            if (_texture != 0)
            {
                try { GL.DeleteTexture(_texture); } catch { }
                _texture = 0;
                _hasBackgroundTexture = false;
                BackgroundTextureChanged?.Invoke(false);
            }
            
            // Clear radar frames
            foreach (var t in _radarFrames)
            {
                try { GL.DeleteTexture(t); } catch { }
            }
            _radarFrames.Clear();

            // Clear both positioned overlays
            ClearPositionedOverlay();
            ClearPositionedOverlay2();
            
            Invalidate();
        }

        /// <summary>
        /// Clears only the positioned overlay (radar animation frames) without touching background/tiles
        /// </summary>
        public void ClearPositionedOverlay()
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => ClearPositionedOverlay()));
                return;
            }

            MakeCurrent();
            if (_overlayTexture != 0)
            {
                try { GL.DeleteTexture(_overlayTexture); } catch { }
                _overlayTexture = 0;
            }
            _hasPositionedOverlay = false;
            Invalidate();
        }

        /// <summary>
        /// Clears the second positioned overlay (temperature layer)
        /// </summary>
        public void ClearPositionedOverlay2()
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => ClearPositionedOverlay2()));
                return;
            }

            MakeCurrent();
            if (_overlay2Texture != 0)
            {
                try { GL.DeleteTexture(_overlay2Texture); } catch { }
                _overlay2Texture = 0;
            }
            _hasPositionedOverlay2 = false;
            Invalidate();
        }

        /// <summary>
        /// Sets a second overlay texture with geographic bounding box (GPU compositing, no CPU compositing needed)
        /// </summary>
        public void SetOverlay2Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            if (InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetOverlay2Bytes(data, minLat, minLon, maxLat, maxLon, sourceZoom)));
                return;
            }

            if (minLat >= maxLat || minLon >= maxLon)
            {
                Console.WriteLine($"[GLRadarControl] ERROR: Invalid bbox for overlay2");
                return;
            }

            try
            {
                MakeCurrent();
                if (_overlay2Texture != 0)
                {
                    try { GL.DeleteTexture(_overlay2Texture); } catch { }
                    _overlay2Texture = 0;
                }

                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                _overlay2Texture = UploadBitmapToOverlayTexture(bmp);

                if (_overlay2Texture != 0)
                {
                    _overlay2MinLat = minLat;
                    _overlay2MinLon = minLon;
                    _overlay2MaxLat = maxLat;
                    _overlay2MaxLon = maxLon;
                    _overlay2Zoom = sourceZoom;
                    _hasPositionedOverlay2 = true;
                    Console.WriteLine($"[GLRadarControl] Overlay2 uploaded: texture={_overlay2Texture}");
                    Invalidate();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Overlay2 upload error: {ex.Message}");
                _hasPositionedOverlay2 = false;
            }
        }


        // Extracted helper so both overloads use the same logic and we can pass metadata
        private void ProcessIncomingBitmap(Bitmap bmp, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            // If incoming image is fully opaque (composited base map + radar),
            // treat it as a pre-composited background and upload to the background
            // texture so it won't be recoloured by the radar palette shader.
            bool hasAlphaChannel = Image.IsAlphaPixelFormat(bmp.PixelFormat);
            bool anyTransparent = false;
            if (hasAlphaChannel)
            {
                // quick scan for any transparent pixel (sample every 8th pixel to be fast)
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var dataBmp = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = dataBmp.Stride;
                    int bytes = Math.Abs(stride) * bmp.Height;
                    var buf = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(dataBmp.Scan0, buf, 0, bytes);
                    // consider only *fully* transparent pixels (or nearly fully transparent) as "transparent".
                    // Ignore semi-transparent pixels (alpha > 0) since composites often contain semi-opaque overlay pixels.
                    const int TRANSPARENT_THRESHOLD = 8; // alpha < 8 treated as transparent
                    for (int y = 0; y < bmp.Height && !anyTransparent; y += 8)
                    {
                        for (int x = 0; x < bmp.Width; x += 8)
                        {
                            int idx = y * stride + x * 4;
                            if (idx + 3 >= buf.Length) break;
                            byte a = buf[idx + 3];
                            if (a < TRANSPARENT_THRESHOLD) { anyTransparent = true; break; }
                        }
                    }
                }
                finally { bmp.UnlockBits(dataBmp); }
            }

            if (!hasAlphaChannel || !anyTransparent)
            {
                Console.WriteLine("[GLRadarControl] Incoming image classified: BACKGROUND (pre-composited)");
                // opaque/composited image -> upload as background (replace any radar frames)
                _radarFrames.ForEach(t => { try { GL.DeleteTexture(t); } catch { } });
                _radarFrames.Clear();

                // delete previous background texture if any (we will replace it)
                if (_texture != 0) { try { GL.DeleteTexture(_texture); } catch { } _texture = 0; }

                // store metadata if provided so the background image will pan/zoom with the map
                if (sourceCenterLat.HasValue && sourceCenterLon.HasValue && sourceZoom.HasValue)
                {
                    _bgCenterLat = sourceCenterLat.Value;
                    _bgCenterLon = sourceCenterLon.Value;
                    _bgSourceZoom = sourceZoom.Value;
                    _bgPixelWidth = bmp.Width;
                    _bgPixelHeight = bmp.Height;
                }

                UploadBitmapToTexture(bmp);
                _hasBackgroundTexture = true;
                BackgroundTextureChanged?.Invoke(true);
            }
            else
            {
                Console.WriteLine("[GLRadarControl] Incoming image classified: OVERLAY (transparent) -> radar frame");
                // if we previously had a background texture, clear it so overlays composite over tiles
                if (_hasBackgroundTexture)
                {
                    if (_texture != 0) { try { GL.DeleteTexture(_texture); } catch { } _texture = 0; }
                    _hasBackgroundTexture = false;
                    BackgroundTextureChanged?.Invoke(false);
                }

                // transparent image -> normal radar overlay processing
                AddRadarFrameFromBitmap(bmp);
            }

            Invalidate();
        }

        /// <summary>
        /// Process incoming bitmap with geographic bounding box for positioned overlay
        /// </summary>
        private void ProcessIncomingBitmapWithBBox(Bitmap bmp, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            Console.WriteLine($"[GLRadarControl] Processing positioned overlay: bbox=({minLat:F4},{minLon:F4}) to ({maxLat:F4},{maxLon:F4}), zoom={sourceZoom}, size={bmp.Width}x{bmp.Height}");
            
            // Validate bounding box
            if (minLat >= maxLat || minLon >= maxLon)
            {
                Console.WriteLine($"[GLRadarControl] ERROR: Invalid bounding box - minLat={minLat} >= maxLat={maxLat} or minLon={minLon} >= maxLon={maxLon}");
                return;
            }

            // Retry GL upload up to 2 times (first attempt sometimes fails with InvalidOperation due to context timing)
            int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    MakeCurrent();
                    
                    // Clear any existing positioned overlay
                    if (_overlayTexture != 0)
                    {
                        try { GL.DeleteTexture(_overlayTexture); } catch { }
                        _overlayTexture = 0;
                    }

                    // Upload to overlay texture
                    _overlayTexture = UploadBitmapToOverlayTexture(bmp);
                    
                    if (_overlayTexture != 0)
                    {
                        _overlayMinLat = minLat;
                        _overlayMinLon = minLon;
                        _overlayMaxLat = maxLat;
                        _overlayMaxLon = maxLon;
                        _overlayZoom = sourceZoom;
                        _hasPositionedOverlay = true;
                        
                        Console.WriteLine($"[GLRadarControl] Positioned overlay uploaded successfully: texture={_overlayTexture} (attempt {attempt})");
                        Invalidate();
                        return;
                    }
                    else
                    {
                        Console.WriteLine($"[GLRadarControl] WARNING: Upload failed on attempt {attempt}/{maxAttempts}");
                        if (attempt < maxAttempts)
                            System.Threading.Thread.Sleep(50); // brief pause before retry
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GLRadarControl] Upload exception on attempt {attempt}: {ex.Message}");
                }
            }

            Console.WriteLine("[GLRadarControl] ERROR: Failed to upload overlay texture after all attempts");
            _hasPositionedOverlay = false;
            Invalidate();
        }

        /// <summary>
        /// Upload bitmap to a new OpenGL texture and return the texture ID
        /// </summary>
        private int UploadBitmapToOverlayTexture(Bitmap bmp)
        {
            try
            {
                int tex = GL.GenTexture();
                if (tex == 0)
                {
                    Console.WriteLine("[GLRadarControl] ERROR: GL.GenTexture() returned 0");
                    return 0;
                }

                GL.BindTexture(TextureTarget.Texture2D, tex);
                
                // Set texture parameters
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                // Upload pixel data
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                    
                    // Check for GL errors
                    ErrorCode err = GL.GetError();
                    if (err != ErrorCode.NoError)
                    {
                        Console.WriteLine($"[GLRadarControl] GL Error after TexImage2D: {err}");
                        GL.DeleteTexture(tex);
                        return 0;
                    }
                }
                finally
                {
                    bmp.UnlockBits(data);
                }

                GL.BindTexture(TextureTarget.Texture2D, 0);
                
                Console.WriteLine($"[GLRadarControl] Texture uploaded: ID={tex}, size={bmp.Width}x{bmp.Height}");
                return tex;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GLRadarControl] Exception in UploadBitmapToOverlayTexture: {ex.Message}");
                return 0;
            }
        }

        private void AddRadarFrameFromBitmap(Bitmap bmp)

        {
            MakeCurrent();
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);

            _radarFrames.Add(tex);
            if (_radarFrames.Count > MAX_RADAR_FRAMES)
            {
                var del = _radarFrames[0];
                _radarFrames.RemoveAt(0);
                try { GL.DeleteTexture(del); } catch { }
            }
        }

        private void UploadBitmapToTexture(Bitmap bmp)
        {
            MakeCurrent();
            if (_texture == 0)
                _texture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _zoomSnapTimer?.Dispose();
                _renderBatchTimer?.Dispose();
                _animRefreshTimer?.Dispose();
                MakeCurrent();
                if (_texture != 0)
                {
                    GL.DeleteTexture(_texture);
                    _texture = 0;
                    if (_hasBackgroundTexture) { _hasBackgroundTexture = false; BackgroundTextureChanged?.Invoke(false); }
                }
                if (_fallbackTexture != 0) GL.DeleteTexture(_fallbackTexture);
                if (_overlayTexture != 0) { try { GL.DeleteTexture(_overlayTexture); } catch { } _overlayTexture = 0; }
                if (_overlay2Texture != 0) { try { GL.DeleteTexture(_overlay2Texture); } catch { } _overlay2Texture = 0; }
                if (_pboId != 0) { try { GL.DeleteBuffer(_pboId); } catch { } _pboId = 0; }
                if (_shader != null) _shader.Dispose();
                if (_tileShader != null) _tileShader.Dispose();
                if (_overlayShader != null) _overlayShader.Dispose();
                if (_weatherOverlayShader != null) _weatherOverlayShader.Dispose();
                _uiRenderer?.Dispose();

                // delete tile textures
                foreach (var kv in _tileTextures)
                {
                    try { GL.DeleteTexture(kv.Value); } catch { }
                }

                // delete radar frames
                foreach (var t in _radarFrames)
                {
                    try { GL.DeleteTexture(t); } catch { }
                }

                if (_vbo != 0) GL.DeleteBuffer(_vbo);
                if (_ebo != 0) GL.DeleteBuffer(_ebo);
                if (_vao != 0) GL.DeleteVertexArray(_vao);
                if (_overlayVbo != 0) GL.DeleteBuffer(_overlayVbo);
                if (_overlayVao != 0) GL.DeleteVertexArray(_overlayVao);

                _tileProvider?.Dispose();
            }
            base.Dispose(disposing);
        }

        // Minimal struct helper for pan vector
        private struct Vector2
        {
            public float X;
            public float Y;
            public static Vector2 Zero => new Vector2 { X = 0f, Y = 0f };
            public Vector2(float x, float y) { X = x; Y = y; }
        }

        // Notify host UI about tile status changes
        public event Action<string, System.Drawing.Color>? TileStatusChanged;
        // Fired when a pre-composited full-screen background texture is set or cleared
        public event Action<bool>? BackgroundTextureChanged;
        private bool _hasBackgroundTexture = false;

        // Overlay opacity (0.0â€“1.0) set by host UI sliders
        private float _overlayOpacity = 0.75f;

        /// <summary>Opacity for the positioned weather overlay (0.0 = transparent, 1.0 = opaque)</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float OverlayOpacity
        {
            get => _overlayOpacity;
            set { _overlayOpacity = Math.Max(0f, Math.Min(1f, value)); Invalidate(); }
        }

        private void NotifyTileStatus(string text, System.Drawing.Color color)
        {
            try
            {
                if (this.IsHandleCreated && this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => TileStatusChanged?.Invoke(text, color)));
                }
                else
                {
                    TileStatusChanged?.Invoke(text, color);
                }
            }
            catch { }
        }

        private void EvictTilesIfNeeded()
        {
            try
            {
                if (_tileTextures.Count <= MAX_TILE_TEXTURES) return;

                // Evict 25% of tiles in one batch using O(n) partial sort approach
                int toEvict = Math.Max(1, _tileTextures.Count / 4);
                
                // Find the N oldest entries by timestamp without full sort
                var entries = _tileLastUsed.ToArray();
                // Partition: find approximate eviction threshold
                if (entries.Length <= toEvict)
                    return; // shouldn't happen, but safety check

                // Use Array.Sort on a copy (still O(n log n) but on a value array, not LINQ allocation)
                Array.Sort(entries, (a, b) => a.Value.CompareTo(b.Value));
                
                for (int i = 0; i < toEvict && i < entries.Length; i++)
                {
                    var key = entries[i].Key;
                    if (_tileTextures.TryRemove(key, out int tex))
                    {
                        try { GL.DeleteTexture(tex); } catch { }
                    }
                    _tileLastUsed.TryRemove(key, out _);
                }
            }
            catch { }
        }

        /// <summary>
        /// Coalesces rapid tile-load Invalidate() calls into a single redraw.
        /// Multiple tiles loading within 16ms will trigger only one paint.
        /// </summary>
        private void BatchedInvalidate()
        {
            if (_renderPending) return;
            _renderPending = true;

            _renderBatchTimer?.Dispose();
            _renderBatchTimer = new System.Threading.Timer(_ =>
            {
                _renderPending = false;
                try
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() => Invalidate()));
                    }
                }
                catch { }
            }, null, 16, System.Threading.Timeout.Infinite); // ~60fps
        }

        private int CreateFallbackTexture(int w, int h)
        {
            MakeCurrent();
            using var bmp = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.FromArgb(245, 245, 235));
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 220, 200));
            for (int x = 0; x < w; x += 32) g.DrawLine(pen, x, 0, x, h);
            for (int y = 0; y < h; y += 32) g.DrawLine(pen, 0, y, w, y);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 200, 200));
            g.DrawString("No Tile", new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold), brush, 8, 8);

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }
        // Map projection helpers
        private static double LonToPixelX(double lon, int z)
        {
            double n = Math.Pow(2.0, z);
            return ((lon + 180.0) / 360.0) * 256.0 * n;
        }

        private static double LatToPixelY(double lat, int z)
        {
            var latRad = lat * Math.PI / 180.0;
            var n = Math.Pow(2.0, z);
            return ( (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 ) * 256.0 * n;
        }

        private static double PixelXToLon(double px, int z)
        {
            double n = Math.Pow(2.0, z) * 256.0;
            return (px / n) * 360.0 - 180.0;
        }

        private static double PixelYToLat(double py, int z)
        {
            double n = Math.Pow(2.0, z) * 256.0;
            double y = py / n;
            double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y)));
            return latRad * 180.0 / Math.PI;
        }

        public void SetCenterLatLon(double lat, double lon)
        {
            _centerLat = lat;
            _centerLon = lon;
            UpdateTiles();
            Invalidate();
            try { MapPositionChanged?.Invoke(_centerLat, _centerLon); } catch { }
        }

        // Raised whenever the map/tile zoom changes (UI and mouse-wheel shifts)
        public event Action<int>? MapZoomChanged;
        // Raised when map center position changes (for overlay refresh)
        public event Action<double, double>? MapPositionChanged;

        public void SetMapZoom(int z)

        {
            if (z == _mapZoom) return;
            _mapZoom = z;
            UpdateTiles();
            Invalidate();
            try { MapZoomChanged?.Invoke(_mapZoom); } catch { }
        }

        /// <summary>
        /// Change the map tile style. Clears all cached tile textures and re-fetches with the new style.
        /// </summary>
        public void SetMapStyle(OpenMap.MapStyle style)
        {
            if (_tileProvider == null) _tileProvider = new TileProvider();
            _tileProvider.CurrentStyle = style;

            // Clear all tile textures (they belong to the old style)
            MakeCurrent();
            foreach (var kv in _tileTextures)
            {
                try { GL.DeleteTexture(kv.Value); } catch { }
            }
            _tileTextures.Clear();
            _tileLastUsed.Clear();
            _blockedTiles.Clear();

            // Re-fetch tiles for new style
            UpdateTiles();
            Invalidate();
        }

        /// <summary>
        /// Use a local folder containing tiles in z/x/y.png layout. If set, TileProvider will prefer local tiles.
        /// </summary>
        public void SetLocalTilesFolder(string? folder)
        {
            _localTileFolder = folder;
            if (_tileProvider == null) _tileProvider = new TileProvider();
            _tileProvider.LocalTilesRoot = folder;
            UpdateTiles();
            Invalidate();
        }

        private async System.Threading.Tasks.Task EnsureTileLoadedAsync(int z, int x, int y)
        {
            try
            {
                if (_tileProvider == null) _tileProvider = new TileProvider();
                var key = (z, x, y);
                if (_tileTextures.ContainsKey(key)) return;

                // Honour blocked-tile TTL: allow retry after expiry
                if (_blockedTiles.TryGetValue(key, out var blockedAt))
                {
                    if (DateTime.UtcNow - blockedAt < BlockedTileTtl) return;
                    _blockedTiles.TryRemove(key, out _);
                }

                // Deduplicate concurrent loads for same tile
                var loadTask = _pendingLoads.GetOrAdd(key, k => LoadTileInternalAsync(k.z, k.x, k.y));
                await loadTask;
            }
            catch { }
        }

        private async System.Threading.Tasks.Task LoadTileInternalAsync(int z, int x, int y)
        {
            var key = (z, x, y);
            await _tileSemaphore.WaitAsync();
            try
            {
                var (bytes, status) = await _tileProvider!.GetTileBytesAsync(z, x, y);
                if (status == TileFetchStatus.Blocked)
                {
                    _blockedTiles[key] = DateTime.UtcNow;
                    NotifyTileStatus("Tiles: Blocked", System.Drawing.Color.OrangeRed);
                    return;
                }
                if (status == TileFetchStatus.NotFound || status == TileFetchStatus.Error || bytes == null)
                {
                    _blockedTiles[key] = DateTime.UtcNow;
                    NotifyTileStatus("Tiles: Missing", System.Drawing.Color.Gray);
                    return;
                }

                // Decode PNG to raw BGRA pixel data on background thread (avoids UI thread stall)
                var decoded = await System.Threading.Tasks.Task.Run(() =>
                {
                    using var ms = new System.IO.MemoryStream(bytes);
                    using var bmp = new System.Drawing.Bitmap(ms);
                    int w = bmp.Width;
                    int h = bmp.Height;
                    var rect = new System.Drawing.Rectangle(0, 0, w, h);
                    var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        int rowBytes = w * 4;
                        byte[] pixels = new byte[w * h * 4];
                        for (int row = 0; row < h; row++)
                        {
                            Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * rowBytes, rowBytes);
                        }
                        return (Pixels: pixels, Width: w, Height: h);
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                });

                // Upload texture on UI/GL thread (only the GL call, no decoding)
                this.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MakeCurrent();
                        int tex = GL.GenTexture();
                        GL.BindTexture(TextureTarget.Texture2D, tex);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                        int pixelByteCount = decoded.Width * decoded.Height * 4;

                        // Use PBO for async DMA upload when available and tile fits
                        if (_usePboUploads && _pboId != 0 && pixelByteCount <= PBO_BUFFER_SIZE)
                        {
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pboId);
                            GL.BufferData(BufferTarget.PixelUnpackBuffer, pixelByteCount, IntPtr.Zero, BufferUsageHint.StreamDraw);
                            IntPtr pboPtr = GL.MapBuffer(BufferTarget.PixelUnpackBuffer, BufferAccess.WriteOnly);
                            if (pboPtr != IntPtr.Zero)
                            {
                                Marshal.Copy(decoded.Pixels, 0, pboPtr, pixelByteCount);
                                GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer);
                                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                                    decoded.Width, decoded.Height, 0,
                                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
                            }
                            else
                            {
                                // PBO map failed â€” direct upload from managed array
                                var handle = System.Runtime.InteropServices.GCHandle.Alloc(decoded.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                                try
                                {
                                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                                        decoded.Width, decoded.Height, 0,
                                        OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject());
                                }
                                finally { handle.Free(); }
                            }
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                        }
                        else
                        {
                            // Direct upload from managed array
                            var handle = System.Runtime.InteropServices.GCHandle.Alloc(decoded.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                            try
                            {
                                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                                    decoded.Width, decoded.Height, 0,
                                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject());
                            }
                            finally { handle.Free(); }
                        }

                        GL.BindTexture(TextureTarget.Texture2D, 0);
                        _tileTextures.TryAdd(key, tex);
                        _tileLastUsed[key] = DateTime.UtcNow.Ticks;
                        NotifyTileStatus("Tiles: Remote", System.Drawing.Color.LightGreen);
                        EvictTilesIfNeeded();
                        BatchedInvalidate();
                    }
                    catch { }
                }));
            }
            catch { }
            finally
            {
                _tileSemaphore.Release();
                _pendingLoads.TryRemove(key, out _);
            }
        }

        private void UpdateTiles()
        {
            try
            {
                if (_tileProvider == null) _tileProvider = new TileProvider();
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z);
                double cy = LatToPixelY(_centerLat, z);

                int centerTileX = (int)Math.Floor(cx / 256.0);
                int centerTileY = (int)Math.Floor(cy / 256.0);

                int tilesWide = (int)Math.Ceiling((double)Width / 256.0) + 2 + PREFETCH_RADIUS * 2;
                int tilesHigh = (int)Math.Ceiling((double)Height / 256.0) + 2 + PREFETCH_RADIUS * 2;

                for (int dx = -tilesWide/2; dx <= tilesWide/2; dx++)
                {
                    for (int dy = -tilesHigh/2; dy <= tilesHigh/2; dy++)
                    {
                        int tileX = centerTileX + dx;
                        int tileY = centerTileY + dy;
                        int wrap = (int)Math.Pow(2, z);
                        int wrappedX = ((tileX % wrap) + wrap) % wrap;
                        if (tileY < 0 || tileY >= (1 << z)) continue;

                        var key = (z, wrappedX, tileY);
                        if (!_tileTextures.ContainsKey(key))
                        {
                            _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
