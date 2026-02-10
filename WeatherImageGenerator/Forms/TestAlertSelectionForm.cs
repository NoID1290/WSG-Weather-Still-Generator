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
            this.Text = "Generate Test Alert";
            this.ClientSize = new Size(520, 380);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 245, 245);

            // Main container
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 20, 24, 20),
                AutoSize = false,
                RowCount = 7,
                ColumnCount = 1
            };

            // Title label
            var titleLabel = new Label
            {
                Text = "Generate Emergency Test Alert",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 73, 94),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };

            // Description label
            _descriptionLabel = new Label
            {
                Text = "Select the alert provider country and the type of alert to generate for testing.",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(127, 140, 141),
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                Margin = new Padding(0, 0, 0, 20)
            };

            // Country label and combo
            var countryLabel = new Label
            {
                Text = "Alert Provider:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _countryCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Items = { "Canada (Alert Ready)", "USA (NWS)" },
                SelectedIndex = 0,
                Font = new Font("Segoe UI", 9.5F),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 32,
                Margin = new Padding(0, 0, 0, 16)
            };
            _countryCombo.SelectedIndexChanged += CountryCombo_SelectedIndexChanged;

            // Alert type label and combo
            var alertTypeLabel = new Label
            {
                Text = "Alert Type:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _alertTypeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 32,
                Margin = new Padding(0, 0, 0, 20)
            };

            // Populate initial alert types
            UpdateAlertTypes();

            // Buttons panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 20, 0, 0),
                Margin = new Padding(0, 20, 0, 0)
            };

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Width = 90,
                Height = 38,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(149, 165, 166),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(8, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            _cancelBtn.FlatAppearance.BorderSize = 0;
            _cancelBtn.MouseEnter += (s, e) => _cancelBtn.BackColor = Color.FromArgb(127, 140, 141);
            _cancelBtn.MouseLeave += (s, e) => _cancelBtn.BackColor = Color.FromArgb(149, 165, 166);

            _okBtn = new Button
            {
                Text = "Generate",
                Width = 100,
                Height = 38,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(230, 126, 34),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            _okBtn.FlatAppearance.BorderSize = 0;
            _okBtn.MouseEnter += (s, e) => _okBtn.BackColor = Color.FromArgb(211, 84, 0);
            _okBtn.MouseLeave += (s, e) => _okBtn.BackColor = Color.FromArgb(230, 126, 34);
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
