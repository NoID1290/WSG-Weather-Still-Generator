using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Models;
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
        CheckBox chkUseTotalDuration;
        NumericUpDown numTotalDuration;
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
        CheckBox chkEnableExperimental; // Opt-in for experimental features
        TabPage tabExperimental; // Experimental tab container
        // New video encoding controls
        CheckBox chkUseCrfEncoding;
        NumericUpDown numCrf;
        ComboBox cmbEncoderPreset;
        System.Windows.Forms.TextBox txtMaxBitrate;
        System.Windows.Forms.TextBox txtBufferSize;
        // End new controls
        // FFmpeg source controls
        ComboBox cmbFfmpegSource;
        TextBox txtFfmpegCustomPath;
        Button btnBrowseFfmpegPath;
        Label lblFfmpegStatus;
        Button btnValidateFfmpeg;
        Button btnClearFfmpegCache;
        Button btnDownloadBundled;
        // End FFmpeg source controls
        CheckBox chkMinimizeToTray; // Enable minimize to system tray
        CheckBox chkMinimizeToTrayOnClose; // Minimize to tray when closing
        CheckBox chkAutoStartCycle; // Auto-start update cycle on application start
        Label lblHwStatus;
        Button btnCheckHw;
        // EAS/AlertReady controls
        CheckBox chkAlertReadyEnabled;
        TextBox txtAlertReadyFeedUrls;
        CheckBox chkAlertReadyIncludeTests;
        NumericUpDown numAlertReadyMaxAgeHours;
        ComboBox cmbAlertReadyLanguage;
        TextBox txtAlertReadyAreaFilters;
        TextBox txtAlertReadyJurisdictions;
        CheckBox chkAlertReadyHighRiskOnly;
        CheckBox chkAlertReadyExcludeWeather;
        // EdgeTTS controls
        ComboBox cmbTtsVoice;
        TextBox txtTtsRate;
        TextBox txtTtsPitch;
        public SettingsForm()
        {
            this.Text = "⚙ Settings";
            this.Width = 750;
            this.Height = 720;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(245, 245, 250);
            this.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            var tabControl = new TabControl 
            { 
                Dock = DockStyle.Top, 
                Height = 600,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            
            // --- General Tab ---
            var tabGeneral = new TabPage("⚙ General") { BackColor = Color.White };
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

            gTop += rowH;
            chkAutoStartCycle = new CheckBox { Text = "Auto start update cycle on application start", Left = leftLabel, Top = gTop, Width = 420 };

            tabGeneral.Controls.AddRange(new Control[] { lblRefresh, numRefresh, lblTheme, cmbTheme, lblOutImg, txtImageOutputDir, btnBrowseImg, lblOutVid, txtVideoOutputDir, btnBrowseVid, chkMinimizeToTray, chkMinimizeToTrayOnClose, chkAutoStartCycle });

            // --- Image Tab ---
            var tabImage = new TabPage("🖼 Image") { BackColor = Color.White };
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
            chkEnableProvinceRadar = new CheckBox { Text = "Enable Province Radar Animation", Left = leftLabel, Top = iTop, Width = 250, Enabled = true };

            iTop += rowH;
            chkEnableWeatherMaps = new CheckBox { Text = "Enable Weather Maps Generation", Left = leftLabel, Top = iTop, Width = 250, Enabled = true };

            iTop += rowH;
            var lblRadarBroken = new Label { Text = "Note: Radar options are currently broken", Left = leftLabel + 10, Top = iTop, Width = 400, ForeColor = System.Drawing.Color.Red, AutoSize = false };

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

            tabImage.Controls.AddRange(new Control[] { lblImgSize, numImgWidth, lblX, numImgHeight, lblFormat, cmbImgFormat, chkEnableProvinceRadar, chkEnableWeatherMaps, lblRadarBroken, btnRegenIcons });

            // --- Video Tab ---
            var tabVideo = new TabPage("🎥 Video") { BackColor = Color.White };
            int vTop = 20;
            int rightCol = 310; // Right column position

            // General video settings group
            chkVideoGeneration = new CheckBox { Text = "Enable Video Generation", Left = leftLabel, Top = vTop, Width = 200 };
            
            vTop += rowH;
            var lblDivider1 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = vTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };
            
            // LEFT COLUMN - Timing Settings
            vTop += 20;
            var lblTimingGroup = new Label { Text = "⏱ Timing Settings", Left = leftLabel, Top = vTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            vTop += 25;
            var lblStatic = new Label { Text = "Static Duration (s):", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numStatic = new NumericUpDown { Left = leftLabel + 135, Top = vTop, Width = 70, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };
            var lblStaticHelp = new Label { Text = "Slide duration", Left = leftLabel + 210, Top = vTop + 3, Width = 90, AutoSize = true, ForeColor = System.Drawing.Color.Gray, Font = new System.Drawing.Font(this.Font.FontFamily, 7.5f) };

            // Total duration mode controls (allow user to enforce an overall total video time)
            vTop += rowH;
            var lblTotal = new Label { Text = "Total Video Duration (s):", Left = leftLabel, Top = vTop, Width = 150, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numTotalDuration = new NumericUpDown { Left = leftLabel + 155, Top = vTop, Width = 80, Minimum = 1, Maximum = 86400, DecimalPlaces = 1, Increment = 1, Value = 60 };
            // Place checkbox directly under Total (left column) and reserve a full row of spacing so following controls do not overlap
            chkUseTotalDuration = new CheckBox { Text = "Enforce total duration", Left = leftLabel + 10, Top = vTop + rowH, Width = 200 };
            chkUseTotalDuration.CheckedChanged += (s, e) => { numTotalDuration.Enabled = chkUseTotalDuration.Checked; numStatic.Enabled = !chkUseTotalDuration.Checked; };
            numTotalDuration.Enabled = false; // disabled by default

            // Reserve space for the checkbox (advance layout cursor by two rows)
            vTop += (rowH * 2);
            var lblFade = new Label { Text = "Fade Duration (s):", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numFade = new NumericUpDown { Left = leftLabel + 135, Top = vTop, Width = 70, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M };
            chkFade = new CheckBox { Text = "Enable", Left = leftLabel + 210, Top = vTop, Width = 70 };

            lblFade.Enabled = false; // Disable until xfade is fixed
            numFade.Enabled = false; // Disable until xfade is fixed


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

            vTop += rowH;
            var lblCrf = new Label { Text = "CRF:", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            chkUseCrfEncoding = new CheckBox { Text = "Use CRF encoding (quality-based)", Left = leftLabel + 135, Top = vTop, Width = 220 };
            numCrf = new NumericUpDown { Left = leftLabel + 360, Top = vTop, Width = 80, Minimum = 0, Maximum = 51, Value = 23 };

            vTop += rowH;
            var lblEncoderPreset = new Label { Text = "Encoder Preset:", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbEncoderPreset = new ComboBox { Left = leftLabel + 135, Top = vTop, Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEncoderPreset.Items.AddRange(new object[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });
            cmbEncoderPreset.SelectedIndex = 5; // medium

            vTop += rowH;
            var lblMaxRate = new Label { Text = "Max Bitrate:", Left = leftLabel, Top = vTop, Width = 130, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtMaxBitrate = new TextBox { Left = leftLabel + 135, Top = vTop, Width = 145 };
            var lblBuf = new Label { Text = "Buffer Size:", Left = leftLabel + 290, Top = vTop, Width = 90, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtBufferSize = new TextBox { Left = leftLabel + 380, Top = vTop, Width = 90 };

            
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
            // New controls should mark preset as custom when changed
            cmbEncoderPreset.SelectedIndexChanged += markCustom;
            txtMaxBitrate.TextChanged += (s, e) => { if (!_isLoadingSettings && cmbQualityPreset.SelectedIndex != 5) cmbQualityPreset.SelectedIndex = 5; };
            txtBufferSize.TextChanged += (s, e) => { if (!_isLoadingSettings && cmbQualityPreset.SelectedIndex != 5) cmbQualityPreset.SelectedIndex = 5; };
            chkUseCrfEncoding.CheckedChanged += markCustom;
            numCrf.ValueChanged += markCustom;
            
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
            // Experimental opt-in checkbox (controls the Experimental tab)
            vTop += rowH;
            chkEnableExperimental = new CheckBox { Text = "Enable Experimental Features", Left = leftLabel, Top = vTop, Width = 300 };
            chkEnableExperimental.CheckedChanged += (s, e) => { if (tabExperimental != null) tabExperimental.Enabled = chkEnableExperimental.Checked; };

            vTop += 5;
            var lblDebugGroup2 = new Label { Text = "🔧 Debug Options", Left = leftLabel, Top = vTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            vTop += 25;
            chkVerbose = new CheckBox { Text = "Verbose FFmpeg Output", Left = leftLabel, Top = vTop, Width = 180 };
            chkShowFfmpeg = new CheckBox { Text = "Show FFmpeg Console", Left = leftLabel + 190, Top = vTop, Width = 180 };

            tabVideo.Controls.AddRange(new Control[] { 
                chkVideoGeneration, lblDivider1,
                // Left column
                lblTimingGroup, lblStatic, numStatic, lblStaticHelp, lblTotal, numTotalDuration, chkUseTotalDuration,
                lblFade, numFade, chkFade, lblDivider2,
                lblEncodingGroup, lblCodec, cmbCodec,
                lblBitrate, cmbBitrate,
                chkEnableHardwareEncoding, lblHwStatus, btnCheckHw,
                // Add experimental toggle here so user can opt-in
                chkEnableExperimental,
                // Right column
                lblVideoFormat, lblQualityPreset, cmbQualityPreset,
                lblResPreset, cmbResolution, 
                lblFps, numFps, lblFpsHelp,
                lblContainer, cmbContainer,
                // Debug section
                lblDivider4, lblDebugGroup2, chkVerbose, chkShowFfmpeg
            });

            // --- Experimental Tab (moved from Video tab) ---
            tabExperimental = new TabPage("⚠ Experimental") { BackColor = Color.White };
            var lblExpNote = new Label { Text = "⚠ Experimental options — disable for now", Left = 10, Top = 20, Width = 520, ForeColor = System.Drawing.Color.OrangeRed, AutoSize = false };
            tabExperimental.Controls.AddRange(new Control[] {
                lblExpNote, lblCrf, chkUseCrfEncoding, numCrf, lblEncoderPreset, cmbEncoderPreset, lblMaxRate, txtMaxBitrate, lblBuf, txtBufferSize
            });
            tabExperimental.Enabled = false; // Disabled by default until user opts-in

            // --- FFmpeg Tab ---
            var tabFfmpeg = new TabPage("🎬 FFmpeg") { BackColor = Color.White };
            int fTop = 20;

            var lblFfmpegSource = new Label { Text = "🎬 FFmpeg Source", Left = leftLabel, Top = fTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            fTop += 30;
            var lblFfmpegSourceDesc = new Label { Text = "Choose where to get FFmpeg binaries from:", Left = leftLabel, Top = fTop, Width = 400, AutoSize = false };

            fTop += 25;
            var lblSource = new Label { Text = "Source:", Left = leftLabel, Top = fTop, Width = 80, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbFfmpegSource = new ComboBox { Left = leftField, Top = fTop, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFfmpegSource.Items.AddRange(new object[] { 
                "Bundled (Auto-download)", 
                "System PATH", 
                "Custom Path" 
            });
            cmbFfmpegSource.SelectedIndex = 0;

            fTop += rowH + 5;
            var lblCustomPath = new Label { Text = "Custom Path:", Left = leftLabel, Top = fTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtFfmpegCustomPath = new TextBox { Left = leftField, Top = fTop, Width = 350, Enabled = false };
            btnBrowseFfmpegPath = new Button { Text = "...", Left = leftField + 355, Top = fTop - 1, Width = 40, Height = 23, Enabled = false };
            btnBrowseFfmpegPath.Click += (s, e) => {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select FFmpeg directory (containing ffmpeg.exe)";
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        txtFfmpegCustomPath.Text = dlg.SelectedPath;
                    }
                }
            };

            // Enable/disable custom path based on source selection
            cmbFfmpegSource.SelectedIndexChanged += (s, e) => {
                bool isCustom = cmbFfmpegSource.SelectedIndex == 2;
                txtFfmpegCustomPath.Enabled = isCustom;
                btnBrowseFfmpegPath.Enabled = isCustom;
            };

            fTop += rowH + 10;
            var lblFfmpegDivider = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = fTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };

            fTop += 20;
            var lblFfmpegStatusGroup = new Label { Text = "📋 Status", Left = leftLabel, Top = fTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            fTop += 30;
            lblFfmpegStatus = new Label { Text = "Not validated", Left = leftLabel, Top = fTop, Width = 500, AutoSize = true, ForeColor = System.Drawing.Color.Gray };

            fTop += rowH;
            btnValidateFfmpeg = new Button { Text = "Validate FFmpeg", Left = leftLabel, Top = fTop, Width = 130, Height = 28 };
            btnValidateFfmpeg.Click += (s, e) => {
                ValidateFfmpegConfiguration();
            };

            btnDownloadBundled = new Button { Text = "Download Bundled", Left = leftLabel + 140, Top = fTop, Width = 130, Height = 28 };
            btnDownloadBundled.Click += async (s, e) => {
                btnDownloadBundled.Enabled = false;
                lblFfmpegStatus.Text = "Downloading FFmpeg binaries...";
                lblFfmpegStatus.ForeColor = System.Drawing.Color.Blue;
                
                try
                {
                    var progress = new Progress<float>(pct => 
                    {
                        if (this.IsHandleCreated)
                        {
                            this.Invoke((Action)(() => 
                            {
                                lblFfmpegStatus.Text = $"Downloading FFmpeg binaries... {pct:F0}%";
                            }));
                        }
                    });
                    
                    bool success = await FFmpegLocator.InitializeAsync(progress);
                    
                    if (success)
                    {
                        lblFfmpegStatus.Text = "FFmpeg downloaded successfully!";
                        lblFfmpegStatus.ForeColor = System.Drawing.Color.Green;
                    }
                    else
                    {
                        lblFfmpegStatus.Text = "Failed to download FFmpeg. Check logs for details.";
                        lblFfmpegStatus.ForeColor = System.Drawing.Color.Red;
                    }
                }
                catch (Exception ex)
                {
                    lblFfmpegStatus.Text = $"Error: {ex.Message}";
                    lblFfmpegStatus.ForeColor = System.Drawing.Color.Red;
                }
                finally
                {
                    btnDownloadBundled.Enabled = true;
                }
            };

            btnClearFfmpegCache = new Button { Text = "Clear Cache", Left = leftLabel + 280, Top = fTop, Width = 110, Height = 28 };
            btnClearFfmpegCache.Click += (s, e) => {
                var result = MessageBox.Show(
                    "This will delete the downloaded FFmpeg binaries. They will be re-downloaded when needed.\n\nContinue?",
                    "Clear FFmpeg Cache",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    FFmpegLocator.ClearCache();
                    lblFfmpegStatus.Text = "Cache cleared. FFmpeg will be re-downloaded when needed.";
                    lblFfmpegStatus.ForeColor = System.Drawing.Color.Orange;
                }
            };

            fTop += rowH + 15;
            var lblFfmpegDivider2 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = fTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };

            fTop += 20;
            var lblFfmpegHelp = new Label { Text = "ℹ Help", Left = leftLabel, Top = fTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            fTop += 25;
            var lblHelpText = new Label { 
                Text = "• Bundled: Automatically downloads FFmpeg to AppData (recommended)\n" +
                       "• System PATH: Uses FFmpeg installed on your system (must be in PATH)\n" +
                       "• Custom Path: Specify a folder containing ffmpeg.exe",
                Left = leftLabel, 
                Top = fTop, 
                Width = 500, 
                Height = 60, 
                AutoSize = false 
            };

            fTop += 70;
            var lblBundledPath = new Label { 
                Text = $"Bundled location: {FFmpegLocator.FFmpegDirectory}",
                Left = leftLabel, 
                Top = fTop, 
                Width = 550, 
                AutoSize = true,
                ForeColor = System.Drawing.Color.DarkGray,
                Font = new System.Drawing.Font(this.Font.FontFamily, 7.5f)
            };

            tabFfmpeg.Controls.AddRange(new Control[] {
                lblFfmpegSource, lblFfmpegSourceDesc, lblSource, cmbFfmpegSource,
                lblCustomPath, txtFfmpegCustomPath, btnBrowseFfmpegPath,
                lblFfmpegDivider, lblFfmpegStatusGroup, lblFfmpegStatus, btnValidateFfmpeg, btnDownloadBundled, btnClearFfmpegCache,
                lblFfmpegDivider2, lblFfmpegHelp, lblHelpText, lblBundledPath
            });

            // --- EAS Tab ---
            var tabEas = new TabPage("🚨 EAS & TTS") { BackColor = Color.White, AutoScroll = true };
            int easTop = 20;

            // AlertReady section
            var lblAlertReadyGroup = new Label { Text = "🚨 Alert Ready (NAAD)", Left = leftLabel, Top = easTop, Width = 300, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            easTop += 30;
            chkAlertReadyEnabled = new CheckBox { Text = "Enable Alert Ready", Left = leftLabel, Top = easTop, Width = 200 };
            
            easTop += rowH;
            var lblFeedUrls = new Label { Text = "Feed URLs (one per line):", Left = leftLabel, Top = easTop, Width = 180, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            
            easTop += 25;
            txtAlertReadyFeedUrls = new TextBox { Left = leftLabel, Top = easTop, Width = 480, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical };
            
            easTop += 70;
            chkAlertReadyIncludeTests = new CheckBox { Text = "Include Test Alerts", Left = leftLabel, Top = easTop, Width = 200 };
            
            easTop += rowH;
            var lblMaxAge = new Label { Text = "Max Alert Age (hours):", Left = leftLabel, Top = easTop, Width = 150, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numAlertReadyMaxAgeHours = new NumericUpDown { Left = leftLabel + 155, Top = easTop, Width = 80, Minimum = 0, Maximum = 168, Value = 24 };
            
            easTop += rowH;
            var lblLanguage = new Label { Text = "Preferred Language:", Left = leftLabel, Top = easTop, Width = 150, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbAlertReadyLanguage = new ComboBox { Left = leftLabel + 155, Top = easTop, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAlertReadyLanguage.Items.AddRange(new object[] { "en-CA", "fr-CA" });
            cmbAlertReadyLanguage.SelectedIndex = 0;
            
            easTop += rowH;
            var lblAreaFilters = new Label { Text = "Area Filters (comma-separated):", Left = leftLabel, Top = easTop, Width = 220, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtAlertReadyAreaFilters = new TextBox { Left = leftLabel + 225, Top = easTop, Width = 255 };
            
            easTop += rowH;
            var lblJurisdictions = new Label { Text = "Jurisdictions (comma-separated):", Left = leftLabel, Top = easTop, Width = 220, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtAlertReadyJurisdictions = new TextBox { Left = leftLabel + 225, Top = easTop, Width = 255 };
            
            easTop += rowH;
            chkAlertReadyHighRiskOnly = new CheckBox { Text = "High Risk Alerts Only (Severe/Extreme)", Left = leftLabel, Top = easTop, Width = 300 };
            
            easTop += rowH;
            chkAlertReadyExcludeWeather = new CheckBox { Text = "Exclude Weather Alerts (use ECCC instead)", Left = leftLabel, Top = easTop, Width = 350 };
            
            easTop += rowH + 10;
            var lblDividerEas = new Label { Text = "", Left = leftLabel, Top = easTop, Width = 700, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            
            // TTS section
            easTop += 15;
            var lblTtsGroup = new Label { Text = "🎤 Text-to-Speech (EdgeTTS)", Left = leftLabel, Top = easTop, Width = 300, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };
            
            easTop += 30;
            var lblTtsVoice = new Label { Text = "Voice:", Left = leftLabel, Top = easTop, Width = 150, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbTtsVoice = new ComboBox { Left = leftLabel + 155, Top = easTop, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTtsVoice.Items.AddRange(new object[] { 
                "fr-CA-SylvieNeural (Female)", 
                "fr-CA-JeanNeural (Male)", 
                "fr-CA-AntoineNeural (Male)",
                "fr-CA-ThierryNeural (Male)",
                "en-CA-ClaraNeural (Female)",
                "en-CA-LiamNeural (Male)",
                "en-US-JennyNeural (Female)",
                "en-US-GuyNeural (Male)"
            });
            cmbTtsVoice.SelectedIndex = 0;
            
            easTop += rowH;
            var lblTtsRate = new Label { Text = "Speech Rate (e.g., +0%, +10%):", Left = leftLabel, Top = easTop, Width = 200, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtTtsRate = new TextBox { Left = leftLabel + 205, Top = easTop, Width = 100 };
            txtTtsRate.Text = "+0%";
            
            easTop += rowH;
            var lblTtsPitch = new Label { Text = "Pitch (e.g., +0Hz, +10Hz):", Left = leftLabel, Top = easTop, Width = 200, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtTtsPitch = new TextBox { Left = leftLabel + 205, Top = easTop, Width = 100 };
            txtTtsPitch.Text = "+0Hz";
            
            easTop += rowH + 5;
            var btnDownloadVoices = new Button 
            { 
                Text = "📥 Download Windows TTS Voices", 
                Left = leftLabel, 
                Top = easTop, 
                Width = 240, 
                Height = 30,
                BackColor = System.Drawing.Color.FromArgb(52, 152, 219),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnDownloadVoices.FlatAppearance.BorderSize = 0;
            btnDownloadVoices.Click += (s, e) => 
            {
                try 
                {
                    // Open Windows Settings to Time & Language > Language & Region > Add a language
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-settings:regionlanguage",
                        UseShellExecute = true
                    });
                    MessageBox.Show(this,
                        "Windows Settings will open.\n\n" +
                        "To add French TTS voices:\n" +
                        "1. Click 'Add a language'\n" +
                        "2. Search for 'French' and select your region (Canada or France)\n" +
                        "3. Check 'Text-to-speech' option\n" +
                        "4. Click Install\n\n" +
                        "Common French voices:\n" +
                        "• French (Canada) - Includes Sylvie, Claude\n" +
                        "• French (France) - Includes Hortense, Julie, Pauline\n\n" +
                        "After installation, restart the application.",
                        "Download TTS Voices",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not open Windows Settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            
            easTop += 40;
            var lblTtsNote = new Label 
            { 
                Text = "💡 EdgeTTS works online without installation. Windows SAPI is the offline fallback.\nIf using SAPI, install French language packs above for French TTS support.",
                Left = leftLabel, 
                Top = easTop, 
                Width = 480, 
                Height = 60,
                ForeColor = System.Drawing.Color.FromArgb(100, 100, 100),
                AutoSize = false
            };

            tabEas.Controls.AddRange(new Control[] {
                lblAlertReadyGroup, chkAlertReadyEnabled, lblFeedUrls, txtAlertReadyFeedUrls,
                chkAlertReadyIncludeTests, lblMaxAge, numAlertReadyMaxAgeHours,
                lblLanguage, cmbAlertReadyLanguage, lblAreaFilters, txtAlertReadyAreaFilters,
                lblJurisdictions, txtAlertReadyJurisdictions, chkAlertReadyHighRiskOnly, chkAlertReadyExcludeWeather,
                lblDividerEas, lblTtsGroup, lblTtsVoice, cmbTtsVoice, lblTtsRate, txtTtsRate,
                lblTtsPitch, txtTtsPitch, btnDownloadVoices, lblTtsNote
            });

            // Add tabs to tab control
            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabImage);
            tabControl.TabPages.Add(tabVideo);
            tabControl.TabPages.Add(tabFfmpeg);
            tabControl.TabPages.Add(tabEas);
            tabControl.TabPages.Add(tabExperimental);

            var btnSave = new Button 
            { 
                Text = "✔ Save", 
                Left = 380, 
                Top = 620, 
                Width = 110, 
                Height = 35,
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.MouseEnter += (s, e) => btnSave.BackColor = Color.FromArgb(67, 160, 71);
            btnSave.MouseLeave += (s, e) => btnSave.BackColor = Color.FromArgb(76, 175, 80);
            
            var btnCancel = new Button 
            { 
                Text = "❌ Cancel", 
                Left = 500, 
                Top = 620, 
                Width = 110, 
                Height = 35,
                BackColor = Color.FromArgb(220, 220, 220),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.MouseEnter += (s, e) => btnCancel.BackColor = Color.FromArgb(200, 200, 200);
            btnCancel.MouseLeave += (s, e) => btnCancel.BackColor = Color.FromArgb(220, 220, 220);

            btnSave.Click += (s, e) => SaveClicked();
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.Add(tabControl);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

            // Position form relative to owner when shown
            this.Shown += (s, e) => {
                if (this.Owner != null)
                {
                    // Center on the owner's screen/monitor
                    this.Location = new Point(
                        this.Owner.Location.X + (this.Owner.Width - this.Width) / 2,
                        this.Owner.Location.Y + (this.Owner.Height - this.Height) / 2
                    );
                }
            };

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
                chkAutoStartCycle.Checked = cfg.AutoStartCycle; // New setting: auto-start cycle on launch

                // Load EAS/AlertReady settings
                var alertReady = cfg.AlertReady ?? new EAS.AlertReadyOptions();
                chkAlertReadyEnabled.Checked = alertReady.Enabled;
                txtAlertReadyFeedUrls.Text = alertReady.FeedUrls != null ? string.Join(Environment.NewLine, alertReady.FeedUrls) : "";
                chkAlertReadyIncludeTests.Checked = alertReady.IncludeTests;
                numAlertReadyMaxAgeHours.Value = alertReady.MaxAgeHours;
                cmbAlertReadyLanguage.SelectedItem = alertReady.PreferredLanguage;
                txtAlertReadyAreaFilters.Text = alertReady.AreaFilters != null ? string.Join(", ", alertReady.AreaFilters) : "";
                txtAlertReadyJurisdictions.Text = alertReady.Jurisdictions != null ? string.Join(", ", alertReady.Jurisdictions) : "QC, CA";
                chkAlertReadyHighRiskOnly.Checked = alertReady.HighRiskOnly;
                chkAlertReadyExcludeWeather.Checked = alertReady.ExcludeWeatherAlerts;
                
                // Load TTS settings
                var tts = cfg.TTS ?? new TTSSettings();
                // Map voice to display format
                var voiceDisplay = tts.Voice switch
                {
                    "fr-CA-SylvieNeural" => "fr-CA-SylvieNeural (Female)",
                    "fr-CA-JeanNeural" => "fr-CA-JeanNeural (Male)",
                    "fr-CA-AntoineNeural" => "fr-CA-AntoineNeural (Male)",
                    "fr-CA-ThierryNeural" => "fr-CA-ThierryNeural (Male)",
                    "en-CA-ClaraNeural" => "en-CA-ClaraNeural (Female)",
                    "en-CA-LiamNeural" => "en-CA-LiamNeural (Male)",
                    "en-US-JennyNeural" => "en-US-JennyNeural (Female)",
                    "en-US-GuyNeural" => "en-US-GuyNeural (Male)",
                    _ => "fr-CA-SylvieNeural (Female)"
                };
                if (cmbTtsVoice.Items.Contains(voiceDisplay)) cmbTtsVoice.SelectedItem = voiceDisplay;
                else cmbTtsVoice.SelectedIndex = 0;
                txtTtsRate.Text = tts.Rate;
                txtTtsPitch.Text = tts.Pitch; 

                numStatic.Value = (decimal)(cfg.Video?.StaticDurationSeconds ?? 8);
                numFade.Value = (decimal)(cfg.Video?.FadeDurationSeconds ?? 0.5);
                chkFade.Checked = cfg.Video?.EnableFadeTransitions ?? false;
                chkFade.Enabled = false; // Disable for now, xfade problems

                // Total duration mode
                chkUseTotalDuration.Checked = cfg.Video?.UseTotalDuration ?? false;
                numTotalDuration.Value = (decimal)(cfg.Video?.TotalDurationSeconds ?? 60);
                numTotalDuration.Enabled = chkUseTotalDuration.Checked;
                numStatic.Enabled = !chkUseTotalDuration.Checked;
                
                // Map old ResolutionMode enum values to new display format
                var resMode = cfg.Video?.ResolutionMode ?? "Mode1080p";
                var resDisplay = resMode switch
                {
                    "Mode1080p" => "1920x1080 (Full HD)",
                    "Mode4K" => "3840x2160 (4K/UHD)",
                    "Mode1440p" => "2560x1440 (2K/QHD)",
                    "Mode900p" => "1600x900 (HD+)",
                    "Mode720p" => "1280x720 (HD)",
                    "Mode540p" => "960x540 (qHD)",
                    "Mode480p" => "854x480 (FWVGA)",
                    "ModeVGA" => "640x480 (VGA)",
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

                // New encoder-related settings
                chkUseCrfEncoding.Checked = cfg.Video?.UseCrfEncoding ?? true;
                numCrf.Value = cfg.Video?.CrfValue ?? 23;
                txtMaxBitrate.Text = cfg.Video?.MaxBitrate ?? string.Empty;
                txtBufferSize.Text = cfg.Video?.BufferSize ?? string.Empty;
                var preset = cfg.Video?.EncoderPreset ?? "medium";
                if (cmbEncoderPreset.Items.Contains(preset)) cmbEncoderPreset.SelectedItem = preset;
                else cmbEncoderPreset.SelectedIndex = 5;

                // Experimental opt-in state (disabled by default)
                chkEnableExperimental.Checked = cfg.Video?.ExperimentalEnabled ?? false;
                if (tabExperimental != null) tabExperimental.Enabled = chkEnableExperimental.Checked;

                // Load quality preset
                var qualityPreset = cfg.Video?.QualityPreset ?? "Balanced";
                var presetDisplay = qualityPreset switch
                {
                    "Ultra" => "Ultra (Best Quality)",
                    "High" => "High Quality",
                    "Balanced" => "Balanced",
                    "Web" => "Web Optimized",
                    "Low" => "Low Bandwidth",
                    "Custom" => "Custom",
                    _ => "Balanced"
                };
                if (cmbQualityPreset.Items.Contains(presetDisplay)) cmbQualityPreset.SelectedItem = presetDisplay;
                else cmbQualityPreset.SelectedIndex = 2; // Default to Balanced

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

                // Load FFmpeg settings
                var ffmpegSource = cfg.FFmpeg?.Source?.ToLowerInvariant() ?? "bundled";
                cmbFfmpegSource.SelectedIndex = ffmpegSource switch
                {
                    "bundled" => 0,
                    "systempath" => 1,
                    "custom" => 2,
                    _ => 0
                };
                txtFfmpegCustomPath.Text = cfg.FFmpeg?.CustomPath ?? "";
                txtFfmpegCustomPath.Enabled = cmbFfmpegSource.SelectedIndex == 2;
                btnBrowseFfmpegPath.Enabled = cmbFfmpegSource.SelectedIndex == 2;

                // Validate FFmpeg configuration asynchronously
                Task.Run(() =>
                {
                    bool valid = FFmpegLocator.ValidateConfiguration(out var msg);
                    if (this.IsHandleCreated)
                    {
                        this.Invoke((Action)(() =>
                        {
                            lblFfmpegStatus.Text = msg;
                            lblFfmpegStatus.ForeColor = valid ? Color.Green : Color.Orange;
                        }));
                    }
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

                // Save EAS/AlertReady settings
                var alertReady = cfg.AlertReady ?? new EAS.AlertReadyOptions();
                alertReady.Enabled = chkAlertReadyEnabled.Checked;
                alertReady.FeedUrls = txtAlertReadyFeedUrls.Text
                    .Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                alertReady.IncludeTests = chkAlertReadyIncludeTests.Checked;
                alertReady.MaxAgeHours = (int)numAlertReadyMaxAgeHours.Value;
                alertReady.PreferredLanguage = cmbAlertReadyLanguage.SelectedItem?.ToString() ?? "en-CA";
                alertReady.AreaFilters = txtAlertReadyAreaFilters.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                alertReady.Jurisdictions = txtAlertReadyJurisdictions.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                alertReady.HighRiskOnly = chkAlertReadyHighRiskOnly.Checked;
                alertReady.ExcludeWeatherAlerts = chkAlertReadyExcludeWeather.Checked;
                cfg.AlertReady = alertReady;
                
                // Save TTS settings
                var tts = cfg.TTS ?? new TTSSettings();
                // Extract voice code from display text (e.g., "fr-CA-SylvieNeural (Female)" -> "fr-CA-SylvieNeural")
                var voiceDisplay = cmbTtsVoice.SelectedItem?.ToString() ?? "fr-CA-SylvieNeural (Female)";
                tts.Voice = voiceDisplay.Split(' ')[0]; // Extract just the voice code
                tts.Rate = txtTtsRate.Text.Trim();
                tts.Pitch = txtTtsPitch.Text.Trim();
                cfg.TTS = tts;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                // Total duration mode settings
                v.UseTotalDuration = chkUseTotalDuration.Checked;
                v.TotalDurationSeconds = (double)numTotalDuration.Value;
                
                // Extract resolution mode from display text and map to enum value
                var resDisplay = cmbResolution.SelectedItem?.ToString() ?? "1920x1080 (Full HD)";
                v.ResolutionMode = resDisplay switch
                {
                    "3840x2160 (4K/UHD)" => "Mode4K",
                    "2560x1440 (2K/QHD)" => "Mode1440p",
                    "1920x1080 (Full HD)" => "Mode1080p",
                    "1600x900 (HD+)" => "Mode900p",
                    "1280x720 (HD)" => "Mode720p",
                    "960x540 (qHD)" => "Mode540p",
                    "854x480 (FWVGA)" => "Mode480p",
                    "640x480 (VGA)" => "ModeVGA",
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

                // New encoding settings persistence
                v.UseCrfEncoding = chkUseCrfEncoding.Checked;
                v.CrfValue = (int)numCrf.Value;
                v.MaxBitrate = string.IsNullOrWhiteSpace(txtMaxBitrate.Text) ? null : txtMaxBitrate.Text.Trim();
                v.BufferSize = string.IsNullOrWhiteSpace(txtBufferSize.Text) ? null : txtBufferSize.Text.Trim();
                v.EncoderPreset = cmbEncoderPreset.SelectedItem?.ToString() ?? "medium";

                // Experimental opt-in persistence
                v.ExperimentalEnabled = chkEnableExperimental.Checked;
                
                // Save quality preset
                var qualityPresetDisplay = cmbQualityPreset.SelectedItem?.ToString() ?? "Balanced";
                v.QualityPreset = qualityPresetDisplay switch
                {
                    "Ultra (Best Quality)" => "Ultra",
                    "High Quality" => "High",
                    "Balanced" => "Balanced",
                    "Web Optimized" => "Web",
                    "Low Bandwidth" => "Low",
                    "Custom" => "Custom",
                    _ => "Balanced"
                };
                
                cfg.Video = v;

                // Persist theme choice
                cfg.Theme = cmbTheme.SelectedItem?.ToString() ?? "Blue";

                // Persist minimize to tray settings
                cfg.MinimizeToTray = chkMinimizeToTray.Checked;
                cfg.MinimizeToTrayOnClose = chkMinimizeToTrayOnClose.Checked;

                // Persist new AutoStartCycle setting
                cfg.AutoStartCycle = chkAutoStartCycle.Checked;

                // Persist FFmpeg settings
                var ffmpeg = cfg.FFmpeg ?? new FFmpegSettings();
                ffmpeg.Source = cmbFfmpegSource.SelectedIndex switch
                {
                    0 => "Bundled",
                    1 => "SystemPath",
                    2 => "Custom",
                    _ => "Bundled"
                };
                ffmpeg.CustomPath = cmbFfmpegSource.SelectedIndex == 2 ? txtFfmpegCustomPath.Text : null;
                cfg.FFmpeg = ffmpeg;

                // Apply FFmpeg settings immediately
                FFmpegLocator.SetSource(
                    cmbFfmpegSource.SelectedIndex switch
                    {
                        0 => Models.FFmpegSource.Bundled,
                        1 => Models.FFmpegSource.SystemPath,
                        2 => Models.FFmpegSource.Custom,
                        _ => Models.FFmpegSource.Bundled
                    },
                    ffmpeg.CustomPath
                );

                ConfigManager.SaveConfig(cfg);

                this.DialogResult = DialogResult.OK; 
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save settings: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ValidateFfmpegConfiguration()
        {
            // Temporarily apply settings to validate
            var tempSource = cmbFfmpegSource.SelectedIndex switch
            {
                0 => Models.FFmpegSource.Bundled,
                1 => Models.FFmpegSource.SystemPath,
                2 => Models.FFmpegSource.Custom,
                _ => Models.FFmpegSource.Bundled
            };
            var tempCustomPath = cmbFfmpegSource.SelectedIndex == 2 ? txtFfmpegCustomPath.Text : null;

            // Store current settings
            var currentSource = FFmpegLocator.CurrentSource;
            var currentCustomPath = FFmpegLocator.CustomPath;

            // Apply temporary settings
            FFmpegLocator.SetSource(tempSource, tempCustomPath);

            // Validate
            bool valid = FFmpegLocator.ValidateConfiguration(out var message);
            
            // Also try to get version if valid
            if (valid && tempSource != Models.FFmpegSource.Bundled || File.Exists(FFmpegLocator.FFmpegExecutable))
            {
                bool hasVersion = VideoGenerator.IsFfmpegInstalled(out var version);
                if (hasVersion)
                {
                    message += $"\nVersion: {version}";
                }
            }

            lblFfmpegStatus.Text = message;
            lblFfmpegStatus.ForeColor = valid ? System.Drawing.Color.Green : System.Drawing.Color.Red;

            // Restore original settings (they'll be saved when user clicks Save)
            FFmpegLocator.SetSource(currentSource, currentCustomPath);
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
