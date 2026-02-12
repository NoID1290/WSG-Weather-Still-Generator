using System;
using System.Drawing;
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
        private ComboBox _cmbMapStyle;
        
        private HttpClient _httpClient;
        private double _currentLat = 56.1304; // Canada centroid
        private double _currentLon = -106.3468;
        private int _currentZoom = 4;

        public WeatherMapControl()
        {
            InitializeComponents();
            InitializeMapControl();
            InitializeWeatherSystem();
            SetupEventHandlers();
            ApplyModernStyling();
            
            // Start update timer
            _updateTimer = new Timer { Interval = 60000 }; // Update every minute
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
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
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(10)
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
        }

        private void InitializeWeatherSystem()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var cacheDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSG", "map_cache");
            
            _tileCache = new BinaryTileCache(cacheDir);
            _overlayManager = new WeatherOverlayManager(_httpClient);
        }

        private void SetupEventHandlers()
        {
            // Build control panel UI
            BuildControlPanel();
            
            // Map events
            _glControl.MapZoomChanged += zoom => 
            {
                _currentZoom = zoom;
                UpdateStatusLabels();
            };
            
            _glControl.TileStatusChanged += (text, color) =>
            {
                // Could update a status bar here
            };
        }

        private void BuildControlPanel()
        {
            int y = 10;
            int spacing = 12;
            int controlWidth = 260;
            int buttonHeight = 40;
            int smallButtonHeight = 32;

            // Header
            var header = new Label
            {
                Text = "Weather Map Controls",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 30),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White
            };
            _controlPanel.Controls.Add(header);
            y += 40;

            // Separator
            AddSeparator(y);
            y += 15;

            // Zoom Controls
            var lblZoomControls = CreateLabel("Zoom Controls", y);
            _controlPanel.Controls.Add(lblZoomControls);
            y += 25;

            var zoomPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 45),
                FlowDirection = FlowDirection.LeftToRight
            };

            _btnZoomIn = CreateButton("➕ Zoom In", smallButtonHeight);
            _btnZoomIn.Click += (s, e) => ZoomIn();
            
            _btnZoomOut = CreateButton("➖ Zoom Out", smallButtonHeight);
            _btnZoomOut.Click += (s, e) => ZoomOut();
            
            _btnCenter = CreateButton("🎯 Center", smallButtonHeight);
            _btnCenter.Click += (s, e) => CenterMap();

            zoomPanel.Controls.Add(_btnZoomIn);
            zoomPanel.Controls.Add(_btnZoomOut);
            zoomPanel.Controls.Add(_btnCenter);
            _controlPanel.Controls.Add(zoomPanel);
            y += 55;

            _lblZoom = CreateLabel($"Zoom Level: {_currentZoom}", y);
            _controlPanel.Controls.Add(_lblZoom);
            y += 25;

            // Separator
            AddSeparator(y);
            y += 15;

            // Weather Overlays
            var lblOverlays = CreateLabel("Weather Overlays", y);
            _controlPanel.Controls.Add(lblOverlays);
            y += 30;

            // Radar Toggle
            _chkRadar = new CheckBox
            {
                Text = "🌧️ Radar Composite",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 25),
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.White,
                Checked = true
            };
            _chkRadar.CheckedChanged += async (s, e) => await UpdateOverlays();
            _controlPanel.Controls.Add(_chkRadar);
            y += 30;

            // Radar Opacity
            var lblRadarOpacity = CreateLabel("Radar Opacity:", y);
            _controlPanel.Controls.Add(lblRadarOpacity);
            y += 20;

            _trackRadarOpacity = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 45),
                Minimum = 0,
                Maximum = 100,
                Value = 75,
                TickFrequency = 10
            };
            _trackRadarOpacity.ValueChanged += async (s, e) =>
            {
                _overlayManager.RadarOpacity = _trackRadarOpacity.Value / 100f;
                await UpdateOverlays();
            };
            _controlPanel.Controls.Add(_trackRadarOpacity);
            y += 50;

            // Temperature Toggle
            _chkTemperature = new CheckBox
            {
                Text = "🌡️ Temperature Grid",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 25),
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.White,
                Checked = false
            };
            _chkTemperature.CheckedChanged += async (s, e) => await UpdateOverlays();
            _controlPanel.Controls.Add(_chkTemperature);
            y += 30;

            // Temperature Opacity
            var lblTempOpacity = CreateLabel("Temperature Opacity:", y);
            _controlPanel.Controls.Add(lblTempOpacity);
            y += 20;

            _trackTempOpacity = new TrackBar
            {
                Location = new Point(10, y),
                Size = new Size(controlWidth, 45),
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
            y += 55;

            // Separator
            AddSeparator(y);
            y += 15;

            // Actions
            var lblActions = CreateLabel("Actions", y);
            _controlPanel.Controls.Add(lblActions);
            y += 30;

            _btnRefresh = CreateActionButton("🔄 Refresh Weather", y);
            _btnRefresh.Click += async (s, e) => await RefreshWeather();
            _controlPanel.Controls.Add(_btnRefresh);
            y += buttonHeight + spacing;

            _btnClearCache = CreateActionButton("🗑️ Clear Cache", y);
            _btnClearCache.Click += async (s, e) => await ClearCache();
            _controlPanel.Controls.Add(_btnClearCache);
            y += buttonHeight + spacing;

            // Separator
            AddSeparator(y);
            y += 15;

            // Status
            var lblStatus = CreateLabel("Status", y);
            _controlPanel.Controls.Add(lblStatus);
            y += 25;

            _lblPosition = CreateLabel($"Lat: {_currentLat:F2}, Lon: {_currentLon:F2}", y);
            _lblPosition.Font = new Font("Consolas", 9);
            _controlPanel.Controls.Add(_lblPosition);
            y += 25;

            _lblCacheStats = CreateLabel("Cache: Loading...", y);
            _lblCacheStats.Font = new Font("Consolas", 9);
            _controlPanel.Controls.Add(_lblCacheStats);
            y += 25;

            UpdateStatusLabels();
        }

        private Label CreateLabel(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(10, y),
                Size = new Size(260, 20),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.LightGray
            };
        }

        private Button CreateButton(string text, int height)
        {
            return new Button
            {
                Text = text,
                Size = new Size(82, height),
                Font = new Font("Segoe UI", 9),
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
                Size = new Size(260, 40),
                Font = new Font("Segoe UI", 11),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void AddSeparator(int y)
        {
            var separator = new Panel
            {
                Location = new Point(10, y),
                Size = new Size(260, 1),
                BackColor = Color.FromArgb(80, 80, 80)
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
        public void SetLocation(double lat, double lon)
        {
            _currentLat = lat;
            _currentLon = lon;
            _glControl.SetCenterLatLon(lat, lon);
            UpdateStatusLabels();
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

        private async Task UpdateOverlays()
        {
            try
            {
                _overlayManager.RadarEnabled = _chkRadar.Checked;
                _overlayManager.TemperatureEnabled = _chkTemperature.Checked;

                if (!_overlayManager.RadarEnabled && !_overlayManager.TemperatureEnabled)
                    return;

                var overlayData = await _overlayManager.GetCompositedOverlaysAsync(
                    _currentLat,
                    _currentLon,
                    _glControl.Width,
                    _glControl.Height,
                    _currentZoom);

                if (overlayData != null)
                {
                    _glControl.SetImageBytes(overlayData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMap] Overlay update error: {ex.Message}");
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
                var stats = _tileCache.GetStats();
                _lblCacheStats.Text = $"Cache: {stats.TileCount} tiles, {stats.TotalSizeMB}";
            }
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Auto-refresh weather overlays periodically
            if (_chkRadar.Checked || _chkTemperature.Checked)
            {
                await UpdateOverlays();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                _tileCache?.Dispose();
                _overlayManager?.Dispose();
                _httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
