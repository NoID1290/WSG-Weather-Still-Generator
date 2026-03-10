#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    /// <summary>
    /// Form for configuring which steps run during the automatic cycle.
    /// </summary>
    public class CycleControlForm : Form
    {
        private CheckBox _chkWeatherFetch = null!;
        private CheckBox _chkAlertsFetch = null!;
        private CheckBox _chkRadarAnimation = null!;
        private CheckBox _chkAlertsImage = null!;
        private CheckBox _chkDetailedWeather = null!;
        private CheckBox _chkGlobalWeatherMap = null!;
        private CheckBox _chkVideoGeneration = null!;

        public CycleControlForm()
        {
            InitializeComponents();
            LoadSettings();
            ThemeManager.ApplyTo(this);
            ThemeManager.ThemeChanged += _ => ThemeManager.ApplyTo(this);
        }

        private void InitializeComponents()
        {
            Text = "Cycle Control";
            Width = 560;
            Height = 470;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(245, 245, 250);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            var lblInfo = new Label
            {
                Left = 16,
                Top = 14,
                Width = 500,
                Height = 42,
                Text = "Choose which steps run when you start the automatic cycle. These settings apply to Start cycle, Web UI start-cycle, and Auto Start only.",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            var grpCycleSteps = new GroupBox
            {
                Left = 16,
                Top = 68,
                Width = 510,
                Height = 270,
                Text = "Automatic Cycle Steps",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            _chkWeatherFetch = CreateCheckBox("Fetch weather data", 18, 32, "Load forecast data for configured locations.");
            _chkAlertsFetch = CreateCheckBox("Fetch alerts", 18, 64, "Load ECCC and Alert Ready alerts.");
            _chkRadarAnimation = CreateCheckBox("Generate radar animation", 18, 96, "Create radar animation frames when radar is enabled.");
            _chkAlertsImage = CreateCheckBox("Generate alerts image", 18, 128, "Create the weather alerts still when alerts are active.");
            _chkDetailedWeather = CreateCheckBox("Generate detailed weather images", 18, 160, "Create the forecast still images for configured locations.");
            _chkGlobalWeatherMap = CreateCheckBox("Generate global weather map", 18, 192, "Create the static weather map image.");
            _chkVideoGeneration = CreateCheckBox("Generate video", 18, 224, "Build the final video from the generated assets.");

            grpCycleSteps.Controls.AddRange(new Control[]
            {
                _chkWeatherFetch,
                _chkAlertsFetch,
                _chkRadarAnimation,
                _chkAlertsImage,
                _chkDetailedWeather,
                _chkGlobalWeatherMap,
                _chkVideoGeneration
            });

            var btnReset = new Button
            {
                Text = "Reset Defaults",
                Left = 16,
                Top = 392,
                Width = 120,
                Height = 34
            };
            btnReset.Click += (s, e) => ApplyDefaults();

            var btnSave = new Button
            {
                Text = "Save",
                Left = 320,
                Top = 392,
                Width = 95,
                Height = 34,
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += SaveClicked;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 431,
                Top = 392,
                Width = 95,
                Height = 34,
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[]
            {
                lblInfo,
                grpCycleSteps,
                btnReset,
                btnSave,
                btnCancel
            });

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private CheckBox CreateCheckBox(string text, int left, int top, string toolTipText)
        {
            var checkBox = new CheckBox
            {
                Text = text,
                Left = left,
                Top = top,
                Width = 420,
                Height = 24,
                AutoSize = false,
                Font = new Font("Segoe UI", 9.25F, FontStyle.Regular)
            };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(checkBox, toolTipText);
            return checkBox;
        }

        private void LoadSettings()
        {
            var config = ConfigManager.LoadConfig();
            var cycleControl = config.CycleControl ?? new CycleControlSettings();

            _chkWeatherFetch.Checked = cycleControl.EnableWeatherDataFetch;
            _chkAlertsFetch.Checked = cycleControl.EnableAlertsFetch;
            _chkRadarAnimation.Checked = cycleControl.EnableRadarAnimation;
            _chkAlertsImage.Checked = cycleControl.EnableAlertsImage;
            _chkDetailedWeather.Checked = cycleControl.EnableDetailedWeatherImages;
            _chkGlobalWeatherMap.Checked = cycleControl.EnableGlobalWeatherMap;
            _chkVideoGeneration.Checked = cycleControl.EnableVideoGeneration;
        }

        private void ApplyDefaults()
        {
            _chkWeatherFetch.Checked = true;
            _chkAlertsFetch.Checked = true;
            _chkRadarAnimation.Checked = true;
            _chkAlertsImage.Checked = true;
            _chkDetailedWeather.Checked = true;
            _chkGlobalWeatherMap.Checked = true;
            _chkVideoGeneration.Checked = true;
        }

        private void SaveClicked(object? sender, EventArgs e)
        {
            var config = ConfigManager.LoadConfig();
            config.CycleControl = new CycleControlSettings
            {
                EnableWeatherDataFetch = _chkWeatherFetch.Checked,
                EnableAlertsFetch = _chkAlertsFetch.Checked,
                EnableRadarAnimation = _chkRadarAnimation.Checked,
                EnableAlertsImage = _chkAlertsImage.Checked,
                EnableDetailedWeatherImages = _chkDetailedWeather.Checked,
                EnableGlobalWeatherMap = _chkGlobalWeatherMap.Checked,
                EnableVideoGeneration = _chkVideoGeneration.Checked
            };

            ConfigManager.SaveConfig(config);
        }
    }
}