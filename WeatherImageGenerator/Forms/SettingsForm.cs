using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Rendering.Common;
using WeatherImageGenerator.Services;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Forms
{
    public class SettingsForm : Form
    {
        private bool _isLoadingSettings = false;
        private int _previousRenderApiIndex = 0;
        private bool _dx11Available = false;

        // UI Style — delegates to ThemeManager so every usage is theme-aware
        private static Color AccentColor => ThemeManager.Current.Accent;
        private static Color AccentColorLight => ControlPaint.Light(ThemeManager.Current.Accent, 0.15f);
        private static Color SuccessColor => ThemeManager.Current.Success;
        private static Color WarningColor => ThemeManager.Current.Warning;
        private static Color DangerColor => ThemeManager.Current.Danger;
        private static Color BackgroundColor => ThemeManager.Current.Background;
        private static Color CardColor => ThemeManager.Current.CardBackground;
        private static Color BorderColor => ThemeManager.Current.Border;
        private static Color TextColor => ThemeManager.Current.TextPrimary;
        private static Color TextMutedColor => ThemeManager.Current.TextSecondary;
        private static readonly Font HeaderFont = new Font("Segoe UI", 12F, FontStyle.Bold);
        private static readonly Font SubHeaderFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        private static readonly Font LabelFont = new Font("Segoe UI", 9.5F);
        private static readonly Font SmallFont = new Font("Segoe UI", 8.5F);
        private static readonly Font HelpFont = new Font("Segoe UI", 8F, FontStyle.Italic);

        // ═══════════════════════════════════════════════════════════════════
        // Controls — General
        // ═══════════════════════════════════════════════════════════════════
        TextBox txtImageOutputDir;
        TextBox txtVideoOutputDir;
        NumericUpDown numRefresh;
        ComboBox cmbTheme;
        ComboBox cmbDefaultWeatherApi;
        CheckBox chkMinimizeToTray;
        CheckBox chkMinimizeToTrayOnClose;
        CheckBox chkAutoStartCycle;
        CheckBox chkStartWithWindows;
        CheckBox chkStartMinimizedToTray;



        // ═══════════════════════════════════════════════════════════════════
        // Controls — Image
        // ═══════════════════════════════════════════════════════════════════
        NumericUpDown numImgWidth;
        NumericUpDown numImgHeight;
        ComboBox cmbImgFormat;
        ComboBox cmbFontFamily;
        CheckBox chkEnableProvinceRadar;
        CheckBox chkEnableWeatherMaps;
        PictureBox _alertPreviewPanel;
        PictureBox _weatherPreviewPanel;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — Video
        // ═══════════════════════════════════════════════════════════════════
        CheckBox chkVideoGeneration;
        CheckBox chkSkipDetailedWeatherOnAlert;
        NumericUpDown numPlayRadarAnimationCountOnAlert;
        NumericUpDown numAlertDisplayDurationSeconds;
        ComboBox cmbQualityPreset;
        ComboBox cmbResolution;
        ComboBox cmbContainer;
        NumericUpDown numFps;
        NumericUpDown numStatic;
        NumericUpDown numTotalDuration;
        CheckBox chkUseTotalDuration;
        NumericUpDown numFade;
        CheckBox chkFade;
        ComboBox cmbCodec;
        ComboBox cmbBitrate;
        CheckBox chkEnableHardwareEncoding;
        Label lblHwStatus;
        Button btnCheckHw;
        CheckBox chkVerbose;
        CheckBox chkShowFfmpeg;
        CheckBox chkEnableExperimental;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — FFmpeg
        // ═══════════════════════════════════════════════════════════════════
        ComboBox cmbFfmpegSource;
        TextBox txtFfmpegCustomPath;
        Button btnBrowseFfmpegPath;
        Label lblFfmpegStatus;
        Button btnValidateFfmpeg;
        Button btnClearFfmpegCache;
        Button btnDownloadBundled;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — Advanced Encoding (Experimental)
        // ═══════════════════════════════════════════════════════════════════
        Panel _experimentalSection;
        CheckBox chkUseCrfEncoding;
        NumericUpDown numCrf;
        ComboBox cmbEncoderPreset;
        TextBox txtMaxBitrate;
        TextBox txtBufferSize;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — EAS / TTS
        // ═══════════════════════════════════════════════════════════════════
        CheckBox chkAlertReadyEnabled;
        TextBox txtAlertReadyFeedUrls;
        CheckBox chkAlertReadyIncludeTests;
        NumericUpDown numAlertReadyMaxAgeHours;
        ComboBox cmbAlertReadyLanguage;
        TextBox txtAlertReadyAreaFilters;
        TextBox txtAlertReadyJurisdictions;
        CheckBox chkAlertReadyHighRiskOnly;
        CheckBox chkAlertReadyExcludeWeather;
        CheckBox chkNwsEnabled;
        TextBox txtNwsStates;
        TextBox txtNwsZones;
        TextBox txtNwsPoint;
        NumericUpDown numNwsMaxAgeHours;
        CheckBox chkNwsHighRiskOnly;
        TextBox txtNwsSeverityFilter;
        TextBox txtNwsUserAgent;
        NumericUpDown numNwsPollingInterval;
        ComboBox cmbTtsEngine;
        ComboBox cmbTtsVoice;
        TextBox txtTtsRate;
        TextBox txtTtsPitch;
        ComboBox cmbPiperVoice;
        NumericUpDown numPiperLengthScale;
        Control[] _edgeTtsControls;
        Control[] _piperTtsControls;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — Map / OpenMap
        // ═══════════════════════════════════════════════════════════════════
        ComboBox cmbMapStyle;
        NumericUpDown numMapZoomLevel;
        TextBox txtMapBackgroundColor;
        NumericUpDown numMapOverlayOpacity;
        NumericUpDown numMapTileTimeout;
        CheckBox chkMapEnableCache;
        TextBox txtMapCacheDirectory;
        NumericUpDown numMapCacheDuration;
        CheckBox chkMapUseDarkMode;
        ComboBox cmbRenderApi;

        // ═══════════════════════════════════════════════════════════════════
        // Controls — Web UI / Network
        // ═══════════════════════════════════════════════════════════════════
        CheckBox chkWebUIEnabled;
        NumericUpDown numWebUIPort;
        CheckBox chkWebUIAllowRemote;
        Label lblWebUIStatus;
        Button btnTestWebUI;
        TextBox txtWebUIUrl;
        Label lblLocalIP;
        Label lblPublicIP;

        // ═══════════════════════════════════════════════════════════════════
        // Constructor
        // ═══════════════════════════════════════════════════════════════════

        public SettingsForm()
        {
            InitializeForm();
            BuildUI();
            LoadSettings();
            ThemeManager.ApplyTo(this);
            ThemeManager.ThemeChanged += _ => ThemeManager.ApplyTo(this);
        }

        private void InitializeForm()
        {
            this.Text = "⚙ Settings";
            this.Width = 1024;
            this.Height = 850;
            this.MinimumSize = new Size(900, 700);
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
                Tag = "accent",
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
                Tag = "muted",
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
                ForeColor = ThemeManager.Current.TextOnAccent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = AccentColorLight;
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(AccentColor, 0.15f);
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
                BackColor = ThemeManager.Current.ButtonBackground,
                ForeColor = ThemeManager.Current.TextOnButton,
                FlatStyle = FlatStyle.Flat,
                Font = LabelFont,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = BorderColor;
            btn.FlatAppearance.MouseOverBackColor = ThemeManager.Current.ButtonHover;
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(ThemeManager.Current.ButtonBackground, 0.1f);
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
                ForeColor = ThemeManager.Current.TextOnAccent,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(SuccessColor, 0.15f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(SuccessColor, 0.15f);
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
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.Current.InputBackground,
                ForeColor = ThemeManager.Current.TextPrimary
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

        // ═══════════════════════════════════════════════════════════════════
        // Build UI — 5 consolidated tabs
        // ═══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            var tabControl = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 720,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Padding = new Point(12, 6)
            };

            var tabGeneral = BuildGeneralTab();
            var tabOutput = BuildOutputTab();
            var tabMap = BuildMapTab();
            var tabAlerts = BuildAlertsTab();
            var tabNetwork = BuildNetworkTab();

            tabControl.TabPages.AddRange(new TabPage[] {
                tabGeneral, tabOutput, tabMap, tabAlerts, tabNetwork
            });

            // Footer buttons
            var btnSave = CreateSuccessButton("✔ Save Settings", 650, 735, 150, 40);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Click += (s, e) => SaveClicked();

            var btnCancel = CreateSecondaryButton("✖ Cancel", 810, 735, 120, 40);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.Add(tabControl);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);

            // Position form relative to owner
            this.Shown += (s, e) =>
            {
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

        // ═══════════════════════════════════════════════════════════════════
        // Tab 1: General (+ Version & Updates)
        // ═══════════════════════════════════════════════════════════════════

        private TabPage BuildGeneralTab()
        {
            var tab = new TabPage("General") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // ── Application Settings ──
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

            var lblDefaultApi = CreateLabel("Weather API:", labelX, y);
            cmbDefaultWeatherApi = CreateComboBox(fieldX, y - 2, 180);
            cmbDefaultWeatherApi.Items.AddRange(new object[] { "OpenMeteo", "ECCC", "Hybrid" });
            cmbDefaultWeatherApi.SelectedIndex = 0;
            var lblDefaultApiHelp = CreateHelpLabel("Default API for all locations", fieldX + 190, y + 2, 220);
            y += rowHeight;

            var divider1 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // ── Output Directories ──
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

            var divider2 = CreateDivider(labelX, y + 5, 700);
            y += 25;

            // ── System Tray & Startup ──
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
            chkStartMinimizedToTray.Tag = "muted";
            chkStartMinimizedToTray.Enabled = false;

            chkStartWithWindows.CheckedChanged += (s, e) =>
            {
                chkStartMinimizedToTray.Enabled = chkStartWithWindows.Checked;
                chkStartMinimizedToTray.ForeColor = chkStartWithWindows.Checked ? TextColor : TextMutedColor;
                chkStartMinimizedToTray.Tag = chkStartWithWindows.Checked ? null : "muted";
            };
            y += 40;

            tab.Controls.AddRange(new Control[] {
                lblAppSettings, lblRefresh, numRefresh, lblRefreshUnit,
                lblTheme, cmbTheme,
                lblDefaultApi, cmbDefaultWeatherApi, lblDefaultApiHelp,
                divider1,
                lblOutputDirs, lblOutImg, txtImageOutputDir, btnBrowseImg,
                lblOutVid, txtVideoOutputDir, btnBrowseVid, divider2,
                lblTraySettings, chkMinimizeToTray, chkMinimizeToTrayOnClose,
                chkAutoStartCycle, chkStartWithWindows, chkStartMinimizedToTray
            });

            return tab;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tab 2: Output (Image + Video + FFmpeg + Advanced Encoding)
        // ═══════════════════════════════════════════════════════════════════

        private TabPage BuildOutputTab()
        {
            var tab = new TabPage("Output") { BackColor = BackgroundColor, Padding = new Padding(15), AutoScroll = true };
            int y = 10;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            // ═════════════════════════════════════════════
            // IMAGE GENERATION
            // ═════════════════════════════════════════════
            var lblImgSettings = CreateSectionHeader("Image Generation", labelX, y, "🎨");
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

            chkEnableProvinceRadar = CreateCheckBox("Enable Province Radar Animation", labelX, y, 300);
            y += 28;

            chkEnableWeatherMaps = CreateCheckBox("Enable Weather Maps Generation", labelX, y, 300);
            y += 32;

            var btnRegenIcons = CreateSecondaryButton("🔄 Regenerate Icons", labelX, y, 160, 28);
            btnRegenIcons.Click += (s, e) => RegenerateIcons(btnRegenIcons);
            y += 38;

            // Font preview
            var lblPreview = CreateSectionHeader("Font Preview", labelX, y, "👁");
            y += 30;

            _alertPreviewPanel = new PictureBox
            {
                Left = labelX, Top = y, Width = 700, Height = 110,
                BorderStyle = BorderStyle.None, BackColor = CardColor,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            y += 115;

            _weatherPreviewPanel = new PictureBox
            {
                Left = labelX, Top = y, Width = 700, Height = 110,
                BorderStyle = BorderStyle.None, BackColor = CardColor,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            y += 120;

            cmbFontFamily.SelectedIndexChanged += (s, e) => UpdateFontPreview();
            UpdateFontPreview();

            var imgDivider = CreateDivider(labelX, y, 700);
            y += 20;

            // ═════════════════════════════════════════════
            // VIDEO GENERATION
            // ═════════════════════════════════════════════
            var lblVideoHeader = CreateSectionHeader("Video Generation", labelX, y, "🎥");
            y += 35;

            chkVideoGeneration = CreateCheckBox("  Enable Video Generation", labelX, y, 280);
            chkVideoGeneration.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            chkVideoGeneration.ForeColor = AccentColor;
            chkVideoGeneration.Tag = "accent";
            y += 38;

            // Dual-column group boxes
            int leftCol = labelX;
            int rightCol = 380;
            int colWidth = 345;
            int grpHeight = 130;
            int innerPad = 15;

            // Row 1: Alert Behavior & Output Format
            var grpAlerts = CreateGroupBox("🚨 Alert Behavior", leftCol, y, colWidth, grpHeight);
            int aY = 25;

            chkSkipDetailedWeatherOnAlert = CreateCheckBox("Skip detailed weather during alerts", innerPad, aY, 280);
            chkSkipDetailedWeatherOnAlert.Font = SmallFont;
            aY += 28;

            var lblRadarCount = CreateLabel("Radar replays:", innerPad, aY);
            lblRadarCount.Width = 95; lblRadarCount.Font = SmallFont;
            numPlayRadarAnimationCountOnAlert = CreateNumericUpDown(innerPad + 100, aY - 2, 55, 1, 10, 1);
            var lblRadarHelp = CreateHelpLabel("times during alert", innerPad + 160, aY);
            aY += 30;

            var lblAlertDur = CreateLabel("Alert duration:", innerPad, aY);
            lblAlertDur.Width = 95; lblAlertDur.Font = SmallFont;
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
            lblQualityPreset.Width = 100; lblQualityPreset.Font = SmallFont;
            cmbQualityPreset = CreateComboBox(innerPad + 105, fY - 2, 180);
            cmbQualityPreset.Items.AddRange(new object[] { "Ultra (Best Quality)", "High Quality", "Balanced", "Web Optimized", "Low Bandwidth", "Custom" });
            cmbQualityPreset.SelectedIndex = 2;
            fY += 30;

            var lblResPreset = CreateLabel("Resolution:", innerPad, fY);
            lblResPreset.Width = 100; lblResPreset.Font = SmallFont;
            cmbResolution = CreateComboBox(innerPad + 105, fY - 2, 180);
            cmbResolution.Items.AddRange(new object[] { "3840x2160 (4K/UHD)", "2560x1440 (2K/QHD)", "1920x1080 (Full HD)", "1600x900 (HD+)", "1280x720 (HD)", "960x540 (qHD)", "854x480 (FWVGA)", "640x480 (VGA)" });
            cmbResolution.SelectedIndex = 2;
            fY += 30;

            var lblContainer = CreateLabel("Container:", innerPad, fY);
            lblContainer.Width = 100; lblContainer.Font = SmallFont;
            cmbContainer = CreateComboBox(innerPad + 105, fY - 2, 80);
            cmbContainer.Items.AddRange(new object[] { "mp4", "mkv", "mov", "avi", "webm" });
            cmbContainer.SelectedIndex = 0;

            var lblFps = CreateLabel("FPS:", innerPad + 195, fY);
            lblFps.Width = 35; lblFps.Font = SmallFont;
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
            lblStatic.Width = 100; lblStatic.Font = SmallFont;
            numStatic = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 1, 60, 8);
            numStatic.DecimalPlaces = 1;
            var lblStaticHelp = CreateHelpLabel("seconds per slide", innerPad + 180, tY);
            tY += 30;

            var lblTotal = CreateLabel("Total Duration:", innerPad, tY);
            lblTotal.Width = 100; lblTotal.Font = SmallFont;
            numTotalDuration = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 1, 86400, 60);
            numTotalDuration.DecimalPlaces = 1;
            numTotalDuration.Enabled = false;
            var lblTotalHelp = CreateHelpLabel("seconds total", innerPad + 180, tY);
            tY += 28;

            chkUseTotalDuration = CreateCheckBox("Use total duration mode", innerPad, tY, 220);
            chkUseTotalDuration.Font = SmallFont;
            chkUseTotalDuration.CheckedChanged += (s, e) =>
            {
                numTotalDuration.Enabled = chkUseTotalDuration.Checked;
                numStatic.Enabled = !chkUseTotalDuration.Checked;
            };
            tY += 32;

            var lblFade = CreateLabel("Fade Duration:", innerPad, tY);
            lblFade.Width = 100; lblFade.Font = SmallFont;
            lblFade.ForeColor = TextMutedColor; lblFade.Tag = "muted";
            numFade = CreateNumericUpDown(innerPad + 105, tY - 2, 70, 0, 10, 0.5M);
            numFade.DecimalPlaces = 2; numFade.Increment = 0.1M; numFade.Enabled = false;

            chkFade = CreateCheckBox("Enable", innerPad + 180, tY, 80);
            chkFade.Font = SmallFont; chkFade.Enabled = false;
            chkFade.ForeColor = TextMutedColor; chkFade.Tag = "muted";

            grpTiming.Controls.AddRange(new Control[] {
                lblStatic, numStatic, lblStaticHelp,
                lblTotal, numTotalDuration, lblTotalHelp,
                chkUseTotalDuration, lblFade, numFade, chkFade
            });

            var grpEncoding = CreateGroupBox("🎬 Encoding", rightCol, y, colWidth, 155);
            int eY = 25;

            var lblCodec = CreateLabel("Codec:", innerPad, eY);
            lblCodec.Width = 80; lblCodec.Font = SmallFont;
            cmbCodec = CreateComboBox(innerPad + 85, eY - 2, 200);
            cmbCodec.Items.AddRange(new object[] { "libx264 (H.264)", "libx265 (H.265/HEVC)", "libvpx-vp9 (VP9)", "libaom-av1 (AV1)", "mpeg4", "msmpeg4" });
            cmbCodec.SelectedIndex = 0;
            eY += 30;

            var lblBitrate = CreateLabel("Bitrate:", innerPad, eY);
            lblBitrate.Width = 80; lblBitrate.Font = SmallFont;
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

            // Row 3: Debug & Advanced
            var grpDebug = CreateGroupBox("🔧 Debug & Advanced", leftCol, y, colWidth * 2 + 20, 85);
            int dY = 25;

            chkVerbose = CreateCheckBox("Verbose FFmpeg Output", innerPad, dY, 200);
            chkVerbose.Font = SmallFont;

            chkShowFfmpeg = CreateCheckBox("Show FFmpeg Console", innerPad + 210, dY, 200);
            chkShowFfmpeg.Font = SmallFont;

            chkEnableExperimental = CreateCheckBox("⚠ Enable Experimental Features", innerPad + 440, dY, 240);
            chkEnableExperimental.Font = SmallFont;
            chkEnableExperimental.ForeColor = WarningColor;
            chkEnableExperimental.Tag = "warning";
            chkEnableExperimental.CheckedChanged += (s, e) =>
            {
                if (_experimentalSection != null) _experimentalSection.Enabled = chkEnableExperimental.Checked;
            };
            dY += 30;

            var lblDebugTip = CreateHelpLabel("💡 Enable Debug options for troubleshooting. Experimental unlocks advanced encoder settings below.", innerPad, dY, 680);

            grpDebug.Controls.AddRange(new Control[] {
                chkVerbose, chkShowFfmpeg, chkEnableExperimental, lblDebugTip
            });

            y += 95;

            // Wire up video event handlers
            SetupVideoTabEventHandlers();

            var videoDivider = CreateDivider(labelX, y, 700);
            y += 20;

            // ═════════════════════════════════════════════
            // FFMPEG SOURCE
            // ═════════════════════════════════════════════
            var lblFfmpegHeader = CreateSectionHeader("FFmpeg Source", labelX, y, "🎬");
            y += 35;

            var lblFfmpegDesc = CreateHelpLabel("Choose where to get FFmpeg binaries from:", labelX, y, 400);
            y += 28;

            var lblSourceLabel = CreateLabel("Source:", labelX, y, 100);
            cmbFfmpegSource = CreateComboBox(fieldX, y - 2, 220);
            cmbFfmpegSource.Items.AddRange(new object[] { "Bundled (Auto-download)", "System PATH", "Custom Path" });
            cmbFfmpegSource.SelectedIndex = 0;
            y += 38;

            var lblCustomPath = CreateLabel("Custom Path:", labelX, y, 150);
            txtFfmpegCustomPath = CreateTextBox(fieldX, y - 2, 380);
            txtFfmpegCustomPath.Enabled = false;
            btnBrowseFfmpegPath = CreateSecondaryButton("...", fieldX + 390, y - 3, 40, 26);
            btnBrowseFfmpegPath.Enabled = false;
            btnBrowseFfmpegPath.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select FFmpeg directory (containing ffmpeg.exe)";
                    if (dlg.ShowDialog() == DialogResult.OK)
                        txtFfmpegCustomPath.Text = dlg.SelectedPath;
                }
            };

            cmbFfmpegSource.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = cmbFfmpegSource.SelectedIndex == 2;
                txtFfmpegCustomPath.Enabled = isCustom;
                btnBrowseFfmpegPath.Enabled = isCustom;
            };
            y += 45;

            lblFfmpegStatus = new Label
            {
                Text = "Not validated",
                Left = labelX, Top = y, Width = 600, Height = 28,
                Font = LabelFont, ForeColor = TextMutedColor, Tag = "muted", AutoSize = false
            };
            y += 35;

            btnValidateFfmpeg = CreatePrimaryButton("✓ Validate", labelX, y, 110, 32);
            btnValidateFfmpeg.Click += (s, e) => ValidateFfmpegConfiguration();

            btnDownloadBundled = CreateSecondaryButton("⬇ Download", labelX + 120, y, 110, 32);
            btnDownloadBundled.Click += async (s, e) => await DownloadFfmpegAsync();

            btnClearFfmpegCache = CreateSecondaryButton("🗑 Clear Cache", labelX + 240, y, 120, 32);
            btnClearFfmpegCache.Click += (s, e) =>
            {
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
            y += 45;

            var ffmpegDivider = CreateDivider(labelX, y, 700);
            y += 20;

            // ═════════════════════════════════════════════
            // ADVANCED ENCODING (Experimental — gated)
            // ═════════════════════════════════════════════
            _experimentalSection = new Panel
            {
                Left = labelX, Top = y, Width = 720, AutoSize = true,
                BackColor = Color.Transparent, Enabled = false
            };
            int ey = 0;

            // Warning banner
            var warningPanel = new Panel
            {
                Left = 0, Top = ey, Width = 700, Height = 45,
                BackColor = ThemeManager.Current.IsDark ? Color.FromArgb(60, 50, 20) : Color.FromArgb(255, 243, 205)
            };
            var lblWarning = new Label
            {
                Text = "⚠ EXPERIMENTAL — These settings affect video quality and encoding. Enable via the checkbox above.",
                Left = 15, Top = 8, Width = 670, Height = 30,
                Font = LabelFont,
                ForeColor = ThemeManager.Current.IsDark ? WarningColor : Color.FromArgb(133, 100, 4),
                AutoSize = false
            };
            warningPanel.Controls.Add(lblWarning);
            ey += 55;

            var lblCrfSection = CreateSectionHeader("Quality-Based Encoding (CRF)", 0, ey, "🎯");
            ey += 35;

            chkUseCrfEncoding = CreateCheckBox("Use CRF encoding (quality-based)", 0, ey, 280);
            ey += 35;

            var lblCrf = CreateLabel("CRF Value:", 0, ey);
            numCrf = CreateNumericUpDown(fieldX - labelX, ey - 2, 80, 0, 51, 23);
            var lblCrfHelp = CreateHelpLabel("Lower = better quality (18-28 typical)", fieldX - labelX + 90, ey + 2, 250);
            ey += rowHeight + 5;

            var expDivider = CreateDivider(0, ey, 700);
            ey += 20;

            var lblEncoderSection = CreateSectionHeader("Encoder Settings", 0, ey, "⚙");
            ey += 35;

            var lblPreset = CreateLabel("Encoder Preset:", 0, ey);
            cmbEncoderPreset = CreateComboBox(fieldX - labelX, ey - 2, 150);
            cmbEncoderPreset.Items.AddRange(new object[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" });
            cmbEncoderPreset.SelectedIndex = 5;
            var lblPresetHelp = CreateHelpLabel("Slower = smaller file, better quality", fieldX - labelX + 160, ey + 2, 250);
            ey += rowHeight;

            var lblMaxBr = CreateLabel("Max Bitrate:", 0, ey);
            txtMaxBitrate = CreateTextBox(fieldX - labelX, ey - 2, 100);
            var lblMaxBrHelp = CreateHelpLabel("e.g., 8M, 12M (optional)", fieldX - labelX + 110, ey + 2);
            ey += rowHeight;

            var lblBuffer = CreateLabel("Buffer Size:", 0, ey);
            txtBufferSize = CreateTextBox(fieldX - labelX, ey - 2, 100);
            var lblBufferHelp = CreateHelpLabel("e.g., 16M (optional)", fieldX - labelX + 110, ey + 2);
            ey += rowHeight + 5;

            var lblTips = new Label
            {
                Text = "💡 Tips:\n• CRF mode provides consistent quality but variable file size\n• Use 'slow' or 'slower' for best quality when time isn't critical\n• Set Max Bitrate to limit file size while using CRF",
                Left = 0, Top = ey, Width = 600, Height = 70,
                Font = SmallFont, ForeColor = TextColor, AutoSize = false
            };

            // Mark quality as custom when experimental settings change
            EventHandler markCustom = (s, e) =>
            {
                if (!_isLoadingSettings && cmbQualityPreset != null && cmbQualityPreset.SelectedIndex != 5)
                    cmbQualityPreset.SelectedIndex = 5;
            };
            cmbEncoderPreset.SelectedIndexChanged += markCustom;
            txtMaxBitrate.TextChanged += markCustom;
            txtBufferSize.TextChanged += markCustom;
            chkUseCrfEncoding.CheckedChanged += markCustom;
            numCrf.ValueChanged += markCustom;

            _experimentalSection.Controls.AddRange(new Control[] {
                warningPanel, lblCrfSection, chkUseCrfEncoding, lblCrf, numCrf, lblCrfHelp, expDivider,
                lblEncoderSection, lblPreset, cmbEncoderPreset, lblPresetHelp,
                lblMaxBr, txtMaxBitrate, lblMaxBrHelp,
                lblBuffer, txtBufferSize, lblBufferHelp, lblTips
            });

            // Add all controls to the tab
            tab.Controls.AddRange(new Control[] {
                // Image
                lblImgSettings, lblImgSize, numImgWidth, lblX, numImgHeight, lblPixels,
                lblFormat, cmbImgFormat, lblFormatHelp,
                lblFontFamily, cmbFontFamily,
                chkEnableProvinceRadar, chkEnableWeatherMaps, btnRegenIcons,
                lblPreview, _alertPreviewPanel, _weatherPreviewPanel,
                imgDivider,
                // Video
                lblVideoHeader, chkVideoGeneration,
                grpAlerts, grpFormat, grpTiming, grpEncoding, grpDebug,
                videoDivider,
                // FFmpeg
                lblFfmpegHeader, lblFfmpegDesc, lblSourceLabel, cmbFfmpegSource,
                lblCustomPath, txtFfmpegCustomPath, btnBrowseFfmpegPath,
                lblFfmpegStatus, btnValidateFfmpeg, btnDownloadBundled, btnClearFfmpegCache,
                ffmpegDivider,
                // Advanced Encoding
                _experimentalSection
            });

            return tab;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tab 3: Map (Map settings + separate Rendering Engine section)
        // ═══════════════════════════════════════════════════════════════════

        private TabPage BuildMapTab()
        {
            var tab = new TabPage("Map") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 230;
            int rowHeight = 38;

            // ── Map Style & Display ──
            var lblBasic = CreateSectionHeader("Map Style & Display", labelX, y, "🗺");
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

            // ── Performance & Caching ──
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

            // ── Rendering Engine (separate from map settings) ──
            var lblRenderSection = CreateSectionHeader("Rendering Engine", labelX, y, "🖥");
            y += 35;

            var lblRenderApi = CreateLabel("Rendering API:", labelX, y);
            cmbRenderApi = new ComboBox
            {
                Left = fieldX, Top = y - 2, Width = 220,
                Font = LabelFont,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                BackColor = ThemeManager.Current.InputBackground,
                ForeColor = ThemeManager.Current.TextPrimary
            };

            // Check DirectX 11 availability at runtime
            try { _dx11Available = RenderingFactory.IsAvailable(RenderingApi.DirectX11); } catch { _dx11Available = false; }

            cmbRenderApi.Items.AddRange(new object[] {
                "OpenGL (Default)",
                "Vulkan (Not Ready)",
                _dx11Available ? "DirectX 11" : "DirectX 11 (Unavailable)"
            });
            cmbRenderApi.SelectedIndex = 0;
            _previousRenderApiIndex = 0;

            // Owner-draw to gray out unavailable items
            cmbRenderApi.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                e.DrawBackground();
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                string text = cmbRenderApi.Items[e.Index]?.ToString() ?? "";
                bool isDisabled = (e.Index == 1) || (e.Index == 2 && !_dx11Available);
                Color textCol;
                if (isDisabled)
                    textCol = TextMutedColor;
                else if ((e.State & DrawItemState.Selected) != 0)
                    textCol = ThemeManager.Current.TextOnAccent;
                else
                    textCol = TextColor;
                using (var brush = new SolidBrush(textCol))
                    e.Graphics.DrawString(text, e.Font ?? LabelFont, brush, e.Bounds.Left + 2, e.Bounds.Top + 2);
                e.DrawFocusRectangle();
            };

            // Prevent selecting disabled items
            cmbRenderApi.SelectedIndexChanged += (s, e) =>
            {
                if (_isLoadingSettings) return;
                int idx = cmbRenderApi.SelectedIndex;
                bool isDisabled = (idx == 1) || (idx == 2 && !_dx11Available);
                if (isDisabled)
                {
                    _isLoadingSettings = true;
                    cmbRenderApi.SelectedIndex = _previousRenderApiIndex;
                    _isLoadingSettings = false;
                    return;
                }
                _previousRenderApiIndex = idx;
            };

            var lblRenderApiHelp = CreateHelpLabel("⚠ Requires application restart", fieldX + 230, y + 2, 180);
            y += rowHeight + 5;

            var lblRenderNote = new Label
            {
                Text = "• OpenGL: Most compatible (default). Works on all GPUs.\n" +
                       "• DirectX 11: Windows-native, hardware-accelerated.\n" +
                       "• Vulkan: High-performance (coming soon).",
                Left = labelX, Top = y, Width = 600, Height = 55,
                Font = SmallFont, ForeColor = TextColor, AutoSize = false
            };
            y += 65;

            var divider3 = CreateDivider(labelX, y, 700);
            y += 25;

            // ── Map Style Reference ──
            var lblStyleGuide = CreateSectionHeader("Map Style Reference", labelX, y, "📖");
            y += 35;

            var styleGuide = new Label
            {
                Text = "• Standard: Traditional OpenStreetMap with detailed roads and cities\n" +
                       "• Minimal: Clean, simplified style (HOT)\n" +
                       "• Terrain: Topographic with elevation contours\n" +
                       "• Satellite: High-resolution imagery (Esri)",
                Left = labelX, Top = y, Width = 600, Height = 90,
                Font = LabelFont, ForeColor = TextColor, AutoSize = false
            };
            y += 100;

            var lblAttribution = new Label
            {
                Text = "⚠ Legal: Generated maps automatically include required attribution per OSM usage policy.",
                Left = labelX, Top = y, Width = 650, Height = 25,
                Font = HelpFont, ForeColor = DangerColor, Tag = "danger", AutoSize = false
            };

            tab.Controls.AddRange(new Control[] {
                lblBasic, lblMapStyle, cmbMapStyle, lblMapZoom, numMapZoomLevel, lblZoomHelp,
                lblMapBgColor, txtMapBackgroundColor, lblBgHelp,
                lblMapOpacity, numMapOverlayOpacity, lblOpacityUnit,
                chkMapUseDarkMode, lblDarkHelp, divider1,
                lblPerf, lblMapTimeout, numMapTileTimeout, lblTimeoutUnit,
                chkMapEnableCache, lblCacheDir, txtMapCacheDirectory,
                lblCacheDuration, numMapCacheDuration, lblCacheHelp, divider2,
                lblRenderSection, lblRenderApi, cmbRenderApi, lblRenderApiHelp, lblRenderNote, divider3,
                lblStyleGuide, styleGuide, lblAttribution
            });

            return tab;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tab 4: Alerts & TTS
        // ═══════════════════════════════════════════════════════════════════

        private TabPage BuildAlertsTab()
        {
            var tab = new TabPage("Alerts & TTS") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 230;
            int rowHeight = 35;

            // ── Alert Ready (NAAD) ──
            var lblAlertReady = CreateSectionHeader("Alert Ready (NAAD)", labelX, y, "🚨");
            y += 35;

            chkAlertReadyEnabled = CreateCheckBox("Enable Alert Ready", labelX, y, 250);
            y += rowHeight;

            var lblFeedUrls = CreateLabel("Feed URLs:", labelX, y);
            y += 25;
            txtAlertReadyFeedUrls = new TextBox
            {
                Left = labelX, Top = y, Width = 500, Height = 55,
                Font = SmallFont, Multiline = true, ScrollBars = ScrollBars.Vertical
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

            var divider1 = CreateDivider(labelX, y, 700);
            y += 25;

            // ── NWS (USA) ──
            var lblNws = CreateSectionHeader("NWS (USA) — National Weather Service", labelX, y, "🇺🇸");
            y += 35;

            chkNwsEnabled = CreateCheckBox("Enable NWS Alerts", labelX, y, 250);
            y += rowHeight;

            var lblNwsStates = CreateLabel("State Codes:", labelX, y);
            txtNwsStates = CreateTextBox(fieldX, y - 2, 270);
            var lblNwsStatesHelp = CreateHelpLabel("comma-separated, e.g. IL, CA, NY", fieldX + 280, y + 2);
            y += rowHeight;

            var lblNwsZones = CreateLabel("Zone IDs:", labelX, y);
            txtNwsZones = CreateTextBox(fieldX, y - 2, 270);
            var lblNwsZonesHelp = CreateHelpLabel("e.g. ILZ014, CAZ006", fieldX + 280, y + 2);
            y += rowHeight;

            var lblNwsPoint = CreateLabel("Point Filter (lat,lon):", labelX, y);
            txtNwsPoint = CreateTextBox(fieldX, y - 2, 200);
            var lblNwsPointHelp = CreateHelpLabel("e.g. 41.88,-87.63 for Chicago", fieldX + 210, y + 2);
            y += rowHeight;

            var lblNwsMaxAge = CreateLabel("Max Alert Age:", labelX, y);
            numNwsMaxAgeHours = CreateNumericUpDown(fieldX, y - 2, 80, 1, 168, 24);
            var lblNwsMaxAgeUnit = CreateHelpLabel("hours", fieldX + 90, y + 2);
            y += rowHeight;

            var lblNwsPolling = CreateLabel("Polling Interval:", labelX, y);
            numNwsPollingInterval = CreateNumericUpDown(fieldX, y - 2, 80, 1, 60, 3);
            var lblNwsPollingUnit = CreateHelpLabel("minutes", fieldX + 90, y + 2);
            y += rowHeight;

            chkNwsHighRiskOnly = CreateCheckBox("High Risk Alerts Only (Extreme/Severe)", labelX, y, 320);
            y += rowHeight;

            var lblNwsSeverity = CreateLabel("Severity Filter:", labelX, y);
            txtNwsSeverityFilter = CreateTextBox(fieldX, y - 2, 270);
            var lblNwsSeverityHelp = CreateHelpLabel("e.g. Extreme, Severe, Moderate", fieldX + 280, y + 2);
            y += rowHeight;

            var lblNwsUserAgent = CreateLabel("User-Agent:", labelX, y);
            txtNwsUserAgent = CreateTextBox(fieldX, y - 2, 320);
            y += 40;

            var divider2 = CreateDivider(labelX, y, 700);
            y += 25;

            // ── Text-to-Speech ──
            var lblTts = CreateSectionHeader("Text-to-Speech Settings", labelX, y, "🎤");
            y += 35;

            var lblEngine = CreateLabel("TTS Engine:", labelX, y);
            cmbTtsEngine = CreateComboBox(fieldX, y - 2, 220);
            cmbTtsEngine.Items.AddRange(new object[] {
                "Piper (Offline, Open-Source)", "EdgeTTS (Online, Microsoft)"
            });
            cmbTtsEngine.SelectedIndex = 0;
            cmbTtsEngine.SelectedIndexChanged += (s, e) => UpdateTtsEngineVisibility();
            y += rowHeight;

            // EdgeTTS controls
            var lblVoice = CreateLabel("EdgeTTS Voice:", labelX, y);
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

            // Piper controls
            var lblPiperVoice = CreateLabel("Piper Voice:", labelX, y);
            cmbPiperVoice = CreateComboBox(fieldX, y - 2, 340);
            cmbPiperVoice.Items.AddRange(new object[] {
                "fr_FR-siwis-medium (French Female)",
                "fr_FR-upmc-medium (French Male)",
                "fr_FR-tom-medium (French Male)",
                "en_US-lessac-medium (English Female)",
                "en_US-amy-medium (English Female)",
                "en_US-ryan-medium (English Male)",
                "en_GB-alan-medium (English UK Male)",
                "en_GB-alba-medium (English UK Female)"
            });
            cmbPiperVoice.SelectedIndex = 0;
            y += rowHeight;

            var lblPiperSpeed = CreateLabel("Speech Speed:", labelX, y);
            numPiperLengthScale = new NumericUpDown
            {
                Left = fieldX, Top = y - 2, Width = 100,
                Minimum = 0.5m, Maximum = 2.0m, DecimalPlaces = 1,
                Increment = 0.1m, Value = 1.0m, Font = LabelFont
            };
            var lblPiperSpeedHelp = CreateHelpLabel("0.5=faster, 1.0=normal, 2.0=slower", fieldX + 110, y + 2);
            y += rowHeight + 5;

            var btnInstallPiper = CreatePrimaryButton("⬇ Install Piper TTS", labelX, y, 260, 32);
            btnInstallPiper.Click += (s, e) => InstallPiperTts();
            y += 45;

            // TTS visibility arrays for clean toggling
            _edgeTtsControls = new Control[] { lblVoice, cmbTtsVoice, lblRate, txtTtsRate, lblRateHelp, lblPitch, txtTtsPitch, lblPitchHelp, btnDownloadVoices };
            _piperTtsControls = new Control[] { lblPiperVoice, cmbPiperVoice, lblPiperSpeed, numPiperLengthScale, lblPiperSpeedHelp, btnInstallPiper };

            var lblTtsNote = new Label
            {
                Text = "💡 Piper TTS: Open-source, offline, high-quality neural TTS (requires download).\n   EdgeTTS: Online Microsoft service, works immediately without installation.\n   Piper is recommended for privacy and offline usage.",
                Left = labelX, Top = y, Width = 550, Height = 55,
                Font = SmallFont, ForeColor = TextMutedColor, Tag = "muted", AutoSize = false
            };

            tab.Controls.AddRange(new Control[] {
                lblAlertReady, chkAlertReadyEnabled, lblFeedUrls, txtAlertReadyFeedUrls,
                chkAlertReadyIncludeTests, lblMaxAge, numAlertReadyMaxAgeHours, lblMaxAgeUnit,
                lblLanguage, cmbAlertReadyLanguage, lblAreaFilters, txtAlertReadyAreaFilters, lblAreaHelp,
                lblJurisdictions, txtAlertReadyJurisdictions, lblJurisHelp,
                chkAlertReadyHighRiskOnly, chkAlertReadyExcludeWeather, divider1,
                lblNws, chkNwsEnabled, lblNwsStates, txtNwsStates, lblNwsStatesHelp,
                lblNwsZones, txtNwsZones, lblNwsZonesHelp,
                lblNwsPoint, txtNwsPoint, lblNwsPointHelp,
                lblNwsMaxAge, numNwsMaxAgeHours, lblNwsMaxAgeUnit,
                lblNwsPolling, numNwsPollingInterval, lblNwsPollingUnit,
                chkNwsHighRiskOnly, lblNwsSeverity, txtNwsSeverityFilter, lblNwsSeverityHelp,
                lblNwsUserAgent, txtNwsUserAgent, divider2,
                lblTts, lblEngine, cmbTtsEngine,
                lblVoice, cmbTtsVoice, lblRate, txtTtsRate, lblRateHelp,
                lblPitch, txtTtsPitch, lblPitchHelp, btnDownloadVoices,
                lblPiperVoice, cmbPiperVoice, lblPiperSpeed, numPiperLengthScale, lblPiperSpeedHelp, btnInstallPiper,
                lblTtsNote
            });

            return tab;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Tab 5: Network (Web UI)
        // ═══════════════════════════════════════════════════════════════════

        private TabPage BuildNetworkTab()
        {
            var tab = new TabPage("Network") { BackColor = BackgroundColor, Padding = new Padding(20), AutoScroll = true };
            int y = 15;
            int labelX = 20;
            int fieldX = 200;
            int rowHeight = 38;

            var lblHeader = CreateSectionHeader("Remote Web Interface", labelX, y, "🌐");
            y += 35;

            var lblDesc = new Label
            {
                Text = "Enable a web interface to access your weather display from any browser on your network.",
                Left = labelX, Top = y, Width = 600, Height = 25,
                Font = LabelFont, ForeColor = TextMutedColor, Tag = "muted", AutoSize = false
            };
            y += 40;

            chkWebUIEnabled = CreateCheckBox("Enable Web UI Server", labelX, y, 250);
            chkWebUIEnabled.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            y += 40;

            var divider1 = CreateDivider(labelX, y, 700);
            y += 25;

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
            txtWebUIUrl.BackColor = ThemeManager.Current.InputBackground;
            y += rowHeight;

            lblLocalIP = new Label
            {
                Left = labelX, Top = y, Width = 520, Height = 25,
                Font = LabelFont, ForeColor = AccentColor, Tag = "accent",
                Text = "Local IP: Checking...", AutoSize = false, Visible = false
            };
            y += 28;

            lblPublicIP = new Label
            {
                Left = labelX, Top = y, Width = 520, Height = 25,
                Font = LabelFont, ForeColor = AccentColor, Tag = "accent",
                Text = "Public IP: Checking...", AutoSize = false, Visible = false
            };
            y += rowHeight;

            lblWebUIStatus = new Label
            {
                Left = labelX, Top = y, Width = 450, Height = 25,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMutedColor, Tag = "muted",
                Text = "Status: Not running", AutoSize = false
            };
            y += 40;

            btnTestWebUI = CreateSecondaryButton("🔗 Test Connection", labelX, y, 150, 34);
            btnTestWebUI.Click += (s, e) => TestWebUIConnection();

            var btnOpenInBrowser = CreatePrimaryButton("🌐 Open in Browser", labelX + 165, y, 150, 34);
            btnOpenInBrowser.Click += (s, e) => OpenWebUIInBrowser();
            y += 55;

            var divider2 = CreateDivider(labelX, y, 700);
            y += 25;

            var lblSecurityHeader = CreateSubHeader("⚠ Security Notice", labelX, y);
            lblSecurityHeader.ForeColor = WarningColor;
            lblSecurityHeader.Tag = "warning";
            y += 30;

            var lblSecurityNote = new Label
            {
                Text = "When 'Allow Remote Access' is enabled, any device on your local network can access the web interface.\n" +
                       "If you want to access from the internet, you'll need to configure port forwarding on your router.\n" +
                       "Consider using a VPN or reverse proxy with authentication for internet access.",
                Left = labelX, Top = y, Width = 680, Height = 60,
                Font = SmallFont, ForeColor = TextColor, AutoSize = false
            };

            // Event handlers
            chkWebUIEnabled.CheckedChanged += (s, e) => { if (!_isLoadingSettings) OnWebUIEnabledChanged(); };
            numWebUIPort.ValueChanged += (s, e) =>
            {
                if (!_isLoadingSettings)
                {
                    UpdateWebUIUrl();
                    if (chkWebUIAllowRemote.Checked) UpdateIPAddressDisplay();
                }
            };
            chkWebUIAllowRemote.CheckedChanged += (s, e) =>
            {
                if (!_isLoadingSettings)
                {
                    UpdateWebUIUrl();
                    UpdateIPAddressDisplay();
                }
            };

            tab.Controls.AddRange(new Control[] {
                lblHeader, lblDesc, chkWebUIEnabled, divider1,
                lblConfig, lblPort, numWebUIPort, lblPortHelp,
                chkWebUIAllowRemote, lblUrl, txtWebUIUrl, lblLocalIP, lblPublicIP, lblWebUIStatus,
                btnTestWebUI, btnOpenInBrowser, divider2,
                lblSecurityHeader, lblSecurityNote
            });

            return tab;
        }

        #endregion

        #region Event Handlers

        private void SetupVideoTabEventHandlers()
        {
            // Quality preset handler
            cmbQualityPreset.SelectedIndexChanged += (s, e) =>
            {
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

            // Mark as custom when user changes individual settings
            EventHandler markCustom = (s, e) =>
            {
                if (!_isLoadingSettings && cmbQualityPreset.SelectedIndex != 5)
                    cmbQualityPreset.SelectedIndex = 5;
            };
            cmbResolution.SelectedIndexChanged += markCustom;
            cmbCodec.SelectedIndexChanged += markCustom;
            cmbBitrate.SelectedIndexChanged += markCustom;
            numFps.ValueChanged += markCustom;

            // Sync alert duration with slide duration
            numStatic.ValueChanged += (s, e) =>
            {
                if (!_isLoadingSettings)
                    numAlertDisplayDurationSeconds.Value = numStatic.Value;
            };

            // Hardware encoding check
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

        private void UpdateTtsEngineVisibility()
        {
            bool isEdge = cmbTtsEngine.SelectedIndex == 1;
            if (_edgeTtsControls != null)
                foreach (var c in _edgeTtsControls) c.Visible = isEdge;
            if (_piperTtsControls != null)
                foreach (var c in _piperTtsControls) c.Visible = !isEdge;
        }

        private async void InstallPiperTts()
        {
            var result = MessageBox.Show(this,
                "This will download and install Piper TTS (approximately 10-15 MB).\n\n" +
                "Piper is an open-source, offline text-to-speech engine that provides high-quality neural voices.\n\n" +
                "Voice models will be downloaded automatically when first used.\n\n" +
                "Continue?",
                "Install Piper TTS",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            var progressForm = new Form
            {
                Text = "Installing Piper TTS",
                Width = 400, Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };

            var lblStatus = new Label
            {
                Text = "Downloading Piper TTS...",
                Left = 20, Top = 20, Width = 350, Height = 60,
                Font = new Font("Segoe UI", 10F)
            };

            progressForm.Controls.Add(lblStatus);
            progressForm.Show(this);

            try
            {
                using var client = new EAS.PiperTtsClient();

                var progress = new Progress<string>(msg =>
                {
                    if (progressForm.IsHandleCreated)
                        progressForm.Invoke((Action)(() => lblStatus.Text = msg));
                });

                bool success = await client.InstallPiperAsync(progress);
                progressForm.Close();

                if (success)
                {
                    MessageBox.Show(this,
                        "Piper TTS has been installed successfully!\n\n" +
                        "Voice models will be downloaded automatically when you generate audio.",
                        "Installation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this,
                        "Failed to install Piper TTS.\n\nPlease check your internet connection and try again.",
                        "Installation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                progressForm.Close();
                MessageBox.Show(this, $"Error installing Piper TTS:\n\n{ex.Message}",
                    "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

                // ── General Tab ──
                txtImageOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                txtVideoOutputDir.Text = Path.Combine(Directory.GetCurrentDirectory(), cfg.Video?.OutputDirectory ?? cfg.ImageGeneration?.OutputDirectory ?? "WeatherImages");
                numRefresh.Value = cfg.RefreshTimeMinutes;

                var theme = cfg.Theme ?? "Blue";
                if (cmbTheme.Items.Contains(theme)) cmbTheme.SelectedItem = theme;
                else cmbTheme.SelectedItem = "Blue";

                cmbDefaultWeatherApi.SelectedIndex = cfg.DefaultWeatherApi switch
                {
                    Models.WeatherApiType.ECCC => 1,
                    Models.WeatherApiType.Hybrid => 2,
                    _ => 0
                };

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

                // ── Image (Output Tab) ──
                numImgWidth.Value = cfg.ImageGeneration?.ImageWidth ?? 1920;
                numImgHeight.Value = cfg.ImageGeneration?.ImageHeight ?? 1080;
                var fmt = (cfg.ImageGeneration?.ImageFormat ?? "png").ToLowerInvariant();
                if (cmbImgFormat.Items.Contains(fmt)) cmbImgFormat.SelectedItem = fmt;

                var fontFamily = cfg.ImageGeneration?.FontFamily ?? "Arial";
                if (cmbFontFamily.Items.Contains(fontFamily)) cmbFontFamily.SelectedItem = fontFamily;
                else cmbFontFamily.SelectedIndex = 0;

                chkEnableProvinceRadar.Checked = cfg.ECCC?.EnableProvinceRadar ?? true;
                chkEnableWeatherMaps.Checked = cfg.ImageGeneration?.EnableWeatherMaps ?? true;

                // ── Video (Output Tab) ──
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
                if (_experimentalSection != null) _experimentalSection.Enabled = chkEnableExperimental.Checked;

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

                // ── Experimental (Output Tab) ──
                chkUseCrfEncoding.Checked = cfg.Video?.UseCrfEncoding ?? true;
                numCrf.Value = cfg.Video?.CrfValue ?? 23;
                txtMaxBitrate.Text = cfg.Video?.MaxBitrate ?? string.Empty;
                txtBufferSize.Text = cfg.Video?.BufferSize ?? string.Empty;
                var preset = cfg.Video?.EncoderPreset ?? "medium";
                if (cmbEncoderPreset.Items.Contains(preset)) cmbEncoderPreset.SelectedItem = preset;
                else cmbEncoderPreset.SelectedIndex = 5;

                // ── FFmpeg (Output Tab) ──
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

                // ── Alerts & TTS Tab ──
                var alertReady = cfg.AlertReady ?? new EAS.AlertReady.AlertReadyOptions();
                chkAlertReadyEnabled.Checked = alertReady.Enabled;
                txtAlertReadyFeedUrls.Text = alertReady.FeedUrls != null ? string.Join(Environment.NewLine, alertReady.FeedUrls) : "";
                chkAlertReadyIncludeTests.Checked = alertReady.IncludeTests;
                numAlertReadyMaxAgeHours.Value = alertReady.MaxAgeHours;
                cmbAlertReadyLanguage.SelectedItem = alertReady.PreferredLanguage;
                txtAlertReadyAreaFilters.Text = alertReady.AreaFilters != null ? string.Join(", ", alertReady.AreaFilters) : "";
                txtAlertReadyJurisdictions.Text = alertReady.Jurisdictions != null ? string.Join(", ", alertReady.Jurisdictions) : "QC, CA";
                chkAlertReadyHighRiskOnly.Checked = alertReady.HighRiskOnly;
                chkAlertReadyExcludeWeather.Checked = alertReady.ExcludeWeatherAlerts;

                var nws = cfg.Nws ?? new EAS.NWS.NwsOptions();
                chkNwsEnabled.Checked = nws.Enabled;
                txtNwsStates.Text = nws.States != null ? string.Join(", ", nws.States) : "";
                txtNwsZones.Text = nws.Zones != null ? string.Join(", ", nws.Zones) : "";
                txtNwsPoint.Text = nws.Point ?? "";
                numNwsMaxAgeHours.Value = Math.Clamp(nws.MaxAgeHours, 1, 168);
                numNwsPollingInterval.Value = Math.Clamp(nws.PollingIntervalMinutes, 1, 60);
                chkNwsHighRiskOnly.Checked = nws.HighRiskOnly;
                txtNwsSeverityFilter.Text = nws.SeverityFilter != null ? string.Join(", ", nws.SeverityFilter) : "";
                txtNwsUserAgent.Text = nws.UserAgent;

                var tts = cfg.TTS ?? new TTSSettings();
                cmbTtsEngine.SelectedIndex = (tts.Engine?.ToLowerInvariant() == "edge") ? 1 : 0;

                var voiceDisplay2 = tts.Voice switch
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
                if (cmbTtsVoice.Items.Contains(voiceDisplay2)) cmbTtsVoice.SelectedItem = voiceDisplay2;
                else cmbTtsVoice.SelectedIndex = 0;
                txtTtsRate.Text = tts.Rate;
                txtTtsPitch.Text = tts.Pitch;

                string piperVoice = tts.PiperVoice ?? "fr_FR-siwis-medium";
                var piperVoiceDisplay = piperVoice switch
                {
                    "fr_FR-siwis-medium" => "fr_FR-siwis-medium (French Female)",
                    "fr_FR-upmc-medium" => "fr_FR-upmc-medium (French Male)",
                    "fr_FR-tom-medium" => "fr_FR-tom-medium (French Male)",
                    "en_US-lessac-medium" => "en_US-lessac-medium (English Female)",
                    "en_US-amy-medium" => "en_US-amy-medium (English Female)",
                    "en_US-ryan-medium" => "en_US-ryan-medium (English Male)",
                    "en_GB-alan-medium" => "en_GB-alan-medium (English UK Male)",
                    "en_GB-alba-medium" => "en_GB-alba-medium (English UK Female)",
                    _ => "fr_FR-siwis-medium (French Female)"
                };
                if (cmbPiperVoice.Items.Contains(piperVoiceDisplay)) cmbPiperVoice.SelectedItem = piperVoiceDisplay;
                else cmbPiperVoice.SelectedIndex = 0;
                numPiperLengthScale.Value = (decimal)(tts.PiperLengthScale ?? 1.0f);

                UpdateTtsEngineVisibility();

                // ── Map Tab ──
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

                var renderApi = (openMap.RenderingApi ?? "OpenGL").ToLowerInvariant();
                int renderIdx = renderApi switch
                {
                    "opengl" => 0,
                    "vulkan" => 1,
                    "directx11" => 2,
                    _ => 0
                };
                // If the saved API is disabled, fall back to OpenGL
                if (renderIdx == 1 || (renderIdx == 2 && !_dx11Available))
                    renderIdx = 0;
                cmbRenderApi.SelectedIndex = renderIdx;
                _previousRenderApiIndex = renderIdx;

                // ── Network Tab ──
                var webUI = cfg.WebUI ?? new WebUISettings();
                chkWebUIEnabled.Checked = webUI.Enabled;
                numWebUIPort.Value = webUI.Port;
                chkWebUIAllowRemote.Checked = webUI.AllowRemoteAccess;
                UpdateWebUIUrl();
                UpdateWebUIStatus();
                UpdateIPAddressDisplay();

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

                var alertReady = cfg.AlertReady ?? new EAS.AlertReady.AlertReadyOptions();
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

                var nwsOpt = cfg.Nws ?? new EAS.NWS.NwsOptions();
                nwsOpt.Enabled = chkNwsEnabled.Checked;
                nwsOpt.States = txtNwsStates.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                nwsOpt.Zones = txtNwsZones.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpperInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                nwsOpt.Point = string.IsNullOrWhiteSpace(txtNwsPoint.Text) ? null : txtNwsPoint.Text.Trim();
                nwsOpt.MaxAgeHours = (int)numNwsMaxAgeHours.Value;
                nwsOpt.PollingIntervalMinutes = (int)numNwsPollingInterval.Value;
                nwsOpt.HighRiskOnly = chkNwsHighRiskOnly.Checked;
                nwsOpt.SeverityFilter = txtNwsSeverityFilter.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                nwsOpt.UserAgent = string.IsNullOrWhiteSpace(txtNwsUserAgent.Text)
                    ? "WSG-Weather-Still-Generator/1.0"
                    : txtNwsUserAgent.Text.Trim();
                cfg.Nws = nwsOpt;

                var tts = cfg.TTS ?? new TTSSettings();
                tts.Engine = cmbTtsEngine.SelectedIndex == 1 ? "edge" : "piper";
                var voiceDisplay = cmbTtsVoice.SelectedItem?.ToString() ?? "fr-CA-SylvieNeural (Female)";
                tts.Voice = voiceDisplay.Split(' ')[0];
                tts.Rate = txtTtsRate.Text.Trim();
                tts.Pitch = txtTtsPitch.Text.Trim();
                var piperVoiceDisplay = cmbPiperVoice.SelectedItem?.ToString() ?? "fr_FR-siwis-medium (French Female)";
                tts.PiperVoice = piperVoiceDisplay.Split(' ')[0];
                tts.PiperLengthScale = (float)numPiperLengthScale.Value;
                cfg.TTS = tts;

                var v = cfg.Video ?? new VideoSettings();
                v.StaticDurationSeconds = (double)numStatic.Value;
                v.FadeDurationSeconds = (double)numFade.Value;
                v.EnableFadeTransitions = chkFade.Checked;
                v.UseTotalDuration = chkUseTotalDuration.Checked;
                v.TotalDurationSeconds = (double)numTotalDuration.Value;

                var resDisplay2 = cmbResolution.SelectedItem?.ToString() ?? "1920x1080 (Full HD)";
                v.ResolutionMode = resDisplay2 switch
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

                var codecDisplay2 = cmbCodec.SelectedItem?.ToString() ?? "libx264 (H.264)";
                v.VideoCodec = codecDisplay2 switch
                {
                    "libx264 (H.264)" => "libx264",
                    "libx265 (H.265/HEVC)" => "libx265",
                    "libvpx-vp9 (VP9)" => "libvpx-vp9",
                    "libaom-av1 (AV1)" => "libaom-av1",
                    "mpeg4" => "mpeg4",
                    "msmpeg4" => "msmpeg4",
                    _ => "libx264"
                };

                var bitrateDisplay2 = cmbBitrate.SelectedItem?.ToString() ?? "4M (Medium)";
                v.VideoBitrate = bitrateDisplay2.Split(' ')[0];

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
                cfg.DefaultWeatherApi = cmbDefaultWeatherApi.SelectedIndex switch
                {
                    1 => Models.WeatherApiType.ECCC,
                    2 => Models.WeatherApiType.Hybrid,
                    _ => Models.WeatherApiType.OpenMeteo
                };
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
                openMap.RenderingApi = cmbRenderApi.SelectedIndex switch
                {
                    0 => "OpenGL",
                    1 => "Vulkan",
                    2 => "DirectX11",
                    _ => "OpenGL"
                };
                cfg.OpenMap = openMap;

                var webUI = cfg.WebUI ?? new WebUISettings();
                bool wasEnabled = webUI.Enabled;
                int oldPort = webUI.Port;
                bool wasRemoteAccessEnabled = webUI.AllowRemoteAccess;

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

                if (chkWebUIAllowRemote.Checked && !wasRemoteAccessEnabled)
                {
                    MessageBox.Show(this,
                        "Remote Access has been enabled. Please restart the application so the boot check service can activate the firewall rule and URL ACL (UAC elevation will be required).",
                        "Restart Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private void UpdateIPAddressDisplay()
        {
            if (chkWebUIAllowRemote.Checked)
            {
                lblLocalIP.Visible = true;
                lblPublicIP.Visible = true;

                Task.Run(async () =>
                {
                    string localIP = Utilities.NetworkHelper.GetLocalIPAddress();
                    string publicIP = await Utilities.NetworkHelper.GetPublicIPAddressAsync();
                    int port = 0;

                    if (this.IsHandleCreated)
                    {
                        this.Invoke((Action)(() =>
                        {
                            port = (int)numWebUIPort.Value;
                            lblLocalIP.Text = $"🌐 Local IP Address: {localIP}:{port}";
                            lblPublicIP.Text = $"🌍 Public IP Address: {publicIP}:{port}";
                            lblLocalIP.ForeColor = localIP == "Unable to determine" ? DangerColor : AccentColor;
                            lblPublicIP.ForeColor = publicIP == "Unable to determine" ? DangerColor : AccentColor;
                        }));
                    }
                });
            }
            else
            {
                lblLocalIP.Visible = false;
                lblPublicIP.Visible = false;
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

        #region Update Methods



        #endregion
    }
}
