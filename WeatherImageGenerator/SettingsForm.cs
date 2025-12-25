using System;
using System.IO;
using System.Threading.Tasks;
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
        CheckBox chkVideoGeneration;
        CheckBox chkVerbose;
        CheckBox chkShowFfmpeg;
        CheckBox chkEnableHardwareEncoding; // New: toggle NVENC / hardware encoding
        Label lblHwStatus;
        Label lblFfmpegInstalled;
        Button btnCheckHw;
        public SettingsForm()
        {
            this.Text = "Settings";
            this.Width = 600;
            this.Height = 500;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var tabControl = new TabControl { Dock = DockStyle.Top, Height = 400 };
            
            // --- General Tab ---
            var tabGeneral = new TabPage("General");
            int gTop = 20;
            int rowH = 35;
            int leftLabel = 10;
            int leftField = 160;

            var lblRefresh = new Label { Text = "Refresh Interval (min):", Left = leftLabel, Top = gTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numRefresh = new NumericUpDown { Left = leftField, Top = gTop, Width = 80, Minimum = 1, Maximum = 1440, Value = 10 };
            
            gTop += rowH;
            var lblOutImg = new Label { Text = "Image Output Dir:", Left = leftLabel, Top = gTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtImageOutputDir = new TextBox { Left = leftField, Top = gTop, Width = 300 };
            var btnBrowseImg = new Button { Text = "...", Left = 470, Top = gTop - 1, Width = 40, Height = 23 };
            btnBrowseImg.Click += (s, e) => BrowseClicked(txtImageOutputDir);

            gTop += rowH;
            var lblOutVid = new Label { Text = "Video Output Dir:", Left = leftLabel, Top = gTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtVideoOutputDir = new TextBox { Left = leftField, Top = gTop, Width = 300 };
            var btnBrowseVid = new Button { Text = "...", Left = 470, Top = gTop - 1, Width = 40, Height = 23 };
            btnBrowseVid.Click += (s, e) => BrowseClicked(txtVideoOutputDir);

            tabGeneral.Controls.AddRange(new Control[] { lblRefresh, numRefresh, lblOutImg, txtImageOutputDir, btnBrowseImg, lblOutVid, txtVideoOutputDir, btnBrowseVid });

            // --- Image Tab ---
            var tabImage = new TabPage("Image");
            int iTop = 20;

            var lblImgSize = new Label { Text = "Resolution (WxH):", Left = leftLabel, Top = iTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numImgWidth = new NumericUpDown { Left = leftField, Top = iTop, Width = 80, Minimum = 320, Maximum = 7680, Value = 1920, Increment = 10 };
            var lblX = new Label { Text = "x", Left = leftField + 85, Top = iTop, Width = 15, AutoSize = true };
            numImgHeight = new NumericUpDown { Left = leftField + 105, Top = iTop, Width = 80, Minimum = 240, Maximum = 4320, Value = 1080, Increment = 10 };
            
            iTop += rowH;
            var lblFormat = new Label { Text = "Format:", Left = leftLabel, Top = iTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbImgFormat = new ComboBox { Left = leftField, Top = iTop, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbImgFormat.Items.AddRange(new object[] { "png", "jpeg", "bmp", "gif" });
            cmbImgFormat.SelectedIndex = 0;

            tabImage.Controls.AddRange(new Control[] { lblImgSize, numImgWidth, lblX, numImgHeight, lblFormat, cmbImgFormat });

            // --- Video Tab ---
            var tabVideo = new TabPage("Video");
            int vTop = 20;

            chkVideoGeneration = new CheckBox { Text = "Enable Video Generation", Left = leftLabel, Top = vTop, Width = 200 };
            lblFfmpegInstalled = new Label { Text = "Checking FFmpeg...", Left = leftLabel + 210, Top = vTop + 4, Width = 300, AutoSize = true, ForeColor = System.Drawing.Color.Gray };
            
            vTop += rowH;
            var lblStatic = new Label { Text = "Static Duration (s):", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numStatic = new NumericUpDown { Left = leftField, Top = vTop, Width = 80, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };

            vTop += rowH;
            var lblFade = new Label { Text = "Fade Duration (s):", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numFade = new NumericUpDown { Left = leftField, Top = vTop, Width = 80, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M };
            chkFade = new CheckBox { Text = "Enable Transitions", Left = leftField + 100, Top = vTop, Width = 150 };

            vTop += rowH;
            var lblResPreset = new Label { Text = "Resolution Preset:", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbResolution = new ComboBox { Left = leftField, Top = vTop, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbResolution.Items.AddRange(Enum.GetNames(typeof(ResolutionMode)));
            cmbResolution.SelectedIndex = 0;

            vTop += rowH;
            var lblFps = new Label { Text = "FPS:", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numFps = new NumericUpDown { Left = leftField, Top = vTop, Width = 80, Minimum = 1, Maximum = 240, Value = 30 };

            vTop += rowH;
            var lblContainer = new Label { Text = "Container:", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbContainer = new ComboBox { Left = leftField, Top = vTop, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;

            vTop += rowH;
            var lblCodec = new Label { Text = "Codec / Bitrate:", Left = leftLabel, Top = vTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtCodec = new TextBox { Left = leftField, Top = vTop, Width = 80, Text = "libx264" };
            txtBitrate = new TextBox { Left = leftField + 90, Top = vTop, Width = 60, Text = "4M" };

            vTop += rowH;
            chkEnableHardwareEncoding = new CheckBox { Text = "Hardware Encoding (NVENC/AMF/QSV)", Left = leftLabel, Top = vTop, Width = 300 };
            btnCheckHw = new Button { Text = "Check", Left = leftLabel + 310, Top = vTop - 2, Width = 60, Height = 24 };

            vTop += rowH;
            lblHwStatus = new Label { Text = "Unknown", Left = leftLabel + 20, Top = vTop, Width = 500, ForeColor = System.Drawing.Color.Gray, AutoSize = true };
            
            btnCheckHw.Click += (s, e) =>
            {
                btnCheckHw.Enabled = false;
                lblHwStatus.Text = "Checking...";
                Task.Run(() =>
                {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    this.Invoke((Action)(() =>
                    {
                        lblHwStatus.Text = msg;
                        lblHwStatus.ForeColor = ok ? System.Drawing.Color.Green : System.Drawing.Color.Red;
                        btnCheckHw.Enabled = true;

                        if (!ok)
                        {
                            chkEnableHardwareEncoding.Checked = false;
                            chkEnableHardwareEncoding.Enabled = false;
                        }
                        else
                        {
                            chkEnableHardwareEncoding.Enabled = true;
                        }
                    }));
                });
            };

            vTop += rowH;
            chkVerbose = new CheckBox { Text = "Verbose FFmpeg", Left = leftLabel, Top = vTop, Width = 130 };
            chkShowFfmpeg = new CheckBox { Text = "Show FFmpeg GUI", Left = leftLabel + 140, Top = vTop, Width = 150 };

            tabVideo.Controls.AddRange(new Control[] { 
                chkVideoGeneration, lblFfmpegInstalled,
                lblStatic, numStatic, 
                lblFade, numFade, chkFade, 
                lblResPreset, cmbResolution, 
                lblFps, numFps, 
                lblContainer, cmbContainer, 
                lblCodec, txtCodec, txtBitrate, 
                chkEnableHardwareEncoding, lblHwStatus, btnCheckHw,
                chkVerbose, chkShowFfmpeg 
            });

            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabImage);
            tabControl.TabPages.Add(tabVideo);

            var btnSave = new Button { Text = "Save", Left = 380, Top = 420, Width = 90, Height = 30 };
            var btnCancel = new Button { Text = "Cancel", Left = 480, Top = 420, Width = 90, Height = 30 };

            btnSave.Click += (s, e) => SaveClicked();
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.Add(tabControl);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

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
                chkVideoGeneration.Checked = cfg.Video?.doVideoGeneration ?? true;
                chkVerbose.Checked = cfg.Video?.VerboseFfmpeg ?? false;
                chkShowFfmpeg.Checked = cfg.Video?.ShowFfmpegOutputInGui ?? true;
                chkEnableHardwareEncoding.Checked = cfg.Video?.EnableHardwareEncoding ?? false;

                // Check hardware encoder availability asynchronously and display the result
                Task.Run(() =>
                {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    this.Invoke((Action)(() =>
                    {
                        lblHwStatus.Text = msg;
                        lblHwStatus.ForeColor = ok ? System.Drawing.Color.Green : System.Drawing.Color.Red;

                        if (!ok)
                        {
                            chkEnableHardwareEncoding.Checked = false;
                            chkEnableHardwareEncoding.Enabled = false;
                        }
                        else
                        {
                            chkEnableHardwareEncoding.Enabled = true;
                        }
                    }));
                });

                // Check FFmpeg availability
                Task.Run(() =>
                {
                    bool installed = VideoGenerator.IsFfmpegInstalled(out var version);
                    this.Invoke((Action)(() =>
                    {
                        if (installed)
                        {
                            lblFfmpegInstalled.Text = $"FFmpeg Installed ({version})";
                            lblFfmpegInstalled.ForeColor = System.Drawing.Color.Green;
                        }
                        else
                        {
                            lblFfmpegInstalled.Text = "FFmpeg NOT found in PATH";
                            lblFfmpegInstalled.ForeColor = System.Drawing.Color.Red;
                        }
                    }));
                });
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
                v.doVideoGeneration = chkVideoGeneration.Checked;
                v.VerboseFfmpeg = chkVerbose.Checked;
                v.ShowFfmpegOutputInGui = chkShowFfmpeg.Checked;
                // If enabling hardware encoding, verify ffmpeg supports NVENC and warn the user if it does not
                if (chkEnableHardwareEncoding.Checked)
                {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    if (!ok)
                    {
                        var res = MessageBox.Show(this, $"FFmpeg does not appear to support hardware encoding on this system. ({msg})\nEnabling hardware encoding may cause ffmpeg to fail. Continue enabling?", "Hardware Encoding Not Available", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (res == DialogResult.No)
                        {
                            chkEnableHardwareEncoding.Checked = false;
                        }
                    }
                }
                v.EnableHardwareEncoding = chkEnableHardwareEncoding.Checked;
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
