using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenMeteo;
using OpenMap;
using WeatherImageGenerator.Models;
using ECCC.Services;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Form to display detailed weather information, forecast, and alerts for a location
    /// </summary>
    public class WeatherDetailsForm : Form
    {
        private readonly WeatherForecast? _forecast;
        private readonly string _locationName;
        private readonly List<AlertEntry> _alerts;
        private readonly OpenMeteoClient _client;
        private TabControl _tabControl;
        private PictureBox? _radarPictureBox;
        private Label? _radarStatusLabel;
        private bool _radarLoaded = false;
        private MapStyle _currentMapStyle = MapStyle.Terrain;
        private static readonly HttpClient _httpClient = new HttpClient();

        public WeatherDetailsForm(string locationName, WeatherForecast? forecast, List<AlertEntry> alerts)
        {
            _locationName = locationName;
            _forecast = forecast;
            _alerts = alerts ?? new List<AlertEntry>();
            _client = new OpenMeteoClient();

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = $"Weather Details - {_locationName}";
            this.Width = 900;
            this.Height = 750;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(700, 500);
            this.Font = new Font("Segoe UI", 9.5F);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F)
            };
            _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

            // Tab 1: Current Conditions
            var currentTab = new TabPage("☀ Current");
            currentTab.Controls.Add(CreateCurrentPanel());
            _tabControl.TabPages.Add(currentTab);

            // Tab 2: Forecast
            var forecastTab = new TabPage("📅 Forecast");
            forecastTab.Controls.Add(CreateForecastPanel());
            _tabControl.TabPages.Add(forecastTab);

            // Tab 3: Hourly (next 24 hours)
            var hourlyTab = new TabPage("🕐 Hourly");
            hourlyTab.Controls.Add(CreateHourlyPanel());
            _tabControl.TabPages.Add(hourlyTab);

            // Tab 4: Radar Image
            var radarTab = new TabPage("🌧 Radar");
            radarTab.Controls.Add(CreateRadarPanel());
            _tabControl.TabPages.Add(radarTab);

            // Tab 5: Alerts
            var alertsTab = new TabPage($"⚠ Alerts ({GetMatchingAlerts().Count})");
            alertsTab.Controls.Add(CreateAlertsPanel());
            _tabControl.TabPages.Add(alertsTab);

            // Highlight alerts tab if there are active alerts
            if (GetMatchingAlerts().Count > 0)
            {
                alertsTab.BackColor = Color.LightCoral;
            }

            this.Controls.Add(_tabControl);

            // Close button
            var btnClose = new Button
            {
                Text = "Close",
                Dock = DockStyle.Bottom,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private Panel CreateCurrentPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15) };

            if (_forecast?.Current == null)
            {
                var noDataLabel = new Label
                {
                    Text = "No current weather data available for this location.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12F, FontStyle.Italic)
                };
                panel.Controls.Add(noDataLabel);
                return panel;
            }

            var current = _forecast.Current;
            var units = _forecast.CurrentUnits;

            var flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(10)
            };

            // Location header
            AddHeaderLabel(flowPanel, $"📍 {_locationName}", 18, FontStyle.Bold);
            AddSeparator(flowPanel);

            // Main temperature display
            string tempDisplay = current.Temperature.HasValue 
                ? $"{current.Temperature:F1}{units?.Temperature ?? "°C"}" 
                : "N/A";
            AddHeaderLabel(flowPanel, $"🌡 {tempDisplay}", 32, FontStyle.Bold, Color.DarkSlateBlue);

            // Condition
            string condition = current.Weathercode.HasValue 
                ? _client.WeathercodeToString(current.Weathercode.Value) 
                : "Unknown";
            AddHeaderLabel(flowPanel, condition, 14, FontStyle.Regular, Color.DimGray);

            AddSeparator(flowPanel);

            // Detail grid - 4 columns for 2 details per row
            var detailsPanel = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 4,
                AutoSize = true,
                Padding = new Padding(5),
                Width = 600
            };
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            detailsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

            // Row 0: Feels Like | Wind Speed
            AddDetailCell(detailsPanel, 0, 0, "🤔 Feels Like", $"{current.Apparent_temperature}{units?.Apparent_temperature ?? "°C"}");
            AddDetailCell(detailsPanel, 0, 2, "💨 Wind Speed", $"{current.Windspeed_10m}{units?.Windspeed_10m ?? "km/h"} {DegreesToCardinal(current.Winddirection_10m)}");
            
            // Row 1: Wind Gusts | Humidity
            AddDetailCell(detailsPanel, 1, 0, "🌬 Wind Gusts", $"{current.Windgusts_10m}{units?.Windgusts_10m ?? "km/h"}");
            AddDetailCell(detailsPanel, 1, 2, "💧 Humidity", $"{current.Relativehumidity_2m}{units?.Relativehumidity_2m ?? "%"}");
            
            // Row 2: Precipitation | Cloud Cover
            AddDetailCell(detailsPanel, 2, 0, "🌧 Precipitation", $"{current.Precipitation}{units?.Precipitation ?? "mm"}");
            AddDetailCell(detailsPanel, 2, 2, "☁ Cloud Cover", $"{current.Cloudcover}{units?.Cloudcover ?? "%"}");
            
            // Row 3: Pressure | Day/Night
            AddDetailCell(detailsPanel, 3, 0, "📊 Pressure", $"{current.Pressure_msl:F0}{units?.Pressure_msl ?? "hPa"}");
            AddDetailCell(detailsPanel, 3, 2, "🌅 Day/Night", current.Is_day == 1 ? "Daytime ☀" : "Nighttime 🌙");

            flowPanel.Controls.Add(detailsPanel);

            // Time info
            AddSeparator(flowPanel);
            string timeInfo = $"Last Updated: {current.Time ?? "Unknown"}";
            if (_forecast.Timezone != null)
                timeInfo += $" ({_forecast.TimezoneAbbreviation ?? _forecast.Timezone})";
            AddInfoLabel(flowPanel, timeInfo, FontStyle.Italic, Color.Gray);

            panel.Controls.Add(flowPanel);
            return panel;
        }

        private Panel CreateForecastPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

            if (_forecast?.Daily?.Time == null || _forecast.Daily.Time.Length == 0)
            {
                var noDataLabel = new Label
                {
                    Text = "No forecast data available for this location.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12F, FontStyle.Italic)
                };
                panel.Controls.Add(noDataLabel);
                return panel;
            }

            var daily = _forecast.Daily;
            var units = _forecast.DailyUnits;

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 10F)
            };

            listView.Columns.Add("Date", 120);
            listView.Columns.Add("Condition", 180);
            listView.Columns.Add("High", 80);
            listView.Columns.Add("Low", 80);
            listView.Columns.Add("Precip", 80);
            listView.Columns.Add("Wind", 100);

            for (int i = 0; i < daily.Time.Length; i++)
            {
                if (!DateTime.TryParse(daily.Time[i], out DateTime date)) continue;

                var item = new ListViewItem(date.ToString("ddd, MMM d"));
                
                // Condition
                string condition = daily.Weathercode != null && i < daily.Weathercode.Length
                    ? _client.WeathercodeToString((int)daily.Weathercode[i])
                    : "N/A";
                item.SubItems.Add(condition);

                // High temp
                string high = daily.Temperature_2m_max != null && i < daily.Temperature_2m_max.Length
                    ? $"{daily.Temperature_2m_max[i]:F0}{units?.Temperature_2m_max ?? "°C"}"
                    : "N/A";
                item.SubItems.Add(high);

                // Low temp
                string low = daily.Temperature_2m_min != null && i < daily.Temperature_2m_min.Length
                    ? $"{daily.Temperature_2m_min[i]:F0}{units?.Temperature_2m_min ?? "°C"}"
                    : "N/A";
                item.SubItems.Add(low);

                // Precipitation
                string precip = daily.Precipitation_sum != null && i < daily.Precipitation_sum.Length
                    ? $"{daily.Precipitation_sum[i]:F1}{units?.Precipitation_sum ?? "mm"}"
                    : "N/A";
                item.SubItems.Add(precip);

                // Wind
                string wind = daily.Windspeed_10m_max != null && i < daily.Windspeed_10m_max.Length
                    ? $"{daily.Windspeed_10m_max[i]:F0}{units?.Windspeed_10m_max ?? "km/h"}"
                    : "N/A";
                item.SubItems.Add(wind);

                // Highlight today
                if (date.Date == DateTime.Today)
                {
                    item.BackColor = Color.LightYellow;
                    item.Font = new Font(listView.Font, FontStyle.Bold);
                }

                listView.Items.Add(item);
            }

            panel.Controls.Add(listView);
            return panel;
        }

        private Panel CreateHourlyPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

            if (_forecast?.Hourly?.Time == null || _forecast.Hourly.Time.Length == 0)
            {
                var noDataLabel = new Label
                {
                    Text = "No hourly data available for this location.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12F, FontStyle.Italic)
                };
                panel.Controls.Add(noDataLabel);
                return panel;
            }

            var hourly = _forecast.Hourly;
            var units = _forecast.HourlyUnits;

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Segoe UI", 9.5F)
            };

            listView.Columns.Add("Time", 140);
            listView.Columns.Add("Temp", 70);
            listView.Columns.Add("Feels", 70);
            listView.Columns.Add("Condition", 150);
            listView.Columns.Add("Precip", 70);
            listView.Columns.Add("Wind", 90);

            // Show next 48 hours
            int maxHours = Math.Min(48, hourly.Time.Length);
            DateTime now = DateTime.Now;

            for (int i = 0; i < maxHours; i++)
            {
                if (!DateTime.TryParse(hourly.Time[i], out DateTime time)) continue;

                // Skip past hours
                if (time < now.AddHours(-1)) continue;

                var item = new ListViewItem(time.ToString("ddd HH:mm"));

                // Temperature
                string temp = hourly.Temperature_2m != null && i < hourly.Temperature_2m.Length && hourly.Temperature_2m[i].HasValue
                    ? $"{hourly.Temperature_2m[i]:F0}{units?.Temperature_2m ?? "°C"}"
                    : "N/A";
                item.SubItems.Add(temp);

                // Feels like
                string feels = hourly.Apparent_temperature != null && i < hourly.Apparent_temperature.Length && hourly.Apparent_temperature[i].HasValue
                    ? $"{hourly.Apparent_temperature[i]:F0}°"
                    : "N/A";
                item.SubItems.Add(feels);

                // Condition
                string condition = hourly.Weathercode != null && i < hourly.Weathercode.Length && hourly.Weathercode[i].HasValue
                    ? _client.WeathercodeToString(hourly.Weathercode[i]!.Value)
                    : "N/A";
                item.SubItems.Add(condition);

                // Precipitation
                string precip = hourly.Precipitation != null && i < hourly.Precipitation.Length && hourly.Precipitation[i].HasValue
                    ? $"{hourly.Precipitation[i]:F1}mm"
                    : "0mm";
                item.SubItems.Add(precip);

                // Wind
                string wind = hourly.Windspeed_10m != null && i < hourly.Windspeed_10m.Length && hourly.Windspeed_10m[i].HasValue
                    ? $"{hourly.Windspeed_10m[i]:F0}km/h"
                    : "N/A";
                item.SubItems.Add(wind);

                // Highlight current hour
                if (time.Hour == now.Hour && time.Date == now.Date)
                {
                    item.BackColor = Color.LightCyan;
                    item.Font = new Font(listView.Font, FontStyle.Bold);
                }

                listView.Items.Add(item);
            }

            panel.Controls.Add(listView);
            return panel;
        }

        private Panel CreateAlertsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            var matchingAlerts = GetMatchingAlerts();

            if (matchingAlerts.Count == 0)
            {
                var noAlertLabel = new Label
                {
                    Text = "✓ No active weather alerts for this location.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 14F),
                    ForeColor = Color.DarkGreen
                };
                panel.Controls.Add(noAlertLabel);
                return panel;
            }

            var flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(10)
            };

            AddHeaderLabel(flowPanel, $"⚠ {matchingAlerts.Count} Active Alert(s)", 16, FontStyle.Bold, Color.DarkRed);
            AddSeparator(flowPanel);

            foreach (var alert in matchingAlerts)
            {
                var alertPanel = new Panel
                {
                    Width = flowPanel.ClientSize.Width - 40,
                    AutoSize = true,
                    MinimumSize = new Size(0, 100),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(10),
                    Margin = new Padding(0, 5, 0, 10)
                };

                // Set background based on severity
                if (alert.SeverityColor.Equals("Red", StringComparison.OrdinalIgnoreCase))
                    alertPanel.BackColor = Color.MistyRose;
                else if (alert.SeverityColor.Equals("Yellow", StringComparison.OrdinalIgnoreCase))
                    alertPanel.BackColor = Color.LemonChiffon;
                else
                    alertPanel.BackColor = Color.WhiteSmoke;

                var alertFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    AutoSize = true,
                    WrapContents = false
                };

                // Type badge
                var typeLabel = new Label
                {
                    Text = alert.Type.ToUpperInvariant(),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = alert.SeverityColor.Equals("Red", StringComparison.OrdinalIgnoreCase) ? Color.Red :
                               alert.SeverityColor.Equals("Yellow", StringComparison.OrdinalIgnoreCase) ? Color.DarkOrange : Color.Gray,
                    Padding = new Padding(5, 2, 5, 2),
                    Margin = new Padding(0, 0, 0, 5)
                };
                alertFlow.Controls.Add(typeLabel);

                // Title
                var titleLabel = new Label
                {
                    Text = alert.Title,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    MaximumSize = new Size(alertPanel.Width - 30, 0),
                    Margin = new Padding(0, 0, 0, 8)
                };
                alertFlow.Controls.Add(titleLabel);

                // Details header
                var detailsHeaderLabel = new Label
                {
                    Text = "📋 Alert Details:",
                    AutoSize = true,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    ForeColor = Color.DarkSlateGray,
                    Margin = new Padding(0, 5, 0, 3)
                };
                alertFlow.Controls.Add(detailsHeaderLabel);

                // Summary/Details - full content in a scrollable text box for long alerts
                var summaryText = alert.Summary;
                if (string.IsNullOrWhiteSpace(summaryText))
                {
                    summaryText = "(No additional details available)";
                }

                // Use a RichTextBox for longer content to allow scrolling and better text display
                var detailsBox = new RichTextBox
                {
                    Text = summaryText,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = alertPanel.BackColor,
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = Color.Black,
                    Width = alertPanel.Width - 40,
                    Height = Math.Min(200, Math.Max(60, summaryText.Length / 2)), // Dynamic height based on content
                    ScrollBars = RichTextBoxScrollBars.Vertical,
                    Margin = new Padding(0, 0, 0, 5)
                };
                alertFlow.Controls.Add(detailsBox);

                // City info
                var cityLabel = new Label
                {
                    Text = $"📍 Location: {alert.City}",
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                    ForeColor = Color.DimGray,
                    Margin = new Padding(0, 5, 0, 0)
                };
                alertFlow.Controls.Add(cityLabel);

                alertPanel.Controls.Add(alertFlow);
                flowPanel.Controls.Add(alertPanel);
            }

            panel.Controls.Add(flowPanel);
            return panel;
        }

        private List<AlertEntry> GetMatchingAlerts()
        {
            return _alerts.Where(a => IsLocationMatch(a.City, _locationName)).ToList();
        }

        private bool IsLocationMatch(string alertCity, string locationName)
        {
            string normalizedAlert = NormalizeForComparison(alertCity);
            string normalizedLocation = NormalizeForComparison(locationName);

            return normalizedAlert == normalizedLocation ||
                   normalizedAlert.Contains(normalizedLocation) ||
                   normalizedLocation.Contains(normalizedAlert);
        }

        private static string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        }

        private string DegreesToCardinal(int? degrees)
        {
            if (!degrees.HasValue) return "";
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            return cardinals[(int)Math.Round(((double)degrees.Value % 360) / 45)];
        }

        private void AddHeaderLabel(FlowLayoutPanel panel, string text, float fontSize, FontStyle style, Color? color = null)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", fontSize, style),
                ForeColor = color ?? Color.Black,
                Margin = new Padding(0, 5, 0, 5)
            };
            panel.Controls.Add(label);
        }

        private void AddInfoLabel(FlowLayoutPanel panel, string text, FontStyle style, Color color)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, style),
                ForeColor = color,
                Margin = new Padding(0, 3, 0, 3)
            };
            panel.Controls.Add(label);
        }

        private void AddSeparator(FlowLayoutPanel panel)
        {
            var sep = new Label
            {
                Height = 2,
                Width = panel.ClientSize.Width - 30,
                BackColor = Color.LightGray,
                Margin = new Padding(0, 10, 0, 10)
            };
            panel.Controls.Add(sep);
        }

        private void AddDetailRow(TableLayoutPanel table, int row, string label, string value)
        {
            var lblName = new Label
            {
                Text = label,
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                Padding = new Padding(5),
                ForeColor = Color.DimGray
            };
            table.Controls.Add(lblName, 0, row);

            var lblValue = new Label
            {
                Text = value ?? "N/A",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Padding = new Padding(5)
            };
            table.Controls.Add(lblValue, 1, row);
        }

        private void AddDetailCell(TableLayoutPanel table, int row, int col, string label, string value)
        {
            var lblName = new Label
            {
                Text = label,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Padding = new Padding(5, 8, 5, 2),
                ForeColor = Color.DimGray
            };
            table.Controls.Add(lblName, col, row);

            var lblValue = new Label
            {
                Text = value ?? "N/A",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Padding = new Padding(5, 2, 5, 8)
            };
            table.Controls.Add(lblValue, col + 1, row);
        }

        private Panel CreateRadarPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

            if (_forecast == null || _forecast.Latitude == 0 || _forecast.Longitude == 0)
            {
                var noDataLabel = new Label
                {
                    Text = "No location data available for radar image.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12F, FontStyle.Italic)
                };
                panel.Controls.Add(noDataLabel);
                return panel;
            }

            var flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(10)
            };

            // Header
            AddHeaderLabel(flowPanel, $"🌧 Radar Image - {_locationName}", 16, FontStyle.Bold);
            AddInfoLabel(flowPanel, $"Location: {_forecast.Latitude:F4}°, {_forecast.Longitude:F4}°", FontStyle.Regular, Color.DimGray);
            AddInfoLabel(flowPanel, "1km Resolution Rain Rate Radar (RADAR_1KM_RRAI) with Base Map", FontStyle.Italic, Color.Gray);
            AddSeparator(flowPanel);

            // Loading label
            _radarStatusLabel = new Label
            {
                Text = "🔄 Loading radar image...",
                AutoSize = true,
                Font = new Font("Segoe UI", 11F),
                ForeColor = Color.DarkBlue,
                Margin = new Padding(0, 10, 0, 10)
            };
            flowPanel.Controls.Add(_radarStatusLabel);

            // Picture box for radar image
            _radarPictureBox = new PictureBox
            {
                Width = 800,
                Height = 600,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Margin = new Padding(0, 10, 0, 10)
            };
            
            // Add Paint event to draw attribution overlay
            _radarPictureBox.Paint += (s, e) =>
            {
                if (_radarPictureBox.Image != null)
                {
                    // Get attribution text based on current map style
                    var attributionText = MapOverlayService.GetAttributionText(_currentMapStyle);
                    
                    // Draw attribution with semi-transparent background
                    using (var font = new Font("Arial", 8, FontStyle.Regular))
                    using (var textBrush = new SolidBrush(Color.Black))
                    using (var bgBrush = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                    {
                        var textSize = e.Graphics.MeasureString(attributionText, font);
                        var padding = 4;
                        var x = _radarPictureBox.Width - textSize.Width - padding - 10;
                        var y = _radarPictureBox.Height - textSize.Height - padding - 5;
                        
                        // Draw background
                        e.Graphics.FillRectangle(bgBrush, x - padding, y - padding, 
                            textSize.Width + (padding * 2), textSize.Height + (padding * 2));
                        
                        // Draw text
                        e.Graphics.DrawString(attributionText, font, textBrush, x, y);
                    }
                    
                    // Also add "Radar: Environment Canada" attribution
                    using (var font = new Font("Arial", 7, FontStyle.Italic))
                    using (var textBrush = new SolidBrush(Color.DimGray))
                    {
                        var radarText = "Radar: Environment Canada";
                        var textSize = e.Graphics.MeasureString(radarText, font);
                        var x = 10;
                        var y = _radarPictureBox.Height - textSize.Height - 5;
                        e.Graphics.DrawString(radarText, font, textBrush, x, y);
                    }
                }
            };
            
            flowPanel.Controls.Add(_radarPictureBox);

            // Refresh button
            var btnRefresh = new Button
            {
                Text = "🔄 Refresh Radar",
                Width = 150,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(0, 10, 0, 0)
            };
            btnRefresh.Click += async (s, e) =>
            {
                _radarLoaded = false;
                await LoadRadarImageAsync(_radarStatusLabel);
            };
            flowPanel.Controls.Add(btnRefresh);

            // Info label
            var infoLabel = new Label
            {
                Text = "Radar data provided by Environment and Climate Change Canada (ECCC)\n" +
                       "Shows precipitation intensity in the region around the selected location.",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = Color.Gray,
                Margin = new Padding(0, 15, 0, 0),
                MaximumSize = new Size(780, 0)
            };
            flowPanel.Controls.Add(infoLabel);

            panel.Controls.Add(flowPanel);

            return panel;
        }

        private async void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // Check if Radar tab is selected (index 3)
            if (_tabControl.SelectedIndex == 3 && !_radarLoaded)
            {
                _radarLoaded = true;
                await LoadRadarImageAsync(_radarStatusLabel);
            }
        }

        private async Task LoadRadarImageAsync(Label? statusLabel = null)
        {
            if (_forecast == null || _radarPictureBox == null)
                return;

            try
            {
                // Update UI on Windows Forms thread
                UpdateRadarStatus(statusLabel, "🔄 Generating map background...", Color.DarkBlue);

                // Generate map background using OpenMap
                var mapService = new MapOverlayService(800, 600);
                Bitmap? mapBackground = null;
                
                try
                {
                    // Calculate appropriate zoom level (wider area for radar)
                    var zoomLevel = 8; // Good zoom for regional radar view
                    _currentMapStyle = MapStyle.Terrain; // Terrain style works well with radar
                    mapBackground = await mapService.GenerateMapBackgroundAsync(
                        _forecast.Latitude,
                        _forecast.Longitude,
                        zoomLevel,
                        800,
                        600,
                        _currentMapStyle
                    );
                }
                catch
                {
                    // If map generation fails, continue without map background
                    mapBackground = null;
                }

                UpdateRadarStatus(statusLabel, "🔄 Loading radar data...", Color.DarkBlue);

                // Fetch radar data on background thread
                var radarService = new RadarImageService(_httpClient);
                var imageData = await radarService.FetchRadarImageAsync(
                    _forecast.Latitude,
                    _forecast.Longitude,
                    width: 800,
                    height: 600,
                    radiusKm: 150 // 150km radius for better coverage
                );

                Image? finalImage = null;

                if (imageData != null && imageData.Length > 0)
                {
                    // Create radar image from data
                    Bitmap? radarImage = null;
                    using (var ms = new MemoryStream(imageData))
                    {
                        radarImage = new Bitmap(Image.FromStream(ms));
                    }

                    // If we have both map and radar, composite them
                    if (mapBackground != null && radarImage != null)
                    {
                        UpdateRadarStatus(statusLabel, "🔄 Compositing radar on map...", Color.DarkBlue);
                        
                        // Create composite image
                        finalImage = new Bitmap(800, 600);
                        using (var g = Graphics.FromImage(finalImage))
                        {
                            // Draw map background
                            g.DrawImage(mapBackground, 0, 0, 800, 600);

                            // Draw radar overlay with transparency
                            var colorMatrix = new ColorMatrix { Matrix33 = 0.7f }; // 70% opacity
                            var imageAttributes = new ImageAttributes();
                            imageAttributes.SetColorMatrix(colorMatrix);

                            g.DrawImage(radarImage,
                                new Rectangle(0, 0, 800, 600),
                                0, 0, radarImage.Width, radarImage.Height,
                                GraphicsUnit.Pixel,
                                imageAttributes);
                        }

                        radarImage?.Dispose();
                    }
                    else if (radarImage != null)
                    {
                        // If only radar, use it directly
                        finalImage = radarImage;
                    }
                    else if (mapBackground != null)
                    {
                        // If only map, use it
                        finalImage = mapBackground;
                    }

                    // Update PictureBox on Windows Forms thread
                    if (_radarPictureBox != null && !_radarPictureBox.IsDisposed && finalImage != null)
                    {
                        if (_radarPictureBox.InvokeRequired)
                        {
                            _radarPictureBox.Invoke((MethodInvoker)delegate
                            {
                                if (!_radarPictureBox.IsDisposed)
                                {
                                    _radarPictureBox.Image?.Dispose();
                                    _radarPictureBox.Image = finalImage;
                                }
                            });
                        }
                        else
                        {
                            _radarPictureBox.Image?.Dispose();
                            _radarPictureBox.Image = finalImage;
                        }
                    }

                    UpdateRadarStatus(statusLabel, $"✓ Radar with map loaded at {DateTime.Now:HH:mm:ss}", Color.DarkGreen);
                }
                else
                {
                    UpdateRadarStatus(statusLabel, "⚠ Failed to load radar image. The service may be unavailable.", Color.DarkOrange);
                }
            }
            catch (Exception ex)
            {
                UpdateRadarStatus(statusLabel, $"✗ Error loading radar: {ex.Message}", Color.Red);
            }
        }

        private void UpdateRadarStatus(Label? statusLabel, string message, Color color)
        {
            if (statusLabel == null || statusLabel.IsDisposed)
                return;

            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke((MethodInvoker)delegate
                {
                    if (!statusLabel.IsDisposed)
                    {
                        statusLabel.Text = message;
                        statusLabel.ForeColor = color;
                    }
                });
            }
            else
            {
                statusLabel.Text = message;
                statusLabel.ForeColor = color;
            }
        }
    }
}
