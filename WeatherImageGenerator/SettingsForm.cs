using System;
using System.IO;
using System.Windows.Forms;

namespace WeatherImageGenerator
{
    public class SettingsForm : Form
    {
        TextBox txtImageOutputDir;
        TextBox txtVideoOutputDir;
        NumericUpDown numStatic;
        NumericUpDown numFade;
        CheckBox chkFade;
        NumericUpDown numRefresh;
        NumericUpDown numImgWidth;
        NumericUpDown numImgHeight;
        ComboBox cmbImgFormat;
        ComboBox cmbResolution;
        TextBox txtCodec;
        TextBox txtBitrate;
        NumericUpDown numFps;
        ComboBox cmbContainer;
        CheckBox chkVerbose;
        CheckBox chkShowFfmpeg;

        public SettingsForm()
        {
            this.Text = "Settings";
            this.Width = 640;
            this.Height = 480;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int leftLabel = 10;
            int leftField = 170;
            int top = 20;
            int rowH = 28;

            var lblOutImg = new Label { Text = "Image Output Directory:", Left = leftLabel, Top = top, Width = 150 };
            txtImageOutputDir = new TextBox { Left = leftField, Top = top - 2, Width = 360 };
            var btnBrowseImg = new Button { Text = "Browse...", Left = 540, Top = top - 4, Width = 70 };
            btnBrowseImg.Click += (s, e) => BrowseClicked(txtImageOutputDir);

            top += rowH;
            var lblOutVid = new Label { Text = "Video Output Directory:", Left = leftLabel, Top = top, Width = 150 };
            txtVideoOutputDir = new TextBox { Left = leftField, Top = top - 2, Width = 360 };
            var btnBrowseVid = new Button { Text = "Browse...", Left = 540, Top = top - 4, Width = 70 };
            btnBrowseVid.Click += (s, e) => BrowseClicked(txtVideoOutputDir);

            top += rowH;
            var lblRefresh = new Label { Text = "Refresh Interval (min):", Left = leftLabel, Top = top, Width = 150 };
            numRefresh = new NumericUpDown { Left = leftField, Top = top - 2, Width = 80, Minimum = 1, Maximum = 1440, Value = 10 };

            top += rowH;
            var lblImgSize = new Label { Text = "Image Resolution (WxH):", Left = leftLabel, Top = top, Width = 150 };
            numImgWidth = new NumericUpDown { Left = leftField, Top = top - 2, Width = 90, Minimum = 320, Maximum = 7680, Value = 1920, Increment = 10 };
            numImgHeight = new NumericUpDown { Left = leftField + 100, Top = top - 2, Width = 90, Minimum = 240, Maximum = 4320, Value = 1080, Increment = 10 };
            cmbImgFormat = new ComboBox { Left = leftField + 200, Top = top - 2, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbImgFormat.Items.AddRange(new object[] { "png", "jpeg", "bmp", "gif" });
            cmbImgFormat.SelectedIndex = 0;

            top += rowH;
            var lblStatic = new Label { Text = "Static Duration (s):", Left = leftLabel, Top = top, Width = 150 };
            numStatic = new NumericUpDown { Left = leftField, Top = top - 2, Width = 80, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };

            var lblFade = new Label { Text = "Fade Duration (s):", Left = leftField + 90, Top = top, Width = 130 };
            numFade = new NumericUpDown { Left = leftField + 220, Top = top - 2, Width = 80, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M };

            chkFade = new CheckBox { Text = "Enable Fade Transitions", Left = leftField + 320, Top = top, Width = 180 };

            top += rowH;
            var lblResolution = new Label { Text = "Video Resolution Preset:", Left = leftLabel, Top = top, Width = 150 };
            cmbResolution = new ComboBox { Left = leftField, Top = top - 2, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbResolution.Items.AddRange(Enum.GetNames(typeof(ResolutionMode)));
            cmbResolution.SelectedIndex = 0;

            var lblFps = new Label { Text = "Video FPS:", Left = leftField + 150, Top = top, Width = 80 };
            numFps = new NumericUpDown { Left = leftField + 230, Top = top - 2, Width = 70, Minimum = 1, Maximum = 240, Value = 30 };

            var lblContainer = new Label { Text = "Container:", Left = leftField + 310, Top = top, Width = 80 };
            cmbContainer = new ComboBox { Left = leftField + 390, Top = top - 2, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;

            top += rowH;
            var lblCodec = new Label { Text = "Video Codec:", Left = leftLabel, Top = top, Width = 150 };
            txtCodec = new TextBox { Left = leftField, Top = top - 2, Width = 140, Text = "libx264" };

            var lblBitrate = new Label { Text = "Video Bitrate (e.g. 4M):", Left = leftField + 150, Top = top, Width = 200 };
            txtBitrate = new TextBox { Left = leftField + 350, Top = top - 2, Width = 140, Text = "4M" };

            top += rowH;
            chkVerbose = new CheckBox { Text = "Verbose FFmpeg output", Left = leftField, Top = top, Width = 180 };
            chkShowFfmpeg = new CheckBox { Text = "Show FFmpeg logs in GUI", Left = leftField + 200, Top = top, Width = 200, Checked = true };

            var btnSave = new Button { Text = "Save", Left = 360, Top = 380, Width = 100 };
            var btnCancel = new Button { Text = "Cancel", Left = 470, Top = 380, Width = 100 };

            btnSave.Click += (s, e) => SaveClicked();
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[]
            {
                lblOutImg, txtImageOutputDir, btnBrowseImg,
                lblOutVid, txtVideoOutputDir, btnBrowseVid,
                lblRefresh, numRefresh,
                lblImgSize, numImgWidth, numImgHeight, cmbImgFormat,
                lblStatic, numStatic, lblFade, numFade, chkFade,
                lblResolution, cmbResolution, lblFps, numFps, lblContainer, cmbContainer,
                lblCodec, txtCodec, lblBitrate, txtBitrate,
                chkVerbose, chkShowFfmpeg,
                btnSave, btnCancel
            });

            LoadSettings();
        }

        private void BrowseClicked(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select output directory";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    target.Text = dlg.SelectedPath;
                }
            }
        }

