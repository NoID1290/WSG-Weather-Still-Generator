using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    public class SettingsForm : Form
    {
        private bool _isLoadingSettings = false; // Prevent event loops during load
        TextBox txtImageOutputDir;
        TextBox txtVideoOutputDir;
        NumericUpDown numStatic;
        NumericUpDown numFade;
        CheckBox chkFade;
        NumericUpDown numRefresh;
        ComboBox cmbTheme;
        NumericUpDown numImgWidth;
        NumericUpDown numImgHeight;
        ComboBox cmbImgFormat;
        ComboBox cmbResolution;
        CheckBox chkEnableProvinceRadar;
        CheckBox chkEnableWeatherMaps;
        ComboBox cmbCodec;  // Changed from TextBox to ComboBox
        ComboBox cmbBitrate; // Changed from TextBox to ComboBox
        ComboBox cmbQualityPreset; // New quality preset selector
        NumericUpDown numFps;
        ComboBox cmbContainer;
        CheckBox chkVideoGeneration;
        CheckBox chkVerbose;
        CheckBox chkShowFfmpeg;
        CheckBox chkEnableHardwareEncoding; // New: toggle NVENC / hardware encoding
        CheckBox chkMinimizeToTray; // Enable minimize to system tray
        CheckBox chkMinimizeToTrayOnClose; // Minimize to tray when closing
        Label lblHwStatus;
        Label lblFfmpegInstalled;
        Button btnCheckHw;
        public SettingsForm()
        {
            this.Text = "Settings";
            this.Width = 700;
            this.Height = 600;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var tabControl = new TabControl { Dock = DockStyle.Top, Height = 600 };
            
            // --- General Tab ---
            var tabGeneral = new TabPage("General");
            int gTop = 20;
            int rowH = 35;
            int leftLabel = 10;
            int leftField = 160;

            var lblRefresh = new Label { Text = "Refresh Interval (min):", Left = leftLabel, Top = gTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numRefresh = new NumericUpDown { Left = leftField, Top = gTop, Width = 80, Minimum = 1, Maximum = 1440, Value = 10 };
            
            gTop += rowH;
            var lblTheme = new Label { Text = "Theme:", Left = leftLabel, Top = gTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbTheme = new ComboBox { Left = leftField, Top = gTop, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTheme.Items.AddRange(new object[] { "Blue", "Light", "Dark", "Green" });
            cmbTheme.SelectedIndex = 0;

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

            gTop += rowH;
            chkMinimizeToTray = new CheckBox { Text = "Minimize to system tray", Left = leftLabel, Top = gTop, Width = 300 };

            gTop += rowH;
            chkMinimizeToTrayOnClose = new CheckBox { Text = "Minimize to tray on close (X button)", Left = leftLabel, Top = gTop, Width = 300 };

            tabGeneral.Controls.AddRange(new Control[] { lblRefresh, numRefresh, lblTheme, cmbTheme, lblOutImg, txtImageOutputDir, btnBrowseImg, lblOutVid, txtVideoOutputDir, btnBrowseVid, chkMinimizeToTray, chkMinimizeToTrayOnClose });

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

            iTop += rowH;
            chkEnableProvinceRadar = new CheckBox { Text = "Enable Province Radar Animation", Left = leftLabel, Top = iTop, Width = 250 };

            iTop += rowH;
            chkEnableWeatherMaps = new CheckBox { Text = "Enable Weather Maps Generation", Left = leftLabel, Top = iTop, Width = 250 };

            iTop += rowH;
            var btnRegenIcons = new Button { Text = "Regenerate Icons", Left = leftLabel, Top = iTop, Width = 150, Height = 25 };
            btnRegenIcons.Click += (s, e) =>
            {
                try
                {
                    btnRegenIcons.Enabled = false;
                    btnRegenIcons.Text = "Generating...";
                    string outDir = txtImageOutputDir.Text;
                    if (string.IsNullOrWhiteSpace(outDir)) outDir = Path.Combine(Directory.GetCurrentDirectory(), "WeatherImages");
                    
                    string iconsDir = Path.Combine(outDir, "Icons");
                    IconGenerator.GenerateAll(iconsDir);
                    MessageBox.Show($"Icons regenerated successfully in {iconsDir}!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error regenerating icons: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnRegenIcons.Enabled = true;
                    btnRegenIcons.Text = "Regenerate Icons";
                }
            };

            tabImage.Controls.AddRange(new Control[] { lblImgSize, numImgWidth, lblX, numImgHeight, lblFormat, cmbImgFormat, chkEnableProvinceRadar, chkEnableWeatherMaps, btnRegenIcons });

            // --- Video Tab ---
            var tabVideo = new TabPage("Video");
            int vTop = 20;
            int rightCol = 310; // Right column position

            // General video settings group
            chkVideoGeneration = new CheckBox { Text = "Enable Video Generation", Left = leftLabel, Top = vTop, Width = 200 };
            lblFfmpegInstalled = new Label { Text = "Checking FFmpeg...", Left = leftLabel, Top = vTop + rowH, Width = 500, AutoSize = true, ForeColor = System.Drawing.Color.Gray };
            
            vTop += rowH + rowH;
            var lblDivider1 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = vTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };
            
            // LEFT COLUMN - Timing Settings
            vTop += 20;
            var lblTimingGroup = new Label { Text = "⏱ Timing Settings", Left = leftLabel, Top = vTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            vTop += 25;
            var lblStatic = new Label { Text = "Static Duration (s):", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numStatic = new NumericUpDown { Left = leftLabel + 135, Top = vTop, Width = 70, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };
            var lblStaticHelp = new Label { Text = "Slide duration", Left = leftLabel + 210, Top = vTop + 3, Width = 90, AutoSize = true, ForeColor = System.Drawing.Color.Gray, Font = new System.Drawing.Font(this.Font.FontFamily, 7.5f) };

            vTop += rowH;
            var lblFade = new Label { Text = "Fade Duration (s):", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numFade = new NumericUpDown { Left = leftLabel + 135, Top = vTop, Width = 70, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M };
            chkFade = new CheckBox { Text = "Enable", Left = leftLabel + 210, Top = vTop, Width = 70 };

            // RIGHT COLUMN - Video Format
            int rTop = 125; // Start position for right column
            var lblVideoFormat = new Label { Text = "📹 Video Format", Left = rightCol, Top = rTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            rTop += 25;
            var lblQualityPreset = new Label { Text = "Quality Preset:", Left = rightCol, Top = rTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbQualityPreset = new ComboBox { Left = rightCol + 105, Top = rTop, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbQualityPreset.Items.AddRange(new object[] { 
                "Ultra (Best Quality)", 
                "High Quality", 
                "Balanced", 
                "Web Optimized", 
                "Low Bandwidth",
                "Custom" 
            });
            cmbQualityPreset.SelectedIndex = 2; // Default to Balanced

            rTop += rowH;
            var lblResPreset = new Label { Text = "Resolution:", Left = rightCol, Top = rTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbResolution = new ComboBox { Left = rightCol + 105, Top = rTop, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbResolution.Items.AddRange(new object[] { 
                "3840x2160 (4K/UHD)",
                "2560x1440 (2K/QHD)",
                "1920x1080 (Full HD)",
                "1600x900 (HD+)",
                "1280x720 (HD)",
                "960x540 (qHD)",
                "854x480 (FWVGA)",
                "640x480 (VGA)"
            });
            cmbResolution.SelectedIndex = 2; // Default to 1080p

            rTop += rowH;
            var lblFps = new Label { Text = "Frame Rate:", Left = rightCol, Top = rTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numFps = new NumericUpDown { Left = rightCol + 105, Top = rTop, Width = 60, Minimum = 1, Maximum = 240, Value = 30 };
            var lblFpsHelp = new Label { Text = "fps", Left = rightCol + 170, Top = rTop + 3, Width = 30, AutoSize = true, ForeColor = System.Drawing.Color.Gray, Font = new System.Drawing.Font(this.Font.FontFamily, 7.5f) };

            rTop += rowH;
            var lblContainer = new Label { Text = "Container:", Left = rightCol, Top = rTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbContainer = new ComboBox { Left = rightCol + 105, Top = rTop, Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;

            // LEFT COLUMN continues - Encoding Settings
            vTop += rowH + 10;
            var lblDivider2 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = vTop, Width = 280, Height = 15, ForeColor = System.Drawing.Color.LightGray };
            
            vTop += 20;
            var lblEncodingGroup = new Label { Text = "🎬 Encoding", Left = leftLabel, Top = vTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            vTop += 25;
            var lblCodec = new Label { Text = "Codec:", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbCodec = new ComboBox { Left = leftLabel + 135, Top = vTop, Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCodec.Items.AddRange(new object[] { 
                "libx264 (H.264)",
                "libx265 (H.265/HEVC)",
                "libvpx-vp9 (VP9)",
                "libaom-av1 (AV1)",
                "mpeg4",
                "msmpeg4"
            });
            cmbCodec.SelectedIndex = 0;

            vTop += rowH;
            var lblBitrate = new Label { Text = "Bitrate:", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbBitrate = new ComboBox { Left = leftLabel + 135, Top = vTop, Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbBitrate.Items.AddRange(new object[] { 
                "1M (Low)",
                "2M (Medium-Low)",
                "4M (Medium)",
                "6M (Medium-High)",
                "8M (High)",
                "12M (Very High)",
                "16M (Ultra)"
            });
            cmbBitrate.SelectedIndex = 2; // Default to 4M

            vTop += rowH;
            chkEnableHardwareEncoding = new CheckBox { Text = "⚡ Hardware Encoding", Left = leftLabel, Top = vTop, Width = 180 };
            btnCheckHw = new Button { Text = "Check", Left = leftLabel + 185, Top = vTop - 2, Width = 60, Height = 24 };

            vTop += rowH;
            lblHwStatus = new Label { Text = "Unknown", Left = leftLabel + 20, Top = vTop, Width = 260, ForeColor = System.Drawing.Color.Gray, AutoSize = true };


            
            // Quality preset change handler
            cmbQualityPreset.SelectedIndexChanged += (s, e) =>
            {
                if (_isLoadingSettings || cmbQualityPreset.SelectedIndex == 5) return; // Custom - don't change anything
                
                // Temporarily disable the markCustom handlers
                _isLoadingSettings = true;
                
                // Apply preset values
                switch (cmbQualityPreset.SelectedIndex)
                {
                    case 0: // Ultra
                        cmbResolution.SelectedIndex = 0; // 4K
                        cmbCodec.SelectedIndex = 0; // H.264
                        cmbBitrate.SelectedIndex = 6; // 16M
                        numFps.Value = 60;
                        break;
                    case 1: // High Quality
                        cmbResolution.SelectedIndex = 2; // 1080p
                        cmbCodec.SelectedIndex = 0; // H.264
                        cmbBitrate.SelectedIndex = 4; // 8M
                        numFps.Value = 30;
                        break;
                    case 2: // Balanced
                        cmbResolution.SelectedIndex = 2; // 1080p
                        cmbCodec.SelectedIndex = 0; // H.264
                        cmbBitrate.SelectedIndex = 2; // 4M
                        numFps.Value = 30;
                        break;
                    case 3: // Web Optimized
                        cmbResolution.SelectedIndex = 4; // 720p
                        cmbCodec.SelectedIndex = 0; // H.264
                        cmbBitrate.SelectedIndex = 1; // 2M
                        numFps.Value = 30;
                        break;
                    case 4: // Low Bandwidth
                        cmbResolution.SelectedIndex = 6; // 480p
                        cmbCodec.SelectedIndex = 0; // H.264
                        cmbBitrate.SelectedIndex = 0; // 1M
                        numFps.Value = 24;
                        break;
                }
                
                _isLoadingSettings = false;
            };
            
            // Mark as custom when user manually changes settings
            EventHandler markCustom = (s, e) => 
            { 
                if (!_isLoadingSettings && cmbQualityPreset.SelectedIndex != 5) 
                    cmbQualityPreset.SelectedIndex = 5; 
            };
            cmbResolution.SelectedIndexChanged += markCustom;
            cmbCodec.SelectedIndexChanged += markCustom;
            cmbBitrate.SelectedIndexChanged += markCustom;
            numFps.ValueChanged += markCustom;
            
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
            var lblDivider4 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = vTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };
            
            vTop += 20;
            var lblDebugGroup2 = new Label { Text = "🔧 Debug Options", Left = leftLabel, Top = vTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            vTop += 25;
            chkVerbose = new CheckBox { Text = "Verbose FFmpeg Output", Left = leftLabel, Top = vTop, Width = 180 };
            chkShowFfmpeg = new CheckBox { Text = "Show FFmpeg Console", Left = leftLabel + 190, Top = vTop, Width = 180 };

            tabVideo.Controls.AddRange(new Control[] { 
                chkVideoGeneration, lblFfmpegInstalled, lblDivider1,
                // Left column
                lblTimingGroup, lblStatic, numStatic, lblStaticHelp,
                lblFade, numFade, chkFade, lblDivider2,
                lblEncodingGroup, lblCodec, cmbCodec,
                lblBitrate, cmbBitrate,
                chkEnableHardwareEncoding, lblHwStatus, btnCheckHw,
                // Right column
                lblVideoFormat, lblQualityPreset, cmbQualityPreset,
                lblResPreset, cmbResolution, 
                lblFps, numFps, lblFpsHelp,
                lblContainer, cmbContainer,
                // Debug section
                lblDivider4, lblDebugGroup2, chkVerbose, chkShowFfmpeg
            });

            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabImage);
            tabControl.TabPages.Add(tabVideo);

            var btnSave = new Button { Text = "Save", Left = 380, Top = 620, Width = 90, Height = 30 };
            var btnCancel = new Button { Text = "Cancel", Left = 480, Top = 620, Width = 90, Height = 30 };

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
            _isLoadingSettings = true; // Prevent event handlers from firing
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
                chkEnableProvinceRadar.Checked = cfg.ECCC?.EnableProvinceRadar ?? true;
                chkEnableWeatherMaps.Checked = cfg.ImageGeneration?.EnableWeatherMaps ?? true;

                var theme = cfg.Theme ?? "Blue";
                if (cmbTheme.Items.Contains(theme)) cmbTheme.SelectedItem = theme;
                else cmbTheme.SelectedItem = "Blue";

                chkMinimizeToTray.Checked = cfg.MinimizeToTray;
                chkMinimizeToTrayOnClose.Checked = cfg.MinimizeToTrayOnClose; 

                numStatic.Value = (decimal)(cfg.Video?.StaticDurationSeconds ?? 8);
                numFade.Value = (decimal)(cfg.Video?.FadeDurationSeconds ?? 0.5);
                chkFade.Checked = cfg.Video?.EnableFadeTransitions ?? false;
                
                // Map old ResolutionMode enum values to new display format
                var resMode = cfg.Video?.ResolutionMode ?? "Mode1080p";
                var resDisplay = resMode switch
                {
                    "Mode1080p" => "1920x1080 (Full HD)",
                    "Mode4K" => "3840x2160 (4K/UHD)",
                    "ModeVertical" => "1920x1080 (Full HD)", // Map old vertical to 1080p
                    _ => "1920x1080 (Full HD)"
                };
                if (cmbResolution.Items.Contains(resDisplay)) cmbResolution.SelectedItem = resDisplay;
                else cmbResolution.SelectedIndex = 2; // Default to 1080p
                
                numFps.Value = cfg.Video?.FrameRate ?? 30;
                
                // Load codec - map from config value to display value
                var codec = cfg.Video?.VideoCodec ?? "libx264";
                var codecDisplay = codec switch
                {
                    "libx264" => "libx264 (H.264)",
                    "libx265" => "libx265 (H.265/HEVC)",
                    "libvpx-vp9" => "libvpx-vp9 (VP9)",
                    "libaom-av1" => "libaom-av1 (AV1)",
                    "mpeg4" => "mpeg4",
                    "msmpeg4" => "msmpeg4",
                    _ => "libx264 (H.264)"
                };
                if (cmbCodec.Items.Contains(codecDisplay)) cmbCodec.SelectedItem = codecDisplay;
                else cmbCodec.SelectedIndex = 0;
                
                // Load bitrate - map from config value to display value
                var bitrate = cfg.Video?.VideoBitrate ?? "4M";
                var bitrateDisplay = bitrate.ToUpper() switch
                {
                    "1M" => "1M (Low)",
                    "2M" => "2M (Medium-Low)",
                    "4M" => "4M (Medium)",
                    "6M" => "6M (Medium-High)",
                    "8M" => "8M (High)",
                    "12M" => "12M (Very High)",
                    "16M" => "16M (Ultra)",
                    _ => "4M (Medium)"
                };
                if (cmbBitrate.Items.Contains(bitrateDisplay)) cmbBitrate.SelectedItem = bitrateDisplay;
                else cmbBitrate.SelectedIndex = 2;
                
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
            finally
            {
                _isLoadingSettings = false; // Re-enable event handlers
            }
        }

        private void SaveClicked()
        {
            try
            {
                var cfg = ConfigManager.LoadConfig();
                var oldTheme = cfg.Theme;
                cfg.RefreshTimeMinutes = (int)numRefresh.Value; 

                var imageGen = cfg.ImageGeneration ?? new ImageGenerationSettings();
                imageGen.OutputDirectory = ToRelative(txtImageOutputDir.Text, "WeatherImages");
                imageGen.ImageWidth = (int)numImgWidth.Value;
                imageGen.ImageHeight = (int)numImgHeight.Value;
                imageGen.ImageFormat = cmbImgFormat.SelectedItem?.ToString() ?? "png";
                imageGen.EnableWeatherMaps = chkEnableWeatherMaps.Checked;
                cfg.ImageGeneration = imageGen;

                var eccc = cfg.ECCC ?? new ECCCSettings();
                eccc.EnableProvinceRadar = chkEnableProvinceRadar.Checked;
                cfg.ECCC = eccc;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                
                // Extract resolution mode from display text and map to enum value
                var resDisplay = cmbResolution.SelectedItem?.ToString() ?? "1920x1080 (Full HD)";
                v.ResolutionMode = resDisplay switch
                {
                    "3840x2160 (4K/UHD)" => "Mode4K",
                    "2560x1440 (2K/QHD)" => "Mode1080p", // Use 1080p mode
                    "1920x1080 (Full HD)" => "Mode1080p",
                    "1600x900 (HD+)" => "Mode1080p",
                    "1280x720 (HD)" => "Mode1080p",
                    "960x540 (qHD)" => "Mode1080p",
                    "854x480 (FWVGA)" => "Mode1080p",
                    "640x480 (VGA)" => "Mode1080p",
                    _ => "Mode1080p"
                };
                
                v.FrameRate = (int)numFps.Value;
                
                // Extract codec value from display text
                var codecDisplay = cmbCodec.SelectedItem?.ToString() ?? "libx264 (H.264)";
                v.VideoCodec = codecDisplay switch
                {
                    "libx264 (H.264)" => "libx264",
                    "libx265 (H.265/HEVC)" => "libx265",
                    "libvpx-vp9 (VP9)" => "libvpx-vp9",
                    "libaom-av1 (AV1)" => "libaom-av1",
                    "mpeg4" => "mpeg4",
                    "msmpeg4" => "msmpeg4",
                    _ => "libx264"
                };
                
                // Extract bitrate value from display text (e.g., "4M (Medium)" -> "4M")
                var bitrateDisplay = cmbBitrate.SelectedItem?.ToString() ?? "4M (Medium)";
                v.VideoBitrate = bitrateDisplay.Split(' ')[0]; // Extract just the "4M" part
                
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

                // Persist theme choice
                cfg.Theme = cmbTheme.SelectedItem?.ToString() ?? "Blue";

                // Persist minimize to tray settings
                cfg.MinimizeToTray = chkMinimizeToTray.Checked;
                cfg.MinimizeToTrayOnClose = chkMinimizeToTrayOnClose.Checked;

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
