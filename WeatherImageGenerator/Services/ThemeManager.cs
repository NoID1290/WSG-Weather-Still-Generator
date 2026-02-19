#nullable enable
using System;
using System.Drawing;
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
            Accent = ColorTranslator.FromHtml("#3498DB"),
            Success = ColorTranslator.FromHtml("#27AE60"),
            Warning = ColorTranslator.FromHtml("#F39C12"),
            Danger = ColorTranslator.FromHtml("#E74C3C"),
            Background = ColorTranslator.FromHtml("#233140"),
            Surface = ColorTranslator.FromHtml("#2C3E50"),
            CardBackground = ColorTranslator.FromHtml("#34495E"),
            InputBackground = ColorTranslator.FromHtml("#3D5266"),
            TextPrimary = ColorTranslator.FromHtml("#FFFFFF"),
            TextSecondary = ColorTranslator.FromHtml("#ECF0F1"),
            TextDim = ColorTranslator.FromHtml("#BDC3C7"),
            TextOnAccent = Color.White,
            TextOnButton = Color.White,
            TextOnWarning = Color.Black,
            ButtonBackground = ColorTranslator.FromHtml("#4E6781"),
            ButtonHover = ColorTranslator.FromHtml("#5A7A99"),
            Border = ColorTranslator.FromHtml("#4A6278"),
            Separator = ColorTranslator.FromHtml("#3D5266")
        };

        public static readonly ThemeColors Dark = new ThemeColors
        {
            Name = "Dark",
            IsDark = true,
            Primary = ColorTranslator.FromHtml("#121212"),
            Secondary = ColorTranslator.FromHtml("#1E1E1E"),
            Accent = ColorTranslator.FromHtml("#3498DB"),
            Success = ColorTranslator.FromHtml("#27AE60"),
            Warning = ColorTranslator.FromHtml("#F39C12"),
            Danger = ColorTranslator.FromHtml("#E74C3C"),
            Background = ColorTranslator.FromHtml("#1E1E1E"),
            Surface = ColorTranslator.FromHtml("#121212"),
            CardBackground = ColorTranslator.FromHtml("#1E1E1E"),
            InputBackground = ColorTranslator.FromHtml("#2A2A2A"),
            TextPrimary = Color.White,
            TextSecondary = ColorTranslator.FromHtml("#E0E0E0"),
            TextDim = ColorTranslator.FromHtml("#B0B0B0"),
            TextOnAccent = Color.White,
            TextOnButton = Color.White,
            TextOnWarning = Color.Black,
            ButtonBackground = ColorTranslator.FromHtml("#333333"),
            ButtonHover = ColorTranslator.FromHtml("#444444"),
            Border = ColorTranslator.FromHtml("#3A3A3A"),
            Separator = ColorTranslator.FromHtml("#333333")
        };

        public static readonly ThemeColors Light = new ThemeColors
        {
            Name = "Light",
            IsDark = false,
            Primary = ColorTranslator.FromHtml("#F8F9FA"),
            Secondary = Color.White,
            Accent = ColorTranslator.FromHtml("#0D6EFD"),
            Success = ColorTranslator.FromHtml("#198754"),
            Warning = ColorTranslator.FromHtml("#FFC107"),
            Danger = ColorTranslator.FromHtml("#DC3545"),
            Background = Color.White,
            Surface = ColorTranslator.FromHtml("#F8F9FA"),
            CardBackground = Color.White,
            InputBackground = Color.White,
            TextPrimary = ColorTranslator.FromHtml("#212529"),
            TextSecondary = ColorTranslator.FromHtml("#495057"),
            TextDim = ColorTranslator.FromHtml("#6C757D"),
            TextOnAccent = Color.White,
            TextOnButton = Color.Black,
            TextOnWarning = Color.Black,
            ButtonBackground = ColorTranslator.FromHtml("#DEE2E6"),
            ButtonHover = ColorTranslator.FromHtml("#C8CDD2"),
            Border = ColorTranslator.FromHtml("#DEE2E6"),
            Separator = ColorTranslator.FromHtml("#DEE2E6")
        };

        public static readonly ThemeColors Green = new ThemeColors
        {
            Name = "Green",
            IsDark = true,
            Primary = ColorTranslator.FromHtml("#102018"),
            Secondary = ColorTranslator.FromHtml("#1B3A28"),
            Accent = ColorTranslator.FromHtml("#2E8B57"),
            Success = ColorTranslator.FromHtml("#27AE60"),
            Warning = ColorTranslator.FromHtml("#F39C12"),
            Danger = ColorTranslator.FromHtml("#E74C3C"),
            Background = ColorTranslator.FromHtml("#0A1510"),
            Surface = ColorTranslator.FromHtml("#102018"),
            CardBackground = ColorTranslator.FromHtml("#1B3A28"),
            InputBackground = ColorTranslator.FromHtml("#264D35"),
            TextPrimary = Color.White,
            TextSecondary = ColorTranslator.FromHtml("#E8F8F1"),
            TextDim = ColorTranslator.FromHtml("#A0C0B0"),
            TextOnAccent = Color.White,
            TextOnButton = Color.White,
            TextOnWarning = Color.Black,
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

        /// <summary>
        /// Applies the current theme's base colors to a form and all its child controls recursively.
        /// For controls that need special treatment (e.g., buttons with specific semantic colors),
        /// the form should handle those individually after calling this method.
        /// </summary>
        public static void ApplyTo(Control control, ThemeColors? theme = null)
        {
            var t = theme ?? _current;

            control.BackColor = t.Surface;
            control.ForeColor = t.TextPrimary;

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
                    break;

                case CheckBox chk:
                    chk.ForeColor = t.TextSecondary;
                    break;

                case NumericUpDown nud:
                    nud.BackColor = t.InputBackground;
                    nud.ForeColor = t.TextPrimary;
                    break;

                case GroupBox gb:
                    gb.ForeColor = t.Accent;
                    foreach (Control c in gb.Controls)
                        ApplyToControl(c, t);
                    break;

                case TabControl tc:
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
                    // Don't override labels with explicit color assignments (e.g., accent-colored headers)
                    if (lbl.ForeColor == SystemColors.ControlText || lbl.ForeColor == Color.Black)
                        lbl.ForeColor = t.TextPrimary;
                    break;

                case ListView lv:
                    lv.BackColor = t.CardBackground;
                    lv.ForeColor = t.TextPrimary;
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
    }
}