        private void LoadSettings()
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                txtImageOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                txtVideoOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.Video?.OutputDirectory ?? cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                numRefresh.Value = cfg.RefreshTimeMinutes;
                numImgWidth.Value = cfg.ImageGeneration?.ImageWidth ?? 1920;
                numImgHeight.Value = cfg.ImageGeneration?.ImageHeight ?? 1080;
                var fmt = (cfg.ImageGeneration?.ImageFormat ?? "png").ToLowerInvariant();
                if (cmbImgFormat.Items.Contains(fmt)) cmbImgFormat.SelectedItem = fmt;

                numStatic.Value = (decimal)(cfg.Video?.StaticDurationSeconds ?? 8);
                numFade.Value = (decimal)(cfg.Video?.FadeDurationSeconds ?? 0.5);
                chkFade.Checked = cfg.Video?.EnableFadeTransitions ?? false;
                cmbResolution.SelectedItem = cfg.Video?.ResolutionMode ?? "Mode1080p";
                numFps.Value = cfg.Video?.FrameRate ?? 30;
                txtCodec.Text = cfg.Video?.VideoCodec ?? "libx264";
                txtBitrate.Text = cfg.Video?.VideoBitrate ?? "4M";
                var container = (cfg.Video?.Container ?? "mp4").ToLowerInvariant();
                if (cmbContainer.Items.Contains(container)) cmbContainer.SelectedItem = container;
                chkVerbose.Checked = cfg.Video?.VerboseFfmpeg ?? false;
                chkShowFfmpeg.Checked = cfg.Video?.ShowFfmpegOutputInGui ?? true;
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
                cfg.RefreshTimeMinutes = (int)numRefresh.Value;

                var imageGen = cfg.ImageGeneration ?? new ImageGenerationSettings();
                imageGen.OutputDirectory = ToRelative(txtImageOutputDir.Text, "WeatherImages");
                imageGen.ImageWidth = (int)numImgWidth.Value;
                imageGen.ImageHeight = (int)numImgHeight.Value;
                imageGen.ImageFormat = cmbImgFormat.SelectedItem?.ToString() ?? "png";
                cfg.ImageGeneration = imageGen;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                v.ResolutionMode = cmbResolution.SelectedItem?.ToString() ?? "Mode1080p";
                v.FrameRate = (int)numFps.Value;
                v.VideoCodec = txtCodec.Text?.Trim();
                v.VideoBitrate = txtBitrate.Text?.Trim();
                v.Container = cmbContainer.SelectedItem?.ToString() ?? "mp4";
                v.OutputDirectory = ToRelative(txtVideoOutputDir.Text, imageGen.OutputDirectory ?? "WeatherImages");
                v.VerboseFfmpeg = chkVerbose.Checked;
                v.ShowFfmpegOutputInGui = chkShowFfmpeg.Checked;
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

        private string ToRelative(string? path, string fallback)
        {
            var outDir = string.IsNullOrWhiteSpace(path) ? fallback : path!;
            var cwd = Directory.GetCurrentDirectory();
            if (outDir.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
            {
                outDir = outDir.Substring(cwd.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return outDir;
        }
    }
}
