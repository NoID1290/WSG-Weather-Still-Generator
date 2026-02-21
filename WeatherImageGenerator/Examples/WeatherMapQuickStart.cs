// Quick Start Example - Using the Weather Interactive Map
// Copy this code to test the new Weather Map system

using System;
using System.Windows.Forms;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Examples
{
    public class QuickStartExample
    {
        /// <summary>
        /// Example 1: Simple Weather Map in a Form
        /// </summary>
        public static void ShowBasicWeatherMap()
        {
            var form = new Form
            {
                Text = "Weather Map - Quick Start",
                Size = new System.Drawing.Size(1400, 900),
                StartPosition = FormStartPosition.CenterScreen
            };

            var weatherMap = new WeatherMapControl
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(weatherMap);

            // Set initial location - Canada
            weatherMap.SetLocation(56.1304, -106.3468);
            weatherMap.SetZoom(4);

            Application.Run(form);
        }

        /// <summary>
        /// Example 2: Weather Map with Multiple Cities
        /// </summary>
        public static void ShowCityExamples()
        {
            var form = new Form
            {
                Text = "Canadian Cities Weather",
                Size = new System.Drawing.Size(1600, 1000),
                StartPosition = FormStartPosition.CenterScreen
            };

            var weatherMap = new WeatherMapControl
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(weatherMap);

            // Create city selector
            var cityPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = System.Drawing.Color.FromArgb(40, 40, 40)
            };

            var btnToronto = CreateCityButton("Toronto", 10);
            btnToronto.Click += (s, e) => weatherMap.SetLocation(43.6532, -79.3832);

            var btnVancouver = CreateCityButton("Vancouver", 120);
            btnVancouver.Click += (s, e) => weatherMap.SetLocation(49.2827, -123.1207);

            var btnMontreal = CreateCityButton("Montreal", 230);
            btnMontreal.Click += (s, e) => weatherMap.SetLocation(45.5017, -73.5673);

            var btnCalgary = CreateCityButton("Calgary", 340);
            btnCalgary.Click += (s, e) => weatherMap.SetLocation(51.0447, -114.0719);

            var btnOttawa = CreateCityButton("Ottawa", 450);
            btnOttawa.Click += (s, e) => weatherMap.SetLocation(45.4215, -75.6972);

            var btnCanada = CreateCityButton("🇨🇦 Full Canada", 560);
            btnCanada.Click += (s, e) => 
            {
                weatherMap.SetLocation(56.1304, -106.3468);
                weatherMap.SetZoom(4);
            };

            cityPanel.Controls.AddRange(new Control[] { 
                btnToronto, btnVancouver, btnMontreal, 
                btnCalgary, btnOttawa, btnCanada 
            });

            form.Controls.Add(cityPanel);
            cityPanel.BringToFront();

            // Default to Toronto
            weatherMap.SetLocation(43.6532, -79.3832);
            weatherMap.SetZoom(10);

            Application.Run(form);
        }

        /// <summary>
        /// Example 3: Programmatic Overlay Control
        /// </summary>
        public static void ShowProgrammaticControl()
        {
            // This example shows how to control overlays programmatically
            // NOTE: The WeatherMapControl has built-in UI, but you can also
            // access the underlying components for custom control

            var form = new Form
            {
                Text = "Programmatic Weather Control",
                Size = new System.Drawing.Size(1200, 800)
            };

            var weatherMap = new WeatherMapControl
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(weatherMap);

            // Example: Auto-cycle through different cities every 10 seconds
            var cities = new[]
            {
                ("Toronto", 43.6532, -79.3832),
                ("Vancouver", 49.2827, -123.1207),
                ("Montreal", 45.5017, -73.5673),
                ("Calgary", 51.0447, -114.0719)
            };

            int currentCity = 0;
            var timer = new Timer { Interval = 10000 };
            timer.Tick += (s, e) =>
            {
                currentCity = (currentCity + 1) % cities.Length;
                var city = cities[currentCity];
                weatherMap.SetLocation(city.Item2, city.Item3);
                weatherMap.SetZoom(10);
                form.Text = $"Now showing: {city.Item1}";
            };
            timer.Start();

            Application.Run(form);
        }

        private static Button CreateCityButton(string text, int x)
        {
            return new Button
            {
                Text = text,
                Location = new System.Drawing.Point(x, 10),
                Size = new System.Drawing.Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = System.Drawing.Color.FromArgb(0, 122, 204),
                ForeColor = System.Drawing.Color.White,
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }
    }

    // NOTE: To run these examples, uncomment and use one at a time:
    /* 
    // Main entry point for testing
    class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Choose which example to run:

            // Example 1: Basic weather map
            QuickStartExample.ShowBasicWeatherMap();

            // Example 2: Multiple cities
            // QuickStartExample.ShowCityExamples();

            // Example 3: Programmatic control
            // QuickStartExample.ShowProgrammaticControl();
        }
    }
    */
}

/*
 * USAGE INSTRUCTIONS:
 * 
 * 1. Build the project:
 *    dotnet build
 * 
 * 2. Run the WeatherImageGenerator application:
 *    dotnet run
 * 
 * 3. Or add to your existing form:
 *    var map = new WeatherMapControl { Dock = DockStyle.Fill };
 *    this.Controls.Add(map);
 * 
 * FEATURES:
 * - ✅ Binary tile cache (.bin files)
 * - ✅ Smart caching (never re-downloads)
 * - ✅ Radar composite overlay
 * - ✅ Temperature grid overlay
 * - ✅ Full OpenGL rendering
 * - ✅ Complete UI controls
 * 
 * CONTROLS:
 * - Left click + drag: Pan map
 * - Mouse wheel: Zoom in/out
 * - Shift + wheel: Change tile zoom
 * - Right panel: All overlay controls
 * 
 * See docs/WEATHER_MAP_GUIDE.md for complete documentation
 */
