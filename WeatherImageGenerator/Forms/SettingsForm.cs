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
        private bool _isLoadingSettings = false;
        
        // UI Style Constants
        private static readonly Color AccentColor = Color.FromArgb(41, 128, 185);
        private static readonly Color AccentColorLight = Color.FromArgb(52, 152, 219);
        private static readonly Color SuccessColor = Color.FromArgb(39, 174, 96);
        private static readonly Color WarningColor = Color.FromArgb(230, 126, 34);
        private static readonly Color DangerColor = Color.FromArgb(192, 57, 43);
        private static readonly Color BackgroundColor = Color.FromArgb(248, 249, 250);
        private static readonly Color CardColor = Color.White;
        private static readonly Color BorderColor = Color.FromArgb(222, 226, 230);
        private static readonly Color TextColor = Color.FromArgb(33, 37, 41);
        private static readonly Color TextMutedColor = Color.FromArgb(108, 117, 125);
        private static readonly Font HeaderFont = new Font("Segoe UI", 12F, FontStyle.Bold);
        private static readonly Font SubHeaderFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        private static readonly Font LabelFont = new Font("Segoe UI", 9.5F);
        private static readonly Font SmallFont = new Font("Segoe UI", 8.5F);
        private static readonly Font HelpFont = new Font("Segoe UI", 8F, FontStyle.Italic);
        
        // Controls
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
        ComboBox cmbFontFamily;
        PictureBox _alertPreviewPanel;
        PictureBox _weatherPreviewPanel;
        ComboBox cmbCodec;
        ComboBox cmbBitrate;
        ComboBox cmbQualityPreset;
        NumericUpDown numFps;
        ComboBox cmbContainer;
        CheckBox chkVideoGeneration;
        CheckBox chkVerbose;
        CheckBox chkShowFfmpeg;
        CheckBox chkEnableHardwareEncoding;
        CheckBox chkEnableExperimental;
        TabPage tabExperimental;
        CheckBox chkUseCrfEncoding;
        NumericUpDown numCrf;
        ComboBox cmbEncoderPreset;
        TextBox txtMaxBitrate;
        TextBox txtBufferSize;
        ComboBox cmbFfmpegSource;
        TextBox txtFfmpegCustomPath;
        Button btnBrowseFfmpegPath;
        Label lblFfmpegStatus;
        Button btnValidateFfmpeg;
        Button btnClearFfmpegCache;
        Button btnDownloadBundled;
        CheckBox chkMinimizeToTray;
        CheckBox chkMinimizeToTrayOnClose;
        CheckBox chkAutoStartCycle;
        CheckBox chkStartWithWindows;
        CheckBox chkStartMinimizedToTray;
        Label lblHwStatus;
        Button btnCheckHw;
        CheckBox chkAlertReadyEnabled;
        TextBox txtAlertReadyFeedUrls;
        CheckBox chkAlertReadyIncludeTests;
        NumericUpDown numAlertReadyMaxAgeHours;
        ComboBox cmbAlertReadyLanguage;
        TextBox txtAlertReadyAreaFilters;
        TextBox txtAlertReadyJurisdictions;
        CheckBox chkAlertReadyHighRiskOnly;
        CheckBox chkAlertReadyExcludeWeather;
        ComboBox cmbTtsVoice;
        TextBox txtTtsRate;
        TextBox txtTtsPitch;
        ComboBox cmbMapStyle;
        NumericUpDown numMapZoomLevel;
        TextBox txtMapBackgroundColor;
        NumericUpDown numMapOverlayOpacity;
        NumericUpDown numMapTileTimeout;
        CheckBox chkMapEnableCache;
        TextBox txtMapCacheDirectory;
        NumericUpDown numMapCacheDuration;
        CheckBox chkWebUIEnabled;
        NumericUpDown numWebUIPort;
        CheckBox chkWebUIAllowRemote;
        Label lblWebUIStatus;
        Button btnTestWebUI;
        TextBox txtWebUIUrl;
        CheckBox chkMapUseDarkMode;
        CheckBox chkSkipDetailedWeatherOnAlert;
        NumericUpDown numPlayRadarAnimationCountOnAlert;
        NumericUpDown numAlertDisplayDurationSeconds;

        public SettingsForm()
        {
            InitializeForm();
            BuildUI();
            LoadSettings();
        }

        private void InitializeForm()
        {
            this.Text = "⚙ Settings";
            this.Width = 820;
            this.Height = 740;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = BackgroundColor;
            this.Font = LabelFont;
            this.Padding = new Padding(10);
        }

        #region UI Helper Methods
        
        private Panel CreateCard(int x, int y, int width, int height)
        {
            return new Panel
            {
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                BackColor = CardColor,
                Padding = new Padding(15),
            };
        }

        private Label CreateSectionHeader(string text, int x, int y, string icon = "")
        {
            return new Label
            {
                Text = string.IsNullOrEmpty(icon) ? text : $"{icon} {text}",
                Left = x,
                Top = y,
                Width = 650,
                Height = 28,
                Font = HeaderFont,
                ForeColor = AccentColor,
                AutoSize = false
            };
        }

        private Label CreateSubHeader(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                Width = 400,
                Height = 24,
                Font = SubHeaderFont,
                ForeColor = TextColor,
                AutoSize = false
            };
        }

        private Label CreateLabel(string text, int x, int y, int width = 180)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = 24,
                Font = LabelFont,
                ForeColor = TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        private Label CreateHelpLabel(string text, int x, int y, int width = 200)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = 20,
                Font = HelpFont,
                ForeColor = TextMutedColor,
                AutoSize = false
            };
        }

        private Label CreateDivider(int x, int y, int width)
        {
            return new Label
            {
                Left = x,
                Top = y,
                Width = width,
                Height = 1,
                BackColor = BorderColor,
                AutoSize = false
            };
        }

        private Button CreatePrimaryButton(string text, int x, int y, int width = 130, int height = 32)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                BackColor = AccentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = AccentColorLight;
            btn.MouseLeave += (s, e) => btn.BackColor = AccentColor;
            return btn;
        }

        private Button CreateSecondaryButton(string text, int x, int y, int width = 130, int height = 32)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(233, 236, 239),
                ForeColor = TextColor,
                FlatStyle = FlatStyle.Flat,
                Font = LabelFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = BorderColor;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(222, 226, 230);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(233, 236, 239);
            return btn;
        }

        private Button CreateSuccessButton(string text, int x, int y, int width = 130, int height = 32)
        {
            var btn = new Button
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                BackColor = SuccessColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(46, 204, 113);
            btn.MouseLeave += (s, e) => btn.BackColor = SuccessColor;
            return btn;
        }

        private GroupBox CreateGroupBox(string title, int x, int y, int width, int height)
        {
            return new GroupBox
            {
                Text = title,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
                Font = SubHeaderFont,
                ForeColor = TextColor,
                BackColor = CardColor,
                Padding = new Padding(10)
            };
        }

        private CheckBox CreateCheckBox(string text, int x, int y, int width = 300)
        {
            return new CheckBox
            {
                Text = text,
                Left = x,
                Top = y,
                Width = width,
                Height = 24,
                Font = LabelFont,
                ForeColor = TextColor,
                AutoSize = false
            };
        }

        private ComboBox CreateComboBox(int x, int y, int width = 200)
        {
            return new ComboBox
            {
                Left = x,
                Top = y,
                Width = width,
                Font = LabelFont,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
        }

        private NumericUpDown CreateNumericUpDown(int x, int y, int width = 100, decimal min = 0, decimal max = 100, decimal value = 0)
        {
            return new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = width,
                Font = LabelFont,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value))
            };
        }

        private TextBox CreateTextBox(int x, int y, int width = 200)
        {
            return new TextBox
            {
                Left = x,
                Top = y,
                Width = width,
                Font = LabelFont
            };
        }

        #endregion

        private void BuildUI()
        {
            var tabControl = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 615,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Padding = new Point(12, 6)
            };

            // Build all tabs
            var tabGeneral = BuildGeneralTab();
            var tabImage = BuildImageTab();
            var tabVideo = BuildVideoTab();
            var tabFfmpeg = BuildFfmpegTab();
            var tabOpenMap = BuildOpenMapTab();
            var tabEas = BuildEasTab();
            var tabWebUI = BuildWebUITab();
            tabExperimental = BuildExperimentalTab();

            tabControl.TabPages.AddRange(new TabPage[] { 
                tabGeneral, tabImage, tabVideo, tabFfmpeg, 
                tabOpenMap, tabEas, tabWebUI, tabExperimental 
            });

            // Footer buttons
            var btnSave = CreateSuccessButton("✔ Save Settings", 470, 630, 140, 38);
            btnSave.Click += (s, e) => SaveClicked();

            var btnCancel = CreateSecondaryButton("✖ Cancel", 620, 630, 110, 38);
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.Add(tabControl);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

            // Position form relative to owner
            this.Shown += (s, e) => {
                if (this.Owner != null)
                {
                    this.Location = new Point(
                        this.Owner.Location.X + (this.Owner.Width - this.Width) / 2,
                        this.Owner.Location.Y + (this.Owner.Height - this.Height) / 2
                    );
                }
            };
        }

        #region Tab Builders

        private TabPage BuildGeneralTab()
        {
            var tab = new TabPage("⚙ General") { BackColor = BackgroundColor, Padding = new Padding(20) };
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // Application Settings Section
            var lblAppSettings = CreateSectionHeader("Application Settings", labelX, y, "🔧");
            y += 35;

            var lblRefresh = CreateLabel("Refresh Interval:", labelX, y);
            numRefresh = CreateNumericUpDown(fieldX, y - 2, 100, 1, 1440, 10);
            var lblRefreshUnit = CreateHelpLabel("minutes", fieldX + 110, y + 2);
            y += rowHeight;

            var lblTheme = CreateLabel("Color Theme:", labelX, y);
            cmbTheme = CreateComboBox(fieldX, y - 2, 180);
            cmbTheme.Items.AddRange(new object[] { "Blue", "Light", "Dark", "Green" });
            cmbTheme.SelectedIndex = 0;
            y += rowHeight;

            // Divider
            var divider1 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // Output Directories Section
            var lblOutputDirs = CreateSectionHeader("Output Directories", labelX, y, "📁");
            y += 35;

            var lblOutImg = CreateLabel("Image Output:", labelX, y);
            txtImageOutputDir = CreateTextBox(fieldX, y - 2, 380);
            var btnBrowseImg = CreateSecondaryButton("...", fieldX + 390, y - 3, 40, 26);
            btnBrowseImg.Click += (s, e) => BrowseClicked(txtImageOutputDir);
            y += rowHeight;

            var lblOutVid = CreateLabel("Video Output:", labelX, y);
            txtVideoOutputDir = CreateTextBox(fieldX, y - 2, 380);
            var btnBrowseVid = CreateSecondaryButton("...", fieldX + 390, y - 3, 40, 26);
            btnBrowseVid.Click += (s, e) => BrowseClicked(txtVideoOutputDir);
            y += rowHeight;

            // Divider
            var divider2 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // System Tray Section
            var lblTraySettings = CreateSectionHeader("System Tray & Startup", labelX, y, "💻");
            y += 35;

            chkMinimizeToTray = CreateCheckBox("Minimize to system tray when minimizing", labelX, y, 350);
            y += 30;

            chkMinimizeToTrayOnClose = CreateCheckBox("Minimize to tray when closing (X button)", labelX, y, 350);
            y += 30;

            chkAutoStartCycle = CreateCheckBox("Auto-start weather update cycle on application launch", labelX, y, 420);
            y += 35;

            chkStartWithWindows = CreateCheckBox("Start WSG when Windows starts", labelX, y, 350);
            y += 28;

            chkStartMinimizedToTray = CreateCheckBox("  └─ Start minimized to system tray", labelX + 20, y, 350);
            chkStartMinimizedToTray.ForeColor = TextMutedColor;
            chkStartMinimizedToTray.Enabled = false;
            
            chkStartWithWindows.CheckedChanged += (s, e) => {
                chkStartMinimizedToTray.Enabled = chkStartWithWindows.Checked;
                chkStartMinimizedToTray.ForeColor = chkStartWithWindows.Checked ? TextColor : TextMutedColor;
            };

            tab.Controls.AddRange(new Control[] {
                lblAppSettings, lblRefresh, numRefresh, lblRefreshUnit,
                lblTheme, cmbTheme, divider1,
                lblOutputDirs, lblOutImg, txtImageOutputDir, btnBrowseImg,
                lblOutVid, txtVideoOutputDir, btnBrowseVid, divider2,
                lblTraySettings, chkMinimizeToTray, chkMinimizeToTrayOnClose,
                chkAutoStartCycle, chkStartWithWindows, chkStartMinimizedToTray
            });

            return tab;
        }

        private TabPage BuildImageTab()
        {
            var tab = new TabPage("🖼 Image") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // Image Settings Section
            var lblImgSettings = CreateSectionHeader("Image Generation Settings", labelX, y, "🎨");
            y += 35;

            var lblImgSize = CreateLabel("Resolution (W × H):", labelX, y);
            numImgWidth = CreateNumericUpDown(fieldX, y - 2, 90, 320, 7680, 1920);
            numImgWidth.Increment = 10;
            var lblX = new Label { Text = "×", Left = fieldX + 95, Top = y, Width = 20, Height = 24, TextAlign = ContentAlignment.MiddleCenter, Font = LabelFont };
            numImgHeight = CreateNumericUpDown(fieldX + 118, y - 2, 90, 240, 4320, 1080);
            numImgHeight.Increment = 10;
            var lblPixels = CreateHelpLabel("pixels", fieldX + 215, y + 2);
            y += rowHeight;

            var lblFormat = CreateLabel("Image Format:", labelX, y);
            cmbImgFormat = CreateComboBox(fieldX, y - 2, 120);
            cmbImgFormat.Items.AddRange(new object[] { "png", "jpeg", "bmp", "gif" });
            cmbImgFormat.SelectedIndex = 0;
            var lblFormatHelp = CreateHelpLabel("PNG recommended for quality", fieldX + 130, y + 2, 200);
            y += rowHeight;

            var lblFontFamily = CreateLabel("Font Family:", labelX, y);
            cmbFontFamily = CreateComboBox(fieldX, y - 2, 250);
            try
            {
                var installedFonts = FontFamily.Families.Select(f => f.Name).OrderBy(n => n).ToArray();
                cmbFontFamily.Items.AddRange(installedFonts.Cast<object>().ToArray());
                if (cmbFontFamily.Items.Count > 0) cmbFontFamily.SelectedIndex = 0;
            }
            catch
            {
                cmbFontFamily.Items.AddRange(new object[] { "Arial", "Segoe UI", "Times New Roman", "Courier New", "Georgia", "Tahoma", "Verdana" });
                cmbFontFamily.SelectedIndex = 0;
            }
            y += rowHeight;

            // Divider
            var divider1 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // Features Section
            var lblFeatures = CreateSectionHeader("Image Features", labelX, y, "✨");
            y += 35;

            chkEnableProvinceRadar = CreateCheckBox("Enable Province Radar Animation", labelX, y, 300);
            y += 30;

            chkEnableWeatherMaps = CreateCheckBox("Enable Weather Maps Generation", labelX, y, 300);
            y += 35;

            var btnRegenIcons = CreateSecondaryButton("🔄 Regenerate Icons", labelX, y, 160, 30);
            btnRegenIcons.Click += (s, e) => RegenerateIcons(btnRegenIcons);
            y += 45;

            // Divider
            var divider2 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // Font Preview Section
            var lblPreview = CreateSectionHeader("Font Preview", labelX, y, "👁");
            y += 35;

            _alertPreviewPanel = new PictureBox
            {
                Left = labelX,
                Top = y,
                Width = 700,
                Height = 110,
                BorderStyle = BorderStyle.None,
                BackColor = CardColor,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            y += 120;

            _weatherPreviewPanel = new PictureBox
            {
                Left = labelX,
                Top = y,
                Width = 700,
                Height = 110,
                BorderStyle = BorderStyle.None,
                BackColor = CardColor,
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            cmbFontFamily.SelectedIndexChanged += (s, e) => UpdateFontPreview();
            UpdateFontPreview();

            tab.Controls.AddRange(new Control[] {
                lblImgSettings, lblImgSize, numImgWidth, lblX, numImgHeight, lblPixels,
                lblFormat, cmbImgFormat, lblFormatHelp,
                lblFontFamily, cmbFontFamily, divider1,
                lblFeatures, chkEnableProvinceRadar, chkEnableWeatherMaps, btnRegenIcons, divider2,
                lblPreview, _alertPreviewPanel, _weatherPreviewPanel
            });

            return tab;
        }

        private TabPage BuildVideoTab()
        {
            var tab = new TabPage("🎥 Video") { BackColor = BackgroundColor, Padding = new Padding(15), AutoScroll = true };
            int y = 10;
            int leftCol = 15;
            int rightCol = 380;
            int colWidth = 345;
            int grpHeight = 130;
            int innerPad = 15;

            // Main Toggle
            chkVideoGeneration = CreateCheckBox("  Enable Video Generation", leftCol, y, 280);
            chkVideoGeneration.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            chkVideoGeneration.ForeColor = AccentColor;
            y += 40;

            // Row 1: Alert Settings & Output Format
            var grpAlerts = CreateGroupBox("🚨 Alert Behavior", leftCol, y, colWidth, grpHeight);
            int aY = 25;
            
            chkSkipDetailedWeatherOnAlert = CreateCheckBox("Skip detailed weather during alerts", innerPad, aY, 280);
            chkSkipDetailedWeatherOnAlert.Font = SmallFont;
            aY += 28;
            
            var lblRadarCount = CreateLabel("Radar replays:", innerPad, aY);
            lblRadarCount.Width = 95;
            lblRadarCount.Font = SmallFont;
            numPlayRadarAnimationCountOnAlert = CreateNumericUpDown(innerPad + 100, aY - 2, 55, 1, 10, 1);
            var lblRadarHelp = CreateHelpLabel("times during alert", innerPad + 160, aY);
            aY += 30;
            
            var lblAlertDur = CreateLabel("Alert duration:", innerPad, aY);
            lblAlertDur.Width = 95;
            lblAlertDur.Font = SmallFont;
            numAlertDisplayDurationSeconds = CreateNumericUpDown(innerPad + 100, aY - 2, 55, 1, 120, 6);
            numAlertDisplayDurationSeconds.DecimalPlaces = 1;
            numAlertDisplayDurationSeconds.Increment = 0.5M;
            var lblAlertDurHelp = CreateHelpLabel("seconds", innerPad + 160, aY);
            
            grpAlerts.Controls.AddRange(new Control[] {
                chkSkipDetailedWeatherOnAlert, lblRadarCount, numPlayRadarAnimationCountOnAlert, lblRadarHelp,
                lblAlertDur, numAlertDisplayDurationSeconds, lblAlertDurHelp
            });

            var grpFormat = CreateGroupBox("📹 Output Format", rightCol, y, colWidth, grpHeight);
            int fY = 25;
            
            var lblQualityPreset = CreateLabel("Quality Preset:", innerPad, fY);
            lblQualityPreset.Width = 100;
            lblQualityPreset.Font = SmallFont;
            cmbQualityPreset = CreateComboBox(innerPad + 105, fY - 2, 180);
            cmbQualityPreset.Items.AddRange(new object[] { "Ultra (Best Quality)", "High Quality", "Balanced", "Web Optimized", "Low Bandwidth", "Custom" });
            cmbQualityPreset.SelectedIndex = 2;
            fY += 30;
            
            var lblResPreset = CreateLabel("Resolution:", innerPad, fY);
            lblResPreset.Width = 100;
            lblResPreset.Font = SmallFont;
            cmbResolution = CreateComboBox(innerPad + 105, fY - 2, 180);
            cmbResolution.Items.AddRange(new object[] { "3840x2160 (4K/UHD)", "2560x1440 (2K/QHD)", "1920x1080 (Full HD)", "1600x900 (HD+)", "1280x720 (HD)", "960x540 (qHD)", "854x480 (FWVGA)", "640x480 (VGA)" });
            cmbResolution.SelectedIndex = 2;
            fY += 30;
            
            var lblContainer = CreateLabel("Container:", innerPad, fY);
            lblContainer.Width = 100;
            lblContainer.Font = SmallFont;
            cmbContainer = CreateComboBox(innerPad + 105, fY - 2, 80);
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;
            
            var lblFps = CreateLabel("FPS:", innerPad + 195, fY);
            lblFps.Width = 35;
            lblFps.Font = SmallFont;
            numFps = CreateNumericUpDown(innerPad + 235, fY - 2, 55, 1, 240, 30);
            
            grpFormat.Controls.AddRange(new Control[] {
                lblQualityPreset, cmbQualityPreset, lblResPreset, cmbResolution,
                lblContainer, cmbContainer, lblFps, numFps
            });

            y += grpHeight + 10;

            // Row 2: Timing & Encoding
            var grpTiming = CreateGroupBox("⏱ Timing", leftCol, y, colWidth, 155);
            int tY = 25;
            
            var lblStatic = CreateLabel("Slide Duration:", innerPad, tY);
            lblStatic.Width = 100;
            lblStatic.Font = SmallFont;
            numStatic = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 1, 60, 8);
            numStatic.DecimalPlaces = 1;
            var lblStaticHelp = CreateHelpLabel("seconds per slide", innerPad + 180, tY);
            tY += 30;
            
            var lblTotal = CreateLabel("Total Duration:", innerPad, tY);
            lblTotal.Width = 100;
            lblTotal.Font = SmallFont;
            numTotalDuration = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 1, 86400, 60);
            numTotalDuration.DecimalPlaces = 1;
            numTotalDuration.Enabled = false;
            var lblTotalHelp = CreateHelpLabel("seconds total", innerPad + 180, tY);
            tY += 28;
            
            chkUseTotalDuration = CreateCheckBox("Use total duration mode", innerPad, tY, 220);
            chkUseTotalDuration.Font = SmallFont;
            chkUseTotalDuration.CheckedChanged += (s, e) => {
                numTotalDuration.Enabled = chkUseTotalDuration.Checked;
                numStatic.Enabled = !chkUseTotalDuration.Checked;
            };
            tY += 32;
            
            var lblFade = CreateLabel("Fade Duration:", innerPad, tY);
            lblFade.Width = 100;
            lblFade.Font = SmallFont;
            lblFade.ForeColor = TextMutedColor;
            numFade = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 0, 10, 0.5M);
            numFade.DecimalPlaces = 2;
            numFade.Increment = 0.1M;
            numFade.Enabled = false;
            
            chkFade = CreateCheckBox("Enable", innerPad + 180, tY, 80);
            chkFade.Font = SmallFont;
            chkFade.Enabled = false;
            chkFade.ForeColor = TextMutedColor;
            
            grpTiming.Controls.AddRange(new Control[] {
                lblStatic, numStatic, lblStaticHelp,
                lblTotal, numTotalDuration, lblTotalHelp,
                chkUseTotalDuration, lblFade, numFade, chkFade
            });

            var grpEncoding = CreateGroupBox("🎬 Encoding", rightCol, y, colWidth, 155);
            int eY = 25;
            
            var lblCodec = CreateLabel("Codec:", innerPad, eY);
            lblCodec.Width = 80;
            lblCodec.Font = SmallFont;
            cmbCodec = CreateComboBox(innerPad + 85, eY - 2, 200);
            cmbCodec.Items.AddRange(new object[] { "libx264 (H.264)", "libx265 (H.265/HEVC)", "libvpx-vp9 (VP9)", "libaom-av1 (AV1)", "mpeg4", "msmpeg4" });
            cmbCodec.SelectedIndex = 0;
            eY += 30;
            
            var lblBitrate = CreateLabel("Bitrate:", innerPad, eY);
            lblBitrate.Width = 80;
            lblBitrate.Font = SmallFont;
            cmbBitrate = CreateComboBox(innerPad + 85, eY - 2, 200);
            cmbBitrate.Items.AddRange(new object[] { "1M (Low)", "2M (Medium-Low)", "4M (Medium)", "6M (Medium-High)", "8M (High)", "12M (Very High)", "16M (Ultra)" });
            cmbBitrate.SelectedIndex = 2;
            eY += 30;
            
            chkEnableHardwareEncoding = CreateCheckBox("⚡ Hardware Encoding (NVENC)", innerPad, eY, 220);
            chkEnableHardwareEncoding.Font = SmallFont;
            btnCheckHw = CreateSecondaryButton("Check", innerPad + 225, eY - 3, 60, 24);
            btnCheckHw.Font = SmallFont;
            eY += 28;
            
            lblHwStatus = CreateHelpLabel("Click Check to verify GPU support", innerPad + 20, eY, 280);
            
            grpEncoding.Controls.AddRange(new Control[] {
                lblCodec, cmbCodec, lblBitrate, cmbBitrate,
                chkEnableHardwareEncoding, btnCheckHw, lblHwStatus
            });

            y += 165;

            // Row 3: Debug Options
            var grpDebug = CreateGroupBox("🔧 Debug & Advanced", leftCol, y, colWidth * 2 + 20, 85);
            int dY = 25;
            
            chkVerbose = CreateCheckBox("Verbose FFmpeg Output", innerPad, dY, 200);
            chkVerbose.Font = SmallFont;
            
            chkShowFfmpeg = CreateCheckBox("Show FFmpeg Console", innerPad + 210, dY, 200);
            chkShowFfmpeg.Font = SmallFont;
            
            chkEnableExperimental = CreateCheckBox("⚠ Enable Experimental Features", innerPad + 440, dY, 240);
            chkEnableExperimental.Font = SmallFont;
            chkEnableExperimental.ForeColor = WarningColor;
            chkEnableExperimental.CheckedChanged += (s, e) => {
                if (tabExperimental != null) tabExperimental.Enabled = chkEnableExperimental.Checked;
            };
            dY += 30;
            
            var lblDebugTip = CreateHelpLabel("💡 Enable Debug options for troubleshooting. Experimental tab unlocks advanced encoder settings.", innerPad, dY, 680);
            
            grpDebug.Controls.AddRange(new Control[] {
                chkVerbose, chkShowFfmpeg, chkEnableExperimental, lblDebugTip
            });

            // Setup event handlers
            SetupVideoTabEventHandlers();

            tab.Controls.AddRange(new Control[] {
                chkVideoGeneration, grpAlerts, grpFormat, grpTiming, grpEncoding, grpDebug
            });

            return tab;
        }

        private void SetupVideoTabEventHandlers()
        {
            // Quality preset handler
            cmbQualityPreset.SelectedIndexChanged += (s, e) => {
                if (_isLoadingSettings || cmbQualityPreset.SelectedIndex == 5) return;
                
                _isLoadingSettings = true;
                switch (cmbQualityPreset.SelectedIndex)
                {
                    case 0: // Ultra
                        cmbResolution.SelectedIndex = 0;
                        cmbCodec.SelectedIndex = 0;
                        cmbBitrate.SelectedIndex = 6;
                        numFps.Value = 60;
                        break;
                    case 1: // High Quality
                        cmbResolution.SelectedIndex = 2;
                        cmbCodec.SelectedIndex = 0;
                        cmbBitrate.SelectedIndex = 4;
                        numFps.Value = 30;
                        break;
                    case 2: // Balanced
                        cmbResolution.SelectedIndex = 2;
                        cmbCodec.SelectedIndex = 0;
                        cmbBitrate.SelectedIndex = 2;
                        numFps.Value = 30;
                        break;
                    case 3: // Web Optimized
                        cmbResolution.SelectedIndex = 4;
                        cmbCodec.SelectedIndex = 0;
                        cmbBitrate.SelectedIndex = 1;
                        numFps.Value = 30;
                        break;
                    case 4: // Low Bandwidth
                        cmbResolution.SelectedIndex = 6;
                        cmbCodec.SelectedIndex = 0;
                        cmbBitrate.SelectedIndex = 0;
                        numFps.Value = 24;
                        break;
                }
                _isLoadingSettings = false;
            };

            // Mark as custom when user changes
            EventHandler markCustom = (s, e) => {
                if (!_isLoadingSettings && cmbQualityPreset.SelectedIndex != 5)
                    cmbQualityPreset.SelectedIndex = 5;
            };
            cmbResolution.SelectedIndexChanged += markCustom;
            cmbCodec.SelectedIndexChanged += markCustom;
            cmbBitrate.SelectedIndexChanged += markCustom;
            numFps.ValueChanged += markCustom;

            // Sync alert duration
            numStatic.ValueChanged += (s, e) => {
                if (!_isLoadingSettings)
                    numAlertDisplayDurationSeconds.Value = numStatic.Value;
            };

            // Hardware check
            btnCheckHw.Click += (s, e) => {
                btnCheckHw.Enabled = false;
                lblHwStatus.Text = "Checking...";
                Task.Run(() => {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    this.Invoke((Action)(() => {
                        lblHwStatus.Text = msg;
                        lblHwStatus.ForeColor = ok ? SuccessColor : DangerColor;
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
        }

        private TabPage BuildFfmpegTab()
        {
            var tab = new TabPage("🎬 FFmpeg") { BackColor = BackgroundColor, Padding = new Padding(20) };
            int y = 15;
            int labelX = 20;
            int fieldX = 180;

            // Source Section
            var lblSource = CreateSectionHeader("FFmpeg Source Configuration", labelX, y, "📦");
            y += 35;

            var lblDesc = CreateHelpLabel("Choose where to get FFmpeg binaries from:", labelX, y, 400);
            y += 30;

            var lblSourceLabel = CreateLabel("Source:", labelX, y, 100);
            cmbFfmpegSource = CreateComboBox(fieldX, y - 2, 220);
            cmbFfmpegSource.Items.AddRange(new object[] { "Bundled (Auto-download)", "System PATH", "Custom Path" });
            cmbFfmpegSource.SelectedIndex = 0;
            y += 40;

            var lblCustomPath = CreateLabel("Custom Path:", labelX, y, 150);
            txtFfmpegCustomPath = CreateTextBox(fieldX, y - 2, 380);
            txtFfmpegCustomPath.Enabled = false;
            btnBrowseFfmpegPath = CreateSecondaryButton("...", fieldX + 390, y - 3, 40, 26);
            btnBrowseFfmpegPath.Enabled = false;
            btnBrowseFfmpegPath.Click += (s, e) => {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select FFmpeg directory (containing ffmpeg.exe)";
                    if (dlg.ShowDialog() == DialogResult.OK)
                        txtFfmpegCustomPath.Text = dlg.SelectedPath;
                }
            };

            cmbFfmpegSource.SelectedIndexChanged += (s, e) => {
                bool isCustom = cmbFfmpegSource.SelectedIndex == 2;
                txtFfmpegCustomPath.Enabled = isCustom;
                btnBrowseFfmpegPath.Enabled = isCustom;
            };

            y += 55;
            var divider1 = CreateDivider(labelX, y, 700);
            y += 30;

            // Status Section
            var lblStatusHeader = CreateSectionHeader("Status", labelX, y, "📋");
            y += 32;

            lblFfmpegStatus = new Label
            {
                Text = "Not validated",
                Left = labelX,
                Top = y,
                Width = 600,
                Height = 28,
                Font = LabelFont,
                ForeColor = TextMutedColor,
                AutoSize = false
            };
            y += 40;

            btnValidateFfmpeg = CreatePrimaryButton("✓ Validate", labelX, y, 110, 32);
            btnValidateFfmpeg.Click += (s, e) => ValidateFfmpegConfiguration();

            btnDownloadBundled = CreateSecondaryButton("⬇ Download", labelX + 120, y, 110, 32);
            btnDownloadBundled.Click += async (s, e) => await DownloadFfmpegAsync();

            btnClearFfmpegCache = CreateSecondaryButton("🗑 Clear Cache", labelX + 240, y, 120, 32);
            btnClearFfmpegCache.Click += (s, e) => {
                var result = MessageBox.Show(
                    "This will delete the downloaded FFmpeg binaries. They will be re-downloaded when needed.\n\nContinue?",
                    "Clear FFmpeg Cache", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    FFmpegLocator.ClearCache();
                    lblFfmpegStatus.Text = "Cache cleared. FFmpeg will be re-downloaded when needed.";
                    lblFfmpegStatus.ForeColor = WarningColor;
                }
            };

            y += 55;
            var divider2 = CreateDivider(labelX, y, 700);
            y += 30;

            // Help Section
            var lblHelpHeader = CreateSectionHeader("Help", labelX, y, "ℹ");
            y += 32;

            var helpText = new Label
            {
                Text = "• Bundled: Automatically downloads FFmpeg to AppData (recommended)\n" +
                       "• System PATH: Uses FFmpeg installed on your system (must be in PATH)\n" +
                       "• Custom Path: Specify a folder containing ffmpeg.exe",
                Left = labelX,
                Top = y,
                Width = 600,
                Height = 65,
                Font = LabelFont,
                ForeColor = TextColor,
                AutoSize = false
            };

            tab.Controls.AddRange(new Control[] {
                lblSource, lblDesc, lblSourceLabel, cmbFfmpegSource,
                lblCustomPath, txtFfmpegCustomPath, btnBrowseFfmpegPath, divider1,
                lblStatusHeader, lblFfmpegStatus, btnValidateFfmpeg, btnDownloadBundled, btnClearFfmpegCache, divider2,
                lblHelpHeader, helpText
            });

            return tab;
        }

        private TabPage BuildOpenMapTab()
        {
            var tab = new TabPage("🗺 OpenMap") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 230;
            int rowHeight = 38;

            // Basic Settings
            var lblBasic = CreateSectionHeader("Basic Map Settings", labelX, y, "🗺");
            y += 35;

            var lblMapStyle = CreateLabel("Default Map Style:", labelX, y);
            cmbMapStyle = CreateComboBox(fieldX, y - 2, 200);
            cmbMapStyle.Items.AddRange(new object[] { "Standard", "Minimal", "Terrain", "Satellite" });
            cmbMapStyle.SelectedIndex = 0;
            y += rowHeight;

            var lblMapZoom = CreateLabel("Default Zoom Level:", labelX, y);
            numMapZoomLevel = CreateNumericUpDown(fieldX, y - 2, 80, 0, 18, 10);
            var lblZoomHelp = CreateHelpLabel("(7-10 for regional weather)", fieldX + 90, y + 2, 180);
            y += rowHeight;

            var lblMapBgColor = CreateLabel("Background Color (Hex):", labelX, y);
            txtMapBackgroundColor = CreateTextBox(fieldX, y - 2, 120);
            txtMapBackgroundColor.Text = "#D3D3D3";
            var lblBgHelp = CreateHelpLabel("e.g., #E8F4F8 for light blue", fieldX + 130, y + 2, 180);
            y += rowHeight;

            var lblMapOpacity = CreateLabel("Overlay Opacity:", labelX, y);
            numMapOverlayOpacity = CreateNumericUpDown(fieldX, y - 2, 80, 0, 100, 70);
            var lblOpacityUnit = CreateHelpLabel("% (70-85 recommended)", fieldX + 90, y + 2, 180);
            y += rowHeight;

            chkMapUseDarkMode = CreateCheckBox("🌙 Use Dark Mode (for Terrain style)", labelX, y, 350);
            var lblDarkHelp = CreateHelpLabel("Best for night weather displays", labelX + 360, y + 3, 200);
            y += rowHeight + 10;

            var divider1 = CreateDivider(labelX, y, 700);
            y += 25;

            // Performance Settings
            var lblPerf = CreateSectionHeader("Performance & Caching", labelX, y, "⚡");
            y += 35;

            var lblMapTimeout = CreateLabel("Tile Download Timeout:", labelX, y);
            numMapTileTimeout = CreateNumericUpDown(fieldX, y - 2, 80, 10, 120, 30);
            var lblTimeoutUnit = CreateHelpLabel("seconds", fieldX + 90, y + 2);
            y += rowHeight;

            chkMapEnableCache = CreateCheckBox("Enable Tile Caching (Recommended)", labelX, y, 350);
            chkMapEnableCache.Checked = true;
            y += 32;

            var lblCacheDir = CreateLabel("Cache Directory:", labelX, y);
            txtMapCacheDirectory = CreateTextBox(fieldX, y - 2, 200);
            txtMapCacheDirectory.Text = "MapCache";
            chkMapEnableCache.CheckedChanged += (s, e) => txtMapCacheDirectory.Enabled = chkMapEnableCache.Checked;
            y += rowHeight;

            var lblCacheDuration = CreateLabel("Cache Duration:", labelX, y);
            numMapCacheDuration = CreateNumericUpDown(fieldX, y - 2, 80, 1, 8760, 168);
            var lblCacheHelp = CreateHelpLabel("hours (168 = 7 days)", fieldX + 90, y + 2, 150);
            y += rowHeight + 10;

            var divider2 = CreateDivider(labelX, y, 700);
            y += 25;

            // Style Guide
            var lblStyleGuide = CreateSectionHeader("Map Style Reference", labelX, y, "📖");
            y += 35;

            var styleGuide = new Label
            {
                Text = "• Standard: Traditional OpenStreetMap with detailed roads and cities\n" +
                       "• Minimal: Clean, simplified style (HOT)\n" +
                       "• Terrain: Topographic with elevation contours\n" +
                       "• Satellite: High-resolution imagery (Esri)",
                Left = labelX,
                Top = y,
                Width = 600,
                Height = 90,
                Font = LabelFont,
                ForeColor = TextColor,
                AutoSize = false
            };
            y += 100;

            var lblAttribution = new Label
            {
                Text = "⚠ Legal: Generated maps automatically include required attribution per OSM usage policy.",
                Left = labelX,
                Top = y,
                Width = 650,
                Height = 25,
                Font = HelpFont,
                ForeColor = DangerColor,
                AutoSize = false
            };

            tab.Controls.AddRange(new Control[] {
                lblBasic, lblMapStyle, cmbMapStyle, lblMapZoom, numMapZoomLevel, lblZoomHelp,
                lblMapBgColor, txtMapBackgroundColor, lblBgHelp,
                lblMapOpacity, numMapOverlayOpacity, lblOpacityUnit,
                chkMapUseDarkMode, lblDarkHelp, divider1,
                lblPerf, lblMapTimeout, numMapTileTimeout, lblTimeoutUnit,
                chkMapEnableCache, lblCacheDir, txtMapCacheDirectory,
                lblCacheDuration, numMapCacheDuration, lblCacheHelp, divider2,
                lblStyleGuide, styleGuide, lblAttribution
            });

            return tab;
        }

        private TabPage BuildEasTab()
        {
            var tab = new TabPage("🚨 EAS & TTS") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 230;
            int rowHeight = 35;

            // Alert Ready Section
            var lblAlertReady = CreateSectionHeader("Alert Ready (NAAD)", labelX, y, "🚨");
            y += 35;

            chkAlertReadyEnabled = CreateCheckBox("Enable Alert Ready", labelX, y, 250);
            y += rowHeight;

            var lblFeedUrls = CreateLabel("Feed URLs:", labelX, y);
            y += 25;
            txtAlertReadyFeedUrls = new TextBox
            {
                Left = labelX,
                Top = y,
                Width = 500,
                Height = 55,
                Font = SmallFont,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            y += 65;

            chkAlertReadyIncludeTests = CreateCheckBox("Include Test Alerts", labelX, y, 200);
            y += rowHeight;

            var lblMaxAge = CreateLabel("Max Alert Age:", labelX, y);
            numAlertReadyMaxAgeHours = CreateNumericUpDown(fieldX, y - 2, 80, 0, 168, 24);
            var lblMaxAgeUnit = CreateHelpLabel("hours", fieldX + 90, y + 2);
            y += rowHeight;

            var lblLanguage = CreateLabel("Preferred Language:", labelX, y);
            cmbAlertReadyLanguage = CreateComboBox(fieldX, y - 2, 120);
            cmbAlertReadyLanguage.Items.AddRange(new object[] { "en-CA", "fr-CA" });
            cmbAlertReadyLanguage.SelectedIndex = 0;
            y += rowHeight;

            var lblAreaFilters = CreateLabel("Area Filters:", labelX, y);
            txtAlertReadyAreaFilters = CreateTextBox(fieldX, y - 2, 270);
            var lblAreaHelp = CreateHelpLabel("comma-separated", fieldX + 280, y + 2);
            y += rowHeight;

            var lblJurisdictions = CreateLabel("Jurisdictions:", labelX, y);
            txtAlertReadyJurisdictions = CreateTextBox(fieldX, y - 2, 270);
            txtAlertReadyJurisdictions.Text = "QC, CA";
            var lblJurisHelp = CreateHelpLabel("comma-separated", fieldX + 280, y + 2);
            y += rowHeight;

            chkAlertReadyHighRiskOnly = CreateCheckBox("High Risk Alerts Only (Severe/Extreme)", labelX, y, 320);
            y += 30;

            chkAlertReadyExcludeWeather = CreateCheckBox("Exclude Weather Alerts (use ECCC instead)", labelX, y, 350);
            y += 40;

            var divider = CreateDivider(labelX, y, 700);
            y += 25;

            // TTS Section
            var lblTts = CreateSectionHeader("Text-to-Speech (EdgeTTS)", labelX, y, "🎤");
            y += 35;

            var lblVoice = CreateLabel("Voice:", labelX, y);
            cmbTtsVoice = CreateComboBox(fieldX, y - 2, 220);
            cmbTtsVoice.Items.AddRange(new object[] {
                "fr-CA-SylvieNeural (Female)", "fr-CA-JeanNeural (Male)",
                "fr-CA-AntoineNeural (Male)", "fr-CA-ThierryNeural (Male)",
                "en-CA-ClaraNeural (Female)", "en-CA-LiamNeural (Male)",
                "en-US-JennyNeural (Female)", "en-US-GuyNeural (Male)"
            });
            cmbTtsVoice.SelectedIndex = 0;
            y += rowHeight;

            var lblRate = CreateLabel("Speech Rate:", labelX, y);
            txtTtsRate = CreateTextBox(fieldX, y - 2, 100);
            txtTtsRate.Text = "+0%";
            var lblRateHelp = CreateHelpLabel("e.g., +0%, +10%, -5%", fieldX + 110, y + 2);
            y += rowHeight;

            var lblPitch = CreateLabel("Pitch:", labelX, y);
            txtTtsPitch = CreateTextBox(fieldX, y - 2, 100);
            txtTtsPitch.Text = "+0Hz";
            var lblPitchHelp = CreateHelpLabel("e.g., +0Hz, +10Hz", fieldX + 110, y + 2);
            y += rowHeight + 5;

            var btnDownloadVoices = CreatePrimaryButton("📥 Download Windows TTS Voices", labelX, y, 260, 32);
            btnDownloadVoices.Click += (s, e) => DownloadWindowsVoices();
            y += 45;

            var lblTtsNote = new Label
            {
                Text = "💡 EdgeTTS works online without installation. Windows SAPI is the offline fallback.\n   If using SAPI, install French language packs above for French TTS support.",
                Left = labelX,
                Top = y,
                Width = 550,
                Height = 45,
                Font = SmallFont,
                ForeColor = TextMutedColor,
                AutoSize = false
            };

            tab.Controls.AddRange(new Control[] {
                lblAlertReady, chkAlertReadyEnabled, lblFeedUrls, txtAlertReadyFeedUrls,
                chkAlertReadyIncludeTests, lblMaxAge, numAlertReadyMaxAgeHours, lblMaxAgeUnit,
                lblLanguage, cmbAlertReadyLanguage, lblAreaFilters, txtAlertReadyAreaFilters, lblAreaHelp,
                lblJurisdictions, txtAlertReadyJurisdictions, lblJurisHelp,
                chkAlertReadyHighRiskOnly, chkAlertReadyExcludeWeather, divider,
                lblTts, lblVoice, cmbTtsVoice, lblRate, txtTtsRate, lblRateHelp,
                lblPitch, txtTtsPitch, lblPitchHelp, btnDownloadVoices, lblTtsNote
            });

            return tab;
        }

        private TabPage BuildWebUITab()
        {
            var tab = new TabPage("🌐 Web UI") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // Header
            var lblHeader = CreateSectionHeader("Remote Web Interface", labelX, y, "🌐");
            y += 35;

            var lblDesc = new Label
            {
                Text = "Enable a web interface to access your weather display from any browser on your network.",
                Left = labelX,
                Top = y,
                Width = 600,
                Height = 25,
                Font = LabelFont,
                ForeColor = TextMutedColor,
                AutoSize = false
            };
            y += 40;

            chkWebUIEnabled = CreateCheckBox("Enable Web UI Server", labelX, y, 250);
            chkWebUIEnabled.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            y += 40;

            var divider1 = CreateDivider(labelX, y, 700);
            y += 25;

            // Configuration
            var lblConfig = CreateSubHeader("Configuration", labelX, y);
            y += 30;

            var lblPort = CreateLabel("Port:", labelX, y);
            numWebUIPort = CreateNumericUpDown(fieldX, y - 2, 100, 1024, 65535, 5000);
            var lblPortHelp = CreateHelpLabel("(1024-65535)", fieldX + 110, y + 2);
            y += rowHeight;

            chkWebUIAllowRemote = CreateCheckBox("Allow Remote Access (other computers on network)", labelX, y, 400);
            y += rowHeight;

            var lblUrl = CreateLabel("Access URL:", labelX, y);
            txtWebUIUrl = CreateTextBox(fieldX, y - 2, 350);
            txtWebUIUrl.ReadOnly = true;
            txtWebUIUrl.BackColor = Color.FromArgb(248, 249, 250);
            y += rowHeight + 5;

            lblWebUIStatus = new Label
            {
                Left = labelX,
                Top = y,
                Width = 450,
                Height = 25,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMutedColor,
                Text = "Status: Not running",
                AutoSize = false
            };
            y += 40;

            // Buttons
            btnTestWebUI = CreateSecondaryButton("🔗 Test Connection", labelX, y, 150, 34);
            btnTestWebUI.Click += (s, e) => TestWebUIConnection();

            var btnOpenInBrowser = CreatePrimaryButton("🌐 Open in Browser", labelX + 165, y, 150, 34);
            btnOpenInBrowser.Click += (s, e) => OpenWebUIInBrowser();
            y += 55;

            var divider2 = CreateDivider(labelX, y, 700);
            y += 25;

            // Security Notice
            var lblSecurityHeader = CreateSubHeader("⚠ Security Notice", labelX, y);
            lblSecurityHeader.ForeColor = WarningColor;
            y += 30;

            var lblSecurityNote = new Label
            {
                Text = "When 'Allow Remote Access' is enabled, any device on your local network can access the web interface.\n" +
                       "If you want to access from the internet, you'll need to configure port forwarding on your router.\n" +
                       "Consider using a VPN or reverse proxy with authentication for internet access.",
                Left = labelX,
                Top = y,
                Width = 680,
                Height = 60,
                Font = SmallFont,
                ForeColor = TextColor,
                AutoSize = false
            };

            // Event handlers
            chkWebUIEnabled.CheckedChanged += (s, e) => { if (!_isLoadingSettings) OnWebUIEnabledChanged(); };
            numWebUIPort.ValueChanged += (s, e) => { if (!_isLoadingSettings) UpdateWebUIUrl(); };
            chkWebUIAllowRemote.CheckedChanged += (s, e) => { if (!_isLoadingSettings) UpdateWebUIUrl(); };

            tab.Controls.AddRange(new Control[] {
                lblHeader, lblDesc, chkWebUIEnabled, divider1,
                lblConfig, lblPort, numWebUIPort, lblPortHelp,
                chkWebUIAllowRemote, lblUrl, txtWebUIUrl, lblWebUIStatus,
                btnTestWebUI, btnOpenInBrowser, divider2,
                lblSecurityHeader, lblSecurityNote
            });

            return tab;
        }

        private TabPage BuildExperimentalTab()
        {
            var tab = new TabPage("⚠ Experimental") { BackColor = Color.FromArgb(255, 252, 245), Padding = new Padding(20) };
            tab.Enabled = false;
            
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // Warning Banner
            var warningPanel = new Panel
            {
                Left = labelX,
                Top = y,
                Width = 700,
                Height = 50,
                BackColor = Color.FromArgb(255, 243, 205)
            };
            var lblWarning = new Label
            {
                Text = "⚠ EXPERIMENTAL FEATURES - These settings can affect video quality and encoding performance.\n   Use with caution. Enable from the Video tab's Debug section.",
                Left = 15,
                Top = 8,
                Width = 670,
                Height = 35,
                Font = LabelFont,
                ForeColor = Color.FromArgb(133, 100, 4),
                AutoSize = false
            };
            warningPanel.Controls.Add(lblWarning);
            y += 70;

            // CRF Encoding
            var lblCrfSection = CreateSectionHeader("Quality-Based Encoding (CRF)", labelX, y, "🎯");
            y += 35;

            chkUseCrfEncoding = CreateCheckBox("Use CRF encoding (quality-based)", labelX, y, 280);
            y += 35;

            var lblCrf = CreateLabel("CRF Value:", labelX, y);
            numCrf = CreateNumericUpDown(fieldX, y - 2, 80, 0, 51, 23);
            var lblCrfHelp = CreateHelpLabel("Lower = better quality (18-28 typical)", fieldX + 90, y + 2, 250);
            y += rowHeight + 5;

            var divider1 = CreateDivider(labelX, y, 700);
            y += 25;

            // Encoder Settings
            var lblEncoderSection = CreateSectionHeader("Encoder Settings", labelX, y, "⚙");
            y += 35;

            var lblPreset = CreateLabel("Encoder Preset:", labelX, y);
            cmbEncoderPreset = CreateComboBox(fieldX, y - 2, 150);
            cmbEncoderPreset.Items.AddRange(new object[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });
            cmbEncoderPreset.SelectedIndex = 5;
            var lblPresetHelp = CreateHelpLabel("Slower = smaller file, better quality", fieldX + 160, y + 2, 250);
            y += rowHeight;

            var lblMaxBitrate = CreateLabel("Max Bitrate:", labelX, y);
            txtMaxBitrate = CreateTextBox(fieldX, y - 2, 100);
            var lblMaxBrHelp = CreateHelpLabel("e.g., 8M, 12M (optional)", fieldX + 110, y + 2);
            y += rowHeight;

            var lblBuffer = CreateLabel("Buffer Size:", labelX, y);
            txtBufferSize = CreateTextBox(fieldX, y - 2, 100);
            var lblBufferHelp = CreateHelpLabel("e.g., 16M (optional)", fieldX + 110, y + 2);
            y += rowHeight + 10;

            // Tips
            var lblTips = new Label
            {
                Text = "💡 Tips:\n" +
                       "• CRF mode provides consistent quality but variable file size\n" +
                       "• Use 'slow' or 'slower' preset for best quality when time isn't critical\n" +
                       "• Set Max Bitrate to limit file size while using CRF mode\n" +
                       "• Leave optional fields empty to use defaults",
                Left = labelX,
                Top = y,
                Width = 600,
                Height = 90,
                Font = SmallFont,
                ForeColor = TextColor,
                AutoSize = false
            };

            // Mark custom on change
            EventHandler markCustom = (s, e) => {
                if (!_isLoadingSettings && cmbQualityPreset != null && cmbQualityPreset.SelectedIndex != 5)
                    cmbQualityPreset.SelectedIndex = 5;
            };
            cmbEncoderPreset.SelectedIndexChanged += markCustom;
            txtMaxBitrate.TextChanged += markCustom;
            txtBufferSize.TextChanged += markCustom;
            chkUseCrfEncoding.CheckedChanged += markCustom;
            numCrf.ValueChanged += markCustom;

            tab.Controls.AddRange(new Control[] {
                warningPanel, lblCrfSection, chkUseCrfEncoding, lblCrf, numCrf, lblCrfHelp, divider1,
                lblEncoderSection, lblPreset, cmbEncoderPreset, lblPresetHelp,
                lblMaxBitrate, txtMaxBitrate, lblMaxBrHelp,
                lblBuffer, txtBufferSize, lblBufferHelp, lblTips
            });

            return tab;
        }

        #endregion

        #region Helper Methods

        private void BrowseClicked(TextBox target)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select output directory";
                if (dlg.ShowDialog() == DialogResult.OK)
                    target.Text = dlg.SelectedPath;
            }
        }

        private void RegenerateIcons(Button btn)
        {
            try
            {
                btn.Enabled = false;
                btn.Text = "Generating...";
                string outDir = txtImageOutputDir.Text;
                if (string.IsNullOrWhiteSpace(outDir))
                    outDir = Path.Combine(Directory.GetCurrentDirectory(), "WeatherImages");

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
                btn.Enabled = true;
                btn.Text = "🔄 Regenerate Icons";
            }
        }

        private void DownloadWindowsVoices()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:regionlanguage",
                    UseShellExecute = true
                });
                MessageBox.Show(this,
                    "Windows Settings will open.\n\n" +
                    "To add French TTS voices:\n" +
                    "1. Click 'Add a language'\n" +
                    "2. Search for 'French' and select your region\n" +
                    "3. Check 'Text-to-speech' option\n" +
                    "4. Click Install\n\n" +
                    "After installation, restart the application.",
                    "Download TTS Voices", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open Windows Settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task DownloadFfmpegAsync()
        {
            btnDownloadBundled.Enabled = false;
            lblFfmpegStatus.Text = "Downloading FFmpeg binaries...";
            lblFfmpegStatus.ForeColor = AccentColor;

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
                    lblFfmpegStatus.Text = "✓ FFmpeg downloaded successfully!";
                    lblFfmpegStatus.ForeColor = SuccessColor;
                }
                else
                {
                    lblFfmpegStatus.Text = "✗ Failed to download FFmpeg. Check logs for details.";
                    lblFfmpegStatus.ForeColor = DangerColor;
                }
            }
            catch (Exception ex)
            {
                lblFfmpegStatus.Text = $"✗ Error: {ex.Message}";
                lblFfmpegStatus.ForeColor = DangerColor;
            }
            finally
            {
                btnDownloadBundled.Enabled = true;
            }
        }

        #endregion

        #region Load/Save Settings

        private void LoadSettings()
        {
            _isLoadingSettings = true;
            try
            {
                var cfg = ConfigManager.LoadConfig();
                
                // General Tab
                txtImageOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                txtVideoOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.Video?.OutputDirectory ?? cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                numRefresh.Value = cfg.RefreshTimeMinutes;
                
                var theme = cfg.Theme ?? "Blue";
                if (cmbTheme.Items.Contains(theme)) cmbTheme.SelectedItem = theme;
                else cmbTheme.SelectedItem = "Blue";

                chkMinimizeToTray.Checked = cfg.MinimizeToTray;
                chkMinimizeToTrayOnClose.Checked = cfg.MinimizeToTrayOnClose;
                chkAutoStartCycle.Checked = cfg.AutoStartCycle;
                
                try
                {
                    chkStartWithWindows.Checked = WindowsStartupManager.IsStartupEnabled();
                    if (chkStartWithWindows.Checked != cfg.StartWithWindows)
                        cfg.StartWithWindows = chkStartWithWindows.Checked;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to check Windows startup status: {ex.Message}", Logger.LogLevel.Warning);
                    chkStartWithWindows.Checked = cfg.StartWithWindows;
                }
                
                chkStartMinimizedToTray.Checked = cfg.StartMinimizedToTray;
                chkStartMinimizedToTray.Enabled = chkStartWithWindows.Checked;

                // Image Tab
                numImgWidth.Value = cfg.ImageGeneration?.ImageWidth ?? 1920;
                numImgHeight.Value = cfg.ImageGeneration?.ImageHeight ?? 1080;
                var fmt = (cfg.ImageGeneration?.ImageFormat ?? "png").ToLowerInvariant();
                if (cmbImgFormat.Items.Contains(fmt)) cmbImgFormat.SelectedItem = fmt;
                
                var fontFamily = cfg.ImageGeneration?.FontFamily ?? "Arial";
                if (cmbFontFamily.Items.Contains(fontFamily)) cmbFontFamily.SelectedItem = fontFamily;
                else cmbFontFamily.SelectedIndex = 0;
                
                chkEnableProvinceRadar.Checked = cfg.ECCC?.EnableProvinceRadar ?? true;
                chkEnableWeatherMaps.Checked = cfg.ImageGeneration?.EnableWeatherMaps ?? true;

                // Video Tab
                chkVideoGeneration.Checked = cfg.Video?.doVideoGeneration ?? true;
                chkSkipDetailedWeatherOnAlert.Checked = cfg.Video?.SkipDetailedWeatherOnAlert ?? false;
                numPlayRadarAnimationCountOnAlert.Value = cfg.Video?.PlayRadarAnimationCountOnAlert ?? 1;
                numAlertDisplayDurationSeconds.Value = (decimal)(cfg.Video?.AlertDisplayDurationSeconds ?? 10);
                
                numStatic.Value = (decimal)(cfg.Video?.StaticDurationSeconds ?? 8);
                numFade.Value = (decimal)(cfg.Video?.FadeDurationSeconds ?? 0.5);
                chkFade.Checked = cfg.Video?.EnableFadeTransitions ?? false;
                chkUseTotalDuration.Checked = cfg.Video?.UseTotalDuration ?? false;
                numTotalDuration.Value = (decimal)(cfg.Video?.TotalDurationSeconds ?? 60);
                numTotalDuration.Enabled = chkUseTotalDuration.Checked;
                numStatic.Enabled = !chkUseTotalDuration.Checked;
                
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
                    _ => "1920x1080 (Full HD)"
                };
                if (cmbResolution.Items.Contains(resDisplay)) cmbResolution.SelectedItem = resDisplay;
                else cmbResolution.SelectedIndex = 2;
                
                numFps.Value = cfg.Video?.FrameRate ?? 30;
                
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
                
                chkVerbose.Checked = cfg.Video?.VerboseFfmpeg ?? false;
                chkShowFfmpeg.Checked = cfg.Video?.ShowFfmpegOutputInGui ?? true;
                chkEnableHardwareEncoding.Checked = cfg.Video?.EnableHardwareEncoding ?? false;
                chkEnableExperimental.Checked = cfg.Video?.ExperimentalEnabled ?? false;
                if (tabExperimental != null) tabExperimental.Enabled = chkEnableExperimental.Checked;
                
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
                else cmbQualityPreset.SelectedIndex = 2;

                // Experimental Tab
                chkUseCrfEncoding.Checked = cfg.Video?.UseCrfEncoding ?? true;
                numCrf.Value = cfg.Video?.CrfValue ?? 23;
                txtMaxBitrate.Text = cfg.Video?.MaxBitrate ?? string.Empty;
                txtBufferSize.Text = cfg.Video?.BufferSize ?? string.Empty;
                var preset = cfg.Video?.EncoderPreset ?? "medium";
                if (cmbEncoderPreset.Items.Contains(preset)) cmbEncoderPreset.SelectedItem = preset;
                else cmbEncoderPreset.SelectedIndex = 5;

                // FFmpeg Tab
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

                // EAS Tab
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
                
                var tts = cfg.TTS ?? new TTSSettings();
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

                // OpenMap Tab
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
                numMapOverlayOpacity.Value = (decimal)(openMap.OverlayOpacity * 100);
                numMapTileTimeout.Value = openMap.TileDownloadTimeoutSeconds;
                chkMapEnableCache.Checked = openMap.EnableTileCache;
                txtMapCacheDirectory.Text = openMap.TileCacheDirectory ?? "MapCache";
                txtMapCacheDirectory.Enabled = openMap.EnableTileCache;
                numMapCacheDuration.Value = openMap.CacheDurationHours;
                chkMapUseDarkMode.Checked = openMap.UseDarkMode;

                // Web UI Tab
                var webUI = cfg.WebUI ?? new WebUISettings();
                chkWebUIEnabled.Checked = webUI.Enabled;
                numWebUIPort.Value = webUI.Port;
                chkWebUIAllowRemote.Checked = webUI.AllowRemoteAccess;
                UpdateWebUIUrl();
                UpdateWebUIStatus();

                // Async validations
                Task.Run(() =>
                {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    this.Invoke((Action)(() =>
                    {
                        lblHwStatus.Text = msg;
                        lblHwStatus.ForeColor = ok ? SuccessColor : DangerColor;
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

                Task.Run(() =>
                {
                    bool valid = FFmpegLocator.ValidateConfiguration(out var msg);
                    if (this.IsHandleCreated)
                    {
                        this.Invoke((Action)(() =>
                        {
                            lblFfmpegStatus.Text = msg;
                            lblFfmpegStatus.ForeColor = valid ? SuccessColor : WarningColor;
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
                _isLoadingSettings = false;
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
                imageGen.FontFamily = cmbFontFamily.SelectedItem?.ToString() ?? "Arial";
                imageGen.EnableWeatherMaps = chkEnableWeatherMaps.Checked;
                cfg.ImageGeneration = imageGen;

                var eccc = cfg.ECCC ?? new ECCCSettings();
                eccc.EnableProvinceRadar = chkEnableProvinceRadar.Checked;
                cfg.ECCC = eccc;

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

                var tts = cfg.TTS ?? new TTSSettings();
                var voiceDisplay = cmbTtsVoice.SelectedItem?.ToString() ?? "fr-CA-SylvieNeural (Female)";
                tts.Voice = voiceDisplay.Split(' ')[0];
                tts.Rate = txtTtsRate.Text.Trim();
                tts.Pitch = txtTtsPitch.Text.Trim();
                cfg.TTS = tts;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                v.UseTotalDuration = chkUseTotalDuration.Checked;
                v.TotalDurationSeconds = (double)numTotalDuration.Value;

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

                var bitrateDisplay = cmbBitrate.SelectedItem?.ToString() ?? "4M (Medium)";
                v.VideoBitrate = bitrateDisplay.Split(' ')[0];

                v.Container = cmbContainer.SelectedItem?.ToString() ?? "mp4";
                v.OutputDirectory = ToRelative(txtVideoOutputDir.Text, imageGen.OutputDirectory ?? "WeatherImages");
                v.doVideoGeneration = chkVideoGeneration.Checked;
                v.SkipDetailedWeatherOnAlert = chkSkipDetailedWeatherOnAlert.Checked;
                v.PlayRadarAnimationCountOnAlert = (int)numPlayRadarAnimationCountOnAlert.Value;
                v.AlertDisplayDurationSeconds = (double)numAlertDisplayDurationSeconds.Value;
                v.VerboseFfmpeg = chkVerbose.Checked;
                v.ShowFfmpegOutputInGui = chkShowFfmpeg.Checked;

                if (chkEnableHardwareEncoding.Checked)
                {
                    bool ok = VideoGenerator.IsHardwareEncodingSupported(out var msg);
                    if (!ok)
                    {
                        var res = MessageBox.Show(this, $"FFmpeg does not appear to support hardware encoding on this system. ({msg})\nEnabling hardware encoding may cause ffmpeg to fail. Continue enabling?", "Hardware Encoding Not Available", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (res == DialogResult.No)
                            chkEnableHardwareEncoding.Checked = false;
                    }
                }
                v.EnableHardwareEncoding = chkEnableHardwareEncoding.Checked;

                v.UseCrfEncoding = chkUseCrfEncoding.Checked;
                v.CrfValue = (int)numCrf.Value;
                v.MaxBitrate = string.IsNullOrWhiteSpace(txtMaxBitrate.Text) ? null : txtMaxBitrate.Text.Trim();
                v.BufferSize = string.IsNullOrWhiteSpace(txtBufferSize.Text) ? null : txtBufferSize.Text.Trim();
                v.EncoderPreset = cmbEncoderPreset.SelectedItem?.ToString() ?? "medium";
                v.ExperimentalEnabled = chkEnableExperimental.Checked;

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
                cfg.Theme = cmbTheme.SelectedItem?.ToString() ?? "Blue";
                cfg.MinimizeToTray = chkMinimizeToTray.Checked;
                cfg.MinimizeToTrayOnClose = chkMinimizeToTrayOnClose.Checked;
                cfg.AutoStartCycle = chkAutoStartCycle.Checked;
                cfg.StartWithWindows = chkStartWithWindows.Checked;
                cfg.StartMinimizedToTray = chkStartMinimizedToTray.Checked;

                try
                {
                    if (chkStartWithWindows.Checked)
                        WindowsStartupManager.EnableStartup();
                    else
                        WindowsStartupManager.DisableStartup();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to update Windows startup setting: {ex.Message}", Logger.LogLevel.Error);
                    MessageBox.Show($"Failed to update Windows startup setting: {ex.Message}", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

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
                openMap.OverlayOpacity = (float)(numMapOverlayOpacity.Value / 100);
                openMap.TileDownloadTimeoutSeconds = (int)numMapTileTimeout.Value;
                openMap.EnableTileCache = chkMapEnableCache.Checked;
                openMap.TileCacheDirectory = txtMapCacheDirectory.Text;
                openMap.CacheDurationHours = (int)numMapCacheDuration.Value;
                openMap.UseDarkMode = chkMapUseDarkMode.Checked;
                cfg.OpenMap = openMap;

                var webUI = cfg.WebUI ?? new WebUISettings();
                bool wasEnabled = webUI.Enabled;
                int oldPort = webUI.Port;

                webUI.Enabled = chkWebUIEnabled.Checked;
                webUI.Port = (int)numWebUIPort.Value;
                webUI.AllowRemoteAccess = chkWebUIAllowRemote.Checked;
                cfg.WebUI = webUI;

                if (chkWebUIEnabled.Checked && wasEnabled && oldPort != webUI.Port)
                {
                    StopWebUIService();
                    Program.SetWebUIService(null);
                    StartWebUIService();
                }

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
            var tempSource = cmbFfmpegSource.SelectedIndex switch
            {
                0 => Models.FFmpegSource.Bundled,
                1 => Models.FFmpegSource.SystemPath,
                2 => Models.FFmpegSource.Custom,
                _ => Models.FFmpegSource.Bundled
            };
            var tempCustomPath = cmbFfmpegSource.SelectedIndex == 2 ? txtFfmpegCustomPath.Text : null;

            var currentSource = FFmpegLocator.CurrentSource;
            var currentCustomPath = FFmpegLocator.CustomPath;

            FFmpegLocator.SetSource(tempSource, tempCustomPath);
            bool valid = FFmpegLocator.ValidateConfiguration(out var message);

            if (valid && (tempSource != Models.FFmpegSource.Bundled || File.Exists(FFmpegLocator.FFmpegExecutable)))
            {
                bool hasVersion = VideoGenerator.IsFfmpegInstalled(out var version);
                if (hasVersion)
                    message += $"\nVersion: {version}";
            }

            lblFfmpegStatus.Text = valid ? $"✓ {message}" : $"✗ {message}";
            lblFfmpegStatus.ForeColor = valid ? SuccessColor : DangerColor;

            FFmpegLocator.SetSource(currentSource, currentCustomPath);
        }

        private string ToRelative(string? path, string fallback)
        {
            var outDir = string.IsNullOrWhiteSpace(path) ? fallback : path!;
            var cwd = Directory.GetCurrentDirectory();
            if (outDir.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
                outDir = outDir.Substring(cwd.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return outDir;
        }

        #endregion

        #region Font Preview

        private void UpdateFontPreview()
        {
            if (_alertPreviewPanel == null || _weatherPreviewPanel == null) return;

            try
            {
                string fontName = cmbFontFamily.SelectedItem?.ToString() ?? "Arial";

                // Alert Preview
                var alertBmp = new Bitmap(700, 110);
                using (var g = Graphics.FromImage(alertBmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.Clear(CardColor);

                    using (var redBgBrush = new SolidBrush(DangerColor))
                        g.FillRectangle(redBgBrush, 0, 0, 700, 38);

                    using (var headerFont = new Font(fontName, 20, FontStyle.Bold))
                    using (var detailFont = new Font(fontName, 11, FontStyle.Regular))
                    using (var whiteBrush = new SolidBrush(Color.White))
                    using (var blackBrush = new SolidBrush(TextColor))
                    {
                        g.DrawString("⚠ Weather Alert", headerFont, whiteBrush, new PointF(15, 6));
                        g.DrawString($"Font: {fontName}", detailFont, blackBrush, new PointF(15, 50));
                        g.DrawString("Sample alert message with your selected font family", detailFont, blackBrush, new PointF(15, 75));
                    }
                }

                // Weather Preview
                var weatherBmp = new Bitmap(700, 110);
                using (var g = Graphics.FromImage(weatherBmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.Clear(Color.FromArgb(235, 245, 255));

                    using (var cityFont = new Font(fontName, 18, FontStyle.Bold))
                    using (var tempFont = new Font(fontName, 28, FontStyle.Bold))
                    using (var labelFont = new Font(fontName, 10, FontStyle.Regular))
                    using (var blackBrush = new SolidBrush(TextColor))
                    using (var accentBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString("Montréal, QC", cityFont, accentBrush, new PointF(15, 8));
                        g.DrawString("23°C", tempFont, blackBrush, new PointF(15, 35));
                        g.DrawString("Humidity: 65%   Wind: 12 km/h   Partly Cloudy", labelFont, blackBrush, new PointF(15, 80));
                    }
                }

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

        #endregion

        #region Web UI Methods

        private void OnWebUIEnabledChanged()
        {
            if (chkWebUIEnabled.Checked)
                StartWebUIService();
            else
                StopWebUIService();
            UpdateWebUIStatus();
        }

        private void StartWebUIService()
        {
            try
            {
                var service = Program.WebUIService;
                if (service == null)
                {
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
                lblWebUIStatus.ForeColor = SuccessColor;
            }
            else
            {
                lblWebUIStatus.Text = "○ Status: Server is not running";
                lblWebUIStatus.ForeColor = TextMutedColor;
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
                        lblWebUIStatus.ForeColor = SuccessColor;
                    }
                    else
                    {
                        lblWebUIStatus.Text = "⚠ Status: Server responded with error";
                        lblWebUIStatus.ForeColor = WarningColor;
                    }
                }
            }
            catch (Exception ex)
            {
                lblWebUIStatus.Text = "✗ Status: Server is not running or not accessible";
                lblWebUIStatus.ForeColor = DangerColor;
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
        }

        #endregion
    }
}
