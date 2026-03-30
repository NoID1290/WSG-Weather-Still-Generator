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
using WeatherImageGenerator.Utilities;
using System.Threading.Tasks;
using System.Windows.Forms;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WeatherImageGenerator.Rendering.Common;
using ImageLayout = Silk.NET.Vulkan.ImageLayout;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace WeatherImageGenerator.Rendering.Vulkan
{
    /// <summary>
    /// Vulkan map renderer — full feature parity with GLRadarControl and DXMapRenderer.
    /// Renders slippy map tiles with per-tile shader effects, geo-positioned weather overlays,
    /// crosshair/markers, and an interactive HUD, all via Vulkan 1.0.
    /// </summary>
    public unsafe class VulkanMapRenderer : IMapRenderer
    {
        // ═══════════════════════════════════════════════════════════════════
        // Vulkan core objects
        // ═══════════════════════════════════════════════════════════════════
        private Vk? _vk;
        private Instance _instance;
        private PhysicalDevice _physicalDevice;
        private Device _device;
        private Queue _graphicsQueue;
        private uint _graphicsFamily;
        private SurfaceKHR _surface;
        private KhrSurface? _khrSurface;
        private KhrSwapchain? _khrSwapchain;

        // Swapchain
        private SwapchainKHR _swapchain;
        private Format _swapchainFormat;
        private Extent2D _swapchainExtent;
        private Silk.NET.Vulkan.Image[] _swapchainImages = Array.Empty<Silk.NET.Vulkan.Image>();
        private ImageView[] _swapchainImageViews = Array.Empty<ImageView>();
        private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();
        private RenderPass _renderPass;

        // Command pool/buffers
        private CommandPool _commandPool;
        private CommandBuffer[] _commandBuffers = Array.Empty<CommandBuffer>();

        // Synchronization
        private VkSemaphore _imageAvailable;
        private VkSemaphore _renderFinished;
        private Fence _inFlightFence;

        // Pipelines/shaders
        private VulkanShader? _tileShader;
        private VulkanShader? _overlayShader;
        private VulkanShader? _weatherOverlayShader;
        private VulkanShader? _generalShader;
        private VulkanShader? _markerShader;

        private StationMarkerEntry[]   _stationMarkers   = Array.Empty<StationMarkerEntry>();
        private EpicenterMarkerEntry[] _epicenterMarkers = Array.Empty<EpicenterMarkerEntry>();
        private float _markerEpicenterPhase;
        private float _markerMostRecentPhase;
        private readonly object _markerLock = new();

        // Shared quad geometry
        private Silk.NET.Vulkan.Buffer _quadVB;
        private DeviceMemory _quadVBMem;
        private Silk.NET.Vulkan.Buffer _quadIB;
        private DeviceMemory _quadIBMem;

        // Dynamic overlay vertex buffer (crosshair)
        private Silk.NET.Vulkan.Buffer _overlayVB;
        private DeviceMemory _overlayVBMem;
        private void* _overlayVBMapped;
        private int _crosshairVertexCount;

        // Invisible cursor for crosshair-as-mouse mode
        private static readonly Cursor _blankCursor = CreateBlankCursor();
        private static Cursor CreateBlankCursor()
        {
            var bmp = new System.Drawing.Bitmap(1, 1);
            bmp.SetPixel(0, 0, System.Drawing.Color.Transparent);
            return new Cursor(bmp.GetHicon());
        }

        // Descriptor pool for texture bindings
        private DescriptorPool _textureDescPool;
        private Sampler _textureSampler;

        // HUD renderer
        private VulkanHudRenderer? _hudRenderer;

        // Host panel
        private Panel _hostPanel;
        private bool _initialized;
        private bool _disposed;

        // ═══════════════════════════════════════════════════════════════════
        // Tile management
        // ═══════════════════════════════════════════════════════════════════
        private TileProvider? _tileProvider;
        private string? _localTileFolder;
        private OpenMap.MapStyle _pendingMapStyle = OpenMap.MapStyle.Standard;

        private readonly ConcurrentDictionary<(int z, int x, int y), VulkanTexture> _tileTextures = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), long> _tileLastUsed = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), DateTime> _blockedTiles = new();
        private readonly ConcurrentDictionary<(int z, int x, int y), Task> _pendingLoads = new();
        private static readonly TimeSpan BlockedTileTtl = TimeSpan.FromMinutes(2);
        private const int MAX_TILE_TEXTURES = 2000;
        private const int PREFETCH_RADIUS = 3;

        /// <summary>Helper struct holding a Vulkan texture + descriptor set for a tile.</summary>
        private struct VulkanTexture : IDisposable
        {
            public Silk.NET.Vulkan.Image Image;
            public DeviceMemory Memory;
            public ImageView View;
            public DescriptorSet DescSet;
            public Vk? Vk;
            public Device Device;

            public void Dispose()
            {
                if (Vk == null) return;
                if (View.Handle != 0) Vk.DestroyImageView(Device, View, null);
                if (Image.Handle != 0) Vk.DestroyImage(Device, Image, null);
                if (Memory.Handle != 0) Vk.FreeMemory(Device, Memory, null);
                Vk = null;
            }
        }

        // Fallback tile
        private VulkanTexture _fallbackTile;

        // Background texture
        private VulkanTexture _backgroundTex;
        private bool _hasBackgroundTexture;
        private double _bgCenterLat, _bgCenterLon;
        private int _bgSourceZoom, _bgPixelWidth, _bgPixelHeight;

        // Positioned overlay 1 (radar)
        private VulkanTexture _overlayTex;
        private bool _hasPositionedOverlay;
        private double _overlayMinLat, _overlayMinLon, _overlayMaxLat, _overlayMaxLon;

        // Positioned overlay 2 (temperature)
        private VulkanTexture _overlay2Tex;
        private bool _hasPositionedOverlay2;
        private double _overlay2MinLat, _overlay2MinLon, _overlay2MaxLat, _overlay2MaxLon;

        // Positioned overlay 3 (GRIB2 forecast)
        private VulkanTexture _overlay3Tex;
        private bool _hasPositionedOverlay3;
        private double _overlay3MinLat, _overlay3MinLon, _overlay3MaxLat, _overlay3MaxLon;

        // Radar frames
        private readonly List<VulkanTexture> _radarFrames = new();
        private const int MAX_RADAR_FRAMES = 6;

        // ═══════════════════════════════════════════════════════════════════
        // Map state
        // ═══════════════════════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════════════════════
        // IMapRenderer properties
        // ═══════════════════════════════════════════════════════════════════
        public Control HostControl => _hostPanel;
        public RenderingApi ActiveApi => RenderingApi.Vulkan;
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
                int count = _tileTextures.Count;
                if (_overlayTex.Image.Handle != 0) count++;
                if (_overlay2Tex.Image.Handle != 0) count++;
                if (_overlay3Tex.Image.Handle != 0) count++;
                if (_backgroundTex.Image.Handle != 0) count++;
                if (_fallbackTile.Image.Handle != 0) count++;
                count += _radarFrames.Count;
                return count;
            }
        }

        public long VramEstimatedBytes
        {
            get
            {
                long bytes = (long)_tileTextures.Count * 256 * 256 * 4;
                if (_overlayTex.Image.Handle != 0) bytes += 1024L * 1024 * 4;
                if (_overlay2Tex.Image.Handle != 0) bytes += 1024L * 1024 * 4;
                if (_backgroundTex.Image.Handle != 0) bytes += (long)_bgPixelWidth * _bgPixelHeight * 4;
                if (_fallbackTile.Image.Handle != 0) bytes += 256L * 256 * 4;
                bytes += (long)_radarFrames.Count * 1024 * 1024 * 4;
                return bytes;
            }
        }

        public event Action<int>? MapZoomChanged;
        public event Action<double, double>? MapPositionChanged;
        public event Action<string, Color>? TileStatusChanged;
        public event Action<bool>? BackgroundTextureChanged;

        // ═══════════════════════════════════════════════════════════════════
        // Constructor
        // ═══════════════════════════════════════════════════════════════════
        public VulkanMapRenderer()
        {
            _hostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            _hostPanel.HandleCreated += (s, e) => InitializeVulkan();
            _hostPanel.Resize += (s, e) => HandleResize();
            _hostPanel.Paint += (s, e) => RenderFrame();
            _hostPanel.MouseWheel += OnMouseWheel;
            _hostPanel.MouseDown += OnMouseDown;
            _hostPanel.MouseMove += OnMouseMove;
            _hostPanel.MouseUp += OnMouseUp;
            _hostPanel.MouseEnter += (s, e) => { _mouseInside = true; UpdateCursorStyle(); };
            _hostPanel.MouseLeave += (s, e) => { _mouseInside = false; _hostPanel.Cursor = Cursors.Default; _hostPanel.Invalidate(); };
        }

        // ═══════════════════════════════════════════════════════════════════
        // Vulkan initialization
        // ═══════════════════════════════════════════════════════════════════
        private void InitializeVulkan()
        {
            if (_initialized) return;

            try
            {
                _vk = Vk.GetApi();

                CreateInstance();
                CreateSurface();
                PickPhysicalDevice();
                CreateLogicalDevice();
                CreateSwapchain();
                CreateRenderPass();
                CreateFramebuffers();
                CreateCommandPool();
                AllocateCommandBuffers();
                CreateSyncObjects();
                CreateQuadGeometry();
                CreateOverlayVB();
                CreateTextureSampler();
                CreateTextureDescriptorPool();
                LoadShaders();
                CreateFallbackTile();

                // HUD
                _hudRenderer = new VulkanHudRenderer();
                _hudRenderer.SetDevice(_vk, _device, _physicalDevice, _renderPass);
                _hudRenderer.Initialize();

                // Tile provider, preserving any style set before Vulkan was ready (e.g. from LoadMapSettings)
                _tileProvider = new TileProvider(_localTileFolder ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png");
                _tileProvider.CurrentStyle = _pendingMapStyle;

                // Start repaint timer (60 FPS)
                _animRefreshTimer = new System.Threading.Timer(_ =>
                {
                    if (!_renderPending)
                    {
                        _renderPending = true;
                        try { _hostPanel.BeginInvoke((Action)(() => _hostPanel.Invalidate())); } catch { }
                    }
                }, null, 0, 16);

                _initialized = true;
                Console.WriteLine("[VulkanMapRenderer] Initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] Init failed: {ex}");
            }
        }

        private void CreateInstance()
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                ApiVersion = Vk.Version10,
            };

            // Required extensions: VK_KHR_surface + VK_KHR_win32_surface
            var extNames = stackalloc byte*[2];
            extNames[0] = (byte*)SilkMarshal.StringToPtr("VK_KHR_surface", NativeStringEncoding.UTF8);
            extNames[1] = (byte*)SilkMarshal.StringToPtr("VK_KHR_win32_surface", NativeStringEncoding.UTF8);

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = 2,
                PpEnabledExtensionNames = extNames,
            };

            fixed (Instance* pInst = &_instance)
                Check(_vk!.CreateInstance(&createInfo, null, pInst));

            SilkMarshal.FreeString((nint)extNames[0]);
            SilkMarshal.FreeString((nint)extNames[1]);

            if (!_vk!.TryGetInstanceExtension(_instance, out _khrSurface))
                throw new Exception("VK_KHR_surface extension not available");
        }

        private void CreateSurface()
        {
            // Win32 surface from the Panel HWND
            var hwnd = _hostPanel.Handle;
            var hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle;

            // Use VK_KHR_win32_surface
            if (!_vk!.TryGetInstanceExtension<KhrWin32Surface>(_instance, out var win32Surface))
                throw new Exception("VK_KHR_win32_surface not available");

            var createInfo = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr,
                Hwnd = hwnd,
                Hinstance = hinstance,
            };
            fixed (SurfaceKHR* pSurface = &_surface)
                Check(win32Surface.CreateWin32Surface(_instance, &createInfo, null, pSurface));
        }

        private void PickPhysicalDevice()
        {
            uint count = 0;
            _vk!.EnumeratePhysicalDevices(_instance, &count, null);
            if (count == 0) throw new Exception("No Vulkan GPU found");

            var devices = stackalloc PhysicalDevice[(int)count];
            _vk.EnumeratePhysicalDevices(_instance, &count, devices);

            // Pick the first device that supports graphics and presentation
            for (int i = 0; i < count; i++)
            {
                if (FindGraphicsQueueFamily(devices[i], out uint family))
                {
                    _physicalDevice = devices[i];
                    _graphicsFamily = family;
                    return;
                }
            }
            throw new Exception("No suitable Vulkan GPU found");
        }

        private bool FindGraphicsQueueFamily(PhysicalDevice device, out uint family)
        {
            family = 0;
            uint queueCount = 0;
            _vk!.GetPhysicalDeviceQueueFamilyProperties(device, &queueCount, null);
            var props = stackalloc QueueFamilyProperties[(int)queueCount];
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueCount, props);

            for (uint i = 0; i < queueCount; i++)
            {
                if ((props[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
                {
                    Silk.NET.Core.Bool32 presentSupport = false;
                    _khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, _surface, &presentSupport);
                    if (presentSupport)
                    {
                        family = i;
                        return true;
                    }
                }
            }
            return false;
        }

        private void CreateLogicalDevice()
        {
            float priority = 1.0f;
            var queueInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = _graphicsFamily,
                QueueCount = 1,
                PQueuePriorities = &priority,
            };

            var extName = (byte*)SilkMarshal.StringToPtr("VK_KHR_swapchain", NativeStringEncoding.UTF8);
            var devInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = &extName,
            };

            fixed (Device* pDev = &_device)
                Check(_vk!.CreateDevice(_physicalDevice, &devInfo, null, pDev));

            SilkMarshal.FreeString((nint)extName);

            fixed (Queue* pQ = &_graphicsQueue)
                _vk.GetDeviceQueue(_device, _graphicsFamily, 0, pQ);

            if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
                throw new Exception("VK_KHR_swapchain not available");
        }

        private void CreateSwapchain()
        {
            SurfaceCapabilitiesKHR caps;
            _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, &caps);

            _swapchainFormat = Format.B8G8R8A8Unorm;
            _swapchainExtent = caps.CurrentExtent;
            if (_swapchainExtent.Width == uint.MaxValue)
            {
                _swapchainExtent.Width = (uint)Math.Max(1, _hostPanel.Width);
                _swapchainExtent.Height = (uint)Math.Max(1, _hostPanel.Height);
            }

            uint imageCount = caps.MinImageCount + 1;
            if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
                imageCount = caps.MaxImageCount;

            var createInfo = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = _swapchainFormat,
                ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                ImageExtent = _swapchainExtent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = caps.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = PresentModeKHR.FifoKhr, // vsync
                Clipped = true,
                OldSwapchain = _swapchain, // for recreation
            };

            SwapchainKHR newSwapchain;
            Check(_khrSwapchain!.CreateSwapchain(_device, &createInfo, null, &newSwapchain));

            // Destroy old swapchain if recreating
            if (_swapchain.Handle != 0)
                _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
            _swapchain = newSwapchain;

            // Get swapchain images
            uint imgCount = 0;
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imgCount, null);
            _swapchainImages = new Silk.NET.Vulkan.Image[imgCount];
            fixed (Silk.NET.Vulkan.Image* pImgs = _swapchainImages)
                _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imgCount, pImgs);

            // Create image views
            _swapchainImageViews = new ImageView[imgCount];
            for (int i = 0; i < imgCount; i++)
            {
                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = _swapchainImages[i],
                    ViewType = ImageViewType.Type2D,
                    Format = _swapchainFormat,
                    Components = new ComponentMapping
                    {
                        R = ComponentSwizzle.Identity,
                        G = ComponentSwizzle.Identity,
                        B = ComponentSwizzle.Identity,
                        A = ComponentSwizzle.Identity,
                    },
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1,
                    },
                };
                fixed (ImageView* pView = &_swapchainImageViews[i])
                    Check(_vk!.CreateImageView(_device, &viewInfo, null, pView));
            }
        }

        private void CreateRenderPass()
        {
            var colorAttach = new AttachmentDescription
            {
                Format = _swapchainFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            };
            var colorRef = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
            var subpass = new SubpassDescription
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = 1,
                PColorAttachments = &colorRef,
            };
            var dependency = new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            };
            var rpInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorAttach,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency,
            };
            fixed (RenderPass* pRp = &_renderPass)
                Check(_vk!.CreateRenderPass(_device, &rpInfo, null, pRp));
        }

        private void CreateFramebuffers()
        {
            _framebuffers = new Framebuffer[_swapchainImageViews.Length];
            for (int i = 0; i < _swapchainImageViews.Length; i++)
            {
                var attachment = _swapchainImageViews[i];
                var fbInfo = new FramebufferCreateInfo
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = _renderPass,
                    AttachmentCount = 1,
                    PAttachments = &attachment,
                    Width = _swapchainExtent.Width,
                    Height = _swapchainExtent.Height,
                    Layers = 1,
                };
                fixed (Framebuffer* pFb = &_framebuffers[i])
                    Check(_vk!.CreateFramebuffer(_device, &fbInfo, null, pFb));
            }
        }

        private void CreateCommandPool()
        {
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = _graphicsFamily,
            };
            fixed (CommandPool* pPool = &_commandPool)
                Check(_vk!.CreateCommandPool(_device, &poolInfo, null, pPool));
        }

        private void AllocateCommandBuffers()
        {
            _commandBuffers = new CommandBuffer[_swapchainImages.Length];
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = (uint)_commandBuffers.Length,
            };
            fixed (CommandBuffer* pCmds = _commandBuffers)
                Check(_vk!.AllocateCommandBuffers(_device, &allocInfo, pCmds));
        }

        private void CreateSyncObjects()
        {
            var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

            fixed (VkSemaphore* pSem = &_imageAvailable) Check(_vk!.CreateSemaphore(_device, &semInfo, null, pSem));
            fixed (VkSemaphore* pSem = &_renderFinished) Check(_vk!.CreateSemaphore(_device, &semInfo, null, pSem));
            fixed (Fence* pFence = &_inFlightFence) Check(_vk!.CreateFence(_device, &fenceInfo, null, pFence));
        }

        // ═══════════════════════════════════════════════════════════════════
        // Geometry creation
        // ═══════════════════════════════════════════════════════════════════
        private void CreateQuadGeometry()
        {
            // Quad: pos(2) + tex(2) = 4 floats per vertex × 4 vertices
            float[] vertices = {
                0f, 0f, 0f, 0f,
                1f, 0f, 1f, 0f,
                1f, 1f, 1f, 1f,
                0f, 1f, 0f, 1f,
            };
            uint[] indices = { 0, 1, 2, 2, 3, 0 };

            CreateBufferWithData(vertices, BufferUsageFlags.VertexBufferBit, out _quadVB, out _quadVBMem);
            CreateBufferWithData(indices, BufferUsageFlags.IndexBufferBit, out _quadIB, out _quadIBMem);
        }

        private void CreateOverlayVB()
        {
            // Dynamic vertex buffer for crosshair lines: pos(2) + lineEdge(1) = 3 floats × 256 verts max
            ulong size = 256 * 3 * sizeof(float);
            CreateBuffer(size, BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out _overlayVB, out _overlayVBMem);

            void* mapped;
            Check(_vk!.MapMemory(_device, _overlayVBMem, 0, size, 0, &mapped));
            _overlayVBMapped = mapped;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Buffer helpers
        // ═══════════════════════════════════════════════════════════════════
        private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags memProps,
            out Silk.NET.Vulkan.Buffer buffer, out DeviceMemory memory)
        {
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            fixed (Silk.NET.Vulkan.Buffer* pBuf = &buffer)
                Check(_vk!.CreateBuffer(_device, &bufInfo, null, pBuf));

            MemoryRequirements memReqs;
            _vk.GetBufferMemoryRequirements(_device, buffer, &memReqs);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits, memProps),
            };
            fixed (DeviceMemory* pMem = &memory)
                Check(_vk.AllocateMemory(_device, &allocInfo, null, pMem));

            Check(_vk.BindBufferMemory(_device, buffer, memory, 0));
        }

        private void CreateBufferWithData<T>(T[] data, BufferUsageFlags usage,
            out Silk.NET.Vulkan.Buffer buffer, out DeviceMemory memory) where T : unmanaged
        {
            ulong size = (ulong)(data.Length * sizeof(T));
            CreateBuffer(size, usage,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                out buffer, out memory);

            void* mapped;
            Check(_vk!.MapMemory(_device, memory, 0, size, 0, &mapped));
            fixed (T* pData = data)
                Unsafe.CopyBlock(mapped, pData, (uint)size);
            _vk.UnmapMemory(_device, memory);
        }

        private uint FindMemoryType(uint filter, MemoryPropertyFlags properties)
        {
            PhysicalDeviceMemoryProperties memProps;
            _vk!.GetPhysicalDeviceMemoryProperties(_physicalDevice, &memProps);

            for (uint i = 0; i < memProps.MemoryTypeCount; i++)
            {
                if ((filter & (1u << (int)i)) != 0 &&
                    (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                    return i;
            }
            throw new Exception("No suitable memory type found");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Texture creation
        // ═══════════════════════════════════════════════════════════════════
        private void CreateTextureSampler()
        {
            var samplerInfo = new SamplerCreateInfo
            {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                AnisotropyEnable = false,
                MaxAnisotropy = 1f,
                BorderColor = BorderColor.IntOpaqueBlack,
                MipLodBias = 0f,
                MinLod = 0f,
                MaxLod = 0f,
                MipmapMode = SamplerMipmapMode.Linear,
            };
            fixed (Sampler* pSampler = &_textureSampler)
                Check(_vk!.CreateSampler(_device, &samplerInfo, null, pSampler));
        }

        private void CreateTextureDescriptorPool()
        {
            // Large pool for tile textures + overlays
            var poolSize = new DescriptorPoolSize
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = MAX_TILE_TEXTURES + 32, // tiles + overlays
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize,
                MaxSets = MAX_TILE_TEXTURES + 32,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            };
            fixed (DescriptorPool* pPool = &_textureDescPool)
                Check(_vk!.CreateDescriptorPool(_device, &poolInfo, null, pPool));
        }

        private VulkanTexture UploadPixelsToTexture(byte[] pixels, int width, int height, Format format = Format.B8G8R8A8Unorm)
        {
            var tex = new VulkanTexture { Vk = _vk, Device = _device };

            // Create image
            var imgInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Extent = new Extent3D((uint)width, (uint)height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Format = format,
                Tiling = ImageTiling.Linear, // for simplicity; staging would be better for production
                InitialLayout = ImageLayout.Preinitialized,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                Samples = SampleCountFlags.Count1Bit,
                SharingMode = SharingMode.Exclusive,
            };
            Check(_vk!.CreateImage(_device, &imgInfo, null, &tex.Image));

            MemoryRequirements memReqs;
            _vk.GetImageMemoryRequirements(_device, tex.Image, &memReqs);

            var allocInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memReqs.Size,
                MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
            };
            Check(_vk.AllocateMemory(_device, &allocInfo, null, &tex.Memory));
            Check(_vk.BindImageMemory(_device, tex.Image, tex.Memory, 0));

            // Copy pixel data — must respect GPU linear tiling row pitch
            var subRes = new ImageSubresource { AspectMask = ImageAspectFlags.ColorBit, MipLevel = 0, ArrayLayer = 0 };
            SubresourceLayout layout;
            _vk.GetImageSubresourceLayout(_device, tex.Image, &subRes, &layout);

            void* mapped;
            Check(_vk.MapMemory(_device, tex.Memory, 0, layout.Size, 0, &mapped));
            int srcRowBytes = width * 4;
            fixed (byte* pPixels = pixels)
            {
                if ((ulong)srcRowBytes == layout.RowPitch)
                {
                    // Row pitch matches — single fast copy
                    Unsafe.CopyBlock(mapped, pPixels, (uint)(width * height * 4));
                }
                else
                {
                    // Row pitch differs — copy row by row
                    byte* dst = (byte*)mapped + (long)layout.Offset;
                    for (int row = 0; row < height; row++)
                    {
                        Unsafe.CopyBlock(dst + row * (long)layout.RowPitch,
                            pPixels + row * srcRowBytes, (uint)srcRowBytes);
                    }
                }
            }
            _vk.UnmapMemory(_device, tex.Memory);

            // Transition layout to ShaderReadOnlyOptimal
            TransitionImageLayout(tex.Image, ImageLayout.Preinitialized, ImageLayout.ShaderReadOnlyOptimal);

            // Create image view
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = tex.Image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0, LevelCount = 1,
                    BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            Check(_vk.CreateImageView(_device, &viewInfo, null, &tex.View));

            // Allocate descriptor set
            tex.DescSet = AllocateTextureDescriptorSet(tex.View);

            return tex;
        }

        private VulkanTexture UploadBitmapToTexture(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int byteCount = bmp.Width * bmp.Height * 4;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);
                return UploadPixelsToTexture(pixels, bmp.Width, bmp.Height);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private DescriptorSet AllocateTextureDescriptorSet(ImageView view)
        {
            DescriptorSet descSet;
            var layout = _tileShader!.DescriptorLayoutHandle;
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _textureDescPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            Check(_vk!.AllocateDescriptorSets(_device, &allocInfo, &descSet));

            var imgInfo = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                ImageView = view,
                Sampler = _textureSampler,
            };
            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descSet,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imgInfo,
            };
            _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);

            return descSet;
        }

        private void TransitionImageLayout(Silk.NET.Vulkan.Image image, ImageLayout oldLayout, ImageLayout newLayout)
        {
            var cmd = BeginSingleTimeCommands();

            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0, LevelCount = 1,
                    BaseArrayLayer = 0, LayerCount = 1,
                },
            };

            PipelineStageFlags srcStage, dstStage;
            if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.ShaderReadOnlyOptimal)
            {
                barrier.SrcAccessMask = AccessFlags.HostWriteBit;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;
                srcStage = PipelineStageFlags.HostBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }
            else
            {
                barrier.SrcAccessMask = 0;
                barrier.DstAccessMask = AccessFlags.ShaderReadBit;
                srcStage = PipelineStageFlags.TopOfPipeBit;
                dstStage = PipelineStageFlags.FragmentShaderBit;
            }

            _vk!.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);

            EndSingleTimeCommands(cmd);
        }

        private CommandBuffer BeginSingleTimeCommands()
        {
            var allocInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                Level = CommandBufferLevel.Primary,
                CommandPool = _commandPool,
                CommandBufferCount = 1,
            };
            CommandBuffer cmd;
            Check(_vk!.AllocateCommandBuffers(_device, &allocInfo, &cmd));

            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            Check(_vk.BeginCommandBuffer(cmd, &beginInfo));
            return cmd;
        }

        private void EndSingleTimeCommands(CommandBuffer cmd)
        {
            Check(_vk!.EndCommandBuffer(cmd));

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd,
            };
            Check(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default));
            Check(_vk.QueueWaitIdle(_graphicsQueue));

            _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmd);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Shader loading
        // ═══════════════════════════════════════════════════════════════════
        private void LoadShaders()
        {
            var shaderDir = "Rendering/Vulkan/shaders";

            // Tile shader: push_constant = mat3(3×vec4=48) + 6 floats(24) = 72 bytes
            _tileShader = CreatePipeline(shaderDir, "tile",
                72,
                new Dictionary<string, (int, int)>
                {
                    ["uTransform"] = (0, 48),
                    ["uOpacity"] = (48, 4),
                    ["uZoomNorm"] = (52, 4),
                    ["uEnableSaturation"] = (56, 4),
                    ["uEnableContrast"] = (60, 4),
                    ["uEnableVignette"] = (64, 4),
                    ["uEnableAtmosphere"] = (68, 4),
                },
                hasTexture: true,
                vertexStride: 16, // pos(2) + tex(2) = 4 floats
                GetQuadVertexAttributes());

            // Weather overlay shader: push_constant = mat3(48) + 3 floats(12) = 60 bytes
            _weatherOverlayShader = CreatePipeline(shaderDir, "weather_overlay",
                60,
                new Dictionary<string, (int, int)>
                {
                    ["uTransform"] = (0, 48),
                    ["uOpacity"] = (48, 4),
                    ["uTime"] = (52, 4),
                    ["uEnableGlow"] = (56, 4),
                },
                hasTexture: true,
                vertexStride: 16,
                GetQuadVertexAttributes());

            // Overlay shader (crosshair): push_constant = 8 floats = 32 bytes
            _overlayShader = CreatePipeline(shaderDir, "overlay",
                32,
                new Dictionary<string, (int, int)>
                {
                    ["uOffsetX"] = (0, 4),
                    ["uOffsetY"] = (4, 4),
                    ["uColorR"] = (8, 4),
                    ["uColorG"] = (12, 4),
                    ["uColorB"] = (16, 4),
                    ["uAlpha"] = (20, 4),
                    ["uTime"] = (24, 4),
                    ["uEnablePulse"] = (28, 4),
                },
                hasTexture: false,
                vertexStride: 12, // pos(2) + lineEdge(1) = 3 floats
                GetOverlayVertexAttributes());

            // General shader (simple textured quad): push_constant = mat3(48) + opacity(4) = 52 bytes
            _generalShader = CreatePipeline(shaderDir, "tile", // reuse tile shaders
                72,
                new Dictionary<string, (int, int)>
                {
                    ["uTransform"] = (0, 48),
                    ["uOpacity"] = (48, 4),
                    ["uZoomNorm"] = (52, 4),
                    ["uEnableSaturation"] = (56, 4),
                    ["uEnableContrast"] = (60, 4),
                    ["uEnableVignette"] = (64, 4),
                    ["uEnableAtmosphere"] = (68, 4),
                },
                hasTexture: true,
                vertexStride: 16,
                GetQuadVertexAttributes());

            _markerShader = CreatePipeline(shaderDir, "station_marker",
                48,
                new Dictionary<string, (int, int)>
                {
                    ["uNdcX"]         = (0,  4),
                    ["uNdcY"]         = (4,  4),
                    ["uHalfSizeX"]    = (8,  4),
                    ["uHalfSizeY"]    = (12, 4),
                    ["uMarkerType"]   = (16, 4),
                    ["uColorR"]       = (20, 4),
                    ["uColorG"]       = (24, 4),
                    ["uColorB"]       = (28, 4),
                    ["uColorA"]       = (32, 4),
                    ["uRingPhase"]    = (36, 4),
                    ["uSelected"]     = (40, 4),
                    ["uGlowStrength"] = (44, 4),
                },
                hasTexture: false,
                vertexStride: 16,
                GetQuadVertexAttributes());
        }

        private VulkanShader? CreatePipeline(string shaderDir, string name,
            uint pushSize, Dictionary<string, (int, int)> uniforms,
            bool hasTexture, uint vertexStride, VertexInputAttributeDescription[] attrs)
        {
            try
            {
                var vertPath = Path.Combine(shaderDir, $"{name}.vert.spv");
                var fragPath = Path.Combine(shaderDir, $"{name}.frag.spv");

                if (!TryReadShaderBytes(vertPath, out var vertSpv) || !TryReadShaderBytes(fragPath, out var fragSpv))
                {
                    Console.WriteLine($"[VulkanMapRenderer] SPIR-V not found for {name} — pipeline skipped.");
                    return null;
                }

                return new VulkanShader(_vk!, _device, _renderPass,
                    vertSpv, fragSpv,
                    pushSize, uniforms, hasTexture,
                    vertexStride, attrs, _swapchainExtent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] Shader '{name}' failed: {ex.Message}");
                return null;
            }
        }

        private static bool TryReadShaderBytes(string shaderPath, out byte[] bytes)
        {
            if (EmbeddedResourceLoader.TryReadBytes(shaderPath, out bytes))
            {
                return true;
            }

            var normalized = shaderPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            {
                bytes = File.ReadAllBytes(normalized);
                return true;
            }

            var combined = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized);
            if (File.Exists(combined))
            {
                bytes = File.ReadAllBytes(combined);
                return true;
            }

            bytes = Array.Empty<byte>();
            return false;
        }

        private VertexInputAttributeDescription[] GetQuadVertexAttributes()
        {
            return new[]
            {
                new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32Sfloat, Offset = 0 },
                new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32G32Sfloat, Offset = 8 },
            };
        }

        private VertexInputAttributeDescription[] GetOverlayVertexAttributes()
        {
            return new[]
            {
                new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32Sfloat, Offset = 0 },
                new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32Sfloat, Offset = 8 },
            };
        }

        private void CreateFallbackTile()
        {
            // Solid dark tile
            var pixels = new byte[256 * 256 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 30; pixels[i + 1] = 30; pixels[i + 2] = 30; pixels[i + 3] = 255;
            }
            _fallbackTile = UploadPixelsToTexture(pixels, 256, 256);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Resize
        // ═══════════════════════════════════════════════════════════════════
        private void HandleResize()
        {
            if (!_initialized || _vk == null) return;
            if (_hostPanel.Width <= 0 || _hostPanel.Height <= 0) return;

            _vk.DeviceWaitIdle(_device);

            // Destroy old framebuffers and views
            foreach (var fb in _framebuffers)
                if (fb.Handle != 0) _vk.DestroyFramebuffer(_device, fb, null);
            foreach (var view in _swapchainImageViews)
                if (view.Handle != 0) _vk.DestroyImageView(_device, view, null);

            CreateSwapchain();
            CreateFramebuffers();

            _hostPanel.Invalidate();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Render frame
        // ═══════════════════════════════════════════════════════════════════
        private void RenderFrame()
        {
            if (!_initialized || _vk == null) return;
            if (_swapchainExtent.Width == 0 || _swapchainExtent.Height == 0) return;
            _renderPending = false;

            try
            {
                var fence = _inFlightFence;
                _vk.WaitForFences(_device, 1, &fence, true, ulong.MaxValue);
                _vk.ResetFences(_device, 1, &fence);

                uint imageIndex;
                var result = _khrSwapchain!.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailable, default, &imageIndex);
                if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
                {
                    HandleResize();
                    return;
                }
                if (result != Result.Success) return;

                var cmd = _commandBuffers[imageIndex];
                _vk.ResetCommandBuffer(cmd, 0);

                var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
                Check(_vk.BeginCommandBuffer(cmd, &beginInfo));

                // Begin render pass
                var clearColor = new ClearValue();
                clearColor.Color = new ClearColorValue(0.118f, 0.118f, 0.118f, 1.0f);
                var rpBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = _renderPass,
                    Framebuffer = _framebuffers[imageIndex],
                    RenderArea = new Rect2D { Offset = default, Extent = _swapchainExtent },
                    ClearValueCount = 1,
                    PClearValues = &clearColor,
                };
                _vk.CmdBeginRenderPass(cmd, &rpBegin, SubpassContents.Inline);

                // Set dynamic viewport and scissor
                int w = (int)_swapchainExtent.Width;
                int h = (int)_swapchainExtent.Height;
                var viewport = new Viewport(0, 0, w, h, 0, 1);
                _vk.CmdSetViewport(cmd, 0, 1, &viewport);
                var scissor = new Rect2D { Offset = default, Extent = _swapchainExtent };
                _vk.CmdSetScissor(cmd, 0, 1, &scissor);

                float time = (float)_elapsedTimer.Elapsed.TotalSeconds;

                // ── Render pipeline ──
                RenderMapTiles(cmd, w, h);

                if (_hasPositionedOverlay && _overlayTex.Image.Handle != 0)
                    RenderPositionedOverlay(cmd, _overlayTex, _overlayMinLat, _overlayMinLon,
                        _overlayMaxLat, _overlayMaxLon, _overlayOpacity, time, w, h);

                if (_hasPositionedOverlay2 && _overlay2Tex.Image.Handle != 0)
                    RenderPositionedOverlay(cmd, _overlay2Tex, _overlay2MinLat, _overlay2MinLon,
                        _overlay2MaxLat, _overlay2MaxLon, _overlay2Opacity, time, w, h);

                if (_hasPositionedOverlay3 && _overlay3Tex.Image.Handle != 0)
                    RenderPositionedOverlay(cmd, _overlay3Tex, _overlay3MinLat, _overlay3MinLon,
                        _overlay3MaxLat, _overlay3MaxLon, _overlay3Opacity, time, w, h);

                RenderStationMarkersPass(cmd, w, h);
                RenderRadarFrames(cmd, time, w, h);
                RenderCrosshair(cmd, time, w, h);
                RenderUserMarker(cmd, w, h);

                // HUD
                if (_hudRenderer != null && _hudRenderer.IsInitialized)
                {
                    _hudRenderer.SetCommandBuffer(cmd);
                    RenderHUD(w, h);
                }

                // End render pass
                _vk.CmdEndRenderPass(cmd);
                Check(_vk.EndCommandBuffer(cmd));

                // Submit
                var waitSem = _imageAvailable;
                var signalSem = _renderFinished;
                var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
                var submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &waitSem,
                    PWaitDstStageMask = &waitStage,
                    CommandBufferCount = 1,
                    PCommandBuffers = &cmd,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = &signalSem,
                };
                Check(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence));

                // Present
                var swapchain = _swapchain;
                var presentInfo = new PresentInfoKHR
                {
                    SType = StructureType.PresentInfoKhr,
                    WaitSemaphoreCount = 1,
                    PWaitSemaphores = &signalSem,
                    SwapchainCount = 1,
                    PSwapchains = &swapchain,
                    PImageIndices = &imageIndex,
                };
                _khrSwapchain.QueuePresent(_graphicsQueue, &presentInfo);

                // FPS
                _frameCount++;
                if (_fpsTimer.ElapsedMilliseconds >= 1000)
                {
                    _currentFps = _frameCount * 1000f / _fpsTimer.ElapsedMilliseconds;
                    _frameCount = 0;
                    _fpsTimer.Restart();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] RenderFrame error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Render stages
        // ═══════════════════════════════════════════════════════════════════
        private void RenderMapTiles(CommandBuffer cmd, int w, int h)
        {
            if (_tileShader == null) return;

            int numTiles = 1 << _mapZoom;
            double tileSize = 256.0;
            double worldSize = numTiles * tileSize;
            double cx = LonToPixelX(_centerLon, _mapZoom);
            double cy = LatToPixelY(_centerLat, _mapZoom);
            double halfW = w / 2.0 / _zoom;
            double halfH = h / 2.0 / _zoom;

            int minTileX = Math.Max(0, (int)Math.Floor((cx - halfW + _panX) / tileSize));
            int maxTileX = Math.Min(numTiles - 1, (int)Math.Floor((cx + halfW + _panX) / tileSize));
            int minTileY = Math.Max(0, (int)Math.Floor((cy - halfH + _panY) / tileSize));
            int maxTileY = Math.Min(numTiles - 1, (int)Math.Floor((cy + halfH + _panY) / tileSize));

            float zoomNorm = Math.Clamp(_mapZoom / 18f, 0f, 1f);

            // Bind quad geometry
            var vb = _quadVB;
            ulong offset = 0;
            _vk!.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            _vk.CmdBindIndexBuffer(cmd, _quadIB, 0, IndexType.Uint32);

            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    var key = (_mapZoom, tx, ty);
                    _tileLastUsed[key] = _elapsedTimer.ElapsedTicks;

                    // Get or load tile texture
                    if (!_tileTextures.TryGetValue(key, out var tileTex))
                    {
                        tileTex = _fallbackTile;
                        EnqueueTileLoad(key);
                    }

                    // Build transform: tile world-space → clip-space
                    double tileWorldX = tx * tileSize;
                    double tileWorldY = ty * tileSize;
                    double screenX = (tileWorldX - cx + _panX) * _zoom + w / 2.0;
                    double screenY = (tileWorldY - cy + _panY) * _zoom + h / 2.0;
                    double screenW = tileSize * _zoom;
                    double screenH = tileSize * _zoom;

                    float[] transform = BuildTileTransform(
                        (float)screenX, (float)screenY,
                        (float)screenW, (float)screenH,
                        w, h);

                    _tileShader.SetMatrix3("uTransform", transform);
                    _tileShader.SetFloat("uOpacity", 1.0f);
                    _tileShader.SetFloat("uZoomNorm", zoomNorm);
                    _tileShader.SetFloat("uEnableSaturation", EnableTileSaturation ? 1f : 0f);
                    _tileShader.SetFloat("uEnableContrast", EnableTileContrast ? 1f : 0f);
                    _tileShader.SetFloat("uEnableVignette", EnableTileVignette ? 1f : 0f);
                    _tileShader.SetFloat("uEnableAtmosphere", EnableTileAtmosphere ? 1f : 0f);

                    _tileShader.BindAndPush(cmd);

                    // Bind tile texture descriptor set
                    var descSet = tileTex.DescSet;
                    _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics,
                        _tileShader.LayoutHandle, 0, 1, &descSet, 0, null);

                    _vk.CmdDrawIndexed(cmd, 6, 1, 0, 0, 0);
                }
            }

            // Evict old tiles if over limit
            if (_tileTextures.Count > MAX_TILE_TEXTURES)
                EvictOldTiles();
        }

        private void RenderPositionedOverlay(CommandBuffer cmd, VulkanTexture tex,
            double minLat, double minLon, double maxLat, double maxLon,
            float opacity, float time, int w, int h)
        {
            if (_weatherOverlayShader == null) return;

            double cx = LonToPixelX(_centerLon, _mapZoom);
            double cy = LatToPixelY(_centerLat, _mapZoom);

            double minPx = LonToPixelX(minLon, _mapZoom);
            double minPy = LatToPixelY(maxLat, _mapZoom); // maxLat = top = min Y
            double maxPx = LonToPixelX(maxLon, _mapZoom);
            double maxPy = LatToPixelY(minLat, _mapZoom); // minLat = bottom = max Y

            double screenX = (minPx - cx + _panX) * _zoom + w / 2.0;
            double screenY = (minPy - cy + _panY) * _zoom + h / 2.0;
            double screenW = (maxPx - minPx) * _zoom;
            double screenH = (maxPy - minPy) * _zoom;

            float[] transform = BuildTileTransform(
                (float)screenX, (float)screenY,
                (float)screenW, (float)screenH,
                w, h);

            // Bind quad
            var vb = _quadVB;
            ulong vbOffset = 0;
            _vk!.CmdBindVertexBuffers(cmd, 0, 1, &vb, &vbOffset);
            _vk.CmdBindIndexBuffer(cmd, _quadIB, 0, IndexType.Uint32);

            _weatherOverlayShader.SetMatrix3("uTransform", transform);
            _weatherOverlayShader.SetFloat("uOpacity", opacity);
            _weatherOverlayShader.SetFloat("uTime", time);
            _weatherOverlayShader.SetFloat("uEnableGlow", EnableRadarGlow ? 1f : 0f);
            _weatherOverlayShader.BindAndPush(cmd);

            var descSet = tex.DescSet;
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics,
                _weatherOverlayShader.LayoutHandle, 0, 1, &descSet, 0, null);

            _vk.CmdDrawIndexed(cmd, 6, 1, 0, 0, 0);
        }

        private unsafe void RenderStationMarkersPass(CommandBuffer cmd, int w, int h)
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

            var vb = _quadVB;
            ulong vbOffset = 0;
            _vk!.CmdBindVertexBuffers(cmd, 0, 1, &vb, &vbOffset);
            _vk.CmdBindIndexBuffer(cmd, _quadIB, 0, IndexType.Uint32);

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
                _markerShader.BindAndPush(cmd);
                _vk.CmdDrawIndexed(cmd, 6, 1, 0, 0, 0);
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
                _markerShader.BindAndPush(cmd);
                _vk.CmdDrawIndexed(cmd, 6, 1, 0, 0, 0);
            }
        }

        private void RenderRadarFrames(CommandBuffer cmd, float time, int w, int h)
        {
            if (_weatherOverlayShader == null || _radarFrames.Count == 0) return;

            // Render the latest radar frame at full opacity
            var latest = _radarFrames[^1];
            if (latest.Image.Handle == 0) return;

            // If a positioned overlay exists, use its bounds for radar frames too
            if (!_hasPositionedOverlay) return;
            RenderPositionedOverlay(cmd, latest,
                _overlayMinLat, _overlayMinLon, _overlayMaxLat, _overlayMaxLon,
                _overlayOpacity, time, w, h);
        }

        private void RenderCrosshair(CommandBuffer cmd, float time, int w, int h)
        {
            if (!ShowCrosshair || _overlayShader == null) return;

            // Build crosshair geometry if needed
            if (_crosshairVertexCount == 0)
                UpdateCrosshairGeometry(w, h);

            if (_crosshairVertexCount == 0) return;

            var vb = _overlayVB;
            ulong offset = 0;
            _vk!.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);

            _overlayShader.SetFloat("uOffsetX", 0);
            _overlayShader.SetFloat("uOffsetY", 0);
            _overlayShader.SetFloat("uColorR", 1f);
            _overlayShader.SetFloat("uColorG", 1f);
            _overlayShader.SetFloat("uColorB", 1f);
            _overlayShader.SetFloat("uAlpha", 0.9f);
            _overlayShader.SetFloat("uTime", time);
            _overlayShader.SetFloat("uEnablePulse", EnableCrosshairPulse ? 1f : 0f);
            _overlayShader.BindAndPush(cmd);

            _vk.CmdDraw(cmd, (uint)_crosshairVertexCount, 1, 0, 0);
        }

        private void UpdateCrosshairGeometry(int w, int h)
        {
            // Build crosshair as two thin rectangles centered at (0,0) in NDC
            float halfLen = 0.03f; // 3% of screen
            float halfThick = 0.002f;
            var verts = new float[]
            {
                // Horizontal bar (6 verts: pos(2) + lineEdge(1))
                -halfLen, -halfThick, -1f,
                 halfLen, -halfThick,  1f,
                 halfLen,  halfThick,  1f,
                -halfLen, -halfThick, -1f,
                 halfLen,  halfThick,  1f,
                -halfLen,  halfThick, -1f,
                // Vertical bar
                -halfThick, -halfLen, -1f,
                 halfThick, -halfLen,  1f,
                 halfThick,  halfLen,  1f,
                -halfThick, -halfLen, -1f,
                 halfThick,  halfLen,  1f,
                -halfThick,  halfLen, -1f,
            };

            _crosshairVertexCount = 12;
            fixed (float* src = verts)
                Unsafe.CopyBlock(_overlayVBMapped, src, (uint)(verts.Length * sizeof(float)));
        }

        private void RenderUserMarker(CommandBuffer cmd, int w, int h)
        {
            if (!_showUserMarker || _overlayShader == null) return;

            double cx = LonToPixelX(_centerLon, _mapZoom);
            double cy = LatToPixelY(_centerLat, _mapZoom);
            double ux = LonToPixelX(_userMarkerLon, _mapZoom);
            double uy = LatToPixelY(_userMarkerLat, _mapZoom);

            float ndcX = (float)((ux - cx + _panX) * _zoom / (w / 2.0));
            float ndcY = (float)((uy - cy + _panY) * _zoom / (h / 2.0));

            // Small diamond marker using crosshair overlay
            var vb = _overlayVB;
            ulong offset = 0;
            _vk!.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);

            _overlayShader.SetFloat("uOffsetX", ndcX);
            _overlayShader.SetFloat("uOffsetY", ndcY);
            _overlayShader.SetFloat("uColorR", 0.2f);
            _overlayShader.SetFloat("uColorG", 0.6f);
            _overlayShader.SetFloat("uColorB", 1.0f);
            _overlayShader.SetFloat("uAlpha", 1.0f);
            _overlayShader.SetFloat("uTime", 0);
            _overlayShader.SetFloat("uEnablePulse", 0);
            _overlayShader.BindAndPush(cmd);

            _vk.CmdDraw(cmd, (uint)Math.Min(_crosshairVertexCount, 12), 1, 0, 0);
        }

        private void RenderHUD(int w, int h)
        {
            if (_hudRenderer == null || !_hudRenderer.IsInitialized) return;

            _hudRenderer.BeginFrame(w, h);

            // Attribution text (bottom-left)
            if (!string.IsNullOrEmpty(HudAttributionText))
            {
                _hudRenderer.DrawRect(0, h - 20, _hudRenderer.MeasureTextWidth(HudAttributionText) + 10, 20, 0, 0, 0, 0.5f);
                _hudRenderer.DrawText(HudAttributionText, 5, h - 18, 0.8f, 0.8f, 0.8f, 0.7f);
            }

            // Status bar (bottom-right)
            if (ShowStatusBar && !string.IsNullOrEmpty(HudStatusBarText))
            {
                float tw = _hudRenderer.MeasureTextWidth(HudStatusBarText);
                _hudRenderer.DrawRect(w - tw - 10, h - 20, tw + 10, 20, 0, 0, 0, 0.5f);
                _hudRenderer.DrawText(HudStatusBarText, w - tw - 5, h - 18, 0.8f, 0.8f, 0.8f, 0.7f);
            }

            // Coordinates HUD (top-center)
            if (ShowCoordinatesHUD)
            {
                string coordText = $"Lat: {_centerLat:F4}  Lon: {_centerLon:F4}  Z: {_mapZoom}";
                float tw = _hudRenderer.MeasureTextWidth(coordText);
                float cx = (w - tw) / 2f;
                _hudRenderer.DrawRect(cx - 5, 2, tw + 10, 22, 0, 0, 0, 0.6f);
                _hudRenderer.DrawText(coordText, cx, 4, 1, 1, 1, 0.9f);
            }

            // Status text (bottom-center)
            if (!string.IsNullOrEmpty(HudStatusText))
            {
                float tw = _hudRenderer.MeasureTextWidth(HudStatusText);
                float cx2 = (w - tw) / 2f;
                _hudRenderer.DrawRect(cx2 - 5, h - 40, tw + 10, 22, 0, 0, 0, 0.6f);
                _hudRenderer.DrawText(HudStatusText, cx2, h - 38, 1, 0.9f, 0.3f, 0.9f);
            }

            // Loading overlay
            if (_mapLoading)
            {
                string loadText = "Loading tiles...";
                float tw = _hudRenderer.MeasureTextWidth(loadText);
                _hudRenderer.DrawRect((w - tw) / 2f - 10, h / 2f - 12, tw + 20, 26, 0, 0, 0, 0.7f);
                _hudRenderer.DrawText(loadText, (w - tw) / 2f, h / 2f - 10, 1, 1, 1, 0.85f);
            }

            // HudSystem panels
            HudSystem?.Render(_hudRenderer, w, h);

            _hudRenderer.EndFrame();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Transform helpers
        // ═══════════════════════════════════════════════════════════════════
        private static float[] BuildTileTransform(float screenX, float screenY, float screenW, float screenH, int vpW, int vpH)
        {
            // Maps unit quad (0,0)-(1,1) to Vulkan clip space for the given screen rect.
            // Vulkan clip: X [-1,+1] left-to-right, Y [-1,+1] top-to-bottom.
            float sx = 2f * screenW / vpW;
            float sy = 2f * screenH / vpH;
            float tx = 2f * screenX / vpW - 1f;
            float ty = 2f * screenY / vpH - 1f;

            // Compact 9-float mat3 — SetMatrix3 pads each row to vec4 for push constants
            return new float[]
            {
                sx,  0f,  0f,   // row 0
                0f,  sy,  0f,   // row 1
                tx,  ty,  1f,   // row 2
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tile loading
        // ═══════════════════════════════════════════════════════════════════
        private void EnqueueTileLoad((int z, int x, int y) key)
        {
            if (_tileProvider == null) return;
            if (_pendingLoads.ContainsKey(key)) return;
            if (_blockedTiles.TryGetValue(key, out var blockedTime) && DateTime.UtcNow - blockedTime < BlockedTileTtl) return;

            var task = VulkanTileLoadHelper.LoadAsync(this, key);
            _pendingLoads[key] = task;
        }

        /// <summary>
        /// Non-unsafe helper to allow async/await in tile loading.
        /// Nested class in unsafe class is still unsafe, so the helper is at namespace level.
        /// </summary>
        // (See VulkanTileLoadHelper below the class)

        /// <summary>Called from VulkanTileLoadHelper (non-unsafe) to upload a tile bitmap.</summary>
        internal void UploadTileFromBitmap((int z, int x, int y) key, Bitmap bmp)
        {
            var tex = UploadBitmapToTexture(bmp);
            _tileTextures[key] = tex;
            _mapLoading = false;
            _hostPanel.Invalidate();
        }

        /// <summary>Called from VulkanTileLoadHelper (non-unsafe) to mark a tile blocked.</summary>
        internal void MarkTileBlocked((int z, int x, int y) key)
        {
            _blockedTiles[key] = DateTime.UtcNow;
            TileStatusChanged?.Invoke($"Tile {key} blocked", Color.OrangeRed);
        }

        /// <summary>Called from VulkanTileLoadHelper (non-unsafe) when a tile load completes.</summary>
        internal void RemovePendingLoad((int z, int x, int y) key)
        {
            _pendingLoads.TryRemove(key, out _);
        }

        internal TileProvider? TileProvider => _tileProvider;

        private void EvictOldTiles()
        {
            var entries = new List<((int, int, int) key, long ticks)>();
            foreach (var kv in _tileLastUsed)
                entries.Add((kv.Key, kv.Value));

            entries.Sort((a, b) => a.ticks.CompareTo(b.ticks));

            int toRemove = entries.Count / 4;
            for (int i = 0; i < toRemove && i < entries.Count; i++)
            {
                if (_tileTextures.TryRemove(entries[i].key, out var tex))
                    tex.Dispose();
                _tileLastUsed.TryRemove(entries[i].key, out _);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Map projection (Mercator)
        // ═══════════════════════════════════════════════════════════════════
        private static double LonToPixelX(double lon, int zoom)
        {
            return ((lon + 180.0) / 360.0) * (1 << zoom) * 256.0;
        }

        private static double LatToPixelY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom) * 256.0;
        }

        private static double PixelXToLon(double px, int zoom)
        {
            return px / ((1 << zoom) * 256.0) * 360.0 - 180.0;
        }

        private static double PixelYToLat(double py, int zoom)
        {
            double n = Math.PI - 2.0 * Math.PI * py / ((1 << zoom) * 256.0);
            return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }

        // ═══════════════════════════════════════════════════════════════════
        // IMapRenderer — image/overlay setters
        // ═══════════════════════════════════════════════════════════════════
        public void SetImageBytes(byte[] data) => SetImageBytes(data, null, null, null);

        public void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);

                bool hasAlpha = false;
                var bits = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = bits.Stride;
                    byte* scan = (byte*)bits.Scan0;
                    // Check first 100 pixels for transparency
                    int checkCount = Math.Min(100, bmp.Width * bmp.Height);
                    for (int i = 0; i < checkCount; i++)
                    {
                        if (scan[i * 4 + 3] < 250) { hasAlpha = true; break; }
                    }
                }
                finally { bmp.UnlockBits(bits); }

                if (hasAlpha)
                {
                    // This is a radar overlay — treat as positioned overlay
                    double cLat = sourceCenterLat ?? _centerLat;
                    double cLon = sourceCenterLon ?? _centerLon;
                    int sZoom = sourceZoom ?? _mapZoom;

                    double halfW = bmp.Width / 2.0;
                    double halfH = bmp.Height / 2.0;
                    double cx = LonToPixelX(cLon, sZoom);
                    double cy = LatToPixelY(cLat, sZoom);

                    double minLon2 = PixelXToLon(cx - halfW, sZoom);
                    double maxLon2 = PixelXToLon(cx + halfW, sZoom);
                    double minLat2 = PixelYToLat(cy + halfH, sZoom);
                    double maxLat2 = PixelYToLat(cy - halfH, sZoom);

                    SetImageBytes(data, minLat2, minLon2, maxLat2, maxLon2, sZoom);
                    return;
                }

                // Opaque = background texture
                if (_backgroundTex.Image.Handle != 0) _backgroundTex.Dispose();
                _backgroundTex = UploadBitmapToTexture(bmp);
                _hasBackgroundTexture = true;
                _bgCenterLat = sourceCenterLat ?? _centerLat;
                _bgCenterLon = sourceCenterLon ?? _centerLon;
                _bgSourceZoom = sourceZoom ?? _mapZoom;
                _bgPixelWidth = bmp.Width;
                _bgPixelHeight = bmp.Height;

                BackgroundTextureChanged?.Invoke(true);
                _hostPanel.Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] SetImageBytes error: {ex.Message}");
            }
        }

        public void SetImageBytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);

                if (_overlayTex.Image.Handle != 0) _overlayTex.Dispose();
                _overlayTex = UploadBitmapToTexture(bmp);
                _hasPositionedOverlay = true;
                _overlayMinLat = minLat; _overlayMinLon = minLon;
                _overlayMaxLat = maxLat; _overlayMaxLon = maxLon;

                // Add to radar frame ring buffer
                if (_radarFrames.Count >= MAX_RADAR_FRAMES)
                {
                    _radarFrames[0].Dispose();
                    _radarFrames.RemoveAt(0);
                }
                var frameTex = UploadBitmapToTexture(bmp);
                _radarFrames.Add(frameTex);

                _hostPanel.Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] SetImageBytes(bounds) error: {ex.Message}");
            }
        }

        public void SetOverlay2Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);

                if (_overlay2Tex.Image.Handle != 0) _overlay2Tex.Dispose();
                _overlay2Tex = UploadBitmapToTexture(bmp);
                _hasPositionedOverlay2 = true;
                _overlay2MinLat = minLat; _overlay2MinLon = minLon;
                _overlay2MaxLat = maxLat; _overlay2MaxLon = maxLon;

                _hostPanel.Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] SetOverlay2Bytes error: {ex.Message}");
            }
        }

        public void ClearOverlay()
        {
            ClearPositionedOverlay();
            ClearPositionedOverlay2();
            ClearPositionedOverlay3();
            if (_backgroundTex.Image.Handle != 0) { _backgroundTex.Dispose(); _backgroundTex = default; }
            _hasBackgroundTexture = false;
            BackgroundTextureChanged?.Invoke(false);
            _hostPanel.Invalidate();
        }

        public void ClearPositionedOverlay()
        {
            _hasPositionedOverlay = false;
            if (_overlayTex.Image.Handle != 0) { _overlayTex.Dispose(); _overlayTex = default; }
            _hostPanel.Invalidate();
        }

        public void ClearPositionedOverlay2()
        {
            _hasPositionedOverlay2 = false;
            if (_overlay2Tex.Image.Handle != 0) { _overlay2Tex.Dispose(); _overlay2Tex = default; }
            _hostPanel.Invalidate();
        }

        public void SetOverlay3Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var bmp = new Bitmap(ms);

                if (_overlay3Tex.Image.Handle != 0) _overlay3Tex.Dispose();
                _overlay3Tex = UploadBitmapToTexture(bmp);
                _hasPositionedOverlay3 = true;
                _overlay3MinLat = minLat; _overlay3MinLon = minLon;
                _overlay3MaxLat = maxLat; _overlay3MaxLon = maxLon;

                _hostPanel.Invalidate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] SetOverlay3Bytes error: {ex.Message}");
            }
        }

        public void ClearPositionedOverlay3()
        {
            _hasPositionedOverlay3 = false;
            if (_overlay3Tex.Image.Handle != 0) { _overlay3Tex.Dispose(); _overlay3Tex = default; }
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

        // ═══════════════════════════════════════════════════════════════════
        // Map navigation
        // ═══════════════════════════════════════════════════════════════════
        public void SetCenterLatLon(double lat, double lon)
        {
            _centerLat = lat; _centerLon = lon;
            _panX = 0; _panY = 0;
            MapPositionChanged?.Invoke(lat, lon);
            _hostPanel.Invalidate();
        }

        public void SetMapZoom(int z)
        {
            _mapZoom = Math.Clamp(z, 1, 18);
            _zoom = 1.0f;
            _panX = 0; _panY = 0;
            MapZoomChanged?.Invoke(_mapZoom);
            _hostPanel.Invalidate();
        }

        public void SetMapStyle(OpenMap.MapStyle style)
        {
            _pendingMapStyle = style; // always store so it survives renderer re-init
            if (_tileProvider != null)
                _tileProvider.CurrentStyle = style;
            // Clear tile cache when style changes
            foreach (var tex in _tileTextures.Values) tex.Dispose();
            _tileTextures.Clear();
            _tileLastUsed.Clear();
            _hostPanel.Invalidate();
        }

        public void SetLocalTilesFolder(string? folder)
        {
            _localTileFolder = folder;
            if (_tileProvider != null)
                _tileProvider.LocalTilesRoot = folder;
        }

        public void InvalidateView() => _hostPanel.Invalidate();

        // ═══════════════════════════════════════════════════════════════════
        // Mouse interaction
        // ═══════════════════════════════════════════════════════════════════
        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            float oldZoom = _zoom;

            if (e.Delta > 0)
                _zoom *= 1.15f;
            else
                _zoom /= 1.15f;

            _zoom = Math.Clamp(_zoom, 0.25f, 8f);

            // Keep the world point under the cursor fixed during smooth wheel zoom.
            int w = _hostPanel.Width;
            int h = _hostPanel.Height;
            if (w > 0 && h > 0 && oldZoom > 0f)
            {
                double dx = e.X - (w / 2.0);
                double dy = e.Y - (h / 2.0);
                _panX = (float)(_panX + dx * (1.0 / _zoom - 1.0 / oldZoom));
                _panY = (float)(_panY + dy * (1.0 / _zoom - 1.0 / oldZoom));
            }

            IsSmoothZooming = true;

            _zoomSnapTimer?.Dispose();
            _zoomSnapTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    _hostPanel.BeginInvoke((Action)(() =>
                    {
                        SnapTileZoom();
                    }));
                }
                catch { }
            }, null, 300, Timeout.Infinite);

            _hostPanel.Invalidate();
        }

        private void SnapTileZoom()
        {
            double logZoom = Math.Log(_zoom) / Math.Log(2);
            int zoomDelta;
            const double snapThreshold = 0.40;

            if (Math.Abs(logZoom) < snapThreshold)
            {
                _zoom = 1.0f;
                _panX = 0f;
                _panY = 0f;
                IsSmoothZooming = false;
                _hostPanel.Invalidate();
                return;
            }

            if (logZoom > 0)
                zoomDelta = (int)Math.Ceiling(logZoom - snapThreshold);
            else
                zoomDelta = (int)Math.Floor(logZoom + snapThreshold);

            int targetZoom = Math.Clamp(_mapZoom + zoomDelta, 1, 18);

            _zoom = 1.0f;
            _panX = 0f;
            _panY = 0f;
            IsSmoothZooming = false;

            if (targetZoom != _mapZoom)
            {
                SetMapZoom(targetZoom);
                return;
            }

            _hostPanel.Invalidate();
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
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
            if (_dragging)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _panX -= dx / _zoom;
                _panY -= dy / _zoom;
                _lastMousePos = e.Location;
                _hostPanel.Invalidate();
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool wasDragging = _dragging;
                _dragging = false;
                UpdateCursorStyle();

                if (wasDragging)
                {
                    // Snap center to account for pan
                    double cx = LonToPixelX(_centerLon, _mapZoom) + _panX;
                    double cy = LatToPixelY(_centerLat, _mapZoom) + _panY;
                    _centerLon = PixelXToLon(cx, _mapZoom);
                    _centerLat = PixelYToLat(cy, _mapZoom);
                    _panX = 0; _panY = 0;

                    MapPositionChanged?.Invoke(_centerLat, _centerLon);
                }
                _hostPanel.Invalidate();
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

        // ═══════════════════════════════════════════════════════════════════
        // Helpers
        // ═══════════════════════════════════════════════════════════════════
        private static void Check(Result result)
        {
            if (result != Result.Success)
                throw new Exception($"Vulkan error: {result}");
        }

        // ═══════════════════════════════════════════════════════════════════
        // Dispose
        // ═══════════════════════════════════════════════════════════════════
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _animRefreshTimer?.Dispose();
            _renderBatchTimer?.Dispose();
            _zoomSnapTimer?.Dispose();

            if (_vk == null) return;
            _vk.DeviceWaitIdle(_device);

            // Textures
            foreach (var tex in _tileTextures.Values) tex.Dispose();
            _tileTextures.Clear();
            foreach (var frame in _radarFrames) frame.Dispose();
            _radarFrames.Clear();
            _fallbackTile.Dispose();
            if (_backgroundTex.Image.Handle != 0) _backgroundTex.Dispose();
            if (_overlayTex.Image.Handle != 0) _overlayTex.Dispose();
            if (_overlay2Tex.Image.Handle != 0) _overlay2Tex.Dispose();

            // Shaders
            _tileShader?.Dispose();
            _weatherOverlayShader?.Dispose();
            _overlayShader?.Dispose();
            _generalShader?.Dispose();
            _markerShader?.Dispose();
            _hudRenderer?.Dispose();

            // Buffers
            if (_quadVB.Handle != 0) { _vk.DestroyBuffer(_device, _quadVB, null); _vk.FreeMemory(_device, _quadVBMem, null); }
            if (_quadIB.Handle != 0) { _vk.DestroyBuffer(_device, _quadIB, null); _vk.FreeMemory(_device, _quadIBMem, null); }
            if (_overlayVB.Handle != 0) { _vk.UnmapMemory(_device, _overlayVBMem); _vk.DestroyBuffer(_device, _overlayVB, null); _vk.FreeMemory(_device, _overlayVBMem, null); }

            // Sampler & descriptor pool
            if (_textureSampler.Handle != 0) _vk.DestroySampler(_device, _textureSampler, null);
            if (_textureDescPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _textureDescPool, null);

            // Sync
            if (_imageAvailable.Handle != 0) _vk.DestroySemaphore(_device, _imageAvailable, null);
            if (_renderFinished.Handle != 0) _vk.DestroySemaphore(_device, _renderFinished, null);
            if (_inFlightFence.Handle != 0) _vk.DestroyFence(_device, _inFlightFence, null);

            // Command pool
            if (_commandPool.Handle != 0) _vk.DestroyCommandPool(_device, _commandPool, null);

            // Framebuffers & render pass
            foreach (var fb in _framebuffers)
                if (fb.Handle != 0) _vk.DestroyFramebuffer(_device, fb, null);
            if (_renderPass.Handle != 0) _vk.DestroyRenderPass(_device, _renderPass, null);

            // Swapchain views & swapchain
            foreach (var view in _swapchainImageViews)
                if (view.Handle != 0) _vk.DestroyImageView(_device, view, null);
            if (_swapchain.Handle != 0) _khrSwapchain?.DestroySwapchain(_device, _swapchain, null);

            // Surface
            if (_surface.Handle != 0) _khrSurface?.DestroySurface(_instance, _surface, null);

            // Device & instance
            if (_device.Handle != 0) _vk.DestroyDevice(_device, null);
            if (_instance.Handle != 0) _vk.DestroyInstance(_instance, null);

            _vk.Dispose();
        }
    }

    /// <summary>
    /// Safe (non-unsafe) helper class for async tile loading.
    /// Must be at namespace level because nested classes in unsafe classes inherit the unsafe context.
    /// </summary>
    internal static class VulkanTileLoadHelper
    {
        private static readonly SemaphoreSlim _sem = new(14, 14);

        internal static async Task LoadAsync(VulkanMapRenderer self, (int z, int x, int y) key)
        {
            await _sem.WaitAsync();
            try
            {
                var (data, status) = await self.TileProvider!.GetTileBytesAsync(key.z, key.x, key.y);
                if (status == TileFetchStatus.Ok && data != null && data.Length > 0)
                {
                    // Clone the bitmap pixel data on this thread so the stream/bitmap
                    // are not disposed before the UI-thread upload runs.
                    Bitmap cloned;
                    using (var ms = new MemoryStream(data))
                    using (var bmp = new Bitmap(ms))
                    {
                        cloned = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(cloned))
                            g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                    }

                    self.HostControl.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            self.UploadTileFromBitmap(key, cloned);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[VulkanMapRenderer] Tile upload failed: {ex.Message}");
                        }
                        finally
                        {
                            cloned.Dispose();
                        }
                    }));
                }
                else if (status == TileFetchStatus.Blocked)
                {
                    self.MarkTileBlocked(key);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VulkanMapRenderer] Tile load error {key}: {ex.Message}");
            }
            finally
            {
                _sem.Release();
                self.RemovePendingLoad(key);
            }
        }
    }
}
