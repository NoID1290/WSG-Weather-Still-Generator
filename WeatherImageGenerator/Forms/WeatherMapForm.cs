using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.OpenGL;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Demonstration form showing the Weather Interactive Map
    /// </summary>
    public partial class WeatherMapForm : Form
    {
        private WeatherMapControl _weatherMap;

        public WeatherMapForm()
        {
            InitializeComponent();
            LoadWeatherMap();
        }

        private void InitializeComponent()
        {
            this.Text = "Weather Interactive Map";
            this.Size = new System.Drawing.Size(1400, 900);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        }

        private void LoadWeatherMap()
        {
            _weatherMap = new WeatherMapControl
            {
                Dock = DockStyle.Fill
            };
            
            this.Controls.Add(_weatherMap);

            // Only auto-detect location if no saved map settings exist.
            // If the user has previously used the map, LoadMapSettings() in the
            // control constructor already restored zoom/lat/lon/layers/etc.
            var config = ConfigManager.LoadConfig();
            if (config.WeatherMapView == null)
            {
                _ = InitializeLocationAsync();
            }
            else
            {
                // Settings were loaded by the control — just apply the saved
                // zoom + position to the GL control so it renders correctly.
                _weatherMap.SetLocationAndZoom(
                    config.WeatherMapView.Latitude,
                    config.WeatherMapView.Longitude,
                    config.WeatherMapView.ZoomLevel);
            }
        }

        private async Task InitializeLocationAsync()
        {
            try
            {
                Console.WriteLine("[WeatherMapForm] Attempting to fetch user location...");
                var location = await GetUserLocationAsync();
                
                if (location.HasValue)
                {
                    Console.WriteLine($"[WeatherMapForm] User location detected: {location.Value.lat:F4}, {location.Value.lon:F4}");
                    _weatherMap.SetLocationAndZoom(location.Value.lat, location.Value.lon, 8);
                }
                else
                {
                    Console.WriteLine("[WeatherMapForm] Using default location (Canada)");
                    _weatherMap.SetLocationAndZoom(56.1304, -106.3468, 4);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WeatherMapForm] Location fetch failed: {ex.Message}");
                _weatherMap.SetLocationAndZoom(56.1304, -106.3468, 4);
            }
        }

        private async Task<(double lat, double lon)?> GetUserLocationAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                
                // Use ip-api.com for free geolocation (no API key needed)
                var response = await client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon,status");
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
                {
                    if (root.TryGetProperty("lat", out var lat) && root.TryGetProperty("lon", out var lon))
                    {
                        return (lat.GetDouble(), lon.GetDouble());
                    }
                }
            }
            catch
            {
                // Silently fail and use default
            }
            
            return null;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            
            // Add keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += WeatherMapForm_KeyDown;
        }

        private void WeatherMapForm_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Oemplus:
                case Keys.Add:
                    _weatherMap.SetZoom(_weatherMap.CurrentZoom + 1);
                    e.Handled = true;
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _weatherMap.SetZoom(_weatherMap.CurrentZoom - 1);
                    e.Handled = true;
                    break;
                case Keys.C:
                    _weatherMap.SetLocation(56.1304, -106.3468); // Center on Canada
                    e.Handled = true;
                    break;
                case Keys.R:
                    _weatherMap.ToggleRadar();
                    e.Handled = true;
                    break;
                case Keys.T:
                    _weatherMap.ToggleTemperature();
                    e.Handled = true;
                    break;
                case Keys.D:
                    _weatherMap.ToggleDebugOverlay();
                    e.Handled = true;
                    break;
                case Keys.F5:
                    _weatherMap.RefreshOverlays();
                    e.Handled = true;
                    break;
                case Keys.Space:
                    _weatherMap.ToggleAnimation();
                    e.Handled = true;
                    break;
                case Keys.Left:
                    _weatherMap.StepBackward();
                    e.Handled = true;
                    break;
                case Keys.Right:
                    _weatherMap.StepForward();
                    e.Handled = true;
                    break;
                case Keys.M:
                    _weatherMap.CycleMapStyle();
                    e.Handled = true;
                    break;
                case Keys.D1:
                    _weatherMap.SetMapStyleByIndex(0); // Standard
                    e.Handled = true;
                    break;
                case Keys.D2:
                    _weatherMap.SetMapStyleByIndex(1); // Dark
                    e.Handled = true;
                    break;
                case Keys.D3:
                    _weatherMap.SetMapStyleByIndex(2); // Terrain
                    e.Handled = true;
                    break;
                case Keys.D4:
                    _weatherMap.SetMapStyleByIndex(3); // Satellite
                    e.Handled = true;
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _weatherMap?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
