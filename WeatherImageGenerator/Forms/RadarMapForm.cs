using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;
using ECCC.Services;
using WeatherImageGenerator.Utilities;
using OpenTK.WinForms;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Rendering.OpenGL;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Legacy radar map form — superseded by <see cref="WeatherMapForm"/> which provides
    /// all radar features plus temperature, animation, multiple layers, and GPU compositing.
    /// </summary>
    [System.Obsolete("Use WeatherMapForm instead. RadarMapForm is a legacy prototype and will be removed in a future version.")]
    public partial class RadarMapForm : Form
    {
        private GLRadarControl _glControl;
        private Panel _controlPanel;
        private Button _refreshBtn;
        private Button _zoomInBtn;
        private Button _zoomOutBtn;
        private ComboBox _radarSiteCombo;
        private Label _statusLabel;
        private TrackBar _zoomTrackBar;
        private Label _tileStatusLabel;
        private Label _bgModeLabel;
        private NumericUpDown _mapZoomNumeric;
        // last composite zoom level (null when no composite present)
        private int? _lastCompositeZoom;
        
        private readonly RadarImageService _radarService;
        private readonly HttpClient _httpClient;
        private Image? _currentRadarImage;

        // Radar site coordinates (major Canadian cities)
        private readonly (string Name, double Lat, double Lon)[] _radarSites = new[]
        {
            ("South Ontario (Toronto)", 43.6532, -79.3832),
            ("Halifax", 44.6488, -63.5752),
            ("Montreal", 45.5017, -73.5673),
            ("Vancouver", 49.2827, -123.1207),
            ("Calgary", 51.0447, -114.0719),
            ("Winnipeg", 49.8951, -97.1384),
            ("Regina", 50.4452, -104.6189),
            ("Fredericton", 45.9636, -66.6431)
        };

        public RadarMapForm()
        {
            _httpClient = new HttpClient();
            _radarService = new RadarImageService(_httpClient);
            InitializeComponent();
            SetupUI();
            _ = LoadDefaultRadarAsync();
        }

        private void InitializeComponent()
        {
            this.Text = "🌧️ Interactive Radar Map";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            try { this.Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WSG.ico")); }
            catch { this.Icon = SystemIcons.Application; }
        }

        private void SetupUI()
        {
            // Control Panel
            _controlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(10)
            };

            // Radar site selection
            var siteLabel = new Label
            {
                Text = "Radar Site:",
                ForeColor = Color.White,
                Location = new Point(10, 15),
                AutoSize = true
            };
            
            _radarSiteCombo = new ComboBox
            {
                Location = new Point(90, 12),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(62, 62, 64),
                ForeColor = Color.White
            };
            
            // Add radar sites from array
            foreach (var site in _radarSites)
            {
                _radarSiteCombo.Items.Add(site.Name);
            }
            _radarSiteCombo.SelectedIndex = 0;
            _radarSiteCombo.SelectedIndexChanged += async (s, e) => await LoadRadarForSelectedSiteAsync();

            // Map zoom numeric control
            var mapZoomLabel = new Label
            {
                Text = "Map Zoom:",
                ForeColor = Color.White,
                Location = new Point(420, 50),
                AutoSize = true
            };

            _mapZoomNumeric = new NumericUpDown
            {
                Location = new Point(490, 47),
                Minimum = 0,
                Maximum = 18,
                Value = 6,
                Width = 60
            };
            _mapZoomNumeric.ValueChanged += (s,e) => { _glControl?.SetMapZoom((int)_mapZoomNumeric.Value); HandleMapZoomChanged((int)_mapZoomNumeric.Value); };

            // Local tiles folder selector
            var tilesBtn = new Button
            {
                Text = "Select Tiles Folder",
                Location = new Point(560, 47),
                Width = 140,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0,122,204),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            tilesBtn.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog();
                dlg.Description = "Select a local tile folder (z/x/y.png layout)";
                dlg.UseDescriptionForTitle = true;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _glControl?.SetLocalTilesFolder(dlg.SelectedPath);
                }
            };

            // Tile status badge
            _tileStatusLabel = new Label
            {
                Text = "Tiles: Remote",
                ForeColor = Color.LightGreen,
                Location = new Point(710, 50),
                AutoSize = true
            };

            _bgModeLabel = new Label
            {
                Text = "BG: Tiles",
                ForeColor = Color.LightGray,
                Location = new Point(790, 50),
                AutoSize = true
            };

            var showTilesLabel = new Label
            {
                Text = "Map Zoom:",
                ForeColor = Color.White,
                Location = new Point(420, 50),
                AutoSize = true
            };

            _controlPanel.Controls.Add(mapZoomLabel);
            _controlPanel.Controls.Add(_mapZoomNumeric);
            _controlPanel.Controls.Add(tilesBtn);
            _controlPanel.Controls.Add(_tileStatusLabel);
            _controlPanel.Controls.Add(_bgModeLabel);

            // Keep last composite zoom so we can auto-refresh when zoom changes by >= 1
            _lastCompositeZoom = null;

            // Refresh button
            _refreshBtn = CreateStyledButton("🔄 Refresh", new Point(310, 10));
            _refreshBtn.Click += async (s, e) => await LoadRadarForSelectedSiteAsync();

            // Zoom controls
            var zoomLabel = new Label
            {
                Text = "Zoom:",
                ForeColor = Color.White,
                Location = new Point(10, 50),
                AutoSize = true
            };

            _zoomOutBtn = CreateStyledButton("➖", new Point(70, 47), 40);
            _zoomOutBtn.Click += ZoomOut_Click;

            _zoomTrackBar = new TrackBar
            {
                Location = new Point(120, 45),
                Width = 200,
                Minimum = 50,
                Maximum = 300,
                Value = 100,
                TickFrequency = 50,
                BackColor = Color.FromArgb(37, 37, 38)
            };
            _zoomTrackBar.ValueChanged += ZoomTrackBar_ValueChanged;

            _zoomInBtn = CreateStyledButton("➕", new Point(330, 47), 40);
            _zoomInBtn.Click += ZoomIn_Click;

            // Status label
            _statusLabel = new Label
            {
                Text = "Loading radar data",
                ForeColor = Color.FromArgb(180, 180, 180),
                Location = new Point(450, 15),
                AutoSize = true
            };

            _controlPanel.Controls.AddRange(new Control[]
            {
                siteLabel, _radarSiteCombo, _refreshBtn,
                zoomLabel, _zoomOutBtn, _zoomTrackBar, _zoomInBtn,
                _statusLabel
            });

            // Radar display (OpenGL)
            _glControl = new GLRadarControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
            };

            // Subscribe to tile status updates (now that _glControl is initialized)
            _glControl.TileStatusChanged += (text, color) =>
            {
                if (this.InvokeRequired) this.BeginInvoke(new Action(() => { _tileStatusLabel.Text = text; _tileStatusLabel.ForeColor = color; }));
                else { _tileStatusLabel.Text = text; _tileStatusLabel.ForeColor = color; }
            };

            // Track whether a composite background is active and remember its zoom
            _glControl.BackgroundTextureChanged += (hasBg) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => {
                        _bgModeLabel.Text = hasBg ? "BG: Composite" : "BG: Tiles";
                        _bgModeLabel.ForeColor = hasBg ? Color.LightGreen : Color.LightGray;
                        _lastCompositeZoom = hasBg ? (int?)_mapZoomNumeric.Value : null;
                    }));
                    return;
                }

                _bgModeLabel.Text = hasBg ? "BG: Composite" : "BG: Tiles";
                _bgModeLabel.ForeColor = hasBg ? Color.LightGreen : Color.LightGray;
                _lastCompositeZoom = hasBg ? (int?)_mapZoomNumeric.Value : null;
            };

            // Sync when map zoom changes (handles Shift+wheel in GL control) and auto-refresh composite when zoom delta >= 1
            _glControl.MapZoomChanged += (z) =>
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => HandleMapZoomChanged(z)));
                    return;
                }
                HandleMapZoomChanged(z);
            };

            // Add controls to form (OpenGL control first so the toolbar stays on top)
            this.Controls.Add(_glControl);
            this.Controls.Add(_controlPanel);
        }

        private Button CreateStyledButton(string text, Point location, int width = 90)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Width = width,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private async Task LoadDefaultRadarAsync()
        {
            // Default view -> Canada
            _glControl.SetCenterLatLon(56.1304, -106.3468);
            _glControl.SetMapZoom(4);
            await LoadRadarForIndexAsync(0, centerMap: false);
        }

        private async Task LoadRadarForSelectedSiteAsync()
        {
            await LoadRadarForIndexAsync(_radarSiteCombo.SelectedIndex);
        }

        private async Task LoadRadarForIndexAsync(int index, bool centerMap = true)
        {
            if (index < 0 || index >= _radarSites.Length) return;

            try
            {
                _statusLabel.Text = "Loading radar data...";
                _statusLabel.ForeColor = Color.Yellow;

                var site = _radarSites[index];
                var radarBytes = await _radarService.FetchRadarImageAsync(site.Lat, site.Lon, 800, 600, 250, (int)_mapZoomNumeric.Value);
                
                // center map on the selected site optionally
                if (centerMap) _glControl.SetCenterLatLon(site.Lat, site.Lon);
                
                if (radarBytes != null && radarBytes.Length > 0)
                {
                    // Push bytes to OpenGL control to create/upload texture (pass source metadata so composite anchors correctly)
                    try
                    {
                        _currentRadarImage?.Dispose();
                        _glControl.SetImageBytes(radarBytes, site.Lat, site.Lon, (int)_mapZoomNumeric.Value);
                        // remember last composite zoom so auto-refresh can react to zoom changes
                        _lastCompositeZoom = (int)_mapZoomNumeric.Value;
                    }
                    catch (Exception imgEx)
                    {
                        Logger.Log($"Failed to upload radar texture: {imgEx.Message}", Logger.LogLevel.Warning);
                    }
                    
                    _statusLabel.Text = $"Radar loaded: {site.Name} - {DateTime.Now:HH:mm:ss}";
                    _statusLabel.ForeColor = Color.LightGreen;
                }
                else
                {
                    _statusLabel.Text = "No radar data available";
                    _statusLabel.ForeColor = Color.Orange;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load radar: {ex.Message}", Logger.LogLevel.Error);
                _statusLabel.Text = $"Error: {ex.Message}";
                _statusLabel.ForeColor = Color.Red;
            }
        }

        private void ZoomIn_Click(object? sender, EventArgs e)
        {
            if (_zoomTrackBar.Value < _zoomTrackBar.Maximum)
            {
                _zoomTrackBar.Value = Math.Min(_zoomTrackBar.Value + 25, _zoomTrackBar.Maximum);
            }
        }

        private void ZoomOut_Click(object? sender, EventArgs e)
        {
            if (_zoomTrackBar.Value > _zoomTrackBar.Minimum)
            {
                _zoomTrackBar.Value = Math.Max(_zoomTrackBar.Value - 25, _zoomTrackBar.Minimum);
            }
        }

        private void ZoomTrackBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_glControl != null)
            {
                _glControl.Zoom = _zoomTrackBar.Value / 100f;
                _glControl.Invalidate();
            }
        }

        // Called when map zoom changes (UI or GL control). Auto-refresh composite when zoom delta >= 1.
        private void HandleMapZoomChanged(int newZoom)
        {
            // keep numeric control in sync if the event comes from GL control (Shift+wheel)
            if ((int)_mapZoomNumeric.Value != newZoom) _mapZoomNumeric.Value = Math.Max(_mapZoomNumeric.Minimum, Math.Min(_mapZoomNumeric.Maximum, newZoom));

            // If we currently have a composite and the zoom changed enough, auto-refresh it
            if (_lastCompositeZoom.HasValue && Math.Abs(newZoom - _lastCompositeZoom.Value) >= 1)
            {
                Logger.Log($"[AUTO-REFRESH] Map zoom changed from {_lastCompositeZoom.Value} → {newZoom}; refreshing composite...", System.ConsoleColor.Cyan);
                // reload radar/composite for the currently selected site (don't recenter)
                _ = LoadRadarForIndexAsync(_radarSiteCombo.SelectedIndex, centerMap: false);
            }
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _currentRadarImage?.Dispose();
            try { _glControl?.Dispose(); } catch { }
            _httpClient?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
