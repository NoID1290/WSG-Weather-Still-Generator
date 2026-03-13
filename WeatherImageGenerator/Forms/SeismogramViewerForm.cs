using System;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Earthquake Canada seismogram viewer — interactive map with live CNSN station data,
    /// real MiniSEED waveform playback, and animated earthquake epicenter display.
    /// </summary>
    public partial class SeismogramViewerForm : Form
    {
        private SeismogramMapControl _seismogramMap;
        private bool _isFullscreen = false;
        private FormBorderStyle _savedBorderStyle;
        private FormWindowState _savedWindowState;
        private Rectangle _savedBounds;

        public SeismogramViewerForm()
        {
            InitializeComponent();
            ThemeManager.ApplyTo(this);
            ThemeManager.ApplyTitleBar(this);
            ThemeManager.ThemeChanged += _ =>
            {
                ThemeManager.ApplyTo(this);
                ThemeManager.ApplyTitleBar(this);
                _seismogramMap?.ApplyTheme();
            };
            LoadSeismogramMap();
        }

        private void InitializeComponent()
        {
            this.Text = "Seismogram Viewer — Earthquakes Canada";
            this.Size = new Size(1400, 1050);
            this.StartPosition = FormStartPosition.CenterScreen;
            try { this.Icon = new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WSG.ico")); }
            catch { }
        }

        private void LoadSeismogramMap()
        {
            _seismogramMap = new SeismogramMapControl
            {
                Dock = DockStyle.Fill
            };

            this.Controls.Add(_seismogramMap);

            var config = ConfigManager.LoadConfig();
            if (config.SeismogramMapView == null)
            {
                _ = InitializeLocationAsync();
            }
            else
            {
                _seismogramMap.SetLocationAndZoom(
                    config.SeismogramMapView.Latitude,
                    config.SeismogramMapView.Longitude,
                    config.SeismogramMapView.ZoomLevel);
            }
        }

        private async Task InitializeLocationAsync()
        {
            try
            {
                var location = await GetUserLocationAsync();
                if (location.HasValue)
                    _seismogramMap.SetLocationAndZoom(location.Value.lat, location.Value.lon, 5);
                else
                    _seismogramMap.SetLocationAndZoom(56.1304, -106.3468, 4);
            }
            catch
            {
                _seismogramMap.SetLocationAndZoom(56.1304, -106.3468, 4);
            }
        }

        private async Task<(double lat, double lon)?> GetUserLocationAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon,status");
                using var doc = JsonDocument.Parse(response);
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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            this.KeyPreview = true;
            this.KeyDown += SeismogramViewerForm_KeyDown;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                ToggleFullscreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _seismogramMap?.BeginShutdown();
            base.OnFormClosing(e);
        }

        private void SeismogramViewerForm_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Oemplus:
                case Keys.Add:
                    _seismogramMap.SetZoom(_seismogramMap.CurrentZoom + 1);
                    e.Handled = true;
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _seismogramMap.SetZoom(_seismogramMap.CurrentZoom - 1);
                    e.Handled = true;
                    break;
                case Keys.C:
                    _seismogramMap.SetLocation(56.1304, -106.3468);
                    e.Handled = true;
                    break;
                case Keys.F5:
                    _seismogramMap.RefreshData();
                    e.Handled = true;
                    break;
                case Keys.Space:
                    _seismogramMap.ToggleWaveformPlayback();
                    e.Handled = true;
                    break;
                case Keys.M:
                    _seismogramMap.CycleMapStyle();
                    e.Handled = true;
                    break;
                case Keys.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    if (_isFullscreen)
                    {
                        ToggleFullscreen();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _savedBorderStyle = this.FormBorderStyle;
                _savedWindowState = this.WindowState;
                _savedBounds = this.Bounds;
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Maximized;
                _isFullscreen = true;
            }
            else
            {
                this.FormBorderStyle = _savedBorderStyle;
                this.WindowState = FormWindowState.Normal;
                this.Bounds = _savedBounds;
                this.WindowState = _savedWindowState;
                _isFullscreen = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _seismogramMap?.BeginShutdown();
                _seismogramMap?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
