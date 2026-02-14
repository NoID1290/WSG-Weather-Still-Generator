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
        private Button _btnPrefetchTiles;
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
                _ = UpdateOverlays(); // Refresh overlays when zoom changes
            };
            
            _glControl.MapPositionChanged += (lat, lon) =>
            {
                _currentLat = lat;
                _currentLon = lon;
                UpdateStatusLabels();
                // Don't update overlays on pan - radar stays at original fetch location
                // Only update on zoom changes, manual refresh, or checkbox toggles
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
                // TODO: Implement opacity modulation in shader or compositing
                // For now, don't refresh (causes flicker and doesn't apply opacity)
                // await UpdateOverlays();
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
                // TODO: Implement opacity modulation in shader or compositing
                // await UpdateOverlays();
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

            _btnPrefetchTiles = CreateActionButton("📥 Prefetch Map Tiles (CAN+USA)", y);
            _btnPrefetchTiles.Click += async (s, e) => await PrefetchMapTiles();
            _controlPanel.Controls.Add(_btnPrefetchTiles);
            y += buttonHeight + spacing;

            var btnGen = CreateActionButton("🖼️ Generate Precomposed Composites", y);
            btnGen.Click += async (s, e) => await GeneratePrecomposedComposites();
            _controlPanel.Controls.Add(btnGen);
            y += buttonHeight + spacing;

            var chkPbo = new CheckBox
            {
                Text = "Use PBO uploads",
                Location = new Point(10, y),
                Size = new Size(controlWidth, 24),
                Checked = true,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            chkPbo.CheckedChanged += (s, e) => { _glControl.UsePboUploads = chkPbo.Checked; };
            _controlPanel.Controls.Add(chkPbo);
            y += 30;

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
                {
                    _glControl.ClearOverlay();
                    return;
                }

                Console.WriteLine($"[WeatherMap] UpdateOverlays: pos=({_currentLat:F2},{_currentLon:F2}), size={_glControl.Width}x{_glControl.Height}, zoom={_currentZoom}");
                
                // Get radar data with bounding box for geographic positioning
                var overlayData = await _overlayManager.UpdateRadarOverlayAsync(
                    _currentLat,
                    _currentLon,
                    _glControl.Width,
                    _glControl.Height,
                    _currentZoom);

                if (overlayData != null && overlayData.Length > 0)
                {
                    // Pass radar with bounding box for geographic positioning
                    if (_overlayManager.LastRadarBBox.HasValue)
                    {
                        var bbox = _overlayManager.LastRadarBBox.Value;
                        _glControl.SetImageBytes(overlayData, bbox.MinLat, bbox.MinLon, bbox.MaxLat, bbox.MaxLon, _currentZoom);
                    }
                    else
                    {
                        _glControl.SetImageBytes(overlayData);
                    }
                }
                else
                {
                    _glControl.ClearOverlay();
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
