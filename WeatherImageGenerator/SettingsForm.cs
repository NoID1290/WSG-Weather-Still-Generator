using System;
using System.IO;
using System.Windows.Forms;

namespace WeatherImageGenerator
{
    public class SettingsForm : Form
    {
        TextBox txtOutputDir;
        NumericUpDown numStatic;
        NumericUpDown numFade;
        CheckBox chkFade;

        public SettingsForm()
        {
            this.Text = "Settings";
            this.Width = 520;
            this.Height = 240;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var lblOut = new Label { Text = "Output Directory:", Left = 10, Top = 20, Width = 120 };
            txtOutputDir = new TextBox { Left = 140, Top = 18, Width = 300 };
            var btnBrowse = new Button { Text = "Browse...", Left = 450, Top = 16, Width = 60 };
            btnBrowse.Click += (s, e) => BrowseClicked();

            var lblStatic = new Label { Text = "Static Duration (s):", Left = 10, Top = 60, Width = 120 };
            numStatic = new NumericUpDown { Left = 140, Top = 58, Width = 80, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };

            var lblFade = new Label { Text = "Fade Duration (s):", Left = 240, Top = 60, Width = 120 };
            numFade = new NumericUpDown { Left = 360, Top = 58, Width = 80, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M };

            chkFade = new CheckBox { Text = "Enable Fade Transitions", Left = 140, Top = 92, Width = 200 };

            var btnSave = new Button { Text = "Save", Left = 300, Top = 130, Width = 100 };
            var btnCancel = new Button { Text = "Cancel", Left = 410, Top = 130, Width = 100 };

            btnSave.Click += (s, e) => SaveClicked();
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { lblOut, txtOutputDir, btnBrowse, lblStatic, numStatic, lblFade, numFade, chkFade, btnSave, btnCancel });

            LoadSettings();
        }

        private void BrowseClicked()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select output directory for generated images and videos";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtOutputDir.Text = dlg.SelectedPath;
                }
            }
        }

        private void LoadSettings()
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                txtOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                numStatic.Value = (decimal)(cfg.Video?.StaticDurationSeconds ?? 8);
                numFade.Value = (decimal)(cfg.Video?.FadeDurationSeconds ?? 0.5);
                chkFade.Checked = cfg.Video?.EnableFadeTransitions ?? false;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load config in settings: {ex.Message}", Logger.LogLevel.Error);
            }
        }

        private void SaveClicked()
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                var imageGen = cfg.ImageGeneration ?? new ImageGenerationSettings();
                // Store path relative to current directory if possible
                var outDir = txtOutputDir.Text ?? "WeatherImages";
                if (outDir.StartsWith(Directory.GetCurrentDirectory()))
                {
                    outDir = outDir.Substring(Directory.GetCurrentDirectory().Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                imageGen.OutputDirectory = outDir;
                cfg.ImageGeneration = imageGen;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                cfg.Video = v;

                ConfigManager.SaveConfig(cfg);
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save settings: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
