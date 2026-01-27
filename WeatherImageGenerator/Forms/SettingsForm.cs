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
        ComboBox cmbFontFamily; // Font family selector for generated images
        PictureBox _alertPreviewPanel; // Preview picture box for alert image
        PictureBox _weatherPreviewPanel; // Preview picture box for weather details image
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
        CheckBox chkStartWithWindows; // Start WSG when Windows starts
        CheckBox chkStartMinimizedToTray; // Start minimized to tray when launched from Windows startup
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
        // OpenMap controls
        ComboBox cmbMapStyle;
        NumericUpDown numMapZoomLevel;
        TextBox txtMapBackgroundColor;
        NumericUpDown numMapOverlayOpacity;
        NumericUpDown numMapTileTimeout;
        CheckBox chkMapEnableCache;
        TextBox txtMapCacheDirectory;
        NumericUpDown numMapCacheDuration;
        // Web UI controls
        CheckBox chkWebUIEnabled;
        NumericUpDown numWebUIPort;
        CheckBox chkWebUIAllowRemote;
        Label lblWebUIStatus;
        Button btnTestWebUI;
        TextBox txtWebUIUrl;
        CheckBox chkMapUseDarkMode;
        // Skip Detailed Weather on alert
        CheckBox chkSkipDetailedWeatherOnAlert;
        // Radar and Alert display settings
        NumericUpDown numPlayRadarAnimationCountOnAlert;
        NumericUpDown numAlertDisplayDurationSeconds;
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

            gTop += rowH;
            chkStartWithWindows = new CheckBox { Text = "Start WSG when Windows starts", Left = leftLabel, Top = gTop, Width = 300 };

            gTop += rowH;
            chkStartMinimizedToTray = new CheckBox { Text = "    ↳ Start minimized to system tray", Left = leftLabel, Top = gTop, Width = 300 };
            chkStartMinimizedToTray.Enabled = false; // Enabled only when chkStartWithWindows is checked
            
            // Enable/disable the minimized checkbox based on Windows startup checkbox
            chkStartWithWindows.CheckedChanged += (s, e) => { chkStartMinimizedToTray.Enabled = chkStartWithWindows.Checked; };

            tabGeneral.Controls.AddRange(new Control[] { lblRefresh, numRefresh, lblTheme, cmbTheme, lblOutImg, txtImageOutputDir, btnBrowseImg, lblOutVid, txtVideoOutputDir, btnBrowseVid, chkMinimizeToTray, chkMinimizeToTrayOnClose, chkAutoStartCycle, chkStartWithWindows, chkStartMinimizedToTray });

            // --- Image Tab ---
            var tabImage = new TabPage("🖼 Image") { BackColor = Color.White, AutoScroll = true };
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
            var lblFontFamily = new Label { Text = "Font Family:", Left = leftLabel, Top = iTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbFontFamily = new ComboBox { Left = leftField, Top = iTop, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            // Populate with all installed system fonts
            try
            {
                var installedFonts = System.Drawing.FontFamily.Families.Select(f => f.Name).OrderBy(n => n).ToArray();
                cmbFontFamily.Items.AddRange(installedFonts.Cast<object>().ToArray());
                if (cmbFontFamily.Items.Count > 0) cmbFontFamily.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading system fonts: {ex.Message}", Logger.LogLevel.Warning);
                // Fallback to common fonts if system fonts cannot be loaded
                var fallbackFonts = new[] { "Arial", "Segoe UI", "Times New Roman", "Courier New", "Georgia", "Tahoma", "Verdana" };
                cmbFontFamily.Items.AddRange(fallbackFonts.Cast<object>().ToArray());
                if (cmbFontFamily.Items.Count > 0) cmbFontFamily.SelectedIndex = 0;
            }

            iTop += rowH;
            chkEnableProvinceRadar = new CheckBox { Text = "Enable Province Radar Animation", Left = leftLabel, Top = iTop, Width = 250, Enabled = true };

            iTop += rowH;
            chkEnableWeatherMaps = new CheckBox { Text = "Enable Weather Maps Generation", Left = leftLabel, Top = iTop, Width = 250, Enabled = true };

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

            iTop += rowH;
            var lblDivider = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = iTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };
            
            iTop += 25;
            var lblPreviewTitle = new Label { Text = "Font Preview", Left = leftLabel, Top = iTop, Width = 400, Font = new Font(this.Font, FontStyle.Bold) };
            iTop += 30;
            
            _alertPreviewPanel = new PictureBox { Left = leftLabel, Top = iTop, Width = 700, Height = 130, BorderStyle = BorderStyle.Fixed3D, BackColor = Color.White, SizeMode = PictureBoxSizeMode.CenterImage };
            iTop += 145;
            
            _weatherPreviewPanel = new PictureBox { Left = leftLabel, Top = iTop, Width = 700, Height = 130, BorderStyle = BorderStyle.Fixed3D, BackColor = Color.White, SizeMode = PictureBoxSizeMode.CenterImage };
            
            tabImage.Controls.AddRange(new Control[] { lblImgSize, numImgWidth, lblX, numImgHeight, lblFormat, cmbImgFormat, lblFontFamily, cmbFontFamily, chkEnableProvinceRadar, chkEnableWeatherMaps, btnRegenIcons, lblDivider, lblPreviewTitle, _alertPreviewPanel, _weatherPreviewPanel });

            // Hook font change to update preview
            cmbFontFamily.SelectedIndexChanged += (s, e) => UpdateFontPreview();
            
            // Generate initial previews
            UpdateFontPreview();

            // --- Video Tab ---
            var tabVideo = new TabPage("🎥 Video") { BackColor = Color.FromArgb(248, 249, 250), AutoScroll = true };
            int vTop = 10;
            int grpPadding = 15;
            int ctrlLeft = 15;
            int ctrlWidth = 280;
            int rightCol = 360;

            // ═══════════════════════════════════════════════════════════════
            // TOP ROW - Main toggle
            // ═══════════════════════════════════════════════════════════════
            chkVideoGeneration = new CheckBox { Text = "  Enable Video Generation", Left = ctrlLeft, Top = vTop, Width = 220, Height = 28, Font = new Font(this.Font.FontFamily, 10f, FontStyle.Bold) };
            vTop += 35;

            // ═══════════════════════════════════════════════════════════════
            // LEFT SIDE - GroupBox: Alert Settings
            // ═══════════════════════════════════════════════════════════════
            var grpAlerts = new GroupBox { Text = "🚨 Alert Settings", Left = ctrlLeft, Top = vTop, Width = 330, Height = 115, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold) };
            int aTop = 22;
            
            chkSkipDetailedWeatherOnAlert = new CheckBox { Text = "Skip Detailed Weather if alert active", Left = grpPadding, Top = aTop, Width = 280, Font = new Font(this.Font.FontFamily, 9f) };
            aTop += 28;
            
            var lblRadarCount = new Label { Text = "Replay Radar:", Left = grpPadding, Top = aTop + 2, Width = 90, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            numPlayRadarAnimationCountOnAlert = new NumericUpDown { Left = grpPadding + 95, Top = aTop, Width = 55, Minimum = 1, Maximum = 10, Value = 1 };
            var lblRadarCountHelp = new Label { Text = "times when alert active", Left = grpPadding + 155, Top = aTop + 3, AutoSize = true, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8f) };
            aTop += 28;
            
            var lblAlertDuration = new Label { Text = "Alert Duration:", Left = grpPadding, Top = aTop + 2, Width = 90, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            numAlertDisplayDurationSeconds = new NumericUpDown { Left = grpPadding + 95, Top = aTop, Width = 55, Minimum = 1, Maximum = 120, DecimalPlaces = 1, Increment = 0.5M, Value = 6 };
            var lblAlertDurationHelp = new Label { Text = "seconds", Left = grpPadding + 155, Top = aTop + 3, AutoSize = true, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8f) };
            
            grpAlerts.Controls.AddRange(new Control[] { chkSkipDetailedWeatherOnAlert, lblRadarCount, numPlayRadarAnimationCountOnAlert, lblRadarCountHelp, lblAlertDuration, numAlertDisplayDurationSeconds, lblAlertDurationHelp });

            // ═══════════════════════════════════════════════════════════════
            // RIGHT SIDE - GroupBox: Output Format
            // ═══════════════════════════════════════════════════════════════
            var grpFormat = new GroupBox { Text = "📹 Output Format", Left = rightCol, Top = vTop, Width = 330, Height = 115, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold) };
            int fTop = 22;
            
            var lblQualityPreset = new Label { Text = "Quality Preset:", Left = grpPadding, Top = fTop + 2, Width = 95, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            cmbQualityPreset = new ComboBox { Left = grpPadding + 100, Top = fTop, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbQualityPreset.Items.AddRange(new object[] { "Ultra (Best Quality)", "High Quality", "Balanced", "Web Optimized", "Low Bandwidth", "Custom" });
            cmbQualityPreset.SelectedIndex = 2;
            fTop += 28;
            
            var lblResPreset = new Label { Text = "Resolution:", Left = grpPadding, Top = fTop + 2, Width = 95, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            cmbResolution = new ComboBox { Left = grpPadding + 100, Top = fTop, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbResolution.Items.AddRange(new object[] { "3840x2160 (4K/UHD)", "2560x1440 (2K/QHD)", "1920x1080 (Full HD)", "1600x900 (HD+)", "1280x720 (HD)", "960x540 (qHD)", "854x480 (FWVGA)", "640x480 (VGA)" });
            cmbResolution.SelectedIndex = 2;
            fTop += 28;
            
            var lblContainer = new Label { Text = "Container:", Left = grpPadding, Top = fTop + 2, Width = 95, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            cmbContainer = new ComboBox { Left = grpPadding + 100, Top = fTop, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;
            var lblFps = new Label { Text = "FPS:", Left = grpPadding + 200, Top = fTop + 2, Width = 35, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            numFps = new NumericUpDown { Left = grpPadding + 240, Top = fTop, Width = 55, Minimum = 1, Maximum = 240, Value = 30 };
            
            grpFormat.Controls.AddRange(new Control[] { lblQualityPreset, cmbQualityPreset, lblResPreset, cmbResolution, lblContainer, cmbContainer, lblFps, numFps });

            vTop += 125;

            // ═══════════════════════════════════════════════════════════════
            // LEFT SIDE - GroupBox: Timing Settings
            // ═══════════════════════════════════════════════════════════════
            var grpTiming = new GroupBox { Text = "⏱ Timing Settings", Left = ctrlLeft, Top = vTop, Width = 330, Height = 140, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold) };
            int tTop = 22;
            
            var lblStatic = new Label { Text = "Slide Duration:", Left = grpPadding, Top = tTop + 2, Width = 100, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            numStatic = new NumericUpDown { Left = grpPadding + 105, Top = tTop, Width = 70, Minimum = 1, Maximum = 60, DecimalPlaces = 1, Increment = 1, Value = 8 };
            var lblStaticHelp = new Label { Text = "seconds per slide", Left = grpPadding + 180, Top = tTop + 3, AutoSize = true, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8f) };
            tTop += 30;
            
            var lblTotal = new Label { Text = "Total Duration:", Left = grpPadding, Top = tTop + 2, Width = 100, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            numTotalDuration = new NumericUpDown { Left = grpPadding + 105, Top = tTop, Width = 70, Minimum = 1, Maximum = 86400, DecimalPlaces = 1, Increment = 1, Value = 60 };
            var lblTotalHelp = new Label { Text = "seconds total", Left = grpPadding + 180, Top = tTop + 3, AutoSize = true, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8f) };
            tTop += 28;
            chkUseTotalDuration = new CheckBox { Text = "Enforce total duration (overrides slide)", Left = grpPadding, Top = tTop, Width = 280, Font = new Font(this.Font.FontFamily, 9f) };
            chkUseTotalDuration.CheckedChanged += (s, e) => { numTotalDuration.Enabled = chkUseTotalDuration.Checked; numStatic.Enabled = !chkUseTotalDuration.Checked; };
            numTotalDuration.Enabled = false;
            tTop += 28;

            var lblFade = new Label { Text = "Fade Duration:", Left = grpPadding, Top = tTop + 2, Width = 100, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 9f) };
            numFade = new NumericUpDown { Left = grpPadding + 105, Top = tTop, Width = 70, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.1M, Value = 0.5M, Enabled = false };
            chkFade = new CheckBox { Text = "Enable", Left = grpPadding + 180, Top = tTop, Width = 80, Enabled = false, Font = new Font(this.Font.FontFamily, 9f) };
            
            grpTiming.Controls.AddRange(new Control[] { lblStatic, numStatic, lblStaticHelp, lblTotal, numTotalDuration, lblTotalHelp, chkUseTotalDuration, lblFade, numFade, chkFade });

            // ═══════════════════════════════════════════════════════════════
            // RIGHT SIDE - GroupBox: Encoding Settings
            // ═══════════════════════════════════════════════════════════════
            var grpEncoding = new GroupBox { Text = "🎬 Encoding", Left = rightCol, Top = vTop, Width = 330, Height = 140, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold) };
            int eTop = 22;
            
            var lblCodec = new Label { Text = "Codec:", Left = grpPadding, Top = eTop + 2, Width = 80, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            cmbCodec = new ComboBox { Left = grpPadding + 85, Top = eTop, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCodec.Items.AddRange(new object[] { "libx264 (H.264)", "libx265 (H.265/HEVC)", "libvpx-vp9 (VP9)", "libaom-av1 (AV1)", "mpeg4", "msmpeg4" });
            cmbCodec.SelectedIndex = 0;
            eTop += 28;
            
            var lblBitrate = new Label { Text = "Bitrate:", Left = grpPadding, Top = eTop + 2, Width = 80, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(this.Font.FontFamily, 9f) };
            cmbBitrate = new ComboBox { Left = grpPadding + 85, Top = eTop, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbBitrate.Items.AddRange(new object[] { 
                "1M (Low)", "2M (Medium-Low)", "4M (Medium)", "6M (Medium-High)", "8M (High)", "12M (Very High)", "16M (Ultra)"
            });
            cmbBitrate.SelectedIndex = 2;
            eTop += 28;
            
            chkEnableHardwareEncoding = new CheckBox { Text = "⚡ Hardware Encoding (NVENC)", Left = grpPadding, Top = eTop, Width = 200, Font = new Font(this.Font.FontFamily, 9f) };
            btnCheckHw = new Button { Text = "Check", Left = grpPadding + 205, Top = eTop - 2, Width = 60, Height = 24 };
            eTop += 26;
            lblHwStatus = new Label { Text = "Click Check to verify", Left = grpPadding + 20, Top = eTop, Width = 260, ForeColor = Color.Gray, AutoSize = true, Font = new Font(this.Font.FontFamily, 8f) };
            
            grpEncoding.Controls.AddRange(new Control[] { lblCodec, cmbCodec, lblBitrate, cmbBitrate, chkEnableHardwareEncoding, btnCheckHw, lblHwStatus });

            vTop += 150;

            // ═══════════════════════════════════════════════════════════════
            // BOTTOM SECTION - Debug & Experimental
            // ═══════════════════════════════════════════════════════════════
            var grpDebug = new GroupBox { Text = "🔧 Debug & Options", Left = ctrlLeft, Top = vTop, Width = 675, Height = 85, Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold) };
            int dTop = 22;
            
            chkVerbose = new CheckBox { Text = "Verbose FFmpeg Output", Left = grpPadding, Top = dTop, Width = 180, Font = new Font(this.Font.FontFamily, 9f) };
            chkShowFfmpeg = new CheckBox { Text = "Show FFmpeg Console Window", Left = grpPadding + 200, Top = dTop, Width = 220, Font = new Font(this.Font.FontFamily, 9f) };
            chkEnableExperimental = new CheckBox { Text = "Enable Experimental Features", Left = grpPadding + 440, Top = dTop, Width = 210, Font = new Font(this.Font.FontFamily, 9f) };
            chkEnableExperimental.CheckedChanged += (s, e) => { if (tabExperimental != null) tabExperimental.Enabled = chkEnableExperimental.Checked; };
            dTop += 28;
            
            var lblDebugNote = new Label { Text = "💡 Tip: Use Debug options to troubleshoot video generation issues. Enable Experimental for advanced encoder settings.", Left = grpPadding, Top = dTop, Width = 640, AutoSize = false, ForeColor = Color.Gray, Font = new Font(this.Font.FontFamily, 8f) };
            
            grpDebug.Controls.AddRange(new Control[] { chkVerbose, chkShowFfmpeg, chkEnableExperimental, lblDebugNote });

            // Experimental controls (for Experimental tab)
            var lblCrf = new Label { Text = "CRF Value:", Left = 15, Top = 60, Width = 130, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            chkUseCrfEncoding = new CheckBox { Text = "Use CRF encoding (quality-based)", Left = 150, Top = 60, Width = 220 };
            numCrf = new NumericUpDown { Left = 380, Top = 60, Width = 80, Minimum = 0, Maximum = 51, Value = 23 };

            var lblEncoderPreset = new Label { Text = "Encoder Preset:", Left = 15, Top = 95, Width = 130, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            cmbEncoderPreset = new ComboBox { Left = 150, Top = 95, Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEncoderPreset.Items.AddRange(new object[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });
            cmbEncoderPreset.SelectedIndex = 5;

            var lblMaxRate = new Label { Text = "Max Bitrate:", Left = 15, Top = 130, Width = 130, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            txtMaxBitrate = new TextBox { Left = 150, Top = 130, Width = 145 };
            var lblBuf = new Label { Text = "Buffer Size:", Left = 310, Top = 130, Width = 90, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            txtBufferSize = new TextBox { Left = 400, Top = 130, Width = 90 };

            
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
            
            // Sync Alert Duration with Static Duration
            numStatic.ValueChanged += (s, e) => 
            { 
                if (!_isLoadingSettings) 
                    numAlertDisplayDurationSeconds.Value = numStatic.Value;
            };
            
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
                        lblHwStatus.ForeColor = ok ? Color.Green : Color.Red;
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

            // Add all controls to the Video tab
            tabVideo.Controls.AddRange(new Control[] { chkVideoGeneration, grpAlerts, grpFormat, grpTiming, grpEncoding, grpDebug });

            // --- Experimental Tab (moved from Video tab) ---
            tabExperimental = new TabPage("⚠ Experimental") { BackColor = Color.FromArgb(255, 252, 245) };
            var lblExpNote = new Label { Text = "⚠ These are experimental encoder options. Changes may affect video quality and encoding performance.", Left = 15, Top = 20, Width = 650, Height = 35, ForeColor = Color.OrangeRed, AutoSize = false, Font = new Font(this.Font.FontFamily, 9f) };
            tabExperimental.Controls.AddRange(new Control[] {
                lblExpNote, lblCrf, chkUseCrfEncoding, numCrf, lblEncoderPreset, cmbEncoderPreset, lblMaxRate, txtMaxBitrate, lblBuf, txtBufferSize
            });
            tabExperimental.Enabled = false; // Disabled by default until user opts-in

            // --- FFmpeg Tab ---
            var tabFfmpeg = new TabPage("🎬 FFmpeg") { BackColor = Color.White };
            int ffTop = 20;

            var lblFfmpegSource = new Label { Text = "🎬 FFmpeg Source", Left = leftLabel, Top = ffTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            ffTop += 30;
            var lblFfmpegSourceDesc = new Label { Text = "Choose where to get FFmpeg binaries from:", Left = leftLabel, Top = ffTop, Width = 400, AutoSize = false };

            ffTop += 25;
            var lblSource = new Label { Text = "Source:", Left = leftLabel, Top = ffTop, Width = 80, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            cmbFfmpegSource = new ComboBox { Left = leftField, Top = ffTop, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFfmpegSource.Items.AddRange(new object[] { 
                "Bundled (Auto-download)", 
                "System PATH", 
                "Custom Path" 
            });
            cmbFfmpegSource.SelectedIndex = 0;

            ffTop += rowH + 5;
            var lblCustomPath = new Label { Text = "Custom Path:", Left = leftLabel, Top = ffTop, Width = 100, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtFfmpegCustomPath = new TextBox { Left = leftField, Top = ffTop, Width = 350, Enabled = false };
            btnBrowseFfmpegPath = new Button { Text = "...", Left = leftField + 355, Top = ffTop - 1, Width = 40, Height = 23, Enabled = false };
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

            ffTop += rowH + 10;
            var lblFfmpegDivider = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = ffTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };

            ffTop += 20;
            var lblFfmpegStatusGroup = new Label { Text = "📋 Status", Left = leftLabel, Top = ffTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            ffTop += 30;
            lblFfmpegStatus = new Label { Text = "Not validated", Left = leftLabel, Top = ffTop, Width = 500, AutoSize = true, ForeColor = System.Drawing.Color.Gray };

            ffTop += rowH;
            btnValidateFfmpeg = new Button { Text = "Validate FFmpeg", Left = leftLabel, Top = ffTop, Width = 130, Height = 28 };
            btnValidateFfmpeg.Click += (s, e) => {
                ValidateFfmpegConfiguration();
            };

            btnDownloadBundled = new Button { Text = "Download Bundled", Left = leftLabel + 140, Top = ffTop, Width = 130, Height = 28 };
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

            btnClearFfmpegCache = new Button { Text = "Clear Cache", Left = leftLabel + 280, Top = ffTop, Width = 110, Height = 28 };
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

            ffTop += rowH + 15;
            var lblFfmpegDivider2 = new Label { Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", Left = leftLabel, Top = ffTop, Width = 540, Height = 15, ForeColor = System.Drawing.Color.LightGray };

            ffTop += 20;
            var lblFfmpegHelp = new Label { Text = "ℹ Help", Left = leftLabel, Top = ffTop, Width = 200, Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold) };

            ffTop += 25;
            var lblHelpText = new Label { 
                Text = "• Bundled: Automatically downloads FFmpeg to AppData (recommended)\n" +
                       "• System PATH: Uses FFmpeg installed on your system (must be in PATH)\n" +
                       "• Custom Path: Specify a folder containing ffmpeg.exe",
                Left = leftLabel, 
                Top = ffTop, 
                Width = 500, 
                Height = 60, 
                AutoSize = false 
            };

            ffTop += 70;
            var lblBundledPath = new Label { 
                Text = $"Bundled location: {FFmpegLocator.FFmpegDirectory}",
                Left = leftLabel, 
                Top = ffTop, 
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

            // --- OpenMap Tab ---
            var tabOpenMap = new TabPage("🗺 OpenMap") { BackColor = Color.White, AutoScroll = true };
            int omTop = 20;
            int omLeft = 20;
            int omLabelWidth = 200;
            int omControlWidth = 250;

            // Section: Basic Settings
            var lblMapBasicSection = new Label
            {
                Text = "Basic Map Settings",
                Left = omLeft,
                Top = omTop,
                Width = 600,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };
            omTop += 30;

            var lblMapStyle = new Label { Text = "Default Map Style:", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            cmbMapStyle = new ComboBox
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = omControlWidth,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMapStyle.Items.AddRange(new object[] { "Standard", "Minimal", "Terrain", "Satellite" });
            cmbMapStyle.SelectedIndex = 0;
            omTop += 35;

            var lblMapZoom = new Label { Text = "Default Zoom Level (0-18):", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            numMapZoomLevel = new NumericUpDown
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = 100,
                Minimum = 0,
                Maximum = 18,
                Value = 10
            };
            var lblMapZoomHelp = new Label
            {
                Text = "(7-10 for regional weather)",
                Left = omLeft + omLabelWidth + 120,
                Top = omTop,
                Width = 200,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };
            omTop += 35;

            var lblMapBgColor = new Label { Text = "Background Color (Hex):", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            txtMapBackgroundColor = new TextBox
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = 120,
                Text = "#D3D3D3"
            };
            var lblMapBgHelp = new Label
            {
                Text = "e.g., #E8F4F8 for light blue",
                Left = omLeft + omLabelWidth + 140,
                Top = omTop,
                Width = 200,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };
            omTop += 35;

            var lblMapOpacity = new Label { Text = "Overlay Opacity (0-100%):", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            numMapOverlayOpacity = new NumericUpDown
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = 100,
                Minimum = 0,
                Maximum = 100,
                Value = 70,
                DecimalPlaces = 0
            };
            var lblMapOpacityHelp = new Label
            {
                Text = "(70-85% recommended)",
                Left = omLeft + omLabelWidth + 120,
                Top = omTop,
                Width = 200,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };
            omTop += 35;

            chkMapUseDarkMode = new CheckBox
            {
                Text = "🌙 Use Dark Mode (for Terrain style)",
                Left = omLeft,
                Top = omTop,
                Width = 400,
                Checked = false,
                Font = new Font("Segoe UI", 9F)
            };
            var lblMapDarkHelp = new Label
            {
                Text = "Best for night weather displays",
                Left = omLeft + 410,
                Top = omTop + 3,
                Width = 250,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };
            omTop += 45;

            // Section: Performance Settings
            var lblMapPerfSection = new Label
            {
                Text = "Performance & Caching",
                Left = omLeft,
                Top = omTop,
                Width = 600,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };
            omTop += 30;

            var lblMapTimeout = new Label { Text = "Tile Download Timeout (sec):", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            numMapTileTimeout = new NumericUpDown
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = 100,
                Minimum = 10,
                Maximum = 120,
                Value = 30
            };
            omTop += 35;

            chkMapEnableCache = new CheckBox
            {
                Text = "Enable Tile Caching (Recommended)",
                Left = omLeft,
                Top = omTop,
                Width = 400,
                Checked = true
            };
            omTop += 30;

            var lblMapCacheDir = new Label { Text = "Cache Directory:", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            txtMapCacheDirectory = new TextBox
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = omControlWidth,
                Text = "MapCache",
                Enabled = true
            };
            chkMapEnableCache.CheckedChanged += (s, e) => txtMapCacheDirectory.Enabled = chkMapEnableCache.Checked;
            omTop += 35;

            var lblMapCacheDuration = new Label { Text = "Cache Duration (hours):", Left = omLeft, Top = omTop, Width = omLabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            numMapCacheDuration = new NumericUpDown
            {
                Left = omLeft + omLabelWidth + 10,
                Top = omTop - 3,
                Width = 100,
                Minimum = 1,
                Maximum = 8760,
                Value = 168
            };
            var lblMapCacheHelp = new Label
            {
                Text = "(168 hrs = 7 days)",
                Left = omLeft + omLabelWidth + 120,
                Top = omTop,
                Width = 200,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5F)
            };
            omTop += 45;

            // Section: Map Style Guide
            var lblMapStyleSection = new Label
            {
                Text = "Map Style Reference",
                Left = omLeft,
                Top = omTop,
                Width = 600,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(41, 128, 185)
            };
            omTop += 30;

            var lblMapStyleGuide = new Label
            {
                Text = "• Standard: Traditional OpenStreetMap with detailed roads and cities\n" +
                       "• Minimal: Clean, simplified style (HOT)\n" +
                       "• Terrain: Topographic with elevation contours\n" +
                       "• Satellite: High-resolution imagery (Esri)\n\n" +
                       "Note: All maps require proper attribution. See OpenMap/LEGAL.md",
                Left = omLeft,
                Top = omTop,
                Width = 650,
                Height = 130,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(52, 73, 94)
            };
            omTop += 140;

            // Attribution note
            var lblMapAttribution = new Label
            {
                Text = "⚠ Legal: Generated maps automatically include required attribution per OSM usage policy.",
                Left = omLeft,
                Top = omTop,
                Width = 650,
                Height = 30,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = Color.FromArgb(192, 57, 43)
            };

            tabOpenMap.Controls.AddRange(new Control[]
            {
                lblMapBasicSection,
                lblMapStyle, cmbMapStyle,
                lblMapZoom, numMapZoomLevel, lblMapZoomHelp,
                lblMapBgColor, txtMapBackgroundColor, lblMapBgHelp,
                lblMapOpacity, numMapOverlayOpacity, lblMapOpacityHelp,
                chkMapUseDarkMode, lblMapDarkHelp,
                lblMapPerfSection,
                lblMapTimeout, numMapTileTimeout,
                chkMapEnableCache,
                lblMapCacheDir, txtMapCacheDirectory,
                lblMapCacheDuration, numMapCacheDuration, lblMapCacheHelp,
                lblMapStyleSection,
                lblMapStyleGuide,
                lblMapAttribution
            });

            // --- Web UI Tab ---
            var tabWebUI = new TabPage("🌐 Web UI") { BackColor = Color.White, AutoScroll = true };
            int wTop = 20;

            var lblWebUIInfo = new Label 
            { 
                Text = "Remote Web Interface Settings", 
                Left = leftLabel, 
                Top = wTop, 
                Width = 400, 
                AutoSize = false, 
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft 
            };

            wTop += 40;
            chkWebUIEnabled = new CheckBox { Text = "Enable Web UI Server", Left = leftLabel, Top = wTop, Width = 300 };

            wTop += rowH;
            var lblPort = new Label { Text = "Port:", Left = leftLabel, Top = wTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            numWebUIPort = new NumericUpDown { Left = leftField, Top = wTop, Width = 80, Minimum = 1024, Maximum = 65535, Value = 5000 };

            wTop += rowH;
            chkWebUIAllowRemote = new CheckBox { Text = "Allow Remote Access (other computers on network)", Left = leftLabel, Top = wTop, Width = 400 };

            wTop += rowH;
            var lblURL = new Label { Text = "Access URL:", Left = leftLabel, Top = wTop, Width = 140, AutoSize = false, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            txtWebUIUrl = new TextBox { Left = leftField, Top = wTop, Width = 350, ReadOnly = true };

            wTop += rowH;
            lblWebUIStatus = new Label { Text = "Status: Inactive", Left = leftLabel, Top = wTop, Width = 400, AutoSize = false };

            wTop += 45;
            btnTestWebUI = new Button { Text = "🔗 Test Connection", Left = leftLabel, Top = wTop, Width = 150, Height = 30 };
            btnTestWebUI.Click += (s, e) => TestWebUIConnection();

            var btnOpenInBrowser = new Button { Text = "🌐 Open in Browser", Left = 170, Top = wTop, Width = 150, Height = 30 };
            btnOpenInBrowser.Click += (s, e) => OpenWebUIInBrowser();

            wTop += 50;
            var lblNote = new Label 
            { 
                Text = "Note: This allows access to your weather interface from any web browser on your network or the internet.", 
                Left = leftLabel, 
                Top = wTop, 
                Width = 600, 
                Height = 60,
                AutoSize = false,
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = Color.Gray
            };

            tabWebUI.Controls.AddRange(new Control[] {
                lblWebUIInfo,
                chkWebUIEnabled,
                lblPort, numWebUIPort,
                chkWebUIAllowRemote,
                lblURL, txtWebUIUrl,
                lblWebUIStatus,
                btnTestWebUI, btnOpenInBrowser,
                lblNote
            });

            // Add tabs to tab control
            tabControl.TabPages.Add(tabGeneral);
            tabControl.TabPages.Add(tabImage);
            tabControl.TabPages.Add(tabVideo);
            tabControl.TabPages.Add(tabFfmpeg);
            tabControl.TabPages.Add(tabOpenMap);
            tabControl.TabPages.Add(tabEas);
            tabControl.TabPages.Add(tabWebUI);
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

            // Add event handlers for Web UI controls
            chkWebUIEnabled.CheckedChanged += (s, e) => { if (!_isLoadingSettings) OnWebUIEnabledChanged(); };
            numWebUIPort.ValueChanged += (s, e) => { if (!_isLoadingSettings) UpdateWebUIUrl(); };
            chkWebUIAllowRemote.CheckedChanged += (s, e) => { if (!_isLoadingSettings) UpdateWebUIUrl(); };

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
                
                // Load font family setting
                var fontFamily = cfg.ImageGeneration?.FontFamily ?? "Arial";
                if (cmbFontFamily.Items.Contains(fontFamily)) cmbFontFamily.SelectedItem = fontFamily;
                else cmbFontFamily.SelectedIndex = 0; // Default to first font if not found
                
                chkEnableProvinceRadar.Checked = cfg.ECCC?.EnableProvinceRadar ?? true;
                chkEnableWeatherMaps.Checked = cfg.ImageGeneration?.EnableWeatherMaps ?? true;

                var theme = cfg.Theme ?? "Blue";
                if (cmbTheme.Items.Contains(theme)) cmbTheme.SelectedItem = theme;
                else cmbTheme.SelectedItem = "Blue";

                chkMinimizeToTray.Checked = cfg.MinimizeToTray;
                chkMinimizeToTrayOnClose.Checked = cfg.MinimizeToTrayOnClose;
                chkAutoStartCycle.Checked = cfg.AutoStartCycle; // New setting: auto-start cycle on launch
                
                // Load Windows startup setting - check actual registry state
                try
                {
                    chkStartWithWindows.Checked = WindowsStartupManager.IsStartupEnabled();
                    // Sync config with actual state if they differ
                    if (chkStartWithWindows.Checked != cfg.StartWithWindows)
                    {
                        cfg.StartWithWindows = chkStartWithWindows.Checked;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to check Windows startup status: {ex.Message}", Logger.LogLevel.Warning);
                    chkStartWithWindows.Checked = cfg.StartWithWindows;
                }
                
                chkStartMinimizedToTray.Checked = cfg.StartMinimizedToTray;
                chkStartMinimizedToTray.Enabled = chkStartWithWindows.Checked;

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

                // Load OpenMap settings
                var openMap = cfg.OpenMap ?? new OpenMapSettings();
                var mapStyle = openMap.DefaultMapStyle?.ToLowerInvariant() ?? "standard";
                cmbMapStyle.SelectedIndex = mapStyle switch
                {
                    "standard" => 0,
                    "minimal" => 1,
                    "terrain" => 2,
                    "satellite" => 3,
                    _ => 0
                };
                numMapZoomLevel.Value = openMap.DefaultZoomLevel;
                txtMapBackgroundColor.Text = openMap.BackgroundColor;
                numMapOverlayOpacity.Value = (decimal)(openMap.OverlayOpacity * 100); // Convert 0.0-1.0 to 0-100
                numMapTileTimeout.Value = openMap.TileDownloadTimeoutSeconds;
                chkMapEnableCache.Checked = openMap.EnableTileCache;
                txtMapCacheDirectory.Text = openMap.TileCacheDirectory ?? "MapCache";
                numMapCacheDuration.Value = openMap.CacheDurationHours;
                chkMapUseDarkMode.Checked = openMap.UseDarkMode;

                // Load Web UI settings
                var webUI = cfg.WebUI ?? new WebUISettings();
                chkWebUIEnabled.Checked = webUI.Enabled;
                numWebUIPort.Value = webUI.Port;
                chkWebUIAllowRemote.Checked = webUI.AllowRemoteAccess;
                UpdateWebUIUrl();
                UpdateWebUIStatus();

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
                chkSkipDetailedWeatherOnAlert.Checked = cfg.Video?.SkipDetailedWeatherOnAlert ?? false;
                numPlayRadarAnimationCountOnAlert.Value = cfg.Video?.PlayRadarAnimationCountOnAlert ?? 1;
                numAlertDisplayDurationSeconds.Value = (decimal)(cfg.Video?.AlertDisplayDurationSeconds ?? 10);
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
                imageGen.FontFamily = cmbFontFamily.SelectedItem?.ToString() ?? "Arial";
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
                v.SkipDetailedWeatherOnAlert = chkSkipDetailedWeatherOnAlert.Checked;
                v.PlayRadarAnimationCountOnAlert = (int)numPlayRadarAnimationCountOnAlert.Value;
                v.AlertDisplayDurationSeconds = (double)numAlertDisplayDurationSeconds.Value;
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

                // Persist and apply Windows startup setting
                cfg.StartWithWindows = chkStartWithWindows.Checked;
                cfg.StartMinimizedToTray = chkStartMinimizedToTray.Checked;
                try
                {
                    if (chkStartWithWindows.Checked)
                    {
                        WindowsStartupManager.EnableStartup();
                    }
                    else
                    {
                        WindowsStartupManager.DisableStartup();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to update Windows startup setting: {ex.Message}", Logger.LogLevel.Error);
                    MessageBox.Show($"Failed to update Windows startup setting: {ex.Message}", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // Persist OpenMap settings
                var openMap = cfg.OpenMap ?? new OpenMapSettings();
                openMap.DefaultMapStyle = cmbMapStyle.SelectedIndex switch
                {
                    0 => "Standard",
                    1 => "Minimal",
                    2 => "Terrain",
                    3 => "Satellite",
                    _ => "Standard"
                };
                openMap.DefaultZoomLevel = (int)numMapZoomLevel.Value;
                openMap.BackgroundColor = txtMapBackgroundColor.Text;
                openMap.OverlayOpacity = (float)(numMapOverlayOpacity.Value / 100); // Convert 0-100 to 0.0-1.0
                openMap.TileDownloadTimeoutSeconds = (int)numMapTileTimeout.Value;
                openMap.EnableTileCache = chkMapEnableCache.Checked;
                openMap.TileCacheDirectory = txtMapCacheDirectory.Text;
                openMap.CacheDurationHours = (int)numMapCacheDuration.Value;
                openMap.UseDarkMode = chkMapUseDarkMode.Checked;
                cfg.OpenMap = openMap;

                // Persist Web UI settings
                var webUI = cfg.WebUI ?? new WebUISettings();
                bool wasEnabled = webUI.Enabled;
                int oldPort = webUI.Port;
                
                webUI.Enabled = chkWebUIEnabled.Checked;
                webUI.Port = (int)numWebUIPort.Value;
                webUI.AllowRemoteAccess = chkWebUIAllowRemote.Checked;
                cfg.WebUI = webUI;
                
                // Restart service if port changed while running
                if (chkWebUIEnabled.Checked && wasEnabled && oldPort != webUI.Port)
                {
                    StopWebUIService();
                    Program.SetWebUIService(null);
                    StartWebUIService();
                }

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

        private void UpdateFontPreview()
        {
            if (_alertPreviewPanel == null || _weatherPreviewPanel == null) return;

            try
            {
                string fontName = cmbFontFamily.SelectedItem?.ToString() ?? "Arial";

                // Alert Preview
                var alertBmp = new Bitmap(700, 130);
                using (var g = Graphics.FromImage(alertBmp))
                {
                    g.Clear(Color.White);
                    
                    // Draw alert style preview
                    using (var headerFont = new Font(fontName, 28, FontStyle.Bold))
                    using (var detailFont = new Font(fontName, 14, FontStyle.Regular))
                    using (var blackBrush = new SolidBrush(Color.Black))
                    using (var redBrush = new SolidBrush(Color.FromArgb(192, 0, 0)))
                    {
                        // Draw red bar at top
                        using (var redBgBrush = new SolidBrush(Color.FromArgb(192, 0, 0)))
                        {
                            g.FillRectangle(redBgBrush, 0, 0, 700, 40);
                        }
                        
                        g.DrawString("⚠ Weather Alert", headerFont, new SolidBrush(Color.White), new PointF(20, 5));
                        g.DrawString($"Font: {fontName}", detailFont, blackBrush, new PointF(20, 50));
                        g.DrawString("This is a sample alert message", detailFont, blackBrush, new PointF(20, 75));
                        g.DrawString("with your selected font", detailFont, blackBrush, new PointF(20, 100));
                    }
                }

                // Weather Details Preview
                var weatherBmp = new Bitmap(700, 130);
                using (var g = Graphics.FromImage(weatherBmp))
                {
                    g.Clear(Color.FromArgb(230, 240, 250)); // Light blue background
                    
                    using (var cityFont = new Font(fontName, 24, FontStyle.Bold))
                    using (var tempFont = new Font(fontName, 32, FontStyle.Bold))
                    using (var labelFont = new Font(fontName, 12, FontStyle.Regular))
                    using (var blackBrush = new SolidBrush(Color.Black))
                    using (var blueBrush = new SolidBrush(Color.FromArgb(41, 128, 185)))
                    {
                        g.DrawString("Montréal", cityFont, blueBrush, new PointF(20, 8));
                        g.DrawString("23°C", tempFont, blackBrush, new PointF(20, 35));
                        g.DrawString("Humidity: 65%  Wind: 12 km/h", labelFont, blackBrush, new PointF(20, 75));
                        g.DrawString("Partly Cloudy", labelFont, blackBrush, new PointF(20, 100));
                    }
                }

                // Update picture boxes with the new images
                var oldAlertImage = _alertPreviewPanel.Image;
                var oldWeatherImage = _weatherPreviewPanel.Image;
                
                _alertPreviewPanel.Image = alertBmp;
                _weatherPreviewPanel.Image = weatherBmp;
                
                oldAlertImage?.Dispose();
                oldWeatherImage?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating font preview: {ex.Message}", Logger.LogLevel.Warning);
            }
        }

        private void OnWebUIEnabledChanged()
        {
            if (chkWebUIEnabled.Checked)
            {
                // Start WebUI service
                StartWebUIService();
            }
            else
            {
                // Stop WebUI service
                StopWebUIService();
            }
            UpdateWebUIStatus();
        }
        
        private void StartWebUIService()
        {
            try
            {
                var service = Program.WebUIService;
                if (service == null)
                {
                    // Create new service
                    int port = (int)numWebUIPort.Value;
                    service = new WebUIService(port);
                    Program.SetWebUIService(service);
                    service.Start();
                    Logger.Log($"Web UI service started on port {port}", Logger.LogLevel.Info);
                }
                else if (!service.IsRunning)
                {
                    service.Start();
                    Logger.Log($"Web UI service started", Logger.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to start Web UI service: {ex.Message}", Logger.LogLevel.Error);
                MessageBox.Show($"Failed to start Web UI service: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                chkWebUIEnabled.Checked = false;
            }
        }
        
        private void StopWebUIService()
        {
            try
            {
                var service = Program.WebUIService;
                if (service != null && service.IsRunning)
                {
                    Task.Run(async () => await service.StopAsync()).GetAwaiter().GetResult();
                    Logger.Log("Web UI service stopped", Logger.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to stop Web UI service: {ex.Message}", Logger.LogLevel.Error);
            }
        }
        
        private void UpdateWebUIStatus()
        {
            var service = Program.WebUIService;
            bool isRunning = service?.IsRunning ?? false;
            
            if (isRunning)
            {
                lblWebUIStatus.Text = "✓ Status: Server is running";
                lblWebUIStatus.ForeColor = Color.Green;
            }
            else
            {
                lblWebUIStatus.Text = "✗ Status: Server is not running";
                lblWebUIStatus.ForeColor = Color.Gray;
            }
        }
        
        private void UpdateWebUIUrl()
        {
            try
            {
                int port = (int)numWebUIPort.Value;
                var hostname = chkWebUIAllowRemote.Checked ? Environment.MachineName : "localhost";
                txtWebUIUrl.Text = $"http://{hostname}:{port}";
            }
            catch
            {
                txtWebUIUrl.Text = "http://localhost:5000";
            }
        }

        private void TestWebUIConnection()
        {
            try
            {
                int port = (int)numWebUIPort.Value;
                using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    var response = client.GetAsync($"http://localhost:{port}/api/status").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        lblWebUIStatus.Text = "✓ Status: Server is running and accessible";
                        lblWebUIStatus.ForeColor = Color.Green;
                    }
                    else
                    {
                        lblWebUIStatus.Text = "⚠ Status: Server responded with error";
                        lblWebUIStatus.ForeColor = Color.Orange;
                    }
                }
            }
            catch (Exception ex)
            {
                lblWebUIStatus.Text = "✗ Status: Server is not running or not accessible";
                lblWebUIStatus.ForeColor = Color.Red;
                Logger.Log($"Web UI connection test failed: {ex.Message}", Logger.LogLevel.Debug);
            }
        }

        public void OpenWebUIInBrowser()
        {
            try
            {
                var url = txtWebUIUrl.Text;
                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("URL is not configured.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }    }
}