#nullable enable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Centralized theme management service. Provides consistent color palettes
    /// across all forms and supports live theme switching via the ThemeChanged event.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>
        /// Immutable set of colors defining a complete application theme.
        /// </summary>
        public class ThemeColors
        {
            public string Name { get; init; } = "Blue";

            // Core palette
            public Color Primary { get; init; }
            public Color Secondary { get; init; }
            public Color Accent { get; init; }
            public Color Success { get; init; }
            public Color Warning { get; init; }
            public Color Danger { get; init; }

            // Backgrounds
            public Color Background { get; init; }
            public Color Surface { get; init; }
            public Color CardBackground { get; init; }
            public Color InputBackground { get; init; }

            // Text
            public Color TextPrimary { get; init; }
            public Color TextSecondary { get; init; }
            public Color TextDim { get; init; }
            public Color TextOnAccent { get; init; }
            public Color TextOnButton { get; init; }
            public Color TextOnWarning { get; init; }

            // UI elements
            public Color ButtonBackground { get; init; }
            public Color ButtonHover { get; init; }
            public Color Border { get; init; }
            public Color Separator { get; init; }

            /// <summary>True if this is a dark theme (text is light on dark backgrounds)</summary>
            public bool IsDark { get; init; }
        }

        // ── Built-in theme presets ──────────────────────────────────
        public static readonly ThemeColors Blue = new ThemeColors
        {
            Name = "Blue",
            IsDark = true,
            Primary = ColorTranslator.FromHtml("#2C3E50"),
            Secondary = ColorTranslator.FromHtml("#34495E"),
            Accent = ColorTranslator.FromHtml("#4A90B8"),
            Success = ColorTranslator.FromHtml("#27AE60"),
            Warning = ColorTranslator.FromHtml("#E6A23C"),
            Danger = ColorTranslator.FromHtml("#D94F4F"),
            Background = ColorTranslator.FromHtml("#1E2A36"),
            Surface = ColorTranslator.FromHtml("#263645"),
            CardBackground = ColorTranslator.FromHtml("#2F4254"),
            InputBackground = ColorTranslator.FromHtml("#374F63"),
            TextPrimary = ColorTranslator.FromHtml("#E8ECF0"),
            TextSecondary = ColorTranslator.FromHtml("#B0BEC5"),
            TextDim = ColorTranslator.FromHtml("#7E929E"),
            TextOnAccent = Color.White,
            TextOnButton = ColorTranslator.FromHtml("#E8ECF0"),
            TextOnWarning = ColorTranslator.FromHtml("#1E2A36"),
            ButtonBackground = ColorTranslator.FromHtml("#3A5268"),
            ButtonHover = ColorTranslator.FromHtml("#4A6A84"),
            Border = ColorTranslator.FromHtml("#3A5268"),
            Separator = ColorTranslator.FromHtml("#2F4254")
        };

        public static readonly ThemeColors Dark = new ThemeColors
        {
            Name = "Dark",
            IsDark = true,
            Primary = ColorTranslator.FromHtml("#161719"),
            Secondary = ColorTranslator.FromHtml("#1E2024"),
            Accent = ColorTranslator.FromHtml("#5B9BD5"),
            Success = ColorTranslator.FromHtml("#4CAF6E"),
            Warning = ColorTranslator.FromHtml("#E6A23C"),
            Danger = ColorTranslator.FromHtml("#D94F4F"),
            Background = ColorTranslator.FromHtml("#121314"),
            Surface = ColorTranslator.FromHtml("#1A1C1E"),
            CardBackground = ColorTranslator.FromHtml("#222528"),
            InputBackground = ColorTranslator.FromHtml("#2A2D31"),
            TextPrimary = ColorTranslator.FromHtml("#DCDFE3"),
            TextSecondary = ColorTranslator.FromHtml("#9EA4AD"),
            TextDim = ColorTranslator.FromHtml("#6B7280"),
            TextOnAccent = Color.White,
            TextOnButton = ColorTranslator.FromHtml("#DCDFE3"),
            TextOnWarning = ColorTranslator.FromHtml("#121314"),
            ButtonBackground = ColorTranslator.FromHtml("#32363B"),
            ButtonHover = ColorTranslator.FromHtml("#42474E"),
            Border = ColorTranslator.FromHtml("#32363B"),
            Separator = ColorTranslator.FromHtml("#2A2D31")
        };

        public static readonly ThemeColors Light = new ThemeColors
        {
            Name = "Light",
            IsDark = false,
            Primary = ColorTranslator.FromHtml("#F5F6F8"),
            Secondary = Color.White,
            Accent = ColorTranslator.FromHtml("#3B6FC1"),
            Success = ColorTranslator.FromHtml("#2D8C55"),
            Warning = ColorTranslator.FromHtml("#D4940A"),
            Danger = ColorTranslator.FromHtml("#C93B3B"),
            Background = ColorTranslator.FromHtml("#F0F2F5"),
            Surface = ColorTranslator.FromHtml("#F7F8FA"),
            CardBackground = Color.White,
            InputBackground = Color.White,
            TextPrimary = ColorTranslator.FromHtml("#1A1D23"),
            TextSecondary = ColorTranslator.FromHtml("#4A5568"),
            TextDim = ColorTranslator.FromHtml("#718096"),
            TextOnAccent = Color.White,
            TextOnButton = ColorTranslator.FromHtml("#1A1D23"),
            TextOnWarning = ColorTranslator.FromHtml("#1A1D23"),
            ButtonBackground = ColorTranslator.FromHtml("#E2E5EA"),
            ButtonHover = ColorTranslator.FromHtml("#CBD0D8"),
            Border = ColorTranslator.FromHtml("#D1D5DB"),
            Separator = ColorTranslator.FromHtml("#E2E5EA")
        };

        public static readonly ThemeColors Green = new ThemeColors
        {
            Name = "Green",
            IsDark = true,
            Primary = ColorTranslator.FromHtml("#152A1E"),
            Secondary = ColorTranslator.FromHtml("#1E3A2A"),
            Accent = ColorTranslator.FromHtml("#3D8B6E"),
            Success = ColorTranslator.FromHtml("#48B07A"),
            Warning = ColorTranslator.FromHtml("#E6A23C"),
            Danger = ColorTranslator.FromHtml("#D94F4F"),
            Background = ColorTranslator.FromHtml("#0E1F15"),
            Surface = ColorTranslator.FromHtml("#152A1E"),
            CardBackground = ColorTranslator.FromHtml("#1E3A2A"),
            InputBackground = ColorTranslator.FromHtml("#264D35"),
            TextPrimary = ColorTranslator.FromHtml("#E0EDE6"),
            TextSecondary = ColorTranslator.FromHtml("#9CBFAA"),
            TextDim = ColorTranslator.FromHtml("#6A9880"),
            TextOnAccent = Color.White,
            TextOnButton = ColorTranslator.FromHtml("#E0EDE6"),
            TextOnWarning = ColorTranslator.FromHtml("#0E1F15"),
            ButtonBackground = ColorTranslator.FromHtml("#2D5A40"),
            ButtonHover = ColorTranslator.FromHtml("#3A7353"),
            Border = ColorTranslator.FromHtml("#2D5A40"),
            Separator = ColorTranslator.FromHtml("#264D35")
        };

        // ── State ──────────────────────────────────────────────────
        private static ThemeColors _current = Blue;

        /// <summary>The currently active theme.</summary>
        public static ThemeColors Current => _current;

        /// <summary>
        /// Fired whenever the theme changes. All open forms should subscribe
        /// to this event and re-apply colors when it fires.
        /// </summary>
        public static event Action<ThemeColors>? ThemeChanged;

        /// <summary>
        /// Sets the active theme by name and notifies all subscribers.
        /// </summary>
        public static void SetTheme(string? name)
        {
            _current = Resolve(name);
            ThemeChanged?.Invoke(_current);
        }

        /// <summary>
        /// Resolves a theme name to its ThemeColors preset.
        /// </summary>
        public static ThemeColors Resolve(string? name)
        {
            return (name?.ToLowerInvariant()) switch
            {
                "light" => Light,
                "dark" => Dark,
                "green" => Green,
                _ => Blue
            };
        }

        /// <summary>
        /// Initializes the theme from the current config without firing the event.
        /// Call once at startup before any forms are created.
        /// </summary>
        public static void Initialize(string? themeName)
        {
            _current = Resolve(themeName);
        }

        // ═══════════════════════════════════════════════════════════
        //  Helper: recursively apply theme colors to a form and all children
        // ═══════════════════════════════════════════════════════════

        // ── Win32 DWM interop for dark title bar ───────────────
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        /// <summary>
        /// Applies dark/light title bar chrome to match the active theme.
        /// </summary>
        public static void ApplyTitleBar(Form form, ThemeColors? theme = null)
        {
            var t = theme ?? _current;
            try
            {
                int useDark = t.IsDark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                // Force Windows to repaint the title bar
                form.Invalidate();
            }
            catch { /* DWM call may fail on older Windows versions */ }
        }

        /// <summary>
        /// Applies the current theme's base colors to a form and all its child controls recursively.
        /// Also sets the window title bar to dark mode when using a dark theme.
        /// </summary>
        public static void ApplyTo(Control control, ThemeColors? theme = null)
        {
            var t = theme ?? _current;

            control.BackColor = t.Surface;
            control.ForeColor = t.TextPrimary;

            // Apply dark/light title bar for Forms
            if (control is Form form)
            {
                ApplyTitleBar(form, t);
            }

            foreach (Control child in control.Controls)
            {
                ApplyToControl(child, t);
            }
        }

        /// <summary>
        /// Applies theme to an individual control based on its type.
        /// </summary>
        public static void ApplyToControl(Control control, ThemeColors? theme = null)
        {
            var t = theme ?? _current;

            switch (control)
            {
                case Button btn:
                    if (btn.FlatStyle == FlatStyle.Flat)
                    {
                        // Don't override buttons that have explicit colors (semantic buttons)
                        // Only set neutral button style if no tag override
                        if (btn.Tag is not Color[])
                        {
                            btn.BackColor = t.ButtonBackground;
                            btn.ForeColor = t.TextOnButton;
                            btn.FlatAppearance.BorderColor = t.Border;
                            btn.FlatAppearance.MouseOverBackColor = t.ButtonHover;
                        }
                    }
                    break;

                case TextBox txt:
                    txt.BackColor = t.InputBackground;
                    txt.ForeColor = t.TextPrimary;
                    txt.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case RichTextBox rtb:
                    rtb.BackColor = t.Background;
                    rtb.ForeColor = t.TextSecondary;
                    break;

                case ComboBox cmb:
                    cmb.BackColor = t.InputBackground;
                    cmb.ForeColor = t.TextPrimary;
                    cmb.FlatStyle = FlatStyle.Flat;
                    break;

                case CheckBox chk:
                    // Respect semantic tags for themed checkboxes
                    if (chk.Tag is string chkSemantic)
                    {
                        chk.ForeColor = ResolveSemanticColor(chkSemantic, t);
                    }
                    else
                    {
                        chk.ForeColor = t.TextPrimary;
                    }
                    break;

                case NumericUpDown nud:
                    nud.BackColor = t.InputBackground;
                    nud.ForeColor = t.TextPrimary;
                    break;

                case GroupBox gb:
                    gb.ForeColor = t.Accent;
                    gb.BackColor = t.CardBackground;
                    foreach (Control c in gb.Controls)
                        ApplyToControl(c, t);
                    break;

                case TabControl tc:
                    ApplyOwnerDrawTabs(tc, t);
                    foreach (TabPage page in tc.TabPages)
                    {
                        page.BackColor = t.Surface;
                        page.ForeColor = t.TextPrimary;
                        foreach (Control c in page.Controls)
                            ApplyToControl(c, t);
                    }
                    break;

                case FlowLayoutPanel flp:
                    flp.BackColor = t.Surface;
                    foreach (Control c in flp.Controls)
                        ApplyToControl(c, t);
                    break;

                case TableLayoutPanel tlp:
                    tlp.BackColor = t.Surface;
                    foreach (Control c in tlp.Controls)
                        ApplyToControl(c, t);
                    break;

                case SplitContainer sc:
                    sc.BackColor = t.Surface;
                    sc.Panel1.BackColor = t.CardBackground;
                    sc.Panel2.BackColor = t.Surface;
                    foreach (Control c in sc.Panel1.Controls)
                        ApplyToControl(c, t);
                    foreach (Control c in sc.Panel2.Controls)
                        ApplyToControl(c, t);
                    break;

                case TabPage tp:
                    tp.BackColor = t.Surface;
                    tp.ForeColor = t.TextPrimary;
                    foreach (Control c in tp.Controls)
                        ApplyToControl(c, t);
                    break;

                case Panel pnl:
                    pnl.BackColor = t.Surface;
                    foreach (Control c in pnl.Controls)
                        ApplyToControl(c, t);
                    break;

                case LinkLabel ll:
                    ll.LinkColor = t.Accent;
                    ll.ActiveLinkColor = ControlPaint.Light(t.Accent, 0.3f);
                    break;

                case Label lbl:
                    // Thin separator / divider lines
                    if (lbl.Height <= 2 && string.IsNullOrEmpty(lbl.Text))
                    {
                        lbl.BackColor = t.Border;
                        break;
                    }
                    // Semantic label color via Tag
                    if (lbl.Tag is string labelSemantic)
                    {
                        lbl.ForeColor = ResolveSemanticColor(labelSemantic, t);
                    }
                    else
                    {
                        lbl.ForeColor = t.TextPrimary;
                    }
                    break;

                case ListView lv:
                    lv.BackColor = t.CardBackground;
                    lv.ForeColor = t.TextPrimary;
                    lv.OwnerDraw = true;
                    ApplyOwnerDrawListView(lv, t);
                    break;

                case TrackBar _:
                    // TrackBar doesn't support BackColor/ForeColor customization well
                    break;

                case ProgressBar _:
                    // ProgressBar visual style is system-managed
                    break;

                default:
                    control.BackColor = t.Surface;
                    control.ForeColor = t.TextPrimary;
                    break;
            }
        }

        /// <summary>
        /// Styles a flat button with a specific semantic color (success, danger, accent, etc.)
        /// </summary>
        public static void StyleButton(Button btn, Color bgColor, Color fgColor, ThemeColors? theme = null)
        {
            var t = theme ?? _current;
            btn.FlatStyle = FlatStyle.Flat;
            btn.BackColor = bgColor;
            btn.ForeColor = fgColor;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.BorderColor = ControlPaint.Light(bgColor, 0.2f);
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bgColor, 0.15f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(bgColor, 0.15f);
            btn.Tag = new Color[] { bgColor, fgColor };
            btn.Cursor = Cursors.Hand;
        }

        /// <summary>
        /// Resolves a semantic tag string (e.g., "accent", "warning") to the corresponding theme color.
        /// Controls can set Tag = "accent" | "muted" | "dim" | "warning" | "danger" | "success"
        /// and ApplyToControl will map it to the active theme's palette.
        /// </summary>
        private static Color ResolveSemanticColor(string semantic, ThemeColors t)
        {
            return semantic switch
            {
                "accent" => t.Accent,
                "muted" => t.TextSecondary,
                "dim" => t.TextDim,
                "warning" => t.Warning,
                "danger" => t.Danger,
                "success" => t.Success,
                _ => t.TextPrimary
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  Owner-drawn ListView for themed column headers
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Applies owner-draw handlers to a ListView so column headers and items
        /// render with theme colors. Safe to call multiple times.
        /// </summary>
        private static void ApplyOwnerDrawListView(ListView lv, ThemeColors t)
        {
            // Remove previous handlers stored in Tag
            if (lv.Tag is (DrawListViewColumnHeaderEventHandler oldCol, DrawListViewItemEventHandler oldItem, DrawListViewSubItemEventHandler oldSub))
            {
                lv.DrawColumnHeader -= oldCol;
                lv.DrawItem -= oldItem;
                lv.DrawSubItem -= oldSub;
            }

            var capturedTheme = t;

            DrawListViewColumnHeaderEventHandler colHandler = (sender, e) =>
            {
                using var bgBrush = new SolidBrush(capturedTheme.Surface);
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
                using var textBrush = new SolidBrush(capturedTheme.TextPrimary);
                var font = new Font(e.Font ?? SystemFonts.DefaultFont, FontStyle.Bold);
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
                var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                e.Graphics.DrawString(e.Header?.Text ?? "", font, textBrush, textRect, sf);
                font.Dispose();
                // Bottom separator
                using var sepPen = new Pen(capturedTheme.Border, 1f);
                e.Graphics.DrawLine(sepPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };

            DrawListViewItemEventHandler itemHandler = (sender, e) =>
            {
                e.DrawDefault = true;
            };

            DrawListViewSubItemEventHandler subHandler = (sender, e) =>
            {
                e.DrawDefault = true;
            };

            lv.DrawColumnHeader += colHandler;
            lv.DrawItem += itemHandler;
            lv.DrawSubItem += subHandler;
            lv.Tag = (colHandler, itemHandler, subHandler);
        }

        // ═══════════════════════════════════════════════════════════
        //  Owner-drawn TabControl for themed tab headers
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Holds the themed painting state attached to a TabControl via its Tag property.
        /// </summary>
        private sealed class TabThemeState
        {
            public DrawItemEventHandler? DrawHandler;
            public PaintEventHandler? PaintHandler;
            public ThemedTabPainter? Painter;
        }

        /// <summary>
        /// NativeWindow subclass that intercepts low-level paint messages for a TabControl
        /// to eliminate all white border artifacts drawn by the native SysTabControl32.
        /// </summary>
        private sealed class ThemedTabPainter : NativeWindow
        {
            public ThemeColors Theme { get; set; } = _current;

            protected override void WndProc(ref Message m)
            {
                const int WM_ERASEBKGND = 0x0014;
                const int WM_PAINT = 0x000F;

                if (m.Msg == WM_ERASEBKGND)
                {
                    // Fill the entire background with the theme color BEFORE
                    // the native control draws its borders.  This ensures no
                    // white pixel survives as a base layer.
                    try
                    {
                        using var g = Graphics.FromHdc(m.WParam);
                        using var brush = new SolidBrush(Theme.Background);
                        var tc = Control.FromHandle(Handle) as TabControl;
                        if (tc != null)
                            g.FillRectangle(brush, tc.ClientRectangle);
                    }
                    catch { /* ignore – fallback to normal erase */ }
                    m.Result = (IntPtr)1; // we handled it
                    return;
                }

                // Let the native control do its normal painting (DrawItem fires here)
                base.WndProc(ref m);

                if (m.Msg == WM_PAINT)
                {
                    // AFTER the native control + DrawItem have painted, paint over
                    // any remaining border artifacts that WM_ERASEBKGND couldn't prevent.
                    try
                    {
                        var tc = Control.FromHandle(Handle) as TabControl;
                        if (tc == null || tc.TabCount == 0) return;

                        using var g = Graphics.FromHwnd(Handle);
                        using var bgBrush = new SolidBrush(Theme.Background);

                        var firstTab = tc.GetTabRect(0);
                        var lastTab = tc.GetTabRect(tc.TabCount - 1);
                        int stripBottom = lastTab.Bottom;

                        // Above tab headers (top border line)
                        if (firstTab.Top > 0)
                            g.FillRectangle(bgBrush, 0, 0, tc.Width, firstTab.Top);

                        // Left of first tab
                        if (firstTab.Left > 0)
                            g.FillRectangle(bgBrush, 0, firstTab.Top, firstTab.Left, stripBottom - firstTab.Top);

                        // Right of last tab
                        if (lastTab.Right < tc.Width)
                            g.FillRectangle(bgBrush, lastTab.Right, 0, tc.Width - lastTab.Right, stripBottom);

                        // Gap between tab strip and page content + page borders
                        if (tc.SelectedTab != null)
                        {
                            var pr = tc.SelectedTab.Bounds;
                            // Below tab strip to page top
                            g.FillRectangle(bgBrush, 0, stripBottom, tc.Width, pr.Top - stripBottom);
                            // Left border
                            g.FillRectangle(bgBrush, 0, pr.Top - 2, pr.Left + 2, pr.Height + 4);
                            // Right border
                            g.FillRectangle(bgBrush, pr.Right - 1, pr.Top - 2, tc.Width - pr.Right + 2, pr.Height + 4);
                            // Bottom border
                            g.FillRectangle(bgBrush, 0, pr.Bottom - 1, tc.Width, tc.Height - pr.Bottom + 2);
                        }

                        // Subtle separator line below tabs
                        using var sepPen = new Pen(Theme.Border, 1f);
                        g.DrawLine(sepPen, 0, stripBottom, tc.Width, stripBottom);
                    }
                    catch { /* ignore paint errors */ }
                }
            }
        }

        /// <summary>
        /// Converts a TabControl to owner-draw mode so tab headers and the strip
        /// background render with theme colors instead of the default white.
        /// Safe to call multiple times — re-wires the DrawItem handler each time
        /// so it captures the latest theme colors.
        /// </summary>
        public static void ApplyOwnerDrawTabs(TabControl tc, ThemeColors? theme = null)
        {
            var t = theme ?? _current;
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.BackColor = t.Background;

            // Retrieve or create the theme state
            var state = tc.Tag as TabThemeState ?? new TabThemeState();

            // Remove previous event handlers
            if (state.DrawHandler != null) tc.DrawItem -= state.DrawHandler;
            if (state.PaintHandler != null) tc.Paint -= state.PaintHandler;

            // Attach or update the NativeWindow painter
            if (state.Painter == null)
            {
                state.Painter = new ThemedTabPainter { Theme = t };
                state.Painter.AssignHandle(tc.Handle);
            }
            else
            {
                state.Painter.Theme = t;
            }

            // Capture current theme in closure
            var capturedTheme = t;

            DrawItemEventHandler drawHandler = (sender, e) =>
            {
                if (sender is not TabControl tabs) return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var bounds = e.Bounds;
                bool isSelected = (e.Index == tabs.SelectedIndex);

                // Background fill
                Color bgColor = isSelected ? capturedTheme.Surface : capturedTheme.Background;
                using (var bgBrush = new SolidBrush(bgColor))
                    g.FillRectangle(bgBrush, bounds);

                // Subtle bottom accent line on selected tab
                if (isSelected)
                {
                    using var accentPen = new Pen(capturedTheme.Accent, 2.5f);
                    g.DrawLine(accentPen, bounds.Left + 4, bounds.Bottom - 1, bounds.Right - 4, bounds.Bottom - 1);
                }

                // Text
                string text = tabs.TabPages[e.Index].Text;
                Color textColor = isSelected ? capturedTheme.TextPrimary : capturedTheme.TextSecondary;
                var font = isSelected
                    ? new Font(tabs.Font, FontStyle.Bold)
                    : tabs.Font;

                using (var textBrush = new SolidBrush(textColor))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(text, font, textBrush, bounds, sf);
                }

                if (isSelected) font.Dispose();
            };

            // Keep a lightweight Paint handler only for the separator line
            PaintEventHandler paintHandler = (sender, e) => { /* handled by NativeWindow */ };

            tc.DrawItem += drawHandler;
            tc.Paint += paintHandler;

            state.DrawHandler = drawHandler;
            state.PaintHandler = paintHandler;
            tc.Tag = state;

            tc.Invalidate();
        }
    }
}
