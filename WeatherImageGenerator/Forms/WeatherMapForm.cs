using System;
using System.Windows.Forms;
using WeatherImageGenerator.OpenGL;

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
            this.Text = "Weather Interactive Map_developement";
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

            // Set initial location (you can customize this)
            // Canada: 56.1304, -106.3468
            // Toronto: 43.6532, -79.3832
            // Vancouver: 49.2827, -123.1207
            // Montreal: 45.5017, -73.5673
            _weatherMap.SetLocation(56.1304, -106.3468);
            _weatherMap.SetZoom(4);
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
            // Add keyboard shortcuts for convenience
            // + or = : Zoom in
            // - : Zoom out
            // C : Center
            // R : Toggle radar
            // T : Toggle temperature
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
