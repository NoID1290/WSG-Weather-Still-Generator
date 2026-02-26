using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Net.Http;
using System.Threading.Tasks;
using OpenMap;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Professional Weather Interactive Map Control with full UI
    /// Features: Radar, Temperature overlays, Zoom controls, Layer toggles
    /// All controls are rendered as floating semi-transparent panels in the GPU viewport.
    /// </summary>
    public class WeatherMapControl : UserControl
    {
        private IMapRenderer _glControl;
        private WeatherOverlayManager _overlayManager;
        private BinaryTileCache _tileCache;
        private Timer _updateTimer;
        
        // GL HUD system replaces WinForms sidebar
        private HudSystem _hudSystem;

        // HUD element references (for reading state in callbacks)
        private HudCheckbox _chkRadar;
        private HudCheckbox _chkTemperature;
        private HudSlider _sldRadarOpacity;
        private HudSlider _sldTempOpacity;
        private HudButtonGroup _grpMapStyle;
        private HudDropdown _ddRadarLayer;
        private HudDropdown _ddRadarStyle;
        private HudLabel _lblZoom;
        private HudLabel _lblPosition;
        private HudLabel _lblCacheStats;
        private HudLabel _lblFrameInfo;
        private HudLabel _lblStatusBar;
        private HudCheckbox _chkAnimFollowMap;
        private HudSlider _sldTimeline;

        // Animation transport button refs (for disabling before load)
        private HudButton? _btnStepBack;
        private HudButton? _btnPlayPause;
        private HudButton? _btnStepFwd;
        private HudLabel? _lblSpeed;

        // Animation controls
        private List<byte[]> _animationFrames = new List<byte[]>();
        private List<string> _animationTimestamps = new List<string>();
        private int _currentFrameIndex;
        private bool _isAnimating;
        private Timer _animationTimer;
        private int _animationSpeedMs = 500;
        private System.Threading.Timer? _animationRefreshDebounce;
        private bool _animationRefreshInProgress = false;

        // Periodic status bar refresh timer (updates FPS, VRAM, cache stats even when idle)
        private Timer _statusUpdateTimer;
        private System.Threading.Timer? _hudClearTimer;
        private (double MinLat, double MinLon, double MaxLat, double MaxLon)? _animationBBox;
        private bool _animationLoop = true;

        private HttpClient _httpClient;
        private double _currentLat = 56.1304; // Canada centroid
        private double _currentLon = -106.3468;
        private int _currentZoom = 4;
        private bool _suppressOverlayUpdates = false;

        // User location (cached from IP geolocation)
        private double? _userLat = null;
        private double? _userLon = null;
        private bool _showUserMarker = false;

        public WeatherMapControl()
        {
            InitializeComponents();
            InitializeMapControl();
            InitializeWeatherSystem();
            SetupEventHandlers();
            ApplyTheme();
            
            // Load persisted map settings (zoom, position, layers, opacity, panel position)
            LoadMapSettings();

            // Start update timer
            _updateTimer = new Timer { Interval = 60000 }; // Update every minute
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Initial overlay is triggered by SetLocation/SetZoom from the host form,
            // NOT here — avoids race with location detection.
        }

        private void InitializeComponents()
        {
            this.Size = new Size(1200, 800);
            this.BackColor = ThemeManager.Current.Background;
            // No WinForms sidebar — all controls are rendered via GL HUD
        }

        private void InitializeMapControl()
        {
            // Read the configured rendering API (defaults to OpenGL)
            var config = ConfigManager.LoadConfig();
            var apiString = config.OpenMap?.RenderingApi ?? "OpenGL";
            var api = RenderingFactory.ParseFromString(apiString);

            _glControl = RenderingFactory.CreateMapRenderer(api);
            _glControl.HostControl.Dock = DockStyle.Fill;
            _glControl.HostControl.BackColor = ThemeManager.Current.Background;
            this.Controls.Add(_glControl.HostControl);

            // Create the HUD system and attach to the renderer
            _hudSystem = new HudSystem();
            _glControl.HudSystem = _hudSystem;
        }

        private void InitializeWeatherSystem()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var cacheDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSG", "map_cache");
            
            _tileCache = new BinaryTileCache(cacheDir);

            // Provide an OpenMap MapOverlayService to the overlay manager so
            // RadarImageService can produce *composited* map+radar images when available.
            // Use overlay-only mode: do NOT provide MapOverlayService here so
            // RadarImageService returns transparent radar overlays (live fetching)
            _overlayManager = new WeatherOverlayManager(_httpClient);

            // Subscribe to overlay status events (e.g., "no snow data") for HUD display
            _overlayManager.OverlayStatusChanged += (msg) =>
            {
                if (_glControl == null) return;

                // Cancel any pending auto-clear timer to prevent race conditions
                _hudClearTimer?.Dispose();
                _hudClearTimer = null;

                if (string.IsNullOrEmpty(msg))
                {
                    // Data is now available — clear HUD immediately
                    if (_glControl.HostControl.IsHandleCreated)
                    {
                        try { _glControl.HostControl.BeginInvoke(new Action(() => _glControl.HudStatusText = "")); }
                        catch { }
                    }
                }
                else
                {
                    // Show status message and auto-clear after 5 seconds
                    _glControl.HudStatusText = msg;
                    _hudClearTimer = new System.Threading.Timer(_ =>
                    {
                        try
                        {
                            if (_glControl.HostControl.IsHandleCreated)
                                _glControl.HostControl.BeginInvoke(new Action(() => _glControl.HudStatusText = ""));
                        }
                        catch { }
                    }, null, 5000, System.Threading.Timeout.Infinite);
                }
            };
        }

        private void SetupEventHandlers()
        {
            // Build GL HUD panels (replaces WinForms sidebar)
            BuildHudPanels();

            // Refresh attribution now that checkboxes exist (radar is checked by default)
            UpdateAttributionText();
            
            // Map events
            _glControl.MapZoomChanged += zoom => 
            {
                _currentZoom = zoom;
                UpdateStatusLabels();
                // Only re-fetch overlays after smooth zoom completes (not during intermediate steps)
                if (!_suppressOverlayUpdates && !_glControl.IsSmoothZooming)
                    _ = UpdateOverlays();
                ScheduleAnimationRefresh();
            };
            
            _glControl.MapPositionChanged += (lat, lon) =>
            {
                _currentLat = lat;
                _currentLon = lon;
                UpdateStatusLabels();
                if (!_suppressOverlayUpdates)
                    _ = UpdateOverlays();
                ScheduleAnimationRefresh();
            };
            
            _glControl.TileStatusChanged += (text, color) =>
            {
                // Could update a status bar here
            };

            // Initialize animation timer
            _animationTimer = new Timer { Interval = _animationSpeedMs };
            _animationTimer.Tick += AnimationTimer_Tick;

            // Periodic status bar refresh (FPS, VRAM, cache stats update even when map is idle)
            _statusUpdateTimer = new Timer { Interval = 500 };
            _statusUpdateTimer.Tick += (s, ev) => UpdateStatusLabels();
            _statusUpdateTimer.Start();
        }

        private void BuildHudPanels()
        {
            // === Actions Panel (top-left, horizontal row with Viewport + Shaders) ===
            var actionsPanel = new HudPanel
            {
                Id = "actions",
                Title = "Actions",
                Anchor = HudAnchor.TopLeft,
                Width = 200f,
                CompactWidth = 80f,
                MarginX = 10, MarginY = 10,
                Collapsible = true,
                Collapsed = true,
                LayoutGroup = "topBar"
            };

            actionsPanel.Elements.Add(new HudButton { Id = "loadAnim", Text = "Load Animation", IsAccent = true, OnClick = async () => await LoadRadarAnimation() });
            actionsPanel.Elements.Add(new HudSeparator());
            actionsPanel.Elements.Add(new HudButton { Id = "refresh", Text = "Refresh Weather", IsAccent = true, OnClick = async () => await RefreshWeather() });
            actionsPanel.Elements.Add(new HudButton { Id = "clearCache", Text = "Clear Cache", OnClick = async () => await ClearCache() });
            actionsPanel.Elements.Add(new HudButton { Id = "prefetchMap", Text = "Prefetch Map Tiles", OnClick = async () => await PrefetchMapTiles() });
            actionsPanel.Elements.Add(new HudButton { Id = "prefetchRadar", Text = "Prefetch Radar", OnClick = async () => await PrefetchRadarTiles() });
            actionsPanel.Elements.Add(new HudButton { Id = "generate", Text = "Generate Precomposed", OnClick = async () => await GeneratePrecomposedComposites() });

            _hudSystem.AddPanel(actionsPanel);

            // === Viewport Panel (top-left, horizontal row) ===
            var viewportPanel = new HudPanel
            {
                Id = "viewport",
                Title = "Viewport",
                Anchor = HudAnchor.TopLeft,
                Width = 210f,
                CompactWidth = 85f,
                MarginX = 10, MarginY = 10,
                Collapsible = true,
                Collapsed = true,
                LayoutGroup = "topBar"
            };

            var chkCrosshairMouse = new HudCheckbox
            {
                Id = "crosshairMouse",
                Text = "Use crosshair as mouse",
                Checked = _glControl.UseCrosshairAsMouse,
                OnChanged = on =>
                {
                    _glControl.UseCrosshairAsMouse = on;
                    _glControl.ShowCrosshair = on;
                    _glControl.InvalidateView();
                    try { var cfg = ConfigManager.LoadConfig(); cfg.ShowCrosshair = on; ConfigManager.SaveConfig(cfg, silent: true); } catch { }
                }
            };
            viewportPanel.Elements.Add(chkCrosshairMouse);

            var chkCoords = new HudCheckbox
            {
                Id = "coords",
                Text = "Show Coordinates",
                Checked = _glControl.ShowCoordinatesHUD,
                OnChanged = on =>
                {
                    _glControl.ShowCoordinatesHUD = on;
                    _glControl.InvalidateView();
                    try { var cfg = ConfigManager.LoadConfig(); cfg.ShowCoordinatesHUD = on; ConfigManager.SaveConfig(cfg, silent: true); } catch { }
                }
            };
            viewportPanel.Elements.Add(chkCoords);

            var chkStatusBar = new HudCheckbox
            {
                Id = "showStatusBar",
                Text = "Status Bar",
                Checked = _glControl.ShowStatusBar,
                OnChanged = on =>
                {
                    _glControl.ShowStatusBar = on;
                    _glControl.InvalidateView();
                }
            };
            viewportPanel.Elements.Add(chkStatusBar);

            var chkRuler = new HudCheckbox
            {
                Id = "showRuler",
                Text = "Ruler",
                Checked = _glControl.ShowRuler,
                OnChanged = on =>
                {
                    _glControl.ShowRuler = on;
                    _glControl.InvalidateView();
                }
            };
            viewportPanel.Elements.Add(chkRuler);

            var chkShowLoc = new HudCheckbox
            {
                Id = "showMyLoc",
                Text = "Show My Location",
                Checked = false,
                OnChanged = on => ToggleUserMarker(on)
            };
            viewportPanel.Elements.Add(chkShowLoc);

            _hudSystem.AddPanel(viewportPanel);

            // === Shaders Panel (top-left, horizontal row) ===
            var shadersPanel = new HudPanel
            {
                Id = "shaders",
                Title = "Shaders",
                Anchor = HudAnchor.TopLeft,
                Width = 220f,
                CompactWidth = 80f,
                MarginX = 10, MarginY = 10,
                Collapsible = true,
                Collapsed = true,
                LayoutGroup = "topBar"
            };
            shadersPanel.Elements.Add(new HudCheckbox
            {
                Id = "shaderSaturation",
                Text = "Saturation Boost",
                Checked = true,
                OnChanged = v => { _glControl.EnableTileSaturation = v; _glControl.InvalidateView(); }
            });
            shadersPanel.Elements.Add(new HudCheckbox
            {
                Id = "shaderContrast",
                Text = "Contrast Curve",
                Checked = true,
                OnChanged = v => { _glControl.EnableTileContrast = v; _glControl.InvalidateView(); }
            });
            shadersPanel.Elements.Add(new HudCheckbox
            {
                Id = "shaderVignette",
                Text = "Vignette",
                Checked = true,
                OnChanged = v => { _glControl.EnableTileVignette = v; _glControl.InvalidateView(); }
            });
            shadersPanel.Elements.Add(new HudCheckbox
            {
                Id = "shaderAtmosphere",
                Text = "Atmospheric Tint",
                Checked = true,
                OnChanged = v => { _glControl.EnableTileAtmosphere = v; _glControl.InvalidateView(); }
            });
            shadersPanel.Elements.Add(new HudCheckbox
            {
                Id = "shaderGlow",
                Text = "Radar Glow",
                Checked = true,
                OnChanged = v => { _glControl.EnableRadarGlow = v; _glControl.InvalidateView(); }
            });

            shadersPanel.Elements.Add(new HudSeparator());
            shadersPanel.Elements.Add(new HudLabel { Id = "lblUITransparency", Text = "UI Transparency", IsSection = true });

            shadersPanel.Elements.Add(new HudSlider
            {
                Id = "panelOpacity",
                Text = "Panel Opacity",
                Value = 75, Min = 20, Max = 100, ShowLabel = true,
                OnChanged = val =>
                {
                    _hudSystem.PanelOpacityMultiplier = val / 75f;
                    _glControl.InvalidateView();
                }
            });

            shadersPanel.Elements.Add(new HudSlider
            {
                Id = "statusBarOpacity",
                Text = "Status Bar Opacity",
                Value = 55, Min = 0, Max = 100, ShowLabel = true,
                OnChanged = val =>
                {
                    _glControl.StatusBarOpacity = val / 100f;
                    _glControl.InvalidateView();
                }
            });

            shadersPanel.Elements.Add(new HudSlider
            {
                Id = "animBarOpacity",
                Text = "Animation Bar Opacity",
                Value = 75, Min = 20, Max = 100, ShowLabel = true,
                OnChanged = val =>
                {
                    _hudSystem.AnimationPanelOpacity = val / 75f;
                    _glControl.InvalidateView();
                }
            });

            _hudSystem.AddPanel(shadersPanel);

            // === Zoom Panel - minimal vertical strip, bottom-right above status bar ===
            var zoomPanel = new HudPanel
            {
                Id = "zoom",
                Title = "",
                Anchor = HudAnchor.BottomRight,
                Width = 40f,
                MarginX = 12, MarginY = 40,
                Collapsible = false,
                TitleVisible = false
            };
            zoomPanel.Elements.Add(new HudButton { Id = "zoomIn", Text = "+", IsCompact = true, OnClick = () => ZoomIn() });
            _lblZoom = new HudLabel { Id = "zoomLevel", Text = $"{_currentZoom}", IsDim = true };
            zoomPanel.Elements.Add(_lblZoom);
            zoomPanel.Elements.Add(new HudButton { Id = "zoomOut", Text = "\u2212", IsCompact = true, OnClick = () => ZoomOut() });
            zoomPanel.Elements.Add(new HudSeparator());
            zoomPanel.Elements.Add(new HudButton { Id = "center", Text = "\u25CE", IsCompact = true, OnClick = () => CenterMap() });
            zoomPanel.Elements.Add(new HudButton { Id = "myLocation", Text = "\u2316", IsCompact = true, OnClick = () => GoToMyLocation() });
            _hudSystem.AddPanel(zoomPanel);

            // === Overlays Panel (top-right, collapsible) ===
            var overlayPanel = new HudPanel
            {
                Id = "overlays",
                Title = "Weather Overlays",
                Anchor = HudAnchor.TopRight,
                Width = 240f,
                MarginX = 10, MarginY = 10,
                Collapsible = true
            };

            _chkRadar = new HudCheckbox
            {
                Id = "radar",
                Text = "Radar Composite",
                Checked = true,
                OnChanged = chk => { UpdateAttributionText(); _ = UpdateOverlays(); }
            };
            overlayPanel.Elements.Add(_chkRadar);

            overlayPanel.Elements.Add(new HudLabel { Id = "lblRadarLayer", Text = "Radar Layer", IsDim = true });
            _ddRadarLayer = new HudDropdown
            {
                Id = "radarLayer",
                Text = "Radar Layer",
                Options = new List<string> { "Rain (RRAI)", "Snow (RSNO)", "Combined (RDBR)", "Coverage" },
                SelectedIndex = 0,
                OnSelectionChanged = _ => OnRadarLayerChanged()
            };
            overlayPanel.Elements.Add(_ddRadarLayer);

            overlayPanel.Elements.Add(new HudLabel { Id = "lblRadarStyle", Text = "Radar Style", IsDim = true });
            _ddRadarStyle = new HudDropdown
            {
                Id = "radarStyle",
                Text = "Radar Style",
                Options = new List<string> { "Precip Linear", "Server Default" },
                SelectedIndex = 0,
                OnSelectionChanged = _ => OnRadarStyleChanged()
            };
            overlayPanel.Elements.Add(_ddRadarStyle);

            _sldRadarOpacity = new HudSlider
            {
                Id = "radarOpacity",
                Text = "Radar Opacity",
                Value = 75, Min = 0, Max = 100, ShowLabel = true,
                OnChanged = val =>
                {
                    _overlayManager.RadarOpacity = val / 100f;
                    _glControl.OverlayOpacity = val / 100f;
                    _glControl.InvalidateView();
                }
            };
            overlayPanel.Elements.Add(_sldRadarOpacity);

            overlayPanel.Elements.Add(new HudSeparator());

            _chkTemperature = new HudCheckbox
            {
                Id = "temperature",
                Text = "Temperature Grid",
                Checked = false,
                OnChanged = chk => { UpdateAttributionText(); _ = UpdateOverlays(); }
            };
            overlayPanel.Elements.Add(_chkTemperature);

            _sldTempOpacity = new HudSlider
            {
                Id = "tempOpacity",
                Text = "Temp Opacity",
                Value = 60, Min = 0, Max = 100, ShowLabel = true,
                OnChanged = val =>
                {
                    _overlayManager.TemperatureOpacity = val / 100f;
                    _glControl.Overlay2Opacity = val / 100f;
                    _glControl.InvalidateView();
                }
            };
            overlayPanel.Elements.Add(_sldTempOpacity);

            var chkTempLabels = new HudCheckbox
            {
                Id = "tempLabels",
                Text = "Show Temp Labels",
                Checked = true,
                OnChanged = on =>
                {
                    _overlayManager.ShowTemperatureLabels = on;
                    _overlayManager.InvalidateTemperatureCache();
                    _ = UpdateOverlays();
                }
            };
            overlayPanel.Elements.Add(chkTempLabels);

            _hudSystem.AddPanel(overlayPanel);

            // === Map Style Panel (top-right, auto-stacked below Overlays) ===
            var mapStylePanel = new HudPanel
            {
                Id = "mapStyle",
                Title = "Map Style",
                Anchor = HudAnchor.TopRight,
                Width = 240f,
                MarginX = 10, MarginY = 10,
                Collapsible = true
            };
            _grpMapStyle = new HudButtonGroup
            {
                Id = "mapStyleGroup",
                Options = new List<string> { "Std", "Dark", "Terrain", "Sat" },
                SelectedIndex = 0,
                OnSelectionChanged = idx => OnMapStyleChanged()
            };
            mapStylePanel.Elements.Add(_grpMapStyle);
            _hudSystem.AddPanel(mapStylePanel);

            // === Animation Panel (bottom-center, always visible, slim toolbar) ===
            var animPanel = new HudPanel
            {
                Id = "animation",
                Title = "",
                Anchor = HudAnchor.BottomCenter,
                Width = 500f,
                MarginX = 0, MarginY = 38,
                Visible = true,
                Collapsible = false,
                TitleVisible = false
            };

            // Row 1: transport controls + frame info + load
            var transportRow = new HudInlineRow { Id = "animTransport" };
            _btnStepBack = new HudButton { Id = "animStepBack", Text = "\u23EE", IsCompact = true, IsDisabled = true, OnClick = () => StepAnimationBackward() };
            transportRow.Children.Add(_btnStepBack);
            _btnPlayPause = new HudButton { Id = "animPlayPause", Text = "\u25B6", IsCompact = true, IsAccent = true, IsDisabled = true, OnClick = () => PlayPauseAnimation() };
            transportRow.Children.Add(_btnPlayPause);
            _btnStepFwd = new HudButton { Id = "animStepFwd", Text = "\u23ED", IsCompact = true, IsDisabled = true, OnClick = () => StepAnimationForward() };
            transportRow.Children.Add(_btnStepFwd);
            transportRow.Children.Add(new HudSeparator());
            _lblFrameInfo = new HudLabel { Id = "frameInfo", Text = "No radar loaded", IsDim = true };
            transportRow.Children.Add(_lblFrameInfo);
            transportRow.Children.Add(new HudSeparator());
            transportRow.Children.Add(new HudButton { Id = "animLoadBtn", Text = "Load", IsCompact = false, IsAccent = true, OnClick = async () => await LoadRadarAnimation() });
            animPanel.Elements.Add(transportRow);

            // Row 2: timeline scrub bar with tick marks
            _sldTimeline = new HudSlider
            {
                Id = "animTimeline",
                Text = "",
                Value = 0f,
                Min = 0f,
                Max = 1f,
                ShowLabel = false,
                ShowTicks = false,
                OnChanged = (val) =>
                {
                    if (_animationFrames.Count > 0)
                    {
                        int idx = Math.Min((int)(val * _animationFrames.Count), _animationFrames.Count - 1);
                        if (idx != _currentFrameIndex)
                            ShowAnimationFrame(idx);
                    }
                }
            };
            animPanel.Elements.Add(_sldTimeline);

            // Row 3: speed + loop + follow + close
            var settingsRow = new HudInlineRow { Id = "animSettings" };
            settingsRow.Children.Add(new HudButton { Id = "animSpeedDown", Text = "\u2212", IsCompact = true, OnClick = () => AdjustAnimationSpeed(200) });
            _lblSpeed = new HudLabel { Id = "lblSpeed", Text = "0.5s", IsDim = true };
            settingsRow.Children.Add(_lblSpeed);
            settingsRow.Children.Add(new HudButton { Id = "animSpeedUp", Text = "+", IsCompact = true, OnClick = () => AdjustAnimationSpeed(-200) });
            settingsRow.Children.Add(new HudSeparator());
            settingsRow.Children.Add(new HudButton
            {
                Id = "animLoop",
                Text = "Loop",
                IsCompact = false,
                IsAccent = true,
                OnClick = () =>
                {
                    _animationLoop = !_animationLoop;
                    var loopBtn = FindHudElement<HudButton>("animLoop");
                    if (loopBtn != null) loopBtn.IsAccent = _animationLoop;
                    _glControl?.InvalidateView();
                }
            });
            settingsRow.Children.Add(new HudSeparator());
            _chkAnimFollowMap = new HudCheckbox { Id = "animFollow", Text = "Follow", Checked = true };
            settingsRow.Children.Add(_chkAnimFollowMap);
            settingsRow.Children.Add(new HudSeparator());
            settingsRow.Children.Add(new HudButton { Id = "animClose", Text = "X", IsCompact = true, OnClick = () => CloseRadarAnimation() });
            animPanel.Elements.Add(settingsRow);

            _hudSystem.AddPanel(animPanel);

            UpdateStatusLabels();
        }

        private T? FindHudElement<T>(string id) where T : HudElement
        {
            foreach (var panel in _hudSystem.Panels)
            {
                foreach (var el in panel.Elements)
                {
                    if (el.Id == id && el is T typed) return typed;
                    if (el is HudInlineRow row)
                    {
                        foreach (var child in row.Children)
                            if (child.Id == id && child is T typedChild) return typedChild;
                    }
                }
            }
            return null;
        }

        private MapStyle GetCurrentMapStyle()
        {
            if (_grpMapStyle == null) return MapStyle.Standard;
            return _grpMapStyle.SelectedIndex switch
            {
                0 => MapStyle.Standard,
                1 => MapStyle.TerrainDark,
                2 => MapStyle.Terrain,
                3 => MapStyle.Satellite,
                _ => MapStyle.Standard
            };
        }

        private void UpdateAttributionText()
        {
            var style = GetCurrentMapStyle();
            var lines = new List<string>();
            lines.Add(MapOverlayService.GetAttributionText(style));

            if (_chkRadar != null && _chkRadar.Checked)
                lines.Add("Radar: Environment and Climate Change Canada");
            if (_chkTemperature != null && _chkTemperature.Checked)
                lines.Add("Weather: Open-Meteo.com (CC BY 4.0)");

            var text = string.Join("  |  ", lines);

            // Pipe to GL HUD
            if (_glControl != null)
                _glControl.HudAttributionText = text;
        }

        // â•â•â• Animation Logic â•â•â•

        private async Task LoadRadarAnimation()
        {
            var loadBtn = FindHudElement<HudButton>("loadAnim");
            if (loadBtn != null) loadBtn.Text = "Loading...";
            _hudSystem.LoadingMessage = "Loading radar frames...";
            _glControl?.InvalidateView();

            try
            {
                // Fetch timestamps
                var timestamps = await _overlayManager.FetchRadarTimestampsAsync(8);
                if (timestamps.Count == 0)
                {
                    MessageBox.Show("No radar timestamps available.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _hudSystem.LoadingMessage = $"Fetching {timestamps.Count} radar frames...";
                _glControl?.InvalidateView();

                // Fetch frames
                var (frames, validTimestamps) = await _overlayManager.FetchMultipleRadarFramesAsync(
                    _currentLat, _currentLon, _glControl.HostControl.Width, _glControl.HostControl.Height, _currentZoom, timestamps);

                if (frames.Count == 0)
                {
                    MessageBox.Show("No radar animation frames available.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _animationFrames = frames;
                _animationTimestamps = validTimestamps;
                _animationBBox = _overlayManager.LastRadarBBox;
                _currentFrameIndex = 0;

                // Enable transport controls now that frames are available
                if (_btnStepBack != null) _btnStepBack.IsDisabled = false;
                if (_btnPlayPause != null) _btnPlayPause.IsDisabled = false;
                if (_btnStepFwd != null) _btnStepFwd.IsDisabled = false;

                // Mark Load button as loaded (green)
                var loadBtnLoaded = FindHudElement<HudButton>("animLoadBtn");
                if (loadBtnLoaded != null)
                {
                    loadBtnLoaded.Text = "Reload";
                    loadBtnLoaded.IsAccent = false;
                    loadBtnLoaded.IsSuccess = true;
                }

                // Populate timeline tick labels (HH:MM per frame)
                if (_sldTimeline != null)
                {
                    _sldTimeline.TickLabels = validTimestamps.Select(ts => FormatTimeOnly(ts)).ToList();
                    _sldTimeline.ShowTicks = true;
                }

                // Show animation HUD panel
                var animPanel = _hudSystem.GetPanel("animation");
                if (animPanel != null) animPanel.Visible = true;

                // Show first frame
                ShowAnimationFrame(0);

                // Reset timeline slider range
                if (_sldTimeline != null)
                {
                    _sldTimeline.Value = 0f;
                    _sldTimeline.Max = 1f;
                }

                _lblFrameInfo.Text = $"1/{_animationFrames.Count} {FormatTimestamp(_animationTimestamps[0])}";
                _glControl?.InvalidateView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load animation: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _hudSystem.LoadingMessage = null;
                if (loadBtn != null) loadBtn.Text = "Load Animation";
                _glControl?.InvalidateView();
            }
        }

        private void PlayPauseAnimation()
        {
            if (_animationFrames.Count == 0) return;

            _isAnimating = !_isAnimating;

            var ppBtn = FindHudElement<HudButton>("animPlayPause");
            if (_isAnimating)
            {
                if (ppBtn != null) { ppBtn.Text = "⏸"; ppBtn.IsAccent = false; }
                _animationTimer.Interval = _animationSpeedMs;
                _animationTimer.Start();
            }
            else
            {
                if (ppBtn != null) { ppBtn.Text = "▶"; ppBtn.IsAccent = true; }
                _animationTimer.Stop();
            }
            _glControl?.InvalidateView();
        }

        private void StepAnimationForward()
        {
            if (_animationFrames.Count == 0) return;
            if (_isAnimating) { PlayPauseAnimation(); } // Pause first
            _currentFrameIndex = (_currentFrameIndex + 1) % _animationFrames.Count;
            ShowAnimationFrame(_currentFrameIndex);
        }

        private void StepAnimationBackward()
        {
            if (_animationFrames.Count == 0) return;
            if (_isAnimating) { PlayPauseAnimation(); } // Pause first
            _currentFrameIndex = (_currentFrameIndex - 1 + _animationFrames.Count) % _animationFrames.Count;
            ShowAnimationFrame(_currentFrameIndex);
        }

        private void ShowAnimationFrame(int index)
        {
            if (index < 0 || index >= _animationFrames.Count) return;
            _currentFrameIndex = index;

            var frameData = _animationFrames[index];
            // Use the saved bbox from when frames were loaded/refreshed,
            // so frames stay anchored to their original position when follow-map is off
            var bbox = (_chkAnimFollowMap?.Checked ?? true)
                ? _overlayManager.LastRadarBBox
                : _animationBBox;
            if (bbox.HasValue)
            {
                _glControl.SetImageBytes(frameData, bbox.Value.MinLat, bbox.Value.MinLon,
                    bbox.Value.MaxLat, bbox.Value.MaxLon, _currentZoom);
            }
            else
            {
                _glControl.SetImageBytes(frameData, _currentLat, _currentLon, _currentZoom);
            }

            var ts = index < _animationTimestamps.Count ? FormatTimestamp(_animationTimestamps[index]) : "";
            _lblFrameInfo.Text = $"{index + 1}/{_animationFrames.Count} {ts}";

            // Update timeline slider position (avoid re-triggering OnChanged)
            if (_sldTimeline != null && _animationFrames.Count > 1)
                _sldTimeline.Value = (float)index / (_animationFrames.Count - 1);

            _glControl?.InvalidateView();
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_animationFrames.Count == 0) return;

            int nextFrame = _currentFrameIndex + 1;
            if (nextFrame >= _animationFrames.Count)
            {
                if (_animationLoop)
                {
                    nextFrame = 0;
                }
                else
                {
                    // Stop at last frame
                    _isAnimating = false;
                    _animationTimer?.Stop();
                    var ppb = FindHudElement<HudButton>("animPlayPause");
                    if (ppb != null) { ppb.Text = "▶"; ppb.IsAccent = true; }
                    _glControl?.InvalidateView();
                    return;
                }
            }
            _currentFrameIndex = nextFrame;
            ShowAnimationFrame(_currentFrameIndex);
        }

        private void AdjustAnimationSpeed(int deltaMs)
        {
            _animationSpeedMs = Math.Max(100, Math.Min(2000, _animationSpeedMs + deltaMs));
            // Update live speed label
            if (_lblSpeed != null) _lblSpeed.Text = $"{_animationSpeedMs / 1000.0:F1}s";
            // Speed is shown via frame info label
            if (_isAnimating)
            {
                _animationTimer.Interval = _animationSpeedMs;
            }
        }

        /// <summary>
        /// Schedule a debounced re-fetch of animation frames after map pan/zoom.
        /// Waits 400ms after the last move event before re-fetching.
        /// </summary>
        private void ScheduleAnimationRefresh()
        {
            // Only refresh if animation frames are loaded and checkbox is checked
            if (_animationTimestamps.Count == 0) return;
            if (!(_chkAnimFollowMap?.Checked ?? true)) return;

            // Reset the debounce timer (400ms after last pan/zoom event)
            _animationRefreshDebounce?.Dispose();
            _animationRefreshDebounce = new System.Threading.Timer(
                _ => this.BeginInvoke(new Action(async () => await RefreshAnimationFrames())),
                null, 400, System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// Re-fetches all animation frames for the current viewport, keeping the same timestamps.
        /// </summary>
        private async Task RefreshAnimationFrames()
        {
            // Re-check checkbox state at execution time (timer may have been scheduled before unchecking)
            if (!(_chkAnimFollowMap?.Checked ?? true)) return;
            if (_animationRefreshInProgress || _animationTimestamps.Count == 0) return;
            _animationRefreshInProgress = true;

            // Remember playback state
            bool wasPlaying = _isAnimating;
            if (wasPlaying)
            {
                _animationTimer.Stop();
            }

            _hudSystem.LoadingMessage = "Updating radar frames...";
            _lblFrameInfo.Text = "Updating...";

            try
            {
                var (frames, validTimestamps) = await _overlayManager.FetchMultipleRadarFramesAsync(
                    _currentLat, _currentLon, _glControl.HostControl.Width, _glControl.HostControl.Height, _currentZoom, _animationTimestamps);

                if (frames.Count > 0)
                {
                    _animationFrames = frames;
                    _animationTimestamps = validTimestamps;
                    _animationBBox = _overlayManager.LastRadarBBox;

                    // Clamp frame index
                    _currentFrameIndex = Math.Min(_currentFrameIndex, _animationFrames.Count - 1);

                    // Show current frame at new viewport position
                    ShowAnimationFrame(_currentFrameIndex);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Animation refresh error: {ex.Message}");
            }
            finally
            {
                _hudSystem.LoadingMessage = null;
                _animationRefreshInProgress = false;

                // Resume playback if it was playing
                if (wasPlaying && _animationFrames.Count > 0)
                {
                    _isAnimating = true;
                    _animationTimer.Interval = _animationSpeedMs;
                    _animationTimer.Start();
                }
            }
        }

        private string FormatTimestamp(string isoTimestamp)
        {
            if (DateTime.TryParse(isoTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt.ToLocalTime().ToString("HH:mm") + " " + dt.ToLocalTime().ToString("MMM dd");
            }
            return isoTimestamp;
        }

        /// <summary>
        /// Returns a compact HH:MM-only string for use in timeline tick labels.
        /// </summary>
        private string FormatTimeOnly(string isoTimestamp)
        {
            if (DateTime.TryParse(isoTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("HH:mm");
            return isoTimestamp.Length >= 5 ? isoTimestamp[..5] : isoTimestamp;
        }

        // â•â•â• Map Style / Radar Config Handlers â•â•â•

        private void OnMapStyleChanged()
        {
            var style = (_grpMapStyle?.SelectedIndex ?? 0) switch
            {
                0 => MapStyle.Standard,
                1 => MapStyle.TerrainDark,
                2 => MapStyle.Terrain,
                3 => MapStyle.Satellite,
                _ => MapStyle.Standard
            };
            _glControl.SetMapStyle(style);
            UpdateAttributionText();
        }

        private void OnRadarLayerChanged()
        {
            _overlayManager.RadarLayer = (_ddRadarLayer?.SelectedIndex ?? 0) switch
            {
                0 => "RADAR_1KM_RRAI",
                1 => "RADAR_1KM_RSNO",
                2 => "RADAR_1KM_RDBR",
                3 => "RADAR_COVERAGE_RRAI.INV",
                _ => "RADAR_1KM_RRAI"
            };
            _overlayManager.RadarWmsStyle = WeatherOverlayManager.GetDefaultStyleForLayer(_overlayManager.RadarLayer);
            if (_ddRadarStyle != null)
                _ddRadarStyle.SelectedIndex = _overlayManager.RadarWmsStyle != null ? 0 : 1;
            _ = UpdateOverlays();
        }

        private void OnRadarStyleChanged()
        {
            _overlayManager.RadarWmsStyle = (_ddRadarStyle?.SelectedIndex ?? 0) switch
            {
                0 => "RADARURPPRECIPR14-LINEAR",
                1 => null,
                _ => "RADARURPPRECIPR14-LINEAR"
            };
            _ = UpdateOverlays();
        }

        // â•â•â• Public API for external control (keyboard shortcuts) â•â•â•

        public void ToggleAnimation()
        {
            if (_animationFrames.Count > 0)
                PlayPauseAnimation();
        }

        public void StepForward() => StepAnimationForward();
        public void StepBackward() => StepAnimationBackward();

        public void CycleMapStyle()
        {
            if (_grpMapStyle == null) return;
            _grpMapStyle.SelectedIndex = (_grpMapStyle.SelectedIndex + 1) % _grpMapStyle.Options.Count;
            OnMapStyleChanged();
            _glControl?.InvalidateView();
        }

        public void SetMapStyleByIndex(int index)
        {
            if (_grpMapStyle == null || index < 0 || index >= _grpMapStyle.Options.Count) return;
            _grpMapStyle.SelectedIndex = index;
            OnMapStyleChanged();
            _glControl?.InvalidateView();
        }

        private void CloseRadarAnimation()
        {
            // Stop animation
            if (_isAnimating)
            {
                _isAnimating = false;
                _animationTimer?.Stop();
                var ppBtn = FindHudElement<HudButton>("animPlayPause");
                if (ppBtn != null) { ppBtn.Text = "▶"; ppBtn.IsAccent = true; }
            }

            // Clear frames
            _animationFrames.Clear();
            _animationTimestamps.Clear();
            _animationBBox = null;
            _currentFrameIndex = 0;

            // Re-disable transport controls
            if (_btnStepBack != null) _btnStepBack.IsDisabled = true;
            if (_btnPlayPause != null) { _btnPlayPause.IsDisabled = true; _btnPlayPause.IsAccent = true; }
            if (_btnStepFwd != null) _btnStepFwd.IsDisabled = true;

            // Reset Load button to original state
            var loadBtn2 = FindHudElement<HudButton>("animLoadBtn");
            if (loadBtn2 != null)
            {
                loadBtn2.Text = "Load";
                loadBtn2.IsAccent = true;
                loadBtn2.IsSuccess = false;
            }

            // Reset timeline ticks
            if (_sldTimeline != null)
            {
                _sldTimeline.ShowTicks = false;
                _sldTimeline.TickLabels.Clear();
                _sldTimeline.Value = 0f;
            }

            // Hide animation HUD panel
            var animPanel = _hudSystem.GetPanel("animation");
            if (animPanel != null) animPanel.Visible = false;

            // Clear overlay from GL
            _glControl.ClearPositionedOverlay();

            _lblFrameInfo.Text = "No radar loaded";
            _glControl?.InvalidateView();
        }

        private void SaveMapSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.WeatherMapView ??= new WeatherMapViewSettings();
                config.WeatherMapView.ZoomLevel = _currentZoom;
                config.WeatherMapView.Latitude = _currentLat;
                config.WeatherMapView.Longitude = _currentLon;
                config.WeatherMapView.MapStyleIndex = _grpMapStyle?.SelectedIndex ?? 0;
                config.WeatherMapView.RadarEnabled = _chkRadar?.Checked ?? true;
                config.WeatherMapView.TemperatureEnabled = _chkTemperature?.Checked ?? false;
                config.WeatherMapView.RadarOpacity = (int)(_sldRadarOpacity?.Value ?? 75);
                config.WeatherMapView.TemperatureOpacity = (int)(_sldTempOpacity?.Value ?? 60);
                config.WeatherMapView.RadarLayerIndex = _ddRadarLayer?.SelectedIndex ?? 0;
                config.WeatherMapView.RadarStyleIndex = _ddRadarStyle?.SelectedIndex ?? 0;
                config.WeatherMapView.PanelPosition = "Right";
                config.WeatherMapView.ShowStatusBar = _glControl?.ShowStatusBar ?? true;
                config.WeatherMapView.ShowRuler = _glControl?.ShowRuler ?? true;
                config.WeatherMapView.PanelOpacity = (int)(((_hudSystem?.PanelOpacityMultiplier ?? 1f) * 75f));
                config.WeatherMapView.StatusBarOpacity = (int)((_glControl?.StatusBarOpacity ?? 0.55f) * 100f);
                config.WeatherMapView.AnimationBarOpacity = (int)(((_hudSystem?.AnimationPanelOpacity ?? 1f) * 75f));
                ConfigManager.SaveConfig(config, silent: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Failed to save map settings: {ex.Message}");
            }
        }

        private void LoadMapSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var s = config.WeatherMapView;
                if (s == null) return;

                _currentZoom = Math.Max(1, Math.Min(20, s.ZoomLevel));
                _currentLat = s.Latitude;
                _currentLon = s.Longitude;

                // Suppress overlay updates while restoring UI state
                _suppressOverlayUpdates = true;
                try
                {
                    if (_grpMapStyle != null && s.MapStyleIndex >= 0 && s.MapStyleIndex < _grpMapStyle.Options.Count)
                        _grpMapStyle.SelectedIndex = s.MapStyleIndex;

                    if (_chkRadar != null) _chkRadar.Checked = s.RadarEnabled;
                    if (_chkTemperature != null) _chkTemperature.Checked = s.TemperatureEnabled;

                    if (_sldRadarOpacity != null && s.RadarOpacity >= 0 && s.RadarOpacity <= 100)
                        _sldRadarOpacity.Value = s.RadarOpacity;

                    if (_sldTempOpacity != null && s.TemperatureOpacity >= 0 && s.TemperatureOpacity <= 100)
                        _sldTempOpacity.Value = s.TemperatureOpacity;

                    if (_ddRadarLayer != null && s.RadarLayerIndex >= 0 && s.RadarLayerIndex < _ddRadarLayer.Options.Count)
                        _ddRadarLayer.SelectedIndex = s.RadarLayerIndex;

                    if (_ddRadarStyle != null && s.RadarStyleIndex >= 0 && s.RadarStyleIndex < _ddRadarStyle.Options.Count)
                        _ddRadarStyle.SelectedIndex = s.RadarStyleIndex;
                }
                finally
                {
                    _suppressOverlayUpdates = false;
                }

                // Always apply map style & overlay manager state explicitly
                OnMapStyleChanged();
                OnRadarLayerChanged();
                OnRadarStyleChanged();

                // Apply opacity to overlay manager
                if (_overlayManager != null)
                {
                    _overlayManager.RadarOpacity = (_sldRadarOpacity?.Value ?? 75) / 100f;
                    _overlayManager.TemperatureOpacity = (_sldTempOpacity?.Value ?? 60) / 100f;
                }
                if (_glControl != null)
                {
                    _glControl.OverlayOpacity = (_sldRadarOpacity?.Value ?? 75) / 100f;
                }

                // Viewport display settings
                if (_glControl != null)
                {
                    _glControl.ShowCrosshair = config.ShowCrosshair;
                    _glControl.UseCrosshairAsMouse = config.ShowCrosshair;
                    _glControl.ShowCoordinatesHUD = config.ShowCoordinatesHUD;
                    _glControl.ShowStatusBar = s.ShowStatusBar;
                    _glControl.ShowRuler = s.ShowRuler;
                    _glControl.StatusBarOpacity = s.StatusBarOpacity / 100f;
                }

                // UI opacity settings
                if (_hudSystem != null)
                {
                    _hudSystem.PanelOpacityMultiplier = s.PanelOpacity / 75f;
                    _hudSystem.AnimationPanelOpacity = s.AnimationBarOpacity / 75f;
                }

                // Sync HUD checkboxes with loaded state
                var chkSB = FindHudElement<HudCheckbox>("showStatusBar");
                if (chkSB != null) chkSB.Checked = s.ShowStatusBar;
                var chkR = FindHudElement<HudCheckbox>("showRuler");
                if (chkR != null) chkR.Checked = s.ShowRuler;

                // Sync opacity sliders
                var sldPanel = FindHudElement<HudSlider>("panelOpacity");
                if (sldPanel != null) sldPanel.Value = s.PanelOpacity;
                var sldSB = FindHudElement<HudSlider>("statusBarOpacity");
                if (sldSB != null) sldSB.Value = s.StatusBarOpacity;
                var sldAnim = FindHudElement<HudSlider>("animBarOpacity");
                if (sldAnim != null) sldAnim.Value = s.AnimationBarOpacity;
                if (_overlayManager != null)
                {
                    _overlayManager.ShowTemperatureLabels = config.ShowTemperatureLabels;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Failed to load map settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-applies theme. GL HUD uses its own built-in color scheme.
        /// </summary>
        public void ApplyTheme()
        {
            this.BackColor = ThemeManager.Current.Background;
            if (_glControl != null)
                _glControl.HostControl.BackColor = ThemeManager.Current.Background;
        }

        // Public API
        public int CurrentZoom => _currentZoom;

        public void SetLocation(double lat, double lon)
        {
            _currentLat = lat;
            _currentLon = lon;
            _glControl.SetCenterLatLon(lat, lon);
            UpdateStatusLabels();
            // Don't call UpdateOverlays here — MapPositionChanged handler does it,
            // and if SetZoom follows immediately, we'd fetch at the wrong zoom.
        }

        /// <summary>
        /// Set location and zoom atomically, triggering a single overlay fetch at the correct viewport.
        /// Use this on startup or when both values change together.
        /// </summary>
        public void SetLocationAndZoom(double lat, double lon, int zoom)
        {
            _currentLat = lat;
            _currentLon = lon;
            _currentZoom = Math.Max(1, Math.Min(20, zoom));

            // Suppress event-driven overlay updates while we set both values
            _suppressOverlayUpdates = true;
            try
            {
                _glControl.SetCenterLatLon(lat, lon);
                _glControl.SetMapZoom(_currentZoom);
            }
            finally
            {
                _suppressOverlayUpdates = false;
            }

            UpdateStatusLabels();
            // Single overlay update with correct lat/lon AND zoom
            _ = UpdateOverlays();
        }

        public void SetZoom(int zoom)
        {
            _currentZoom = Math.Max(1, Math.Min(20, zoom));
            _glControl.SetMapZoom(_currentZoom);
            UpdateStatusLabels();
        }

        private void ZoomIn()
        {
            SetZoom(_currentZoom + 1);
            _ = UpdateOverlays();
        }

        private void ZoomOut()
        {
            SetZoom(_currentZoom - 1);
            _ = UpdateOverlays();
        }

        private void CenterMap()
        {
            SetLocation(56.1304, -106.3468); // Canada
        }

        private async void GoToMyLocation()
        {
            _glControl.HudStatusText = "Locating...";
            _glControl.InvalidateView();

            var loc = await GetUserLocationAsync();
            if (loc.HasValue)
            {
                _userLat = loc.Value.lat;
                _userLon = loc.Value.lon;
                SetLocationAndZoom(loc.Value.lat, loc.Value.lon, 10);
                _ = UpdateOverlays();
                _glControl.HudStatusText = $"Located: {loc.Value.lat:F2}, {loc.Value.lon:F2}";

                // Update marker if visible
                if (_showUserMarker)
                {
                    _glControl.UserMarkerLat = _userLat.Value;
                    _glControl.UserMarkerLon = _userLon.Value;
                    _glControl.ShowUserMarker = true;
                }
            }
            else
            {
                _glControl.HudStatusText = "Location unavailable";
            }
            _glControl.InvalidateView();

            // Auto-clear status after 3 seconds
            _hudClearTimer?.Dispose();
            _hudClearTimer = new System.Threading.Timer(_ =>
            {
                try { if (_glControl.HostControl.IsHandleCreated) _glControl.HostControl.BeginInvoke(new Action(() => _glControl.HudStatusText = "")); } catch { }
            }, null, 3000, System.Threading.Timeout.Infinite);
        }

        private async void ToggleUserMarker(bool on)
        {
            _showUserMarker = on;
            if (on)
            {
                if (_userLat == null || _userLon == null)
                {
                    _glControl.HudStatusText = "Fetching location...";
                    _glControl.InvalidateView();
                    var loc = await GetUserLocationAsync();
                    if (loc.HasValue)
                    {
                        _userLat = loc.Value.lat;
                        _userLon = loc.Value.lon;
                    }
                    else
                    {
                        _glControl.HudStatusText = "Location unavailable";
                        _glControl.InvalidateView();
                        _hudClearTimer?.Dispose();
                        _hudClearTimer = new System.Threading.Timer(_ =>
                        {
                            try { if (_glControl.HostControl.IsHandleCreated) _glControl.HostControl.BeginInvoke(new Action(() => _glControl.HudStatusText = "")); } catch { }
                        }, null, 3000, System.Threading.Timeout.Infinite);
                        return;
                    }
                }
                _glControl.UserMarkerLat = _userLat.Value;
                _glControl.UserMarkerLon = _userLon.Value;
                _glControl.ShowUserMarker = true;
                _glControl.HudStatusText = "";
            }
            else
            {
                _glControl.ShowUserMarker = false;
            }
            _glControl.InvalidateView();
        }

        /// <summary>Fetch user's approximate location via IP geolocation (ip-api.com)</summary>
        private static async Task<(double lat, double lon)?> GetUserLocationAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon,status");
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
                {
                    if (root.TryGetProperty("lat", out var lat) && root.TryGetProperty("lon", out var lon))
                        return (lat.GetDouble(), lon.GetDouble());
                }
            }
            catch { }
            return null;
        }

        private bool _overlayUpdateInProgress = false;

        private async Task UpdateOverlays()
        {
            // Prevent concurrent overlay updates (races cause GL errors)
            if (_overlayUpdateInProgress) return;
            _overlayUpdateInProgress = true;

            try
            {
                _overlayManager.RadarEnabled = _chkRadar.Checked;
                _overlayManager.TemperatureEnabled = _chkTemperature.Checked;

                if (!_overlayManager.RadarEnabled && !_overlayManager.TemperatureEnabled)
                {
                    _glControl.ClearOverlay();
                    return;
                }

                Console.WriteLine($"[WeatherMap] UpdateOverlays: pos=({_currentLat:F2},{_currentLon:F2}), size={_glControl.HostControl.Width}x{_glControl.HostControl.Height}, zoom={_currentZoom}");

                // â”€â”€ GPU compositing: upload radar and temperature as separate GL textures â”€â”€
                // Each overlay gets its own texture slot with independent opacity.
                // Alpha blending on the GPU composites them — no CPU-side GDI+ CompositeOverlays needed.

                byte[]? radarData = null;
                byte[]? tempData = null;

                if (_overlayManager.RadarEnabled)
                {
                    radarData = await _overlayManager.UpdateRadarOverlayAsync(
                        _currentLat, _currentLon, _glControl.HostControl.Width, _glControl.HostControl.Height, _currentZoom);
                }

                if (_overlayManager.TemperatureEnabled)
                {
                    tempData = await _overlayManager.UpdateTemperatureOverlayAsync(
                        _currentLat, _currentLon, _glControl.HostControl.Width, _glControl.HostControl.Height, _currentZoom);
                }

                Console.WriteLine($"[WeatherMap] Radar: {(radarData != null ? $"{radarData.Length} bytes" : "null")}, Temp: {(tempData != null ? $"{tempData.Length} bytes" : "null")}");

                // Upload radar overlay to primary overlay slot
                if (radarData != null && radarData.Length > 0)
                {
                    _glControl.OverlayOpacity = _overlayManager.RadarOpacity;
                    var radarBBox = _overlayManager.LastRadarBBox;
                    if (radarBBox.HasValue)
                    {
                        Console.WriteLine($"[WeatherMap] Setting radar overlay (slot 1) with bbox: ({radarBBox.Value.MinLat:F4},{radarBBox.Value.MinLon:F4}) to ({radarBBox.Value.MaxLat:F4},{radarBBox.Value.MaxLon:F4})");
                        _glControl.SetImageBytes(radarData, radarBBox.Value.MinLat, radarBBox.Value.MinLon, radarBBox.Value.MaxLat, radarBBox.Value.MaxLon, _currentZoom);
                    }
                    else
                    {
                        _glControl.SetImageBytes(radarData, _currentLat, _currentLon, _currentZoom);
                    }
                }
                else if (!_overlayManager.RadarEnabled)
                {
                    _glControl.ClearPositionedOverlay();
                }

                // Upload temperature overlay to second overlay slot (GPU composites via alpha blend)
                if (tempData != null && tempData.Length > 0)
                {
                    _glControl.Overlay2Opacity = _overlayManager.TemperatureOpacity;
                    var tempBBox = _overlayManager.LastTemperatureBBox;
                    if (tempBBox.HasValue)
                    {
                        Console.WriteLine($"[WeatherMap] Setting temperature overlay (slot 2) with bbox: ({tempBBox.Value.MinLat:F4},{tempBBox.Value.MinLon:F4}) to ({tempBBox.Value.MaxLat:F4},{tempBBox.Value.MaxLon:F4})");
                        _glControl.SetOverlay2Bytes(tempData, tempBBox.Value.MinLat, tempBBox.Value.MinLon, tempBBox.Value.MaxLat, tempBBox.Value.MaxLon, _currentZoom);
                    }
                }
                else if (!_overlayManager.TemperatureEnabled)
                {
                    _glControl.ClearPositionedOverlay2();
                }

                // If only temperature (no radar), use primary slot for it instead
                if (!_overlayManager.RadarEnabled && _overlayManager.TemperatureEnabled && tempData != null && tempData.Length > 0)
                {
                    _glControl.OverlayOpacity = _overlayManager.TemperatureOpacity;
                    var tempBBox = _overlayManager.LastTemperatureBBox;
                    if (tempBBox.HasValue)
                    {
                        _glControl.SetImageBytes(tempData, tempBBox.Value.MinLat, tempBBox.Value.MinLon, tempBBox.Value.MaxLat, tempBBox.Value.MaxLon, _currentZoom);
                    }
                    else
                    {
                        _glControl.SetImageBytes(tempData, _currentLat, _currentLon, _currentZoom);
                    }
                    _glControl.ClearPositionedOverlay2(); // don't double-draw
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Overlay update error: {ex.Message}");
            }
            finally
            {
                _overlayUpdateInProgress = false;
            }
        }

        private async Task RefreshWeather()
        {
            var btn = FindHudElement<HudButton>("refresh");
            if (btn != null) btn.Text = "Refreshing...";
            _glControl?.InvalidateView();
            
            try
            {
                await UpdateOverlays();
                UpdateCacheStats();
            }
            finally
            {
                if (btn != null) btn.Text = "Refresh Weather";
                _glControl?.InvalidateView();
            }
        }

        private async Task ClearCache()
        {
            var result = MessageBox.Show(
                "Clear all cached map tiles and weather data?",
                "Clear Cache",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                await _tileCache.ClearCacheAsync();
                UpdateCacheStats();
                MessageBox.Show("Cache cleared successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private async Task PrefetchMapTiles()
        {
            var confirm = MessageBox.Show(
                "This will download map tiles for Canada/USA (zoom 3–7) into your local cache.\n\nPlease ensure you comply with tile provider usage policies. Continue?",
                "Prefetch Map Tiles",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            _lblCacheStats.Text = "Prefetching tiles...";
            _glControl?.InvalidateView();

            try
            {
                // Bounding box roughly covering continental Canada + USA (includes AK/HI margins)
                double minLat = 10.0, minLon = -170.0, maxLat = 72.0, maxLon = -50.0;
                int minZoom = 3, maxZoom = 7;

                // Prepare caches: existing _tileCache (map_cache) + tileprovider binary cache folder
                var tileProviderCacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "tilecache");
                using var tileProviderCache = new BinaryTileCache(tileProviderCacheDir);

                // Local tiles folder (z/x/y.png) so GL control can be pointed to it after generation
                var localTilesRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "local_tiles");
                System.IO.Directory.CreateDirectory(localTilesRoot);

                var mapService = new MapOverlayService();

                var progress = new Progress<TilePyramidGenerator.ProgressState>(s =>
                {
                    _lblCacheStats.Text = $"Prefetch: {s.Completed}/{s.Total} (fetched={s.Fetched})";
                    _glControl?.InvalidateView();
                });

                var state = await TilePyramidGenerator.GenerateAsync(
                    mapCache: _tileCache,
                    mapService: mapService,
                    minZoom: minZoom,
                    maxZoom: maxZoom,
                    minLat: minLat,
                    minLon: minLon,
                    maxLat: maxLat,
                    maxLon: maxLon,
                    tileProviderCache: tileProviderCache,
                    localTilesRoot: localTilesRoot,
                    progress: progress);

                // Point GL control to local tiles (immediate benefit)
                _glControl.SetLocalTilesFolder(localTilesRoot);
                UpdateCacheStats();

                MessageBox.Show($"Prefetch complete — fetched {state.Fetched} tiles (processed {state.Completed} total).\nLocal tiles: {localTilesRoot}", "Prefetch Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Prefetch failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateCacheStats();
            }
        }

        private async Task PrefetchRadarTiles()
        {
            var confirm = MessageBox.Show(
                "This will download ECCC radar tiles (latest frame) for Canada/USA (zoom 3–7) into your local map cache.\n\nPlease ensure you comply with ECCC usage policies. Continue?",
                "Prefetch Radar Tiles",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            _lblCacheStats.Text = "Radar: Prefetching tiles...";
            _glControl?.InvalidateView();

            try
            {
                double minLat = 10.0, minLon = -170.0, maxLat = 72.0, maxLon = -50.0;
                int minZoom = 3, maxZoom = 7;

                // Use 'latest' (no TIME parameter) for initial implementation; folder will be named 'latest'
                var times = new[] { "latest" };

                var mapCacheDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "map_cache");
                System.IO.Directory.CreateDirectory(mapCacheDir);

                var progress = new Progress<TilePyramidGenerator.ProgressState>(s =>
                {
                    _lblCacheStats.Text = $"Radar Prefetch: {s.Completed}/{s.Total} (fetched={s.Fetched})";
                    _glControl?.InvalidateView();
                });

                var state = await TilePyramidGenerator.GenerateRadarTilesAsync(
                    httpClient: _httpClient,
                    radarLayer: "RADAR_1KM_RRAI",
                    times: times,
                    minZoom: minZoom,
                    maxZoom: maxZoom,
                    minLat: minLat,
                    minLon: minLon,
                    maxLat: maxLat,
                    maxLon: maxLon,
                    outputBaseDir: mapCacheDir,
                    parallelism: 4,
                    delayBetweenRequestsMs: 200,
                    progress: progress);

                UpdateCacheStats();
                MessageBox.Show($"Radar tile prefetch complete — fetched {state.Fetched} tiles (processed {state.Completed}).\nCache: {mapCacheDir}", "Prefetch Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Radar prefetch failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateCacheStats();
            }
        }

        private async Task GeneratePrecomposedComposites()
        {
            var confirm = MessageBox.Show(
                "Generate pre-composed radar+map images for Canada/USA (z=3..7). This will download tiles/radar and may use significant bandwidth. Continue?",
                "Generate Precomposed Composites",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            _lblCacheStats.Text = "Generating composites...";
            _glControl?.InvalidateView();

            try
            {
                // Country bounding box (same as tile prefetch)
                double minLat = 10.0, minLon = -170.0, maxLat = 72.0, maxLon = -50.0;
                int minZoom = 3, maxZoom = 7;
                int width = 4096, height = 3072; // high-res composite size (GPU-friendly)
                var outDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG", "precomposed");
                System.IO.Directory.CreateDirectory(outDir);

                var mapService = new MapOverlayService();
                var radarSvc = new ECCC.Services.RadarImageService(_httpClient, mapService);

                for (int z = minZoom; z <= maxZoom; z++)
                {
                    _lblCacheStats.Text = $"Generating composite z={z}...";
                    _glControl?.InvalidateView();
                    var bytes = await radarSvc.FetchRadarImageAsync((MinLat: minLat, MinLon: minLon, MaxLat: maxLat, MaxLon: maxLon), width, height, z);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var path = System.IO.Path.Combine(outDir, $"composite_z{z}.png");
                        System.IO.File.WriteAllBytes(path, bytes);
                    }
                }

                // Auto-load the zoom-4 composite if present for immediate UX benefit
                var centerLat = (minLat + maxLat) / 2.0;
                var centerLon = (minLon + maxLon) / 2.0;
                var primePath = System.IO.Path.Combine(outDir, "composite_z4.png");
                if (System.IO.File.Exists(primePath))
                {
                    var data = System.IO.File.ReadAllBytes(primePath);
                    _glControl.SetImageBytes(data, centerLat, centerLon, 4);
                }

                MessageBox.Show($"Precomposed composites created in: {outDir}", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate composites: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateCacheStats();
            }
        }

        private void UpdateStatusLabels()
        {
            if (_glControl != null)
            {
                // Coordinate
                string coord = $"{_currentLat:F2}°,{_currentLon:F2}°";

                // Zoom
                string zoom = $"Z:{_currentZoom}";

                // Cache (disk) size
                long cacheBytes = 0;
                var tpStats = _glControl.ActiveTileProvider?.GetAggregateStats();
                if (tpStats != null) cacheBytes = tpStats.TotalSizeBytes;
                string cache = FormatBytes(cacheBytes);

                // RAM (tile RAM cache)
                long ramBytes = 0;
                if (tpStats != null) ramBytes = tpStats.RamCacheBytes;
                string ram = FormatBytes(ramBytes);

                // VRAM
                long vramBytes = 0;
                try { vramBytes = _glControl.VramEstimatedBytes; } catch { }
                string vram = FormatBytes(vramBytes);

                // Viewport resolution
                string res = $"{_glControl.HostControl.Width}x{_glControl.HostControl.Height}";

                // FPS
                int fps = (int)Math.Round(_glControl.CurrentFps);

                string apiName = RenderingFactory.ToConfigString(_glControl.ActiveApi);
                string statusText = $"{coord} | {zoom} | Cache:{cache} | RAM:{ram} | VRAM:{vram} | {res} | {fps}fps | {apiName}";

                _glControl.HudStatusBarText = statusText;
            }

            _glControl?.InvalidateView();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2}GB";
            return $"{bytes / 1024.0 / 1024.0:F1}MB";
        }

        private void UpdateCacheStats()
        {
            // No-op: stats are now part of the single-row status bar
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Auto-refresh weather overlays periodically
            if (_chkRadar.Checked || _chkTemperature.Checked)
            {
                await UpdateOverlays();
            }
            // Keep cache stats fresh
            UpdateCacheStats();
        }

        /// <summary>Toggle radar checkbox from external code (keyboard shortcut)</summary>
        public void ToggleRadar()
        {
            _chkRadar.Checked = !_chkRadar.Checked;
            _chkRadar.OnChanged?.Invoke(_chkRadar.Checked);
            _glControl?.InvalidateView();
        }

        /// <summary>Toggle temperature checkbox from external code (keyboard shortcut)</summary>
        public void ToggleTemperature()
        {
            _chkTemperature.Checked = !_chkTemperature.Checked;
            _chkTemperature.OnChanged?.Invoke(_chkTemperature.Checked);
            _glControl?.InvalidateView();
        }

        /// <summary>Toggle debug overlay bounding box</summary>
        public void ToggleDebugOverlay()
        {
            _glControl.DebugOverlayBounds = !_glControl.DebugOverlayBounds;
            _glControl.InvalidateView();
        }

        /// <summary>Force refresh all overlays</summary>
        public void RefreshOverlays()
        {
            _ = UpdateOverlays();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Save all map settings before disposing
                try { SaveMapSettings(); } catch { }

                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                _animationTimer?.Stop();
                _animationTimer?.Dispose();
                _statusUpdateTimer?.Stop();
                _statusUpdateTimer?.Dispose();
                _animationRefreshDebounce?.Dispose();
                _tileCache?.Dispose();
                _overlayManager?.Dispose();
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
