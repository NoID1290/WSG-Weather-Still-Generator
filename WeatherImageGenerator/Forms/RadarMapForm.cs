using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;
using ECCC.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    public partial class RadarMapForm : Form
    {
        private PictureBox _radarPictureBox;
        private Panel _controlPanel;
        private Button _refreshBtn;
        private Button _zoomInBtn;
        private Button _zoomOutBtn;
        private ComboBox _radarSiteCombo;
        private Label _statusLabel;
        private TrackBar _zoomTrackBar;
        
        private readonly RadarImageService _radarService;
        private readonly HttpClient _httpClient;
        private float _zoomLevel = 1.0f;
        private Point _panOffset = Point.Empty;
        private Point _lastMousePos;
        private bool _isDragging = false;
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
            this.Icon = SystemIcons.Application;
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

            // Radar display
            _radarPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand
            };

            // Mouse events for pan
            _radarPictureBox.MouseDown += RadarPictureBox_MouseDown;
            _radarPictureBox.MouseMove += RadarPictureBox_MouseMove;
            _radarPictureBox.MouseUp += RadarPictureBox_MouseUp;
            _radarPictureBox.Paint += RadarPictureBox_Paint;

            // Add controls to form
            this.Controls.Add(_radarPictureBox);
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
            await LoadRadarForIndexAsync(0);
        }

        private async Task LoadRadarForSelectedSiteAsync()
        {
            await LoadRadarForIndexAsync(_radarSiteCombo.SelectedIndex);
        }

        private async Task LoadRadarForIndexAsync(int index)
        {
            if (index < 0 || index >= _radarSites.Length) return;

            try
            {
                _statusLabel.Text = "Loading radar data...";
                _statusLabel.ForeColor = Color.Yellow;

                var site = _radarSites[index];
                var radarBytes = await _radarService.FetchRadarImageAsync(site.Lat, site.Lon, 800, 600, 250);
                
                if (radarBytes != null && radarBytes.Length > 0)
                {
                    using (var ms = new System.IO.MemoryStream(radarBytes))
                    {
                        _currentRadarImage?.Dispose();
                        _currentRadarImage = Image.FromStream(ms);
                        _radarPictureBox.Image = _currentRadarImage;
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
            _zoomLevel = _zoomTrackBar.Value / 100f;
            _radarPictureBox.Invalidate();
        }

        private void RadarPictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _lastMousePos = e.Location;
                _radarPictureBox.Cursor = Cursors.SizeAll;
            }
        }

        private void RadarPictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _panOffset.X += e.X - _lastMousePos.X;
                _panOffset.Y += e.Y - _lastMousePos.Y;
                _lastMousePos = e.Location;
                _radarPictureBox.Invalidate();
            }
        }

        private void RadarPictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            _isDragging = false;
            _radarPictureBox.Cursor = Cursors.Hand;
        }

        private void RadarPictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_currentRadarImage == null) return;

            var g = e.Graphics;
            g.Clear(_radarPictureBox.BackColor);

            // Apply zoom and pan
            var scaledWidth = (int)(_currentRadarImage.Width * _zoomLevel);
            var scaledHeight = (int)(_currentRadarImage.Height * _zoomLevel);

            var x = (_radarPictureBox.Width - scaledWidth) / 2 + _panOffset.X;
            var y = (_radarPictureBox.Height - scaledHeight) / 2 + _panOffset.Y;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_currentRadarImage, x, y, scaledWidth, scaledHeight);

            // Draw crosshair at center
            var centerX = _radarPictureBox.Width / 2;
            var centerY = _radarPictureBox.Height / 2;
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1))
            {
                g.DrawLine(pen, centerX - 10, centerY, centerX + 10, centerY);
                g.DrawLine(pen, centerX, centerY - 10, centerX, centerY + 10);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _currentRadarImage?.Dispose();
            _httpClient?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
