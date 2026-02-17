using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Net.Http;
using System.Threading.Tasks;
using OpenMap;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// Professional Weather Interactive Map Control with full UI
    /// Features: Radar, Temperature overlays, Zoom controls, Layer toggles
    /// </summary>
    public class WeatherMapControl : UserControl
    {
        private GLRadarControl _glControl;
        private WeatherOverlayManager _overlayManager;
        private BinaryTileCache _tileCache;
        private Panel _controlPanel;
        private Timer _updateTimer;
        
        // UI Controls
        private Button _btnZoomIn;
        private Button _btnZoomOut;
        private Button _btnCenter;
        private CheckBox _chkRadar;
        private CheckBox _chkTemperature;
        private TrackBar _trackRadarOpacity;
        private TrackBar _trackTempOpacity;
        private Label _lblZoom;
        private Label _lblPosition;
        private Label _lblCacheStats;
        private Button _btnRefresh;
        private Button _btnClearCache;
        private Button _btnPrefetchTiles;
        private Button _btnPrefetchRadarTiles;
        private ComboBox _cmbMapStyle;

        // Radar configuration selectors
        private ComboBox _cmbRadarLayer;
        private ComboBox _cmbRadarStyle;

        // Animation controls
        private Panel _animationPanel;
        private Button _btnPlayPause;
        private Button _btnStepBack;
        private Button _btnStepForward;
        private TrackBar _trackTimeline;
        private Label _lblFrameInfo;
        private Label _lblSpeed;
        private Button _btnLoadAnimation;
        private CheckBox _chkAnimationFollowMap;
        private List<byte[]> _animationFrames = new List<byte[]>();
        private List<string> _animationTimestamps = new List<string>();
        private int _currentFrameIndex;
        private bool _isAnimating;
        private Timer _animationTimer;
        private int _animationSpeedMs = 500;
        private System.Threading.Timer? _animationRefreshDebounce;
        private bool _animationRefreshInProgress = false;
        private (double MinLat, double MinLon, double MaxLat, double MaxLon)? _animationBBox;

        // Attribution overlay
        private Panel _attributionPanel;

        // Close animation button
        private Button _btnCloseAnimation;

        // Collapsible options panel
        private Panel _optionsPanel;
        private Label _lblOptionsHeader;
        private bool _optionsExpanded = false;

        // Panel position
        public enum PanelPosition { Left, Right, Top }
        private PanelPosition _panelPosition = PanelPosition.Right;
        private FlowLayoutPanel _panelPositionBar;

        private HttpClient _httpClient;
        private double _currentLat = 56.1304; // Canada centroid
        private double _currentLon = -106.3468;
        private int _currentZoom = 4;
        private bool _suppressOverlayUpdates = false;

        public WeatherMapControl()
        {
            InitializeComponents();
            InitializeMapControl();
            InitializeWeatherSystem();
            SetupEventHandlers();
            ApplyModernStyling();
            
            // Load persisted panel position
            LoadPanelPosition();

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
            this.BackColor = Color.FromArgb(30, 30, 30);
            
            // Control panel (right side)
            _controlPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 280,
                BackColor = Color.FromArgb(40, 40, 43),
                Padding = new Padding(10),
                AutoScroll = true
            };
            this.Controls.Add(_controlPanel);
        }

        private void InitializeMapControl()
        {
            _glControl = new GLRadarControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20)
            };
            this.Controls.Add(_glControl);
            _glControl.BringToFront();
            _controlPanel.BringToFront();

            // Attribution overlay (bottom-left of map viewport)
            BuildAttributionPanel();
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
        }

        private void SetupEventHandlers()
        {
            // Build control panel UI
            BuildControlPanel();

            // Refresh attribution now that checkboxes exist (radar is checked by default)
            UpdateAttributionText();
            
            // Map events
            _glControl.MapZoomChanged += zoom => 
            {
                _currentZoom = zoom;
                UpdateStatusLabels();
                if (!_suppressOverlayUpdates)
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
        }

        private void BuildControlPanel()
        {
            int y = 6;
            int spacing = 6;
            int controlWidth = 256;
            int buttonHeight = 28;
            int smallButtonHeight = 26;

            // ═══ Panel Position Bar ═══
            _panelPositionBar = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 24),
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };
            var btnDockLeft = CreateDockButton("◀", PanelPosition.Left);
            var btnDockTop = CreateDockButton("▲", PanelPosition.Top);
            var btnDockRight = CreateDockButton("▶", PanelPosition.Right);
            _panelPositionBar.Controls.Add(btnDockLeft);
            _panelPositionBar.Controls.Add(btnDockTop);
            _panelPositionBar.Controls.Add(btnDockRight);
            _controlPanel.Controls.Add(_panelPositionBar);
            y += 28;

            // ═══ Header ═══
            var header = new Label
            {
                Text = "Weather Map Controls",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 26),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.White
            };
            _controlPanel.Controls.Add(header);
            y += 30;

            AddSeparator(y); y += 8;

            // ═══ Map Style ═══
            var lblMapStyle = CreateSectionLabel("🗺️ Map Style", y);
            _controlPanel.Controls.Add(lblMapStyle);
            y += 22;

            _cmbMapStyle = new ComboBox
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(55, 55, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbMapStyle.Items.AddRange(new object[] { "Standard", "Dark", "Terrain", "Satellite" });
            _cmbMapStyle.SelectedIndex = 0;
            _cmbMapStyle.SelectedIndexChanged += (s, e) => OnMapStyleChanged();
            _controlPanel.Controls.Add(_cmbMapStyle);
            y += 30;

            // ═══ Zoom Controls ═══
            var lblZoomControls = CreateSectionLabel("🔍 Zoom Controls", y);
            _controlPanel.Controls.Add(lblZoomControls);
            y += 22;

            var zoomPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 30),
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent
            };

            _btnZoomIn = CreateSmallButton("+ Zoom", smallButtonHeight);
            _btnZoomIn.Click += (s, e) => ZoomIn();
            
            _btnZoomOut = CreateSmallButton("- Zoom", smallButtonHeight);
            _btnZoomOut.Click += (s, e) => ZoomOut();
            
            _btnCenter = CreateSmallButton("◎ Center", smallButtonHeight);
            _btnCenter.Click += (s, e) => CenterMap();

            zoomPanel.Controls.Add(_btnZoomIn);
            zoomPanel.Controls.Add(_btnZoomOut);
            zoomPanel.Controls.Add(_btnCenter);
            _controlPanel.Controls.Add(zoomPanel);
            y += 34;

            _lblZoom = new Label
            {
                Text = $"Zoom Level: {_currentZoom}",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 16),
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            _controlPanel.Controls.Add(_lblZoom);
            y += 20;

            AddSeparator(y); y += 8;

            // ═══ Weather Overlays ═══
            var lblOverlays = CreateSectionLabel("🌦️ Weather Overlays", y);
            _controlPanel.Controls.Add(lblOverlays);
            y += 24;

            // Radar Toggle
            _chkRadar = new CheckBox
            {
                Text = "🌧️ Radar Composite",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White,
                Checked = true
            };
            _chkRadar.CheckedChanged += async (s, e) => { UpdateAttributionText(); await UpdateOverlays(); };
            _controlPanel.Controls.Add(_chkRadar);
            y += 24;

            // Radar Layer
            var lblRadarLayer = CreateSmallLabel("Radar Layer:", y);
            _controlPanel.Controls.Add(lblRadarLayer);
            y += 16;

            _cmbRadarLayer = new ComboBox
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(55, 55, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbRadarLayer.Items.AddRange(new object[] { 
                "Rain Rate (RRAI)", 
                "Snow Rate (RSNO)", 
                "Combined Rain/Snow (RDBR)",
                "Radar Coverage"
            });
            _cmbRadarLayer.SelectedIndex = 0;
            _cmbRadarLayer.SelectedIndexChanged += (s, e) => OnRadarLayerChanged();
            _controlPanel.Controls.Add(_cmbRadarLayer);
            y += 28;

            // Radar Style
            var lblRadarStyle = CreateSmallLabel("Radar Style:", y);
            _controlPanel.Controls.Add(lblRadarStyle);
            y += 16;

            _cmbRadarStyle = new ComboBox
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(55, 55, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbRadarStyle.Items.AddRange(new object[] { 
                "Precipitation Linear", 
                "Server Default"
            });
            _cmbRadarStyle.SelectedIndex = 0;
            _cmbRadarStyle.SelectedIndexChanged += (s, e) => OnRadarStyleChanged();
            _controlPanel.Controls.Add(_cmbRadarStyle);
            y += 28;

            // Radar Opacity
            var lblRadarOpacity = CreateSmallLabel("Radar Opacity:", y);
            _controlPanel.Controls.Add(lblRadarOpacity);
            y += 16;

            _trackRadarOpacity = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 34),
                Minimum = 0,
                Maximum = 100,
                Value = 75,
                TickFrequency = 10
            };
            _trackRadarOpacity.ValueChanged += async (s, e) =>
            {
                _overlayManager.RadarOpacity = _trackRadarOpacity.Value / 100f;
                _glControl.OverlayOpacity = _trackRadarOpacity.Value / 100f;
                await UpdateOverlays();
            };
            _controlPanel.Controls.Add(_trackRadarOpacity);
            y += 38;

            // Temperature Toggle
            _chkTemperature = new CheckBox
            {
                Text = "🌡️ Temperature Grid",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White,
                Checked = false
            };
            _chkTemperature.CheckedChanged += async (s, e) => { UpdateAttributionText(); await UpdateOverlays(); };
            _controlPanel.Controls.Add(_chkTemperature);
            y += 24;

            // Temperature Opacity
            var lblTempOpacity = CreateSmallLabel("Temperature Opacity:", y);
            _controlPanel.Controls.Add(lblTempOpacity);
            y += 16;

            _trackTempOpacity = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 34),
                Minimum = 0,
                Maximum = 100,
                Value = 60,
                TickFrequency = 10
            };
            _trackTempOpacity.ValueChanged += async (s, e) =>
            {
                _overlayManager.TemperatureOpacity = _trackTempOpacity.Value / 100f;
                await UpdateOverlays();
            };
            _controlPanel.Controls.Add(_trackTempOpacity);
            y += 38;

            AddSeparator(y); y += 8;

            // ═══ Radar Animation ═══
            var lblAnimation = CreateSectionLabel("🎬 Radar Animation", y);
            _controlPanel.Controls.Add(lblAnimation);
            y += 24;

            var animBtnPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, buttonHeight + 2),
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };

            _btnLoadAnimation = new Button
            {
                Text = "▶ Load Frames",
                Size = new Size(160, buttonHeight),
                Font = new Font("Segoe UI", 9),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnLoadAnimation.FlatAppearance.BorderSize = 0;
            _btnLoadAnimation.Click += async (s, e) => await LoadRadarAnimation();
            animBtnPanel.Controls.Add(_btnLoadAnimation);

            _btnCloseAnimation = new Button
            {
                Text = "✕ Close",
                Size = new Size(90, buttonHeight),
                Font = new Font("Segoe UI", 9),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(160, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _btnCloseAnimation.FlatAppearance.BorderSize = 0;
            _btnCloseAnimation.Click += (s, e) => CloseRadarAnimation();
            animBtnPanel.Controls.Add(_btnCloseAnimation);

            _controlPanel.Controls.Add(animBtnPanel);
            y += buttonHeight + spacing;

            _chkAnimationFollowMap = new CheckBox
            {
                Text = "Fetch new animation when moving",
                Location = new Point(14, y),
                Size = new Size(controlWidth - 14, 20),
                Checked = true,
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat
            };
            _controlPanel.Controls.Add(_chkAnimationFollowMap);
            y += 24;

            AddSeparator(y); y += 8;

            // ═══ Actions ═══
            var lblActions = CreateSectionLabel("⚡ Actions", y);
            _controlPanel.Controls.Add(lblActions);
            y += 24;

            _btnRefresh = CreateActionButton("🔄 Refresh Weather", y);
            _btnRefresh.Click += async (s, e) => await RefreshWeather();
            _controlPanel.Controls.Add(_btnRefresh);
            y += buttonHeight + spacing;

            _btnClearCache = CreateActionButton("🗑️ Clear Cache", y);
            _btnClearCache.Click += async (s, e) => await ClearCache();
            _controlPanel.Controls.Add(_btnClearCache);
            y += buttonHeight + spacing;

            var btnGen = CreateActionButton("🖼️ Generate Precomposed", y);
            btnGen.Click += async (s, e) => await GeneratePrecomposedComposites();
            _controlPanel.Controls.Add(btnGen);
            y += buttonHeight + spacing;

            AddSeparator(y); y += 8;

            // ═══ Options (collapsible) ═══
            _lblOptionsHeader = new Label
            {
                Text = "▶ ⚙ Options",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 22),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 160, 230),
                Cursor = Cursors.Hand
            };
            _lblOptionsHeader.Click += (s, e) => ToggleOptionsPanel();
            _controlPanel.Controls.Add(_lblOptionsHeader);
            y += 24;

            _optionsPanel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(controlWidth + 20, 130),
                BackColor = Color.Transparent,
                Visible = false
            };

            int oy = 2;
            _btnPrefetchTiles = CreateActionButton("📥 Prefetch Map Tiles (CAN+USA)", oy);
            _btnPrefetchTiles.Click += async (s, e) => await PrefetchMapTiles();
            _optionsPanel.Controls.Add(_btnPrefetchTiles);
            oy += buttonHeight + spacing;

            _btnPrefetchRadarTiles = CreateActionButton("📥 Prefetch Radar Tiles", oy);
            _btnPrefetchRadarTiles.Click += async (s, e) => await PrefetchRadarTiles();
            _optionsPanel.Controls.Add(_btnPrefetchRadarTiles);
            oy += buttonHeight + spacing;

            var chkPbo = new CheckBox
            {
                Text = "Use PBO uploads",
                Location = new Point(10, oy),
                Size = new Size(controlWidth, 20),
                Checked = true,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8F)
            };
            chkPbo.CheckedChanged += (s, e) => { _glControl.UsePboUploads = chkPbo.Checked; };
            _optionsPanel.Controls.Add(chkPbo);
            oy += 24;
            _optionsPanel.Size = new Size(controlWidth + 20, oy + 4);

            _controlPanel.Controls.Add(_optionsPanel);
            // Don't add options panel height to y when collapsed

            AddSeparator(y); y += 8;

            // ═══ Status ═══
            var lblStatus = CreateSectionLabel("📊 Status", y);
            _controlPanel.Controls.Add(lblStatus);
            y += 22;

            _lblPosition = new Label
            {
                Text = $"Lat: {_currentLat:F2}, Lon: {_currentLon:F2}",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 16),
                Font = new Font("Consolas", 8),
                ForeColor = Color.FromArgb(160, 160, 160)
            };
            _controlPanel.Controls.Add(_lblPosition);
            y += 18;

            _lblCacheStats = new Label
            {
                Text = "Cache: Loading...",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 32),
                Font = new Font("Consolas", 8),
                ForeColor = Color.FromArgb(160, 160, 160)
            };
            _controlPanel.Controls.Add(_lblCacheStats);
            y += 34;

            UpdateStatusLabels();

            // Build floating animation panel (initially hidden)
            BuildFloatingAnimationPanel();
        }

        // ═══ Floating Animation Panel ═══

        private void BuildFloatingAnimationPanel()
        {
            _animationPanel = new Panel
            {
                Size = new Size(540, 90),
                BackColor = Color.FromArgb(210, 22, 22, 26),
                Visible = false
            };
            this.Controls.Add(_animationPanel);
            _animationPanel.BringToFront();

            // Close button on floating panel
            var btnCloseFloat = new Button
            {
                Text = "✕",
                Location = new Point(514, 4),
                Size = new Size(22, 22),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(140, 40, 40),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnCloseFloat.FlatAppearance.BorderSize = 0;
            btnCloseFloat.Click += (s, e) => CloseRadarAnimation();
            _animationPanel.Controls.Add(btnCloseFloat);

            // Row 1: Transport controls
            int ax = 10, ay = 8;

            _btnStepBack = new Button
            {
                Text = "⏮",
                Location = new Point(ax, ay),
                Size = new Size(40, 32),
                Font = new Font("Segoe UI", 12),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnStepBack.FlatAppearance.BorderSize = 0;
            _btnStepBack.Click += (s, e) => StepAnimationBackward();
            _animationPanel.Controls.Add(_btnStepBack);
            ax += 44;

            _btnPlayPause = new Button
            {
                Text = "▶",
                Location = new Point(ax, ay),
                Size = new Size(50, 32),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnPlayPause.FlatAppearance.BorderSize = 0;
            _btnPlayPause.Click += (s, e) => PlayPauseAnimation();
            _animationPanel.Controls.Add(_btnPlayPause);
            ax += 54;

            _btnStepForward = new Button
            {
                Text = "⏭",
                Location = new Point(ax, ay),
                Size = new Size(40, 32),
                Font = new Font("Segoe UI", 12),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnStepForward.FlatAppearance.BorderSize = 0;
            _btnStepForward.Click += (s, e) => StepAnimationForward();
            _animationPanel.Controls.Add(_btnStepForward);
            ax += 50;

            // Timeline slider
            _trackTimeline = new TrackBar
            {
                Location = new Point(ax, ay + 2),
                Size = new Size(200, 28),
                Minimum = 0,
                Maximum = 1,
                Value = 0,
                TickStyle = TickStyle.None
            };
            _trackTimeline.ValueChanged += (s, e) =>
            {
                if (_animationFrames.Count > 0 && !_isAnimating)
                {
                    ShowAnimationFrame(_trackTimeline.Value);
                }
            };
            _animationPanel.Controls.Add(_trackTimeline);
            ax += 206;

            // Speed controls
            var btnSpeedDown = new Button
            {
                Text = "◀",
                Location = new Point(ax, ay + 2),
                Size = new Size(26, 26),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSpeedDown.FlatAppearance.BorderSize = 0;
            btnSpeedDown.Click += (s, e) => AdjustAnimationSpeed(200);
            _animationPanel.Controls.Add(btnSpeedDown);
            ax += 28;

            _lblSpeed = new Label
            {
                Text = "0.5s",
                Location = new Point(ax, ay + 5),
                Size = new Size(38, 20),
                Font = new Font("Consolas", 9),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _animationPanel.Controls.Add(_lblSpeed);
            ax += 40;

            var btnSpeedUp = new Button
            {
                Text = "▶",
                Location = new Point(ax, ay + 2),
                Size = new Size(26, 26),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSpeedUp.FlatAppearance.BorderSize = 0;
            btnSpeedUp.Click += (s, e) => AdjustAnimationSpeed(-200);
            _animationPanel.Controls.Add(btnSpeedUp);

            // Row 2: Frame info
            _lblFrameInfo = new Label
            {
                Text = "No animation loaded",
                Location = new Point(10, 50),
                Size = new Size(500, 30),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 200, 200),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _animationPanel.Controls.Add(_lblFrameInfo);

            // Apply styling
            foreach (Control c in _animationPanel.Controls)
            {
                if (c is Button btn)
                {
                    btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 150, 230);
                    btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 100, 180);
                }
            }

            // Initialize animation timer
            _animationTimer = new Timer { Interval = _animationSpeedMs };
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        private void BuildAttributionPanel()
        {
            _attributionPanel = new Panel
            {
                AutoSize = true,
                BackColor = Color.FromArgb(160, 0, 0, 0),
                Padding = new Padding(6, 4, 6, 4)
            };

            var lblAttribution = new Label
            {
                Name = "lblAttributionText",
                AutoSize = true,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(210, 255, 255, 255),
                BackColor = Color.Transparent,
                Location = new Point(6, 4),
                MaximumSize = new Size(500, 0),
                Cursor = Cursors.Hand
            };
            lblAttribution.Click += (s, e) =>
            {
                var style = GetCurrentMapStyle();
                var url = MapOverlayService.GetAttributionUrl(style);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            _attributionPanel.Controls.Add(lblAttribution);

            this.Controls.Add(_attributionPanel);
            _attributionPanel.BringToFront();

            UpdateAttributionText();
        }

        private MapStyle GetCurrentMapStyle()
        {
            if (_cmbMapStyle == null) return MapStyle.Standard;
            return _cmbMapStyle.SelectedIndex switch
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

            // Update WinForms label for fallback
            if (_attributionPanel != null)
            {
                var lbl = _attributionPanel.Controls["lblAttributionText"] as Label;
                if (lbl != null) lbl.Text = string.Join("\n", lines);
            }

            // Pipe to GL HUD
            if (_glControl != null)
                _glControl.HudAttributionText = text;
        }

        private void RepositionAttributionPanel()
        {
            if (_attributionPanel == null) return;
            int panelX = 6;
            int panelY = this.Height - _attributionPanel.Height - 6;
            _attributionPanel.Location = new Point(panelX, Math.Max(0, panelY));
        }

        private void RepositionAnimationPanel()
        {
            if (_animationPanel == null) return;
            int mapWidth, mapHeight;
            switch (_panelPosition)
            {
                case PanelPosition.Top:
                    mapWidth = this.Width;
                    mapHeight = this.Height - _controlPanel.Height;
                    int xTop = Math.Max(0, (mapWidth - _animationPanel.Width) / 2);
                    int yTop = Math.Max(0, this.Height - _animationPanel.Height - 15);
                    _animationPanel.Location = new Point(xTop, yTop);
                    break;
                case PanelPosition.Left:
                    mapWidth = this.Width - _controlPanel.Width;
                    int xLeft = Math.Max(0, _controlPanel.Width + (mapWidth - _animationPanel.Width) / 2);
                    int panelYL = Math.Max(0, this.Height - _animationPanel.Height - 15);
                    _animationPanel.Location = new Point(xLeft, panelYL);
                    break;
                default: // Right
                    mapWidth = this.Width - _controlPanel.Width;
                    int x = Math.Max(0, (mapWidth - _animationPanel.Width) / 2);
                    int panelY = Math.Max(0, this.Height - _animationPanel.Height - 15);
                    _animationPanel.Location = new Point(x, panelY);
                    break;
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            RepositionAnimationPanel();
            RepositionAttributionPanel();
        }

        // ═══ Animation Logic ═══

        private async Task LoadRadarAnimation()
        {
            _btnLoadAnimation.Enabled = false;
            _btnLoadAnimation.Text = "⏳ Loading frames...";

            try
            {
                // Fetch timestamps
                var timestamps = await _overlayManager.FetchRadarTimestampsAsync(8);
                if (timestamps.Count == 0)
                {
                    MessageBox.Show("No radar timestamps available.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Fetch frames
                var (frames, validTimestamps) = await _overlayManager.FetchMultipleRadarFramesAsync(
                    _currentLat, _currentLon, _glControl.Width, _glControl.Height, _currentZoom, timestamps);

                if (frames.Count == 0)
                {
                    MessageBox.Show("No radar animation frames available.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _animationFrames = frames;
                _animationTimestamps = validTimestamps;
                _animationBBox = _overlayManager.LastRadarBBox;
                _currentFrameIndex = 0;

                // Configure timeline slider
                _trackTimeline.Maximum = Math.Max(1, _animationFrames.Count - 1);
                _trackTimeline.Value = 0;

                // Show animation panel
                _animationPanel.Visible = true;
                _btnCloseAnimation.Enabled = true;
                RepositionAnimationPanel();

                // Show first frame
                ShowAnimationFrame(0);

                _lblFrameInfo.Text = $"Frame 1/{_animationFrames.Count} — {FormatTimestamp(_animationTimestamps[0])}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load animation: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnLoadAnimation.Enabled = true;
                _btnLoadAnimation.Text = "▶ Load Animation Frames";
            }
        }

        private void PlayPauseAnimation()
        {
            if (_animationFrames.Count == 0) return;

            _isAnimating = !_isAnimating;

            if (_isAnimating)
            {
                _btnPlayPause.Text = "⏸";
                _btnPlayPause.BackColor = Color.FromArgb(200, 80, 20);
                _animationTimer.Interval = _animationSpeedMs;
                _animationTimer.Start();
            }
            else
            {
                _btnPlayPause.Text = "▶";
                _btnPlayPause.BackColor = Color.FromArgb(0, 122, 204);
                _animationTimer.Stop();
            }
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
            var bbox = _chkAnimationFollowMap.Checked
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

            // Update UI (avoid recursion from TrackBar.ValueChanged)
            if (_trackTimeline.Value != index)
                _trackTimeline.Value = index;

            var ts = index < _animationTimestamps.Count ? FormatTimestamp(_animationTimestamps[index]) : "";
            _lblFrameInfo.Text = $"Frame {index + 1}/{_animationFrames.Count} — {ts}";
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            _currentFrameIndex = (_currentFrameIndex + 1) % _animationFrames.Count;
            ShowAnimationFrame(_currentFrameIndex);
        }

        private void AdjustAnimationSpeed(int deltaMs)
        {
            _animationSpeedMs = Math.Max(100, Math.Min(2000, _animationSpeedMs + deltaMs));
            _lblSpeed.Text = $"{_animationSpeedMs / 1000.0:F1}s";
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
            if (!_chkAnimationFollowMap.Checked) return;

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
            if (!_chkAnimationFollowMap.Checked) return;
            if (_animationRefreshInProgress || _animationTimestamps.Count == 0) return;
            _animationRefreshInProgress = true;

            // Remember playback state
            bool wasPlaying = _isAnimating;
            if (wasPlaying)
            {
                _animationTimer.Stop();
            }

            _lblFrameInfo.Text = "Updating frames...";

            try
            {
                var (frames, validTimestamps) = await _overlayManager.FetchMultipleRadarFramesAsync(
                    _currentLat, _currentLon, _glControl.Width, _glControl.Height, _currentZoom, _animationTimestamps);

                if (frames.Count > 0)
                {
                    _animationFrames = frames;
                    _animationTimestamps = validTimestamps;
                    _animationBBox = _overlayManager.LastRadarBBox;

                    // Clamp frame index
                    _currentFrameIndex = Math.Min(_currentFrameIndex, _animationFrames.Count - 1);
                    _trackTimeline.Maximum = Math.Max(1, _animationFrames.Count - 1);
                    if (_trackTimeline.Value > _trackTimeline.Maximum)
                        _trackTimeline.Value = _trackTimeline.Maximum;

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

        // ═══ Map Style / Radar Config Handlers ═══

        private void OnMapStyleChanged()
        {
            var style = _cmbMapStyle.SelectedIndex switch
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
            _overlayManager.RadarLayer = _cmbRadarLayer.SelectedIndex switch
            {
                0 => "RADAR_1KM_RRAI",
                1 => "RADAR_1KM_RSNO",
                2 => "RADAR_1KM_RDBR",
                3 => "RADAR_COVERAGE_RRAI.INV",
                _ => "RADAR_1KM_RRAI"
            };
            _ = UpdateOverlays();
        }

        private void OnRadarStyleChanged()
        {
            _overlayManager.RadarWmsStyle = _cmbRadarStyle.SelectedIndex switch
            {
                0 => "RADARURPPRECIPR14-LINEAR",
                1 => null, // Server default
                _ => "RADARURPPRECIPR14-LINEAR"
            };
            _ = UpdateOverlays();
        }

        // ═══ Public API for external control (keyboard shortcuts) ═══

        public void ToggleAnimation()
        {
            if (_animationFrames.Count > 0)
                PlayPauseAnimation();
        }

        public void StepForward() => StepAnimationForward();
        public void StepBackward() => StepAnimationBackward();

        public void CycleMapStyle()
        {
            if (_cmbMapStyle == null) return;
            _cmbMapStyle.SelectedIndex = (_cmbMapStyle.SelectedIndex + 1) % _cmbMapStyle.Items.Count;
        }

        public void SetMapStyleByIndex(int index)
        {
            if (_cmbMapStyle == null || index < 0 || index >= _cmbMapStyle.Items.Count) return;
            _cmbMapStyle.SelectedIndex = index;
        }

        private Label CreateSectionLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(256, 22),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 160, 230)
            };
        }

        private Label CreateSmallLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(256, 16),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(170, 170, 170)
            };
        }

        private Label CreateLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(256, 20),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.LightGray
            };
        }

        private Button CreateSmallButton(string text, int height)
        {
            return new Button
            {
                Text = text,
                Size = new Size(78, height),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
        }

        private Button CreateActionButton(string text, int y)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(256, 28),
                Font = new Font("Segoe UI", 9),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Button CreateDockButton(string text, PanelPosition pos)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(40, 20),
                Font = new Font("Segoe UI", 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(1)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => SetPanelPosition(pos);
            return btn;
        }

        private void ToggleOptionsPanel()
        {
            _optionsExpanded = !_optionsExpanded;
            _optionsPanel.Visible = _optionsExpanded;
            _lblOptionsHeader.Text = _optionsExpanded ? "▼ ⚙ Options" : "▶ ⚙ Options";
            // Reflow controls below the options panel
            ReflowControlsAfterOptions();
        }

        private void ReflowControlsAfterOptions()
        {
            // Shift all controls below the options header by the options panel height delta
            int baseY = _optionsPanel.Location.Y;
            int delta = _optionsExpanded ? _optionsPanel.Height : -_optionsPanel.Height;

            _controlPanel.SuspendLayout();
            foreach (Control c in _controlPanel.Controls)
            {
                // Skip the options panel itself and its header label — they don't move
                if (c == _optionsPanel || c == _lblOptionsHeader) continue;
                // Only shift controls that are at or below the options panel's Y origin
                if (c.Location.Y >= baseY)
                {
                    c.Location = new Point(c.Location.X, c.Location.Y + delta);
                }
            }
            _controlPanel.ResumeLayout(true);
        }

        private void CloseRadarAnimation()
        {
            // Stop animation
            if (_isAnimating)
            {
                _isAnimating = false;
                _animationTimer?.Stop();
                _btnPlayPause.Text = "▶";
                _btnPlayPause.BackColor = Color.FromArgb(0, 122, 204);
            }

            // Clear frames
            _animationFrames.Clear();
            _animationTimestamps.Clear();
            _animationBBox = null;
            _currentFrameIndex = 0;

            // Hide animation panel
            _animationPanel.Visible = false;

            // Clear overlay from GL
            _glControl.ClearPositionedOverlay();

            // Reset timeline
            _trackTimeline.Value = 0;
            _trackTimeline.Maximum = 1;
            _lblFrameInfo.Text = "No animation loaded";

            // Disable close button
            _btnCloseAnimation.Enabled = false;
        }

        private void SetPanelPosition(PanelPosition pos)
        {
            if (_panelPosition == pos) return;
            _panelPosition = pos;
            ApplyPanelPosition();
            SavePanelPosition();
        }

        private void ApplyPanelPosition()
        {
            _controlPanel.SuspendLayout();
            switch (_panelPosition)
            {
                case PanelPosition.Right:
                    _controlPanel.Dock = DockStyle.Right;
                    _controlPanel.Width = 280;
                    _controlPanel.Height = 0; // auto from dock
                    break;
                case PanelPosition.Left:
                    _controlPanel.Dock = DockStyle.Left;
                    _controlPanel.Width = 280;
                    _controlPanel.Height = 0;
                    break;
                case PanelPosition.Top:
                    _controlPanel.Dock = DockStyle.Top;
                    _controlPanel.Height = 140;
                    _controlPanel.Width = 0;
                    break;
            }
            _controlPanel.ResumeLayout(true);

            // Ensure GL control is behind panel
            _glControl.BringToFront();
            _controlPanel.BringToFront();
            if (_animationPanel != null) _animationPanel.BringToFront();
            if (_attributionPanel != null) _attributionPanel.BringToFront();

            RepositionAnimationPanel();
            RepositionAttributionPanel();
        }

        private void SavePanelPosition()
        {
            try
            {
                var settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!System.IO.File.Exists(settingsPath)) return;
                var json = System.IO.File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Build new JSON with WeatherMap section
                var options = new System.Text.Json.JsonWriterOptions { Indented = true };
                using var ms = new System.IO.MemoryStream();
                using (var writer = new System.Text.Json.Utf8JsonWriter(ms, options))
                {
                    writer.WriteStartObject();
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name == "WeatherMap") continue; // skip old, we'll rewrite
                        prop.WriteTo(writer);
                    }
                    writer.WriteStartObject("WeatherMap");
                    writer.WriteString("PanelPosition", _panelPosition.ToString());
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                System.IO.File.WriteAllText(settingsPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Failed to save panel position: {ex.Message}");
            }
        }

        private void LoadPanelPosition()
        {
            try
            {
                var settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!System.IO.File.Exists(settingsPath)) return;
                var json = System.IO.File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("WeatherMap", out var wm)
                    && wm.TryGetProperty("PanelPosition", out var pp))
                {
                    var posStr = pp.GetString();
                    if (Enum.TryParse<PanelPosition>(posStr, out var pos))
                    {
                        _panelPosition = pos;
                        ApplyPanelPosition();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Failed to load panel position: {ex.Message}");
            }
        }

        private void AddSeparator(int y)
        {
            var separator = new Panel
            {
                Location = new Point(10, y),
                Size = new Size(256, 1),
                BackColor = Color.FromArgb(65, 65, 70)
            };
            _controlPanel.Controls.Add(separator);
        }

        private void ApplyModernStyling()
        {
            foreach (Control ctrl in _controlPanel.Controls)
            {
                if (ctrl is Button btn)
                {
                    btn.FlatAppearance.BorderSize = 0;
                    btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 150, 230);
                    btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 100, 180);
                }
            }
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

                Console.WriteLine($"[WeatherMap] UpdateOverlays: pos=({_currentLat:F2},{_currentLon:F2}), size={_glControl.Width}x{_glControl.Height}, zoom={_currentZoom}");

                // ── GPU compositing: upload radar and temperature as separate GL textures ──
                // Each overlay gets its own texture slot with independent opacity.
                // Alpha blending on the GPU composites them — no CPU-side GDI+ CompositeOverlays needed.

                byte[]? radarData = null;
                byte[]? tempData = null;

                if (_overlayManager.RadarEnabled)
                {
                    radarData = await _overlayManager.UpdateRadarOverlayAsync(
                        _currentLat, _currentLon, _glControl.Width, _glControl.Height, _currentZoom);
                }

                if (_overlayManager.TemperatureEnabled)
                {
                    tempData = await _overlayManager.UpdateTemperatureOverlayAsync(
                        _currentLat, _currentLon, _glControl.Width, _glControl.Height, _currentZoom);
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
            _btnRefresh.Enabled = false;
            _btnRefresh.Text = "⏳ Refreshing...";
            
            try
            {
                await UpdateOverlays();
                UpdateCacheStats();
            }
            finally
            {
                _btnRefresh.Enabled = true;
                _btnRefresh.Text = "🔄 Refresh Weather";
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

            _btnPrefetchTiles.Enabled = false;
            _btnRefresh.Enabled = false;
            _lblCacheStats.Text = "Cache: Prefetching tiles...";

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
                _btnPrefetchTiles.Enabled = true;
                _btnRefresh.Enabled = true;
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

            _btnPrefetchRadarTiles.Enabled = false;
            _btnRefresh.Enabled = false;
            _lblCacheStats.Text = "Radar cache: Prefetching tiles...";

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
                _btnPrefetchRadarTiles.Enabled = true;
                _btnRefresh.Enabled = true;
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

            _btnPrefetchTiles.Enabled = false;
            _btnRefresh.Enabled = false;
            _lblCacheStats.Text = "Generating precomposed composites...";

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
                _btnPrefetchTiles.Enabled = true;
                _btnRefresh.Enabled = true;
                UpdateCacheStats();
            }
        }

        private void UpdateStatusLabels()
        {
            if (_lblZoom != null)
                _lblZoom.Text = $"Zoom Level: {_currentZoom}";
            
            if (_lblPosition != null)
                _lblPosition.Text = $"Lat: {_currentLat:F2}, Lon: {_currentLon:F2}";
            
            UpdateCacheStats();
        }

        private void UpdateCacheStats()
        {
            if (_lblCacheStats != null)
            {
                // Always include VRAM stats from the GL control
                int vramCount = _glControl?.VramTextureCount ?? 0;
                long vramBytes = 0;
                try { vramBytes = _glControl?.VramEstimatedBytes ?? 0; } catch { }
                string vramMB = $"{vramBytes / 1024.0 / 1024.0:F1} MB";

                // Show stats from the TileProvider's active caches (these are the ones actually used)
                var tpStats = _glControl?.ActiveTileProvider?.GetAggregateStats();
                if (tpStats != null && tpStats.TileCount > 0)
                {
                    _lblCacheStats.Text = $"Cache: {tpStats.TileCount} tiles, {tpStats.TotalSizeMB}\nRAM: {tpStats.RamCacheEntries} | VRAM: {vramCount} (~{vramMB})";
                }
                else
                {
                    // Fallback to prefetch/map_cache stats
                    var stats = _tileCache.GetStats();
                    _lblCacheStats.Text = $"Cache: {stats.TileCount} tiles, {stats.TotalSizeMB}\nVRAM: {vramCount} (~{vramMB})";
                }
            }
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
        }

        /// <summary>Toggle temperature checkbox from external code (keyboard shortcut)</summary>
        public void ToggleTemperature()
        {
            _chkTemperature.Checked = !_chkTemperature.Checked;
        }

        /// <summary>Toggle debug overlay bounding box</summary>
        public void ToggleDebugOverlay()
        {
            _glControl.DebugOverlayBounds = !_glControl.DebugOverlayBounds;
            _glControl.Invalidate();
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
                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                _animationTimer?.Stop();
                _animationTimer?.Dispose();
                _animationRefreshDebounce?.Dispose();
                _tileCache?.Dispose();
                _overlayManager?.Dispose();
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
