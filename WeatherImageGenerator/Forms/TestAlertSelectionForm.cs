using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Dialog for selecting test alert country/provider and alert type
    /// </summary>
    public class TestAlertSelectionForm : Form
    {
        private ComboBox _countryCombo = null!;
        private ComboBox _alertTypeCombo = null!;
        private Button _okBtn = null!;
        private Button _cancelBtn = null!;
        private Label _descriptionLabel = null!;

        public string SelectedCountry { get; private set; } = "Canada";
        public string SelectedAlertType { get; private set; } = "";

        // Alert types for each country
        private readonly Dictionary<string, List<string>> _alertTypes = new()
        {
            {
                "Canada (Alert Ready)",
                new List<string>
                {
                    "AMBER Alert - Missing Child",
                    "Civil Emergency - Public Safety",
                    "Public Safety Advisory"
                }
            },
            {
                "USA (NWS)",
                new List<string>
                {
                    "Tornado Warning",
                    "Severe Thunderstorm Warning",
                    "Winter Weather Advisory",
                    "Flash Flood Warning",
                    "Heat Advisory"
                }
            }
        };

        public TestAlertSelectionForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Test Alert Type";
            this.Width = 450;
            this.Height = 300;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(240, 240, 240);

            // Main container
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                AutoSize = false,
                RowCount = 7,
                ColumnCount = 1
            };

            // Title label
            var titleLabel = new Label
            {
                Text = "Generate Emergency Test Alert",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            // Description label
            _descriptionLabel = new Label
            {
                Text = "Select the alert provider country and the type of alert to generate for testing.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(127, 140, 141),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };

            // Country label and combo
            var countryLabel = new Label
            {
                Text = "Alert Provider:",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            _countryCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Items = { "Canada (Alert Ready)", "USA (NWS)" },
                SelectedIndex = 0,
                Width = 300,
                Height = 30,
                Margin = new Padding(0, 0, 0, 12)
            };
            _countryCombo.SelectedIndexChanged += CountryCombo_SelectedIndexChanged;

            // Alert type label and combo
            var alertTypeLabel = new Label
            {
                Text = "Alert Type:",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            _alertTypeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 300,
                Height = 30,
                Margin = new Padding(0, 0, 0, 16)
            };

            // Populate initial alert types
            UpdateAlertTypes();

            // Buttons panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 12, 0, 0),
                Margin = new Padding(0, 16, 0, 0)
            };

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Width = 80,
                Height = 36,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(189, 195, 199),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(4, 0, 0, 0)
            };
            _cancelBtn.FlatAppearance.BorderSize = 0;

            _okBtn = new Button
            {
                Text = "Generate",
                Width = 80,
                Height = 36,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(230, 126, 34),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Margin = new Padding(4, 0, 0, 0)
            };
            _okBtn.FlatAppearance.BorderSize = 0;
            _okBtn.Click += OkBtn_Click;

            buttonPanel.Controls.Add(_cancelBtn);
            buttonPanel.Controls.Add(_okBtn);

            // Add all controls
            this.Controls.Add(mainPanel);
            mainPanel.Controls.Add(titleLabel, 0, 0);
            mainPanel.Controls.Add(_descriptionLabel, 0, 1);
            mainPanel.Controls.Add(countryLabel, 0, 2);
            mainPanel.Controls.Add(_countryCombo, 0, 3);
            mainPanel.Controls.Add(alertTypeLabel, 0, 4);
            mainPanel.Controls.Add(_alertTypeCombo, 0, 5);
            mainPanel.Controls.Add(buttonPanel, 0, 6);

            // Set row heights
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            this.AcceptButton = _okBtn;
            this.CancelButton = _cancelBtn;
        }

        private void CountryCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateAlertTypes();
        }

        private void UpdateAlertTypes()
        {
            _alertTypeCombo.Items.Clear();
            string country = _countryCombo.SelectedItem?.ToString() ?? "Canada (Alert Ready)";

            if (_alertTypes.TryGetValue(country, out var types))
            {
                foreach (var type in types)
                {
                    _alertTypeCombo.Items.Add(type);
                }
                _alertTypeCombo.SelectedIndex = 0;
            }
        }

        private void OkBtn_Click(object? sender, EventArgs e)
        {
            if (_alertTypeCombo.SelectedIndex < 0)
            {
                MessageBox.Show("Please select an alert type.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectedCountry = _countryCombo.SelectedItem?.ToString() ?? "Canada (Alert Ready)";
            SelectedAlertType = _alertTypeCombo.SelectedItem?.ToString() ?? "";

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
