using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Rendering.DirectX
{
    /// <summary>
    /// DirectX 11 map renderer using Silk.NET â€” full feature parity with GLRadarControl.
    /// Renders slippy map tiles with per-tile shader effects, geo-positioned weather overlays,
    /// crosshair/markers, and an interactive HUD, all via Direct3D 11.
    /// </summary>
    public class DXMapRenderer : IMapRenderer
    {
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // D3D11 core objects
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private ComPtr<ID3D11Device> _device;
        private ComPtr<ID3D11DeviceContext> _context;
        private ComPtr<IDXGISwapChain1> _swapChain;
        private ComPtr<ID3D11RenderTargetView> _rtv;
        private ComPtr<ID3D11BlendState> _blendState;
        private ComPtr<ID3D11SamplerState> _sampler;
        private ComPtr<ID3D11RasterizerState> _rasterizerState;

        // Shaders
        private DXShader? _tileShader;
        private DXShader? _overlayShader;
        private DXShader? _weatherOverlayShader;
        private DXShader? _generalShader;
        private DXShader? _markerShader;

        // GPU vector station / epicenter markers
        private StationMarkerEntry[]  _stationMarkers   = Array.Empty<StationMarkerEntry>();
        private EpicenterMarkerEntry[] _epicenterMarkers = Array.Empty<EpicenterMarkerEntry>();
        private float _markerEpicenterPhase;
        private float _markerMostRecentPhase;
        private readonly object _markerLock = new();

        // Shared quad geometry (4 verts + 6 indices)
        private ComPtr<ID3D11Buffer> _quadVB;
        private ComPtr<ID3D11Buffer> _quadIB;

        // Dynamic overlay vertex buffer (crosshair geometry)
        private ComPtr<ID3D11Buffer> _overlayVB;
        private int _crosshairVertexCount;

        // Invisible cursor for crosshair-as-mouse mode
        private static readonly Cursor _blankCursor = CreateBlankCursor();
        private static Cursor CreateBlankCursor()
        {
            var bmp = new System.Drawing.Bitmap(1, 1);
            bmp.SetPixel(0, 0, System.Drawing.Color.Transparent);
            return new Cursor(bmp.GetHicon());
        }

        // Semantic name pointers (must stay alive while input layouts exist)
        private nint _semanticPOSITION;
        private nint _semanticTEXCOORD;

        // HUD renderer
        private DXHudRenderer? _hudRenderer;

        // Host panel
        private Panel _hostPanel;
        private bool _initialized;
        private bool _disposed;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Tile management (mirrors GLRadarControl)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private TileProvider? _tileProvider;
        private string? _localTileFolder;
        private readonly ConcurrentDictionary<(int z, int x, int y), ComPtr<ID3D11ShaderResourceView>> _tileTextures = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), long> _tileLastUsed = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), DateTime> _blockedTiles = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), Task> _pendingLoads = new();
        private static readonly SemaphoreSlim _tileSemaphore = new(14, 14);
        private static readonly TimeSpan BlockedTileTtl = TimeSpan.FromMinutes(2);
        private const int MAX_TILE_TEXTURES = 2000;
        private const int PREFETCH_RADIUS = 3;

        // Fallback tile texture
        private ComPtr<ID3D11ShaderResourceView> _fallbackTileSrv;

        // Background composite texture
        private ComPtr<ID3D11ShaderResourceView> _backgroundSrv;
        private bool _hasBackgroundTexture;
        private double _bgCenterLat, _bgCenterLon;
        private int _bgSourceZoom, _bgPixelWidth, _bgPixelHeight;

        // Positioned overlay 1 (radar)
        private ComPtr<ID3D11ShaderResourceView> _overlaySrv;
        private bool _hasPositionedOverlay;
        private double _overlayMinLat, _overlayMinLon, _overlayMaxLat, _overlayMaxLon;

        // Positioned overlay 2 (temperature)
        private ComPtr<ID3D11ShaderResourceView> _overlay2Srv;
        private bool _hasPositionedOverlay2;
        private double _overlay2MinLat, _overlay2MinLon, _overlay2MaxLat, _overlay2MaxLon;

        // Positioned overlay 3 (GRIB2 forecast)
        private ComPtr<ID3D11ShaderResourceView> _overlay3Srv;
        private bool _hasPositionedOverlay3;
        private double _overlay3MinLat, _overlay3MinLon, _overlay3MaxLat, _overlay3MaxLon;

        // Radar frames
        private readonly List<ComPtr<ID3D11ShaderResourceView>> _radarFrames = new();
        private const int MAX_RADAR_FRAMES = 6;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Map state
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private int _mapZoom = 4;
        private double _centerLat = 56.1304;
        private double _centerLon = -106.3468;
        private float _zoom = 1.0f;
        private float _panX, _panY;
        private bool _dragging;
        private Point _lastMousePos;
        private Point _mouseScreenPos = new(-1, -1);
        private bool _mouseInside;
        private bool _mapLoading = true;

        // Smooth zoom
        private System.Threading.Timer? _zoomSnapTimer;
        public bool IsSmoothZooming { get; private set; }

        // FPS tracking
        private int _frameCount;
        private float _currentFps;
        private readonly Stopwatch _fpsTimer = Stopwatch.StartNew();

        // Animation timer
        private readonly Stopwatch _elapsedTimer = Stopwatch.StartNew();
        private System.Threading.Timer? _animRefreshTimer;
        private System.Threading.Timer? _renderBatchTimer;
        private volatile bool _renderPending;

        // User marker
        private double _userMarkerLat, _userMarkerLon;
        private bool _showUserMarker;

        // Overlay opacity
        private float _overlayOpacity = 0.75f;
        private float _overlay2Opacity = 0.6f;
        private float _overlay3Opacity = 0.6f;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // IMapRenderer properties
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        public Control HostControl => _hostPanel;
        public RenderingApi ActiveApi => RenderingApi.DirectX11;
        public HudSystem? HudSystem { get; set; }
        public string HudStatusBarText { get; set; } = "";
        public string HudAttributionText { get; set; } = "";
        public string HudStatusText { get; set; } = "";

        public bool ShowCrosshair { get; set; } = true;
        public bool UseCrosshairAsMouse { get; set; } = true;
        public bool ShowCoordinatesHUD { get; set; } = true;

        public double UserMarkerLat { get => _userMarkerLat; set { _userMarkerLat = value; _hostPanel.Invalidate(); } }
        public double UserMarkerLon { get => _userMarkerLon; set { _userMarkerLon = value; _hostPanel.Invalidate(); } }
        public bool ShowUserMarker { get => _showUserMarker; set { _showUserMarker = value; _hostPanel.Invalidate(); } }

        public bool EnableTileSaturation { get; set; } = true;
        public bool EnableTileContrast { get; set; } = true;
        public bool EnableTileVignette { get; set; } = true;
        public bool EnableTileAtmosphere { get; set; } = true;
        public bool EnableRadarGlow { get; set; } = true;
        public bool EnableCrosshairPulse { get; set; } = true;

        public bool ShowStatusBar { get; set; } = true;
        public bool ShowRuler { get; set; } = true;
        public float StatusBarOpacity { get; set; } = 0.55f;
        public bool EnableVSync { get; set; } = false;

        public float OverlayOpacity { get => _overlayOpacity; set { _overlayOpacity = Math.Clamp(value, 0f, 1f); _hostPanel.Invalidate(); } }
        public float Overlay2Opacity { get => _overlay2Opacity; set { _overlay2Opacity = Math.Clamp(value, 0f, 1f); _hostPanel.Invalidate(); } }
        public float Overlay3Opacity { get => _overlay3Opacity; set { _overlay3Opacity = Math.Clamp(value, 0f, 1f); _hostPanel.Invalidate(); } }
        public bool DebugOverlayBounds { get; set; }
        public bool UsePboUploads { get; set; } = true;

        public float CurrentFps => _currentFps;
        public float Zoom => _zoom;
        public TileProvider? ActiveTileProvider => _tileProvider;

        public int VramTextureCount
        {
            get
            {
                unsafe
                {
                    int count = _tileTextures.Count;
                    if (_overlaySrv.Handle != null) count++;
                    if (_overlay2Srv.Handle != null) count++;
                    if (_overlay3Srv.Handle != null) count++;
                    if (_backgroundSrv.Handle != null) count++;
                    if (_fallbackTileSrv.Handle != null) count++;
                    count += _radarFrames.Count;
                    return count;
                }
            }
        }

        public long VramEstimatedBytes
        {
            get
            {
                unsafe
                {
                    long bytes = (long)_tileTextures.Count * 256 * 256 * 4;
                    if (_overlaySrv.Handle != null) bytes += 1024L * 1024 * 4;
                    if (_overlay2Srv.Handle != null) bytes += 1024L * 1024 * 4;
                    if (_overlay3Srv.Handle != null) bytes += 1024L * 1024 * 4;
                    if (_backgroundSrv.Handle != null) bytes += (long)_bgPixelWidth * _bgPixelHeight * 4;
                    if (_fallbackTileSrv.Handle != null) bytes += 256L * 256 * 4;
                    bytes += (long)_radarFrames.Count * 1024 * 1024 * 4;
                    return bytes;
                }
            }
        }

        public event Action<int>? MapZoomChanged;
        public event Action<double, double>? MapPositionChanged;
        public event Action<string, Color>? TileStatusChanged;
        public event Action<bool>? BackgroundTextureChanged;

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Constructor & Initialization
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        public DXMapRenderer()
        {
            _hostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            _hostPanel.HandleCreated += (s, e) => InitializeD3D();
            _hostPanel.Resize += (s, e) => HandleResize();
            _hostPanel.Paint += (s, e) => RenderFrame();
            _hostPanel.MouseWheel += OnMouseWheel;
            _hostPanel.MouseDown += OnMouseDown;
            _hostPanel.MouseMove += OnMouseMove;
            _hostPanel.MouseUp += OnMouseUp;
            _hostPanel.MouseEnter += (s, e) => { _mouseInside = true; UpdateCursorStyle(); };
            _hostPanel.MouseLeave += (s, e) => { _mouseInside = false; _hostPanel.Cursor = Cursors.Default; _hostPanel.Invalidate(); };

            // Allocate persistent semantic name strings (must outlive input layouts)
            _semanticPOSITION = SilkMarshal.StringToPtr("POSITION", NativeStringEncoding.UTF8);
            _semanticTEXCOORD = SilkMarshal.StringToPtr("TEXCOORD", NativeStringEncoding.UTF8);
        }

        private unsafe void InitializeD3D()
        {
            if (_initialized || _disposed) return;

            try
            {
                var dxgi = DXGI.GetApi();
                var d3d11 = D3D11.GetApi();

                // Create D3D11 device
                D3DFeatureLevel[] featureLevels = { D3DFeatureLevel.Level110 };
                ID3D11Device* pDevice = null;
                ID3D11DeviceContext* pContext = null;
                D3DFeatureLevel actualLevel;

                fixed (D3DFeatureLevel* pLevels = featureLevels)
                {
                    SilkMarshal.ThrowHResult(d3d11.CreateDevice(
                        (IDXGIAdapter*)null,
                        D3DDriverType.Hardware,
                        0,
                        (uint)CreateDeviceFlag.BgraSupport,
                        pLevels, 1,
                        D3D11.SdkVersion,
                        &pDevice,
                        &actualLevel,
                        &pContext));
                }

                _device = default;
                _device.Handle = pDevice;
                _context = default;
                _context.Handle = pContext;

                // Create DXGI swapchain targeting our panel's HWND
                ComPtr<IDXGIFactory2> factory = default;
                ComPtr<IDXGIDevice> dxgiDevice = default;
                SilkMarshal.ThrowHResult(_device.Handle->QueryInterface(
                    SilkMarshal.GuidPtrOf<IDXGIDevice>(), (void**)dxgiDevice.GetAddressOf()));

                ComPtr<IDXGIAdapter> adapter = default;
                SilkMarshal.ThrowHResult(dxgiDevice.Handle->GetAdapter(adapter.GetAddressOf()));

                SilkMarshal.ThrowHResult(adapter.Handle->GetParent(
                    SilkMarshal.GuidPtrOf<IDXGIFactory2>(), (void**)factory.GetAddressOf()));

                var swapDesc = new SwapChainDesc1
                {
                    Width = (uint)Math.Max(1, _hostPanel.Width),
                    Height = (uint)Math.Max(1, _hostPanel.Height),
                    Format = Format.FormatB8G8R8A8Unorm,
                    Stereo = 0,
                    SampleDesc = new SampleDesc(1, 0),
                    BufferUsage = DXGI.UsageRenderTargetOutput,
                    BufferCount = 3,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Unspecified,
                    Flags = (uint)SwapChainFlag.AllowTearing
                };

                IDXGISwapChain1* pSc = null;
                SilkMarshal.ThrowHResult(factory.Handle->CreateSwapChainForHwnd(
                    (IUnknown*)_device.Handle, _hostPanel.Handle,
                    &swapDesc, null, (IDXGIOutput*)null, &pSc));
                _swapChain = default;
                _swapChain.Handle = pSc;

                factory.Dispose();
                adapter.Dispose();
                dxgiDevice.Dispose();

                CreateRenderTargetView();
                CreateBlendState();
                CreateSampler();
                CreateRasterizerState();
                CreateGeometry();
                LoadShaders();
                CreateFallbackTile();

                // Initialize tile provider
                _tileProvider = new TileProvider();
                if (!string.IsNullOrEmpty(_localTileFolder))
                    _tileProvider.LocalTilesRoot = _localTileFolder;

                // Initialize HUD renderer
                _hudRenderer = new DXHudRenderer();
                _hudRenderer.SetDevice(_device.Handle, _context.Handle);
                _hudRenderer.Initialize();

                // Animation timer (~60fps)
                _animRefreshTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (_hostPanel.IsHandleCreated)
                            _hostPanel.BeginInvoke(new Action(() => _hostPanel.Invalidate()));
                    }
                    catch { }
                }, null, 16, 16);

                UpdateOverlayVertices();
                _initialized = true;
                Console.WriteLine($"[DXMapRenderer] Initialized: D3D11 Feature Level {actualLevel}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DXMapRenderer] Initialization failed: {ex.Message}");
                var lbl = new Label
                {
                    Text = $"\u26A0 DirectX 11 initialization failed:\n{ex.Message}\nFalling back to OpenGL.",
                    ForeColor = Color.FromArgb(200, 200, 210),
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 11f),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Dock = DockStyle.Fill
                };
                _hostPanel.Controls.Add(lbl);
            }
        }

        private unsafe void CreateRenderTargetView()
        {
            ComPtr<ID3D11Texture2D> backBuffer = default;
            SilkMarshal.ThrowHResult(
                _swapChain.Handle->GetBuffer(0, SilkMarshal.GuidPtrOf<ID3D11Texture2D>(), (void**)backBuffer.GetAddressOf()));

            ID3D11RenderTargetView* pRtv = null;
            SilkMarshal.ThrowHResult(
                _device.Handle->CreateRenderTargetView((ID3D11Resource*)backBuffer.Handle, null, &pRtv));
            _rtv = default;
            _rtv.Handle = pRtv;
            backBuffer.Dispose();
        }

        private unsafe void CreateBlendState()
        {
            var desc = new BlendDesc();
            desc.RenderTarget[0] = new RenderTargetBlendDesc
            {
                BlendEnable = 1,
                SrcBlend = Blend.SrcAlpha,
                DestBlend = Blend.InvSrcAlpha,
                BlendOp = BlendOp.Add,
                SrcBlendAlpha = Blend.One,
                DestBlendAlpha = Blend.InvSrcAlpha,
                BlendOpAlpha = BlendOp.Add,
                RenderTargetWriteMask = (byte)ColorWriteEnable.All,
            };
            ID3D11BlendState* pBs = null;
            SilkMarshal.ThrowHResult(_device.Handle->CreateBlendState(&desc, &pBs));
            _blendState = default;
            _blendState.Handle = pBs;
        }

        private unsafe void CreateSampler()
        {
            var desc = new SamplerDesc
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxAnisotropy = 1,
                ComparisonFunc = ComparisonFunc.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
            };
            ID3D11SamplerState* pSs = null;
            SilkMarshal.ThrowHResult(_device.Handle->CreateSamplerState(&desc, &pSs));
            _sampler = default;
            _sampler.Handle = pSs;
        }

        private unsafe void CreateRasterizerState()
        {
            var desc = new RasterizerDesc
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.None,
                FrontCounterClockwise = 0,
                DepthClipEnable = 1,
                ScissorEnable = 0,
                MultisampleEnable = 0,
                AntialiasedLineEnable = 0,
            };
            ID3D11RasterizerState* pRs = null;
            SilkMarshal.ThrowHResult(_device.Handle->CreateRasterizerState(&desc, &pRs));
            _rasterizerState = default;
            _rasterizerState.Handle = pRs;
        }

        private unsafe void CreateGeometry()
        {
            // Shared fullscreen quad (positions + texcoords) â€” same layout as GL
            float[] vertices = {
                -1f, -1f, 0f, 0f,
                 1f, -1f, 1f, 0f,
                 1f,  1f, 1f, 1f,
                -1f,  1f, 0f, 1f
            };
            uint[] indices = { 0, 1, 2, 2, 3, 0 };

            _quadVB = CreateStaticBuffer(vertices, BindFlag.VertexBuffer);
            _quadIB = CreateIndexBuffer(indices);

            // Dynamic overlay VB (128 verts * 3 floats = crosshair lines)
            var ovDesc = new BufferDesc
            {
                ByteWidth = 128 * 3 * sizeof(float),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };
            ID3D11Buffer* pOvBuf = null;
            SilkMarshal.ThrowHResult(_device.Handle->CreateBuffer(&ovDesc, null, &pOvBuf));
            _overlayVB = default;
            _overlayVB.Handle = pOvBuf;
        }

        private unsafe ComPtr<ID3D11Buffer> CreateStaticBuffer(float[] data, BindFlag bindFlag)
        {
            var desc = new BufferDesc
            {
                ByteWidth = (uint)(data.Length * sizeof(float)),
                Usage = Usage.Default,
                BindFlags = (uint)bindFlag,
            };
            ID3D11Buffer* pBuf = null;
            fixed (float* pData = data)
            {
                var init = new SubresourceData { PSysMem = pData };
                SilkMarshal.ThrowHResult(_device.Handle->CreateBuffer(&desc, &init, &pBuf));
            }
            ComPtr<ID3D11Buffer> buf = default;
            buf.Handle = pBuf;
            return buf;
        }

        private unsafe ComPtr<ID3D11Buffer> CreateIndexBuffer(uint[] data)
        {
            var desc = new BufferDesc
            {
                ByteWidth = (uint)(data.Length * sizeof(uint)),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.IndexBuffer,
            };
            ID3D11Buffer* pBuf = null;
            fixed (uint* pData = data)
            {
                var init = new SubresourceData { PSysMem = pData };
                SilkMarshal.ThrowHResult(_device.Handle->CreateBuffer(&desc, &init, &pBuf));
            }
            ComPtr<ID3D11Buffer> buf = default;
            buf.Handle = pBuf;
            return buf;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Shader loading
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private unsafe void LoadShaders()
        {
            // === Tile shader ===
            var tileUniforms = new Dictionary<string, (bool, int, int)>
            {
                ["uTransform"] = (true, 0, 48),   // VS: float3x3 = 3 float4 rows = 48 bytes
                ["uOpacity"] = (false, 0, 4),      // PS
                ["uZoomNorm"] = (false, 4, 4),
                ["uEnableSaturation"] = (false, 8, 4),
                ["uEnableContrast"] = (false, 12, 4),
                ["uEnableVignette"] = (false, 16, 4),
                ["uEnableAtmosphere"] = (false, 20, 4),
            };

            _tileShader = CreateShaderFromFiles(
                "Rendering/DirectX/shaders/tile.vs.hlsl",
                "Rendering/DirectX/shaders/tile.ps.hlsl",
                GetQuadInputLayout(), tileUniforms, "tile");
            _tileShader?.SetFloat("uOpacity", 1.0f);

            // === Weather overlay shader ===
            var woUniforms = new Dictionary<string, (bool, int, int)>
            {
                ["uTransform"] = (true, 0, 48),    // VS
                ["uOpacity"] = (false, 0, 4),       // PS
                ["uTime"] = (false, 4, 4),
                ["uEnableGlow"] = (false, 8, 4),
            };

            _weatherOverlayShader = CreateShaderFromFiles(
                "Rendering/DirectX/shaders/weather_overlay.vs.hlsl",
                "Rendering/DirectX/shaders/weather_overlay.ps.hlsl",
                GetQuadInputLayout(), woUniforms, "weather_overlay");
            _weatherOverlayShader?.SetFloat("uOpacity", 1.0f);

            // === Overlay/crosshair shader ===
            var ovUniforms = new Dictionary<string, (bool, int, int)>
            {
                ["uOffset"] = (true, 0, 8),         // VS: float2
                ["uColor"] = (false, 0, 12),         // PS: float3
                ["uAlpha"] = (false, 12, 4),
                ["uTime"] = (false, 16, 4),
                ["uEnablePulse"] = (false, 20, 4),
            };

            _overlayShader = CreateShaderFromFiles(
                "Rendering/DirectX/shaders/overlay.vs.hlsl",
                "Rendering/DirectX/shaders/overlay.ps.hlsl",
                GetOverlayInputLayout(), ovUniforms, "overlay");

            // === General (vertex/fragment) shader ===
            var genUniforms = new Dictionary<string, (bool, int, int)>
            {
                ["uTransform"] = (true, 0, 48),     // VS
                ["uOpacity"] = (false, 0, 4),        // PS
            };

            _generalShader = CreateShaderFromFiles(
                "Rendering/DirectX/shaders/vertex.vs.hlsl",
                "Rendering/DirectX/shaders/fragment.ps.hlsl",
                GetQuadInputLayout(), genUniforms, "general");

            // === Station / epicenter marker shader ===
            var markerUniforms = new Dictionary<string, (bool, int, int)>
            {
                // VS cbuffer b0
                ["uNdcX"]        = (true,  0, 4),
                ["uNdcY"]        = (true,  4, 4),
                ["uHalfSizeX"]   = (true,  8, 4),
                ["uHalfSizeY"]   = (true, 12, 4),
                ["uMarkerType"]  = (true, 16, 4),
                // PS cbuffer b0
                ["uColorR"]      = (false,  0, 4),
                ["uColorG"]      = (false,  4, 4),
                ["uColorB"]      = (false,  8, 4),
                ["uColorA"]      = (false, 12, 4),
                ["uRingPhase"]   = (false, 16, 4),
                ["uSelected"]    = (false, 20, 4),
                ["uGlowStrength"]= (false, 24, 4),
            };
            _markerShader = CreateShaderFromFiles(
                "Rendering/DirectX/shaders/station_marker.vs.hlsl",
                "Rendering/DirectX/shaders/station_marker.ps.hlsl",
                GetQuadInputLayout(), markerUniforms, "station_marker");
        }

        private unsafe DXShader? CreateShaderFromFiles(
            string vsPath, string psPath,
            InputElementDesc[] inputLayout,
            Dictionary<string, (bool, int, int)> uniforms,
            string name)
        {
            try
            {
                if (!TryReadShaderText(vsPath, out var vsSrc) || !TryReadShaderText(psPath, out var psSrc))
                {
                    Console.WriteLine($"[DXMapRenderer] Failed to load {name} shader source.");
                    return null;
                }

                return new DXShader(_device.Handle, _context.Handle, vsSrc, psSrc, "main", "main", inputLayout, uniforms);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DXMapRenderer] Failed to load {name} shader: {ex.Message}");
                return null;
            }
        }

        private static bool TryReadShaderText(string shaderPath, out string source)
        {
            if (TryReadShaderTextSingle(shaderPath, out source))
            {
                return true;
            }

            foreach (var alt in GetShaderPathAlternates(shaderPath))
            {
                if (TryReadShaderTextSingle(alt, out source))
                {
                    return true;
                }
            }

            source = string.Empty;
            return false;
        }

        private static bool TryReadShaderTextSingle(string shaderPath, out string source)
        {
            if (EmbeddedResourceLoader.TryReadText(shaderPath, out source))
            {
                return true;
            }

            var normalized = shaderPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            {
                source = File.ReadAllText(normalized);
                return true;
            }

            var combined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized);
            if (File.Exists(combined))
            {
                source = File.ReadAllText(combined);
                return true;
            }

            source = string.Empty;
            return false;
        }

        private static IEnumerable<string> GetShaderPathAlternates(string shaderPath)
        {
            if (shaderPath.EndsWith(".ps.hlsl", StringComparison.OrdinalIgnoreCase))
            {
                yield return shaderPath[..^8] + ".frag.hlsl";
                // MSBuild strips ".ps" (Pashto culture code) from embedded resource names
                yield return shaderPath[..^8] + ".hlsl";
            }
            else if (shaderPath.EndsWith(".vs.hlsl", StringComparison.OrdinalIgnoreCase))
            {
                yield return shaderPath[..^8] + ".vert.hlsl";
                yield return shaderPath[..^8] + ".hlsl";
            }
        }

        private unsafe InputElementDesc[] GetQuadInputLayout()
        {
            return new InputElementDesc[]
            {
                new()
                {
                    SemanticName = (byte*)_semanticPOSITION,
                    SemanticIndex = 0, Format = Format.FormatR32G32Float,
                    InputSlot = 0, AlignedByteOffset = 0,
                    InputSlotClass = InputClassification.PerVertexData, InstanceDataStepRate = 0
                },
                new()
                {
                    SemanticName = (byte*)_semanticTEXCOORD,
                    SemanticIndex = 0, Format = Format.FormatR32G32Float,
                    InputSlot = 0, AlignedByteOffset = 8,
                    InputSlotClass = InputClassification.PerVertexData, InstanceDataStepRate = 0
                }
            };
        }

        private unsafe InputElementDesc[] GetOverlayInputLayout()
        {
            return new InputElementDesc[]
            {
                new()
                {
                    SemanticName = (byte*)_semanticPOSITION,
                    SemanticIndex = 0, Format = Format.FormatR32G32Float,
                    InputSlot = 0, AlignedByteOffset = 0,
                    InputSlotClass = InputClassification.PerVertexData, InstanceDataStepRate = 0
                },
                new()
                {
                    SemanticName = (byte*)_semanticTEXCOORD,
                    SemanticIndex = 0, Format = Format.FormatR32Float,
                    InputSlot = 0, AlignedByteOffset = 8,
                    InputSlotClass = InputClassification.PerVertexData, InstanceDataStepRate = 0
                }
            };
        }

        private void CreateFallbackTile()
        {
            using var bmp = new Bitmap(256, 256);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(245, 245, 235));
            using var pen = new Pen(Color.FromArgb(220, 220, 200));
            for (int x = 0; x < 256; x += 32) g.DrawLine(pen, x, 0, x, 256);
            for (int y = 0; y < 256; y += 32) g.DrawLine(pen, 0, y, 256, y);
            using var brush = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString("No Tile", new Font("Segoe UI", 12, FontStyle.Bold), brush, 8, 8);
            _fallbackTileSrv = UploadBitmapToSrv(bmp);
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Resize handling
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private unsafe void HandleResize()
        {
            if (!_initialized || _disposed || _swapChain.Handle == null) return;

            uint w = (uint)_hostPanel.Width, h = (uint)_hostPanel.Height;
            if (w == 0 || h == 0) return;

            // Release ALL pipeline references to the back buffer.
            // ClearState resets every stage (OM, VS, PS, etc.) so nothing holds a ref.
            _context.Handle->ClearState();
            _context.Handle->Flush();

            _rtv.Dispose();
            _rtv = default;

            SilkMarshal.ThrowHResult(
                _swapChain.Handle->ResizeBuffers(0, w, h, Format.FormatUnknown,
                    (uint)SwapChainFlag.AllowTearing));

            CreateRenderTargetView();
            UpdateOverlayVertices();
            _hostPanel.Invalidate();
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Render frame (10-step pipeline â€” mirrors GLRadarControl_Paint)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private unsafe void RenderFrame()
        {
            if (!_initialized || _disposed || _rtv.Handle == null) return;

            int w = _hostPanel.Width, h = _hostPanel.Height;
            if (w <= 0 || h <= 0) return;

            // FPS tracking
            _frameCount++;
            double elapsed = _fpsTimer.Elapsed.TotalSeconds;
            if (elapsed >= 0.5)
            {
                _currentFps = (float)(_frameCount / elapsed);
                _frameCount = 0;
                _fpsTimer.Restart();
            }

            // Set render target + viewport
            var rtv = _rtv.GetAddressOf();
            _context.Handle->OMSetRenderTargets(1, rtv, (ID3D11DepthStencilView*)null);

            var vp = new Viewport(0, 0, w, h, 0f, 1f);
            _context.Handle->RSSetViewports(1, &vp);
            _context.Handle->RSSetState(_rasterizerState);

            // Clear to dark background
            float* clearColor = stackalloc float[4] { 0.12f, 0.12f, 0.12f, 1f };
            _context.Handle->ClearRenderTargetView(_rtv, clearColor);

            // Set blend state and sampler
            float* bf = stackalloc float[4] { 0, 0, 0, 0 };
            _context.Handle->OMSetBlendState(_blendState, bf, 0xffffffff);
            var samp = _sampler.GetAddressOf();
            _context.Handle->PSSetSamplers(0, 1, samp);

            float time = (float)_elapsedTimer.Elapsed.TotalSeconds;

            // â”€â”€â”€ Step 1-3: Map tiles â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            RenderMapTiles(w, h);

            // â”€â”€â”€ Step 4: Positioned overlay 1 (radar) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_hasPositionedOverlay && _overlaySrv.Handle != null)
                RenderPositionedOverlay(_overlaySrv, _overlayMinLat, _overlayMinLon, _overlayMaxLat, _overlayMaxLon, _overlayOpacity, time, w, h);

            // â”€â”€â”€ Step 5: Positioned overlay 2 (temperature) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_hasPositionedOverlay2 && _overlay2Srv.Handle != null)
                RenderPositionedOverlay(_overlay2Srv, _overlay2MinLat, _overlay2MinLon, _overlay2MaxLat, _overlay2MaxLon, _overlay2Opacity, time, w, h);
            // ── Step 5b: Positioned overlay 3 (GRIB2 forecast) ──────────
            if (_hasPositionedOverlay3 && _overlay3Srv.Handle != null)
                RenderPositionedOverlay(_overlay3Srv, _overlay3MinLat, _overlay3MinLon, _overlay3MaxLat, _overlay3MaxLon, _overlay3Opacity, time, w, h);

            // â”€â”€â”€ Step 6: Radar frames â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            RenderStationMarkersPass(w, h);
            RenderRadarFrames(time, w, h);

            // â”€â”€â”€ Step 7: Crosshair â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            RenderCrosshair(time, w, h);

            // â”€â”€â”€ Step 8: User marker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            RenderUserMarker(w, h);

            // â”€â”€â”€ Step 9-11: HUD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            RenderHUD(w, h);

            // Mark loading complete once we have some tiles
            if (_mapLoading && _tileTextures.Count > 0) _mapLoading = false;

            // Present (vsync controlled by EnableVSync property)
            _swapChain.Handle->Present(EnableVSync ? 1u : 0u, 0);
        }

        // â”€â”€â”€ Tile rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private unsafe void RenderMapTiles(int w, int h)
        {
            if (_tileShader == null) return;

            float zoomNorm = Math.Clamp(_mapZoom / 20f, 0f, 1f);
            _tileShader.SetFloat("uZoomNorm", zoomNorm);
            _tileShader.SetBool("uEnableSaturation", EnableTileSaturation);
            _tileShader.SetBool("uEnableContrast", EnableTileContrast);
            _tileShader.SetBool("uEnableVignette", EnableTileVignette);
            _tileShader.SetBool("uEnableAtmosphere", EnableTileAtmosphere);
            _tileShader.SetFloat("uOpacity", 1f);

            BindQuadGeometry();

            if (_hasBackgroundTexture && _backgroundSrv.Handle != null && _bgSourceZoom != 0)
            {
                // Pre-composited background with geo-positioning
                int z = _mapZoom;
                double cx = LonToPixelX(_centerLon, z), cy = LatToPixelY(_centerLat, z);
                double imgCenterPx = LonToPixelX(_bgCenterLon, z), imgCenterPy = LatToPixelY(_bgCenterLat, z);
                double scaleFactor = Math.Pow(2.0, z - _bgSourceZoom);
                double imgW = _bgPixelWidth * scaleFactor, imgH = _bgPixelHeight * scaleFactor;
                double screenCx = (imgCenterPx - cx) + w / 2.0, screenCy = (imgCenterPy - cy) + h / 2.0;
                float imgWnd = (float)(imgW / (w / 2.0)), imgHnd = (float)(imgH / (h / 2.0));
                float tileSx = imgWnd / 2f * _zoom, tileSy = imgHnd / 2f * _zoom;
                float ndcX = (float)(screenCx / (w / 2.0) - 1.0) * _zoom + _panX;
                float ndcY = (float)(1.0 - screenCy / (h / 2.0)) * _zoom + _panY;

                float[] tmat = { tileSx, 0f, 0f, 0f, tileSy, 0f, ndcX, ndcY, 1f };
                _tileShader.SetMatrix3("uTransform", tmat);
                _tileShader.Use();

                var srv = _backgroundSrv.GetAddressOf();
                _context.Handle->PSSetShaderResources(0, 1, (ID3D11ShaderResourceView**)srv);
                _context.Handle->DrawIndexed(6, 0, 0);
            }
            else
            {
                // Per-tile rendering
                int z = _mapZoom;
                double centerPxX = LonToPixelX(_centerLon, z), centerPxY = LatToPixelY(_centerLat, z);
                int extraTiles = _zoom < 1f ? (int)Math.Ceiling(1.0 / _zoom) : 0;
                int tilesWide = (int)Math.Ceiling((double)w / 256.0) + 2 + extraTiles * 2;
                int tilesHigh = (int)Math.Ceiling((double)h / 256.0) + 2 + extraTiles * 2;
                int centerTileX = (int)Math.Floor(centerPxX / 256.0);
                int centerTileY = (int)Math.Floor(centerPxY / 256.0);

                for (int dx = -tilesWide / 2; dx <= tilesWide / 2; dx++)
                {
                    for (int dy = -tilesHigh / 2; dy <= tilesHigh / 2; dy++)
                    {
                        int tileX = centerTileX + dx, tileY = centerTileY + dy;
                        int wrap = 1 << z;
                        int wrappedX = ((tileX % wrap) + wrap) % wrap;
                        if (tileY < 0 || tileY >= wrap) continue;

                        var key = (z, wrappedX, tileY);
                        ComPtr<ID3D11ShaderResourceView> tileSrv;
                        if (_tileTextures.TryGetValue(key, out var existingSrv))
                        {
                            tileSrv = existingSrv;
                            _tileLastUsed[key] = DateTime.UtcNow.Ticks;
                        }
                        else
                        {
                            tileSrv = _fallbackTileSrv;
                            if (!_blockedTiles.ContainsKey(key))
                                _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                        }

                        // Per-tile transform matrix (same math as GLRadarControl)
                        double tilePx = tileX * 256.0, tilePy = tileY * 256.0;
                        double screenCx = (tilePx - centerPxX) + w / 2.0 + 128.0;
                        double screenCy = (tilePy - centerPxY) + h / 2.0 + 128.0;
                        float tileW = (float)(256.0 / (w / 2.0)) * _zoom;
                        float tileH = (float)(256.0 / (h / 2.0)) * _zoom;
                        float ndcX = ((float)(screenCx / (w / 2.0) - 1.0)) * _zoom + _panX;
                        float ndcY = ((float)(1.0 - screenCy / (h / 2.0))) * _zoom + _panY;
                        float halfPixNdcX = _zoom / (float)w, halfPixNdcY = _zoom / (float)h;
                        float tileSx = tileW / 2f + halfPixNdcX, tileSy = tileH / 2f + halfPixNdcY;

                        float[] tmat = { tileSx, 0f, 0f, 0f, tileSy, 0f, ndcX, ndcY, 1f };
                        _tileShader.SetMatrix3("uTransform", tmat);
                        _tileShader.Use();

                        var pSrv = tileSrv.GetAddressOf();
                        _context.Handle->PSSetShaderResources(0, 1, (ID3D11ShaderResourceView**)pSrv);
                        _context.Handle->DrawIndexed(6, 0, 0);
                    }
                }
            }
        }

        // â”€â”€â”€ Positioned overlay rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private unsafe void RenderPositionedOverlay(
            ComPtr<ID3D11ShaderResourceView> overlaySrv,
            double minLat, double minLon, double maxLat, double maxLon,
            float opacity, float time, int w, int h)
        {
            var shader = _weatherOverlayShader ?? _tileShader;
            if (shader == null) return;

            BindQuadGeometry();

            int z = _mapZoom;
            double cx = LonToPixelX(_centerLon, z), cy = LatToPixelY(_centerLat, z);
            double leftPx = LonToPixelX(minLon, z), rightPx = LonToPixelX(maxLon, z);
            double topPy = LatToPixelY(maxLat, z), bottomPy = LatToPixelY(minLat, z);
            double imgW = Math.Abs(rightPx - leftPx), imgH = Math.Abs(bottomPy - topPy);
            double imgCx = (leftPx + rightPx) / 2.0, imgCy = (topPy + bottomPy) / 2.0;
            double screenCx = (imgCx - cx) + w / 2.0, screenCy = (imgCy - cy) + h / 2.0;
            float wNdc = (float)(imgW / (w / 2.0)) * _zoom, hNdc = (float)(imgH / (h / 2.0)) * _zoom;
            float sx = wNdc / 2f, sy = hNdc / 2f;
            float ndcX = ((float)(screenCx / (w / 2.0) - 1.0)) * _zoom + _panX;
            float ndcY = ((float)(1.0 - screenCy / (h / 2.0))) * _zoom + _panY;

            float[] tmat = { sx, 0f, 0f, 0f, sy, 0f, ndcX, ndcY, 1f };
            shader.SetMatrix3("uTransform", tmat);
            shader.SetFloat("uOpacity", opacity);
            shader.SetFloat("uTime", time);
            shader.SetBool("uEnableGlow", EnableRadarGlow);
            shader.Use();

            var srv = overlaySrv.GetAddressOf();
            _context.Handle->PSSetShaderResources(0, 1, (ID3D11ShaderResourceView**)srv);
            _context.Handle->DrawIndexed(6, 0, 0);

            shader.SetFloat("uOpacity", 1f);
        }

        private unsafe void RenderStationMarkersPass(int w, int h)
        {
            if (_markerShader == null) return;

            StationMarkerEntry[]   stations;
            EpicenterMarkerEntry[] epicenters;
            float epPhase, mrPhase;
            lock (_markerLock)
            {
                stations   = _stationMarkers;
                epicenters = _epicenterMarkers;
                epPhase    = _markerEpicenterPhase;
                mrPhase    = _markerMostRecentPhase;
            }
            if (stations.Length == 0 && epicenters.Length == 0) return;

            BindQuadGeometry();
            int    z  = _mapZoom;
            double cx = LonToPixelX(_centerLon, z);
            double cy = LatToPixelY(_centerLat, z);

            foreach (var s in stations)
            {
                double rawSx = LonToPixelX(s.Lon, z) - cx + w / 2.0;
                double rawSy = LatToPixelY(s.Lat, z) - cy + h / 2.0;
                float ndcX = ((float)(rawSx / (w / 2.0) - 1.0)) * _zoom + _panX;
                float ndcY = ((float)(1.0 - rawSy / (h / 2.0))) * _zoom + _panY;
                if (ndcX < -1.5f || ndcX > 1.5f || ndcY < -1.5f || ndcY > 1.5f) continue;

                float pxSize    = s.Selected ? 26f : 19f;
                float halfSizeX = pxSize / (float)(w / 2.0);
                float halfSizeY = pxSize / (float)(h / 2.0);
                var   col       = System.Drawing.Color.FromArgb(s.ColorArgb);

                _markerShader.SetFloat("uNdcX",       ndcX);
                _markerShader.SetFloat("uNdcY",       ndcY);
                _markerShader.SetFloat("uHalfSizeX",  halfSizeX);
                _markerShader.SetFloat("uHalfSizeY",  halfSizeY);
                _markerShader.SetFloat("uMarkerType", 0f);
                _markerShader.SetFloat("uColorR",     col.R / 255f);
                _markerShader.SetFloat("uColorG",     col.G / 255f);
                _markerShader.SetFloat("uColorB",     col.B / 255f);
                _markerShader.SetFloat("uColorA",     col.A / 255f);
                _markerShader.SetFloat("uRingPhase",  0f);
                _markerShader.SetFloat("uSelected",   s.Selected ? 1f : 0f);
                _markerShader.SetFloat("uGlowStrength", s.Selected ? 1.5f : 1.0f);
                _markerShader.Use();
                _context.Handle->DrawIndexed(6, 0, 0);
            }

            foreach (var e in epicenters)
            {
                double rawEx = LonToPixelX(e.Lon, z) - cx + w / 2.0;
                double rawEy = LatToPixelY(e.Lat, z) - cy + h / 2.0;
                float ndcX = ((float)(rawEx / (w / 2.0) - 1.0)) * _zoom + _panX;
                float ndcY = ((float)(1.0 - rawEy / (h / 2.0))) * _zoom + _panY;
                if (ndcX < -1.5f || ndcX > 1.5f || ndcY < -1.5f || ndcY > 1.5f) continue;

                float phase     = e.IsMostRecent ? mrPhase : epPhase;
                float halfSizeX = 95f / (float)(w / 2.0);
                float halfSizeY = 95f / (float)(h / 2.0);
                var   col       = System.Drawing.Color.FromArgb(e.ColorArgb);

                _markerShader.SetFloat("uNdcX",       ndcX);
                _markerShader.SetFloat("uNdcY",       ndcY);
                _markerShader.SetFloat("uHalfSizeX",  halfSizeX);
                _markerShader.SetFloat("uHalfSizeY",  halfSizeY);
                _markerShader.SetFloat("uMarkerType", 1f);
                _markerShader.SetFloat("uColorR",     col.R / 255f);
                _markerShader.SetFloat("uColorG",     col.G / 255f);
                _markerShader.SetFloat("uColorB",     col.B / 255f);
                _markerShader.SetFloat("uColorA",     col.A / 255f);
                _markerShader.SetFloat("uRingPhase",  phase);
                _markerShader.SetFloat("uSelected",   0f);
                _markerShader.SetFloat("uGlowStrength", 1.0f);
                _markerShader.Use();
                _context.Handle->DrawIndexed(6, 0, 0);
            }
        }

        // â”€â”€â”€ Radar frames â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private unsafe void RenderRadarFrames(float time, int w, int h)
        {
            if (_radarFrames.Count == 0) return;
            var shader = _weatherOverlayShader ?? _tileShader;
            if (shader == null) return;

            BindQuadGeometry();
            shader.SetFloat("uTime", time);
            shader.SetBool("uEnableGlow", EnableRadarGlow);

            for (int i = 0; i < _radarFrames.Count; i++)
            {
                float alpha = (float)(i + 1) / (_radarFrames.Count + 1);
                float[] tmat = { _zoom, 0f, 0f, 0f, _zoom, 0f, _panX, _panY, 1f };
                shader.SetMatrix3("uTransform", tmat);
                shader.SetFloat("uOpacity", alpha);
                shader.Use();

                var srv = _radarFrames[i].GetAddressOf();
                _context.Handle->PSSetShaderResources(0, 1, (ID3D11ShaderResourceView**)srv);
                _context.Handle->DrawIndexed(6, 0, 0);
            }
        }

        // â”€â”€â”€ Crosshair â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private unsafe void RenderCrosshair(float time, int w, int h)
        {
            if (!ShowCrosshair || _overlayShader == null || _crosshairVertexCount == 0) return;

            bool drawAtMouse = UseCrosshairAsMouse && _mouseInside && _mouseScreenPos.X >= 0;
            if (UseCrosshairAsMouse && !drawAtMouse) return;

            float ndcOffsetX = 0, ndcOffsetY = 0;
            if (drawAtMouse && w > 0 && h > 0)
            {
                ndcOffsetX = (2f * _mouseScreenPos.X / w) - 1f;
                ndcOffsetY = 1f - (2f * _mouseScreenPos.Y / h);
            }

            _overlayShader.SetVec2("uOffset", ndcOffsetX, ndcOffsetY);
            _overlayShader.SetVec3("uColor", 0.6f, 1f, 0.2f);
            _overlayShader.SetFloat("uAlpha", 1f);
            _overlayShader.SetFloat("uTime", time);
            _overlayShader.SetBool("uEnablePulse", UseCrosshairAsMouse || EnableCrosshairPulse);
            _overlayShader.Use();

            uint stride = 3 * sizeof(float);
            uint offset = 0;
            var vb = _overlayVB.GetAddressOf();
            _context.Handle->IASetVertexBuffers(0, 1, vb, &stride, &offset);
            _context.Handle->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
            _context.Handle->Draw((uint)_crosshairVertexCount, 0);
        }

        // â”€â”€â”€ User marker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void RenderUserMarker(int w, int h)
        {
            if (!_showUserMarker || _hudRenderer == null || !_hudRenderer.IsInitialized || w <= 0 || h <= 0) return;

            int z = _mapZoom;
            double cx = LonToPixelX(_centerLon, z), cy = LatToPixelY(_centerLat, z);
            double markerPx = LonToPixelX(_userMarkerLon, z), markerPy = LatToPixelY(_userMarkerLat, z);
            double screenX = (markerPx - cx) * _zoom + w / 2.0 + _panX * w / 2.0;
            double screenY = (markerPy - cy) * _zoom + h / 2.0 - _panY * h / 2.0;

            if (screenX < -20 || screenX > w + 20 || screenY < -20 || screenY > h + 20) return;

            _hudRenderer.BeginFrame(w, h);
            float mx = (float)screenX, my = (float)screenY;
            float half = 5f;
            _hudRenderer.DrawRect(mx - half - 2f, my - half - 2f, 14f, 14f, 0f, 0f, 0f, 0.9f);
            _hudRenderer.DrawRect(mx - half, my - half, 10f, 10f, 0.2f, 0.45f, 1f, 0.9f);
            _hudRenderer.DrawRect(mx - 1.5f, my - 1.5f, 3f, 3f, 1f, 1f, 1f, 0.9f);
            _hudRenderer.EndFrame();
        }

        // â”€â”€â”€ HUD rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void RenderHUD(int w, int h)
        {
            if (_hudRenderer == null || !_hudRenderer.IsInitialized) return;

            _hudRenderer.BeginFrame(w, h);

            // Attribution text (bottom-left)
            if (!string.IsNullOrEmpty(HudAttributionText))
            {
                float textW = _hudRenderer.MeasureTextWidth(HudAttributionText);
                float lh = _hudRenderer.LineHeight, pad = 6f;
                float barH = lh + pad * 2, barW = textW + pad * 2;
                _hudRenderer.DrawRect(0, h - barH, barW, barH, 0f, 0f, 0f, 0.55f);
                _hudRenderer.DrawText(HudAttributionText, pad, h - barH + pad, 0.82f, 0.82f, 0.82f, 0.85f);
            }

            // Status bar (bottom-right)
            if (ShowStatusBar && !string.IsNullOrEmpty(HudStatusBarText))
            {
                float textW = _hudRenderer.MeasureTextWidth(HudStatusBarText);
                float lh = _hudRenderer.LineHeight, pad = 6f;
                float barH = lh + pad * 2, barW = textW + pad * 2;
                _hudRenderer.DrawRect(w - barW, h - barH, barW, barH, 0f, 0f, 0f, StatusBarOpacity);
                _hudRenderer.DrawText(HudStatusBarText, w - barW + pad, h - barH + pad, 0.82f, 0.82f, 0.82f, 0.85f);
            }

            // Center status text
            if (!string.IsNullOrEmpty(HudStatusText))
            {
                float textW = _hudRenderer.MeasureTextWidth(HudStatusText);
                float lh = _hudRenderer.LineHeight, pad = 6f;
                float barH = lh + pad * 2, barW = textW + pad * 2;
                float barX = (w - barW) / 2f;
                _hudRenderer.DrawRect(barX, h - barH - 4f, barW, barH, 0.05f, 0.05f, 0.08f, 0.85f);
                _hudRenderer.DrawText(HudStatusText, barX + pad, h - barH - 4f + pad, 1f, 1f, 1f, 1f);
            }

            // Coordinates HUD
            if (ShowCoordinatesHUD && ShowCrosshair)
            {
                double coordLat, coordLon;
                float anchorX, anchorY;
                bool drawAtMouse = UseCrosshairAsMouse && _mouseInside && _mouseScreenPos.X >= 0;

                if (drawAtMouse && w > 0 && h > 0)
                {
                    double cx = LonToPixelX(_centerLon, _mapZoom), cy = LatToPixelY(_centerLat, _mapZoom);
                    double mPxX = cx + (_mouseScreenPos.X - w / 2.0), mPxY = cy + (_mouseScreenPos.Y - h / 2.0);
                    coordLat = PixelYToLat(mPxY, _mapZoom);
                    coordLon = PixelXToLon(mPxX, _mapZoom);
                    anchorX = _mouseScreenPos.X + 20f;
                    anchorY = _mouseScreenPos.Y + 20f;
                }
                else
                {
                    coordLat = _centerLat;
                    coordLon = _centerLon;
                    anchorX = w / 2f + 20f;
                    anchorY = h / 2f + 15f;
                }

                string latDir = coordLat >= 0 ? "N" : "S", lonDir = coordLon >= 0 ? "E" : "W";
                string coordText = $"{Math.Abs(coordLat):F4}\u00b0{latDir}, {Math.Abs(coordLon):F4}\u00b0{lonDir}";
                float coordW = _hudRenderer.MeasureTextWidth(coordText);
                float lhc = _hudRenderer.LineHeight, padC = 5f;
                float cbH = lhc + padC * 2, cbW = coordW + padC * 2;
                float cbX = Math.Min(anchorX, w - cbW - 5f);
                float cbY = Math.Min(anchorY, h - cbH - 5f);
                _hudRenderer.DrawRect(cbX, cbY, cbW, cbH, 0.05f, 0.05f, 0.08f, 0.65f);
                _hudRenderer.DrawText(coordText, cbX + padC, cbY + padC, 0.6f, 1f, 0.2f, 0.95f);
            }

            // Scale bar
            if (ShowRuler)
            {
                double mpp = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0) / Math.Pow(2.0, _mapZoom);
                double[] distances = { 5000000, 2000000, 1000000, 500000, 200000, 100000, 50000, 20000, 10000, 5000, 2000, 1000, 500, 200, 100, 50, 20, 10, 5 };
                double chosenDist = 1000;
                foreach (var d in distances)
                {
                    double pixels = d / mpp;
                    if (pixels >= 60 && pixels <= 250) { chosenDist = d; break; }
                }
                float barPx = (float)(chosenDist / mpp);
                string distLabel = chosenDist >= 1000 ? $"{chosenDist / 1000:F0} km" : $"{chosenDist:F0} m";
                float lhS = _hudRenderer.LineHeight;
                float attrBarH = lhS + 12f;
                float scaleY = h - attrBarH - 16f;
                float labelW = _hudRenderer.MeasureTextWidth(distLabel);
                float bgW = Math.Max(barPx, labelW) + 16f;
                float bgH = 6f + lhS + 16f + 4f;
                float bgX = 10f, bgY = scaleY - lhS - 9f - 4f;
                _hudRenderer.DrawRect(bgX, bgY, bgW, bgH, 0f, 0f, 0f, 0.5f);
                float lineX = bgX + (bgW - barPx) / 2f;
                _hudRenderer.DrawRect(lineX, scaleY, barPx, 6f, 1f, 1f, 1f, 0.9f);
                _hudRenderer.DrawRect(lineX, scaleY - 4f, 2f, 14f, 1f, 1f, 1f, 0.9f);
                _hudRenderer.DrawRect(lineX + barPx - 2f, scaleY - 4f, 2f, 14f, 1f, 1f, 1f, 0.9f);
                float labelX = lineX + (barPx - labelW) / 2f;
                _hudRenderer.DrawText(distLabel, labelX, scaleY - lhS - 4f, 1f, 1f, 1f, 0.9f);
            }

            _hudRenderer.EndFrame();

            // Interactive HUD panels (HudSystem)
            if (HudSystem != null)
            {
                _hudRenderer.BeginFrame(w, h);
                HudSystem.Render(_hudRenderer, w, h);
                _hudRenderer.EndFrame();
            }

            // Loading overlay
            if (_mapLoading)
            {
                _hudRenderer.BeginFrame(w, h);
                _hudRenderer.DrawRect(0, 0, w, h, 0.06f, 0.06f, 0.09f, 0.92f);
                float t = (float)_elapsedTimer.Elapsed.TotalSeconds;
                int dotCount = ((int)(t * 2.5f)) % 4;
                string loadingText = $"Loading Map{new string('.', dotCount)}";
                float textW = _hudRenderer.MeasureTextWidth(loadingText);
                float lh = _hudRenderer.LineHeight;
                float pulse = 0.7f + 0.3f * (float)Math.Sin(t * 2.0);
                _hudRenderer.DrawText(loadingText, (w - textW) / 2f, (h - lh) / 2f, 0.75f, 0.82f, 0.95f, pulse);
                string sub = "Fetching tiles...";
                float subW = _hudRenderer.MeasureTextWidth(sub);
                _hudRenderer.DrawText(sub, (w - subW) / 2f, (h - lh) / 2f + lh + 8f, 0.5f, 0.5f, 0.6f, 0.6f);
                _hudRenderer.EndFrame();
            }
        }

        // â”€â”€â”€ Geometry helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private unsafe void BindQuadGeometry()
        {
            uint stride = 4 * sizeof(float);
            uint offset = 0;
            var vb = _quadVB.GetAddressOf();
            _context.Handle->IASetVertexBuffers(0, 1, vb, &stride, &offset);
            _context.Handle->IASetIndexBuffer(_quadIB, Format.FormatR32Uint, 0);
            _context.Handle->IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
        }

        private unsafe void UpdateOverlayVertices()
        {
            int w = _hostPanel.Width, h = _hostPanel.Height;
            if (w <= 0 || h <= 0) return;

            int lenPx = 20;
            float halfW = 3.5f;
            float hx = (lenPx * 2f) / Math.Max(1, w);
            float hy = (lenPx * 2f) / Math.Max(1, h);
            float wx = halfW * 2f / Math.Max(1, w);
            float wy = halfW * 2f / Math.Max(1, h);

            var verts = new List<float>();

            // Horizontal line quad (2 triangles)
            verts.AddRange(new float[] { -hx, +wy, -1f, -hx, -wy, +1f, +hx, +wy, -1f });
            verts.AddRange(new float[] { +hx, +wy, -1f, -hx, -wy, +1f, +hx, -wy, +1f });

            // Vertical line quad
            verts.AddRange(new float[] { -wx, -hy, -1f, +wx, -hy, +1f, -wx, +hy, -1f });
            verts.AddRange(new float[] { -wx, +hy, -1f, +wx, -hy, +1f, +wx, +hy, +1f });

            // Center dot
            float dotR = 3f / Math.Max(1, w), dotRy = 3f / Math.Max(1, h);
            verts.AddRange(new float[] { -dotR, +dotRy, 0f, -dotR, -dotRy, 0f, +dotR, +dotRy, 0f });
            verts.AddRange(new float[] { +dotR, +dotRy, 0f, -dotR, -dotRy, 0f, +dotR, -dotRy, 0f });

            _crosshairVertexCount = verts.Count / 3;
            float[] arr = verts.ToArray();

            if (_overlayVB.Handle == null) return;
            MappedSubresource mapped;
            SilkMarshal.ThrowHResult(
                _context.Handle->Map((ID3D11Resource*)_overlayVB.Handle, 0, Map.WriteDiscard, 0, &mapped));
            fixed (float* pData = arr)
                Unsafe.CopyBlock(mapped.PData, pData, (uint)(arr.Length * sizeof(float)));
            _context.Handle->Unmap((ID3D11Resource*)_overlayVB.Handle, 0);
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Texture upload helpers
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private unsafe ComPtr<ID3D11ShaderResourceView> UploadBitmapToSrv(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var texDesc = new Texture2DDesc
                {
                    Width = (uint)bmp.Width,
                    Height = (uint)bmp.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.FormatB8G8R8A8Unorm,
                    SampleDesc = new SampleDesc(1, 0),
                    Usage = Usage.Default,
                    BindFlags = (uint)BindFlag.ShaderResource,
                };

                var initData = new SubresourceData
                {
                    PSysMem = (void*)data.Scan0,
                    SysMemPitch = (uint)data.Stride,
                };

                ID3D11Texture2D* pTex = null;
                SilkMarshal.ThrowHResult(_device.Handle->CreateTexture2D(&texDesc, &initData, &pTex));

                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = Format.FormatB8G8R8A8Unorm,
                    ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D,
                };
                srvDesc.Texture2D.MostDetailedMip = 0;
                srvDesc.Texture2D.MipLevels = 1;

                ID3D11ShaderResourceView* pSrv = null;
                SilkMarshal.ThrowHResult(
                    _device.Handle->CreateShaderResourceView((ID3D11Resource*)pTex, &srvDesc, &pSrv));
                if (pTex != null) pTex->Release();

                ComPtr<ID3D11ShaderResourceView> srv = default;
                srv.Handle = pSrv;
                return srv;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private unsafe ComPtr<ID3D11ShaderResourceView> UploadPixelsToSrv(byte[] pixels, int width, int height)
        {
            var texDesc = new Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)BindFlag.ShaderResource,
            };

            fixed (byte* pData = pixels)
            {
                var initData = new SubresourceData
                {
                    PSysMem = pData,
                    SysMemPitch = (uint)(width * 4),
                };

                ID3D11Texture2D* pTex = null;
                SilkMarshal.ThrowHResult(_device.Handle->CreateTexture2D(&texDesc, &initData, &pTex));

                var srvDesc = new ShaderResourceViewDesc
                {
                    Format = Format.FormatB8G8R8A8Unorm,
                    ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D,
                };
                srvDesc.Texture2D.MostDetailedMip = 0;
                srvDesc.Texture2D.MipLevels = 1;

                ID3D11ShaderResourceView* pSrv = null;
                SilkMarshal.ThrowHResult(
                    _device.Handle->CreateShaderResourceView((ID3D11Resource*)pTex, &srvDesc, &pSrv));
                if (pTex != null) pTex->Release();

                ComPtr<ID3D11ShaderResourceView> srv = default;
                srv.Handle = pSrv;
                return srv;
            }
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // IMapRenderer methods â€” overlay + navigation
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        public void SetImageBytes(byte[] data) => SetImageBytes(data, null, null, null);

        public void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            if (_hostPanel.InvokeRequired)
            {
                _hostPanel.BeginInvoke(new Action(() => SetImageBytes(data, sourceCenterLat, sourceCenterLon, sourceZoom)));
                return;
            }
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                bool hasAlpha = Image.IsAlphaPixelFormat(bmp.PixelFormat);
                bool anyTransparent = false;
                if (hasAlpha)
                {
                    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = bd.Stride;
                        byte[] buf = new byte[Math.Abs(stride) * bmp.Height];
                        Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                        for (int y = 0; y < bmp.Height && !anyTransparent; y += 8)
                            for (int x = 0; x < bmp.Width; x += 8)
                            {
                                int idx = y * stride + x * 4;
                                if (idx + 3 < buf.Length && buf[idx + 3] < 8) { anyTransparent = true; break; }
                            }
                    }
                    finally { bmp.UnlockBits(bd); }
                }

                if (!hasAlpha || !anyTransparent)
                {
                    // Background composite
                    foreach (var f in _radarFrames) f.Dispose();
                    _radarFrames.Clear();
                    _backgroundSrv.Dispose(); _backgroundSrv = default;
                    if (sourceCenterLat.HasValue && sourceCenterLon.HasValue && sourceZoom.HasValue)
                    {
                        _bgCenterLat = sourceCenterLat.Value; _bgCenterLon = sourceCenterLon.Value;
                        _bgSourceZoom = sourceZoom.Value; _bgPixelWidth = bmp.Width; _bgPixelHeight = bmp.Height;
                    }
                    _backgroundSrv = UploadBitmapToSrv(bmp);
                    _hasBackgroundTexture = true;
                    BackgroundTextureChanged?.Invoke(true);
                }
                else
                {
                    // Radar frame
                    if (_hasBackgroundTexture)
                    {
                        _backgroundSrv.Dispose(); _backgroundSrv = default;
                        _hasBackgroundTexture = false;
                        BackgroundTextureChanged?.Invoke(false);
                    }
                    var srv = UploadBitmapToSrv(bmp);
                    _radarFrames.Add(srv);
                    if (_radarFrames.Count > MAX_RADAR_FRAMES)
                    {
                        _radarFrames[0].Dispose();
                        _radarFrames.RemoveAt(0);
                    }
                }
                _hostPanel.Invalidate();
            }
            catch (Exception ex) { Console.WriteLine($"[DXMapRenderer] SetImageBytes error: {ex.Message}"); }
        }

        public void SetImageBytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            if (_hostPanel.InvokeRequired)
            {
                _hostPanel.BeginInvoke(new Action(() => SetImageBytes(data, minLat, minLon, maxLat, maxLon, sourceZoom)));
                return;
            }
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                _overlaySrv.Dispose(); _overlaySrv = default;
                _overlaySrv = UploadBitmapToSrv(bmp);
                unsafe
                {
                    if (_overlaySrv.Handle != null)
                    {
                        _overlayMinLat = minLat; _overlayMinLon = minLon;
                        _overlayMaxLat = maxLat; _overlayMaxLon = maxLon;
                        _hasPositionedOverlay = true;
                    }
                }
                _hostPanel.Invalidate();
            }
            catch (Exception ex) { Console.WriteLine($"[DXMapRenderer] SetImageBytes(bbox) error: {ex.Message}"); }
        }

        public void SetOverlay2Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            if (_hostPanel.InvokeRequired)
            {
                _hostPanel.BeginInvoke(new Action(() => SetOverlay2Bytes(data, minLat, minLon, maxLat, maxLon, sourceZoom)));
                return;
            }
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                _overlay2Srv.Dispose(); _overlay2Srv = default;
                _overlay2Srv = UploadBitmapToSrv(bmp);
                unsafe
                {
                    if (_overlay2Srv.Handle != null)
                    {
                        _overlay2MinLat = minLat; _overlay2MinLon = minLon;
                        _overlay2MaxLat = maxLat; _overlay2MaxLon = maxLon;
                        _hasPositionedOverlay2 = true;
                    }
                }
                _hostPanel.Invalidate();
            }
            catch (Exception ex) { Console.WriteLine($"[DXMapRenderer] SetOverlay2Bytes error: {ex.Message}"); }
        }

        public void ClearOverlay()
        {
            if (_hostPanel.InvokeRequired) { _hostPanel.BeginInvoke(new Action(ClearOverlay)); return; }
            _backgroundSrv.Dispose(); _backgroundSrv = default;
            _hasBackgroundTexture = false; BackgroundTextureChanged?.Invoke(false);
            foreach (var f in _radarFrames) f.Dispose(); _radarFrames.Clear();
            ClearPositionedOverlay();
            ClearPositionedOverlay2();
            ClearPositionedOverlay3();
            _hostPanel.Invalidate();
        }

        public void ClearPositionedOverlay()
        {
            if (_hostPanel.InvokeRequired) { _hostPanel.BeginInvoke(new Action(ClearPositionedOverlay)); return; }
            _overlaySrv.Dispose(); _overlaySrv = default;
            _hasPositionedOverlay = false;
            _hostPanel.Invalidate();
        }

        public void ClearPositionedOverlay2()
        {
            if (_hostPanel.InvokeRequired) { _hostPanel.BeginInvoke(new Action(ClearPositionedOverlay2)); return; }
            _overlay2Srv.Dispose(); _overlay2Srv = default;
            _hasPositionedOverlay2 = false;
            _hostPanel.Invalidate();
        }

        public void SetOverlay3Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            if (_hostPanel.InvokeRequired)
            {
                _hostPanel.BeginInvoke(new Action(() => SetOverlay3Bytes(data, minLat, minLon, maxLat, maxLon, sourceZoom)));
                return;
            }
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);
                _overlay3Srv.Dispose(); _overlay3Srv = default;
                _overlay3Srv = UploadBitmapToSrv(bmp);
                unsafe
                {
                    if (_overlay3Srv.Handle != null)
                    {
                        _overlay3MinLat = minLat; _overlay3MinLon = minLon;
                        _overlay3MaxLat = maxLat; _overlay3MaxLon = maxLon;
                        _hasPositionedOverlay3 = true;
                    }
                }
                _hostPanel.Invalidate();
            }
            catch (Exception ex) { Console.WriteLine($"[DXMapRenderer] SetOverlay3Bytes error: {ex.Message}"); }
        }

        public void ClearPositionedOverlay3()
        {
            if (_hostPanel.InvokeRequired) { _hostPanel.BeginInvoke(new Action(ClearPositionedOverlay3)); return; }
            _overlay3Srv.Dispose(); _overlay3Srv = default;
            _hasPositionedOverlay3 = false;
            _hostPanel.Invalidate();
        }

        public void SetStationMarkers(StationMarkerEntry[] markers)
        {
            lock (_markerLock) { _stationMarkers = markers; }
            _hostPanel.Invalidate();
        }

        public void SetEpicenterMarkers(EpicenterMarkerEntry[] epicenters)
        {
            lock (_markerLock) { _epicenterMarkers = epicenters; }
            _hostPanel.Invalidate();
        }

        public void SetMarkerAnimPhase(float epicenterPhase, float mostRecentPhase)
        {
            lock (_markerLock) { _markerEpicenterPhase = epicenterPhase; _markerMostRecentPhase = mostRecentPhase; }
            _hostPanel.Invalidate();
        }

        public void SetCenterLatLon(double lat, double lon)
        {
            _centerLat = lat; _centerLon = lon;
            UpdateTiles(); _hostPanel.Invalidate();
            try { MapPositionChanged?.Invoke(_centerLat, _centerLon); } catch { }
        }

        public void SetMapZoom(int z)
        {
            if (z == _mapZoom) return;
            _mapZoom = Math.Clamp(z, 0, 20);
            UpdateTiles(); _hostPanel.Invalidate();
            try { MapZoomChanged?.Invoke(_mapZoom); } catch { }
        }

        public void SetMapStyle(OpenMap.MapStyle style)
        {
            if (_tileProvider == null) _tileProvider = new TileProvider();
            _tileProvider.CurrentStyle = style;
            foreach (var kv in _tileTextures) kv.Value.Dispose();
            _tileTextures.Clear(); _tileLastUsed.Clear(); _blockedTiles.Clear();
            UpdateTiles(); _hostPanel.Invalidate();
        }

        public void SetLocalTilesFolder(string? folder)
        {
            _localTileFolder = folder;
            if (_tileProvider == null) _tileProvider = new TileProvider();
            _tileProvider.LocalTilesRoot = folder;
            UpdateTiles(); _hostPanel.Invalidate();
        }

        public void InvalidateView() => _hostPanel.Invalidate();

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Mouse input â€” mirrors GLRadarControl
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            if (HudSystem != null && HudSystem.ProcessMouseWheel(e.X, e.Y, e.Delta))
            { _hostPanel.Invalidate(); return; }

            float oldZoom = _zoom;
            float delta = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _zoom *= delta;
            _zoom = Math.Clamp(_zoom, 0.25f, 8f);

            // Cursor-centered pan adjustment
            int w = _hostPanel.Width, h = _hostPanel.Height;
            if (w > 0 && h > 0)
            {
                float nx = (2f * e.X / w) - 1f, ny = 1f - (2f * e.Y / h);
                _panX = nx - (nx - _panX) * (oldZoom / _zoom);
                _panY = ny - (ny - _panY) * (oldZoom / _zoom);
            }

            _hostPanel.Invalidate();
            IsSmoothZooming = true;

            // 300ms debounce â†’ snap to integer tile zoom
            _zoomSnapTimer?.Dispose();
            _zoomSnapTimer = new System.Threading.Timer(_ =>
            {
                try { if (_hostPanel.IsHandleCreated) _hostPanel.BeginInvoke(new Action(SnapTileZoom)); } catch { }
            }, null, 300, Timeout.Infinite);
        }

        private void SnapTileZoom()
        {
            int zoomDelta = (int)Math.Round(Math.Log(_zoom) / Math.Log(2));
            if (zoomDelta == 0 && Math.Abs(_zoom - 1f) < 0.05f)
            {
                _zoom = 1f; _panX = _panY = 0; IsSmoothZooming = false;
                _hostPanel.Invalidate(); return;
            }
            int targetZoom = Math.Clamp(_mapZoom + zoomDelta, 0, 20);
            _zoom = 1f; _panX = _panY = 0; IsSmoothZooming = false;
            if (targetZoom != _mapZoom) SetMapZoom(targetZoom); else _hostPanel.Invalidate();
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HudSystem != null && HudSystem.ProcessMouseDown(e.X, e.Y))
            { _hostPanel.Invalidate(); return; }
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _lastMousePos = e.Location;
                _hostPanel.Cursor = (UseCrosshairAsMouse && ShowCrosshair) ? _blankCursor : Cursors.SizeAll;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            _mouseScreenPos = e.Location;
            if (HudSystem != null)
            {
                bool overHud = HudSystem.ProcessMouseMove(e.X, e.Y);
                if (overHud && !_dragging) { _hostPanel.Cursor = Cursors.Hand; _hostPanel.Invalidate(); return; }
                if (!_dragging && !overHud) UpdateCursorStyle();
                if (overHud) _hostPanel.Invalidate();
            }
            if (UseCrosshairAsMouse && ShowCrosshair && !_dragging) _hostPanel.Invalidate();
            if (!_dragging) return;

            int dx = e.X - _lastMousePos.X, dy = e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;
            int w = _hostPanel.Width, h = _hostPanel.Height;
            if (w > 0 && h > 0)
            {
                double cx = LonToPixelX(_centerLon, _mapZoom), cy = LatToPixelY(_centerLat, _mapZoom);
                _centerLon = PixelXToLon(cx - dx, _mapZoom);
                _centerLat = PixelYToLat(cy - dy, _mapZoom);
                _hostPanel.Invalidate();
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (HudSystem != null && HudSystem.ProcessMouseUp(e.X, e.Y)) _hostPanel.Invalidate();
            if (e.Button == MouseButtons.Left)
            {
                bool wasDragging = _dragging;
                _dragging = false;
                UpdateCursorStyle();
                if (wasDragging)
                {
                    UpdateTiles();
                    try { MapPositionChanged?.Invoke(_centerLat, _centerLon); } catch { }
                }
            }
        }

        /// <summary>Set cursor style based on UseCrosshairAsMouse mode</summary>
        private void UpdateCursorStyle()
        {
            if (UseCrosshairAsMouse && ShowCrosshair && _mouseInside)
                _hostPanel.Cursor = _blankCursor;
            else
                _hostPanel.Cursor = Cursors.Default;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Tile loading (mirrors GLRadarControl)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private async Task EnsureTileLoadedAsync(int z, int x, int y)
        {
            try
            {
                if (_tileProvider == null) _tileProvider = new TileProvider();
                var key = (z, x, y);
                if (_tileTextures.ContainsKey(key)) return;
                if (_blockedTiles.TryGetValue(key, out var blockedAt))
                {
                    if (DateTime.UtcNow - blockedAt < BlockedTileTtl) return;
                    _blockedTiles.TryRemove(key, out _);
                }
                var loadTask = _pendingLoads.GetOrAdd(key, k => LoadTileInternalAsync(k.z, k.x, k.y));
                await loadTask;
            }
            catch { }
        }

        private async Task LoadTileInternalAsync(int z, int x, int y)
        {
            var key = (z, x, y);
            await _tileSemaphore.WaitAsync();
            try
            {
                var (bytes, status) = await _tileProvider!.GetTileBytesAsync(z, x, y);
                if (status == TileFetchStatus.Blocked) { _blockedTiles[key] = DateTime.UtcNow; return; }
                if (status == TileFetchStatus.NotFound || status == TileFetchStatus.Error || bytes == null)
                { _blockedTiles[key] = DateTime.UtcNow; return; }

                // Decode bitmap on background thread
                var decoded = await Task.Run(() =>
                {
                    using var ms = new MemoryStream(bytes);
                    using var bmp = new Bitmap(ms);
                    int w2 = bmp.Width, h2 = bmp.Height;
                    var rect = new Rectangle(0, 0, w2, h2);
                    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        byte[] pixels = new byte[w2 * h2 * 4];
                        for (int row = 0; row < h2; row++)
                            Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * w2 * 4, w2 * 4);
                        return (Pixels: pixels, Width: w2, Height: h2);
                    }
                    finally { bmp.UnlockBits(data); }
                });

                // Upload to GPU on UI thread
                _hostPanel.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_disposed) return;
                        var srv = UploadPixelsToSrv(decoded.Pixels, decoded.Width, decoded.Height);
                        _tileTextures.TryAdd(key, srv);
                        _tileLastUsed[key] = DateTime.UtcNow.Ticks;
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
                double cx = LonToPixelX(_centerLon, z), cy = LatToPixelY(_centerLat, z);
                int centerTX = (int)Math.Floor(cx / 256.0), centerTY = (int)Math.Floor(cy / 256.0);
                int w = _hostPanel.Width, h = _hostPanel.Height;
                int tilesW = (int)Math.Ceiling((double)w / 256.0) + 2 + PREFETCH_RADIUS * 2;
                int tilesH = (int)Math.Ceiling((double)h / 256.0) + 2 + PREFETCH_RADIUS * 2;

                for (int dx = -tilesW / 2; dx <= tilesW / 2; dx++)
                    for (int dy = -tilesH / 2; dy <= tilesH / 2; dy++)
                    {
                        int tileX = centerTX + dx, tileY = centerTY + dy;
                        int wrap = 1 << z;
                        int wrappedX = ((tileX % wrap) + wrap) % wrap;
                        if (tileY < 0 || tileY >= wrap) continue;
                        var key = (z, wrappedX, tileY);
                        if (!_tileTextures.ContainsKey(key))
                            _ = EnsureTileLoadedAsync(z, wrappedX, tileY);
                    }
            }
            catch { }
        }

        private void EvictTilesIfNeeded()
        {
            if (_tileTextures.Count <= MAX_TILE_TEXTURES) return;
            int toEvict = Math.Max(1, _tileTextures.Count / 4);
            var entries = _tileLastUsed.ToArray();
            Array.Sort(entries, (a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < toEvict && i < entries.Length; i++)
            {
                var ek = entries[i].Key;
                if (_tileTextures.TryRemove(ek, out var srv)) srv.Dispose();
                _tileLastUsed.TryRemove(ek, out _);
            }
        }

        private void BatchedInvalidate()
        {
            if (_renderPending) return;
            _renderPending = true;
            _renderBatchTimer?.Dispose();
            _renderBatchTimer = new System.Threading.Timer(_ =>
            {
                _renderPending = false;
                try { if (_hostPanel.IsHandleCreated) _hostPanel.BeginInvoke(new Action(() => _hostPanel.Invalidate())); } catch { }
            }, null, 16, Timeout.Infinite);
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Map projection helpers (Mercator â€” same as GLRadarControl)
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        private static double LonToPixelX(double lon, int z)
            => ((lon + 180.0) / 360.0) * 256.0 * Math.Pow(2.0, z);

        private static double LatToPixelY(double lat, int z)
        {
            var latRad = lat * Math.PI / 180.0;
            return ((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0) * 256.0 * Math.Pow(2.0, z);
        }

        private static double PixelXToLon(double px, int z)
            => (px / (Math.Pow(2.0, z) * 256.0)) * 360.0 - 180.0;

        private static double PixelYToLat(double py, int z)
        {
            double y = py / (Math.Pow(2.0, z) * 256.0);
            return Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * 180.0 / Math.PI;
        }

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // Dispose
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _zoomSnapTimer?.Dispose();
            _renderBatchTimer?.Dispose();
            _animRefreshTimer?.Dispose();

            _hudRenderer?.Dispose();
            _tileShader?.Dispose();
            _overlayShader?.Dispose();
            _weatherOverlayShader?.Dispose();
            _generalShader?.Dispose();

            foreach (var kv in _tileTextures) kv.Value.Dispose();
            foreach (var f in _radarFrames) f.Dispose();
            _fallbackTileSrv.Dispose();
            _backgroundSrv.Dispose();
            _overlaySrv.Dispose();
            _overlay2Srv.Dispose();

            _quadVB.Dispose();
            _quadIB.Dispose();
            _overlayVB.Dispose();
            _blendState.Dispose();
            _sampler.Dispose();
            _rasterizerState.Dispose();
            _rtv.Dispose();
            _swapChain.Dispose();
            _context.Dispose();
            _device.Dispose();

            // Free semantic name strings
            if (_semanticPOSITION != 0) SilkMarshal.FreeString(_semanticPOSITION);
            if (_semanticTEXCOORD != 0) SilkMarshal.FreeString(_semanticTEXCOORD);

            _tileProvider?.Dispose();
            _hostPanel?.Dispose();
        }
    }
}
