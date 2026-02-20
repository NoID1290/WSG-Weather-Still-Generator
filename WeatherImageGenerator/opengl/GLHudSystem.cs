using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WeatherImageGenerator.OpenGL
{
    /// <summary>
    /// GPU-rendered HUD overlay system for the weather map viewport.
    /// All controls (buttons, checkboxes, dropdowns, sliders) are rendered via GLTextRenderer
    /// as semi-transparent floating panels directly in the OpenGL viewport.
    /// </summary>
    public class GLHudSystem
    {
        // ═══ Color constants ═══
        private static readonly HudColor PanelBg = new(15, 15, 20, 0.75f);
        private static readonly HudColor PanelBgLight = new(25, 25, 35, 0.80f);
        private static readonly HudColor ButtonBg = new(45, 45, 60, 0.85f);
        private static readonly HudColor ButtonHover = new(65, 65, 90, 0.90f);
        private static readonly HudColor ButtonActive = new(40, 120, 255, 0.85f);
        private static readonly HudColor AccentColor = new(40, 120, 255, 1.0f);
        private static readonly HudColor DangerColor = new(200, 60, 50, 1.0f);
        private static readonly HudColor TextPrimary = new(240, 240, 240, 0.95f);
        private static readonly HudColor TextSecondary = new(170, 170, 180, 0.75f);
        private static readonly HudColor TextDim = new(120, 120, 130, 0.55f);
        private static readonly HudColor SliderTrack = new(80, 80, 100, 0.6f);
        private static readonly HudColor SliderThumb = new(40, 120, 255, 1.0f);
        private static readonly HudColor SeparatorColor = new(80, 80, 100, 0.35f);
        private static readonly HudColor CheckActive = new(50, 200, 120, 1.0f);

        private readonly List<HudPanel> _panels = new();
        private HudElement? _hoveredElement = null;
        private HudElement? _pressedElement = null;
        private HudDropdown? _openDropdown = null;
        private HudSlider? _draggingSlider = null;
        private bool _isDraggingPanel = false;
        private HudPanel? _draggedPanel = null;
        private float _dragOffsetX, _dragOffsetY;

        // Layout constants
        private const float Padding = 10f;
        private const float ItemSpacing = 4f;
        private const float SectionSpacing = 8f;
        private const float ButtonHeight = 26f;
        private const float CheckboxHeight = 22f;
        private const float SliderHeight = 20f;
        private const float DropdownHeight = 26f;
        private const float PanelTitleHeight = 28f;
        private const float PanelCornerRadius = 6f;

        public GLHudSystem() { }

        public void AddPanel(HudPanel panel) => _panels.Add(panel);
        public HudPanel? GetPanel(string id) => _panels.FirstOrDefault(p => p.Id == id);
        public IReadOnlyList<HudPanel> Panels => _panels;

        /// <summary>
        /// Render all HUD panels and their child elements.
        /// Call within the GL paint loop after map rendering, with blend enabled.
        /// </summary>
        public void Render(GLTextRenderer renderer, int viewportW, int viewportH)
        {
            if (renderer == null || !renderer.IsInitialized) return;

            // First pass: layout panels that use anchored positioning
            foreach (var panel in _panels)
            {
                if (!panel.Visible) continue;
                LayoutPanel(panel, viewportW, viewportH);
            }

            // Second pass: render panels
            foreach (var panel in _panels)
            {
                if (!panel.Visible) continue;
                RenderPanel(renderer, panel, viewportW, viewportH);
            }

            // Render open dropdown overlay on top of everything
            if (_openDropdown != null && _openDropdown.IsOpen)
            {
                RenderDropdownOverlay(renderer, _openDropdown, viewportW, viewportH);
            }
        }

        private void LayoutPanel(HudPanel panel, int vw, int vh)
        {
            float w = panel.Width;
            float h = CalculatePanelHeight(panel);
            panel.ComputedHeight = h;

            // Apply anchor positioning
            switch (panel.Anchor)
            {
                case HudAnchor.TopLeft:
                    panel.X = panel.MarginX;
                    panel.Y = panel.MarginY;
                    break;
                case HudAnchor.TopRight:
                    panel.X = vw - w - panel.MarginX;
                    panel.Y = panel.MarginY;
                    break;
                case HudAnchor.TopCenter:
                    panel.X = (vw - w) / 2f;
                    panel.Y = panel.MarginY;
                    break;
                case HudAnchor.BottomLeft:
                    panel.X = panel.MarginX;
                    panel.Y = vh - h - panel.MarginY;
                    break;
                case HudAnchor.BottomRight:
                    panel.X = vw - w - panel.MarginX;
                    panel.Y = vh - h - panel.MarginY;
                    break;
                case HudAnchor.BottomCenter:
                    panel.X = (vw - w) / 2f;
                    panel.Y = vh - h - panel.MarginY;
                    break;
                case HudAnchor.RightCenter:
                    panel.X = vw - w - panel.MarginX;
                    panel.Y = (vh - h) / 2f;
                    break;
                case HudAnchor.Manual:
                    break; // Use panel.X, panel.Y as-is
            }
        }

        private float CalculatePanelHeight(HudPanel panel)
        {
            if (panel.Collapsed)
                return PanelTitleHeight + Padding;

            float y = PanelTitleHeight + ItemSpacing;
            foreach (var el in panel.Elements)
            {
                if (!el.Visible) continue;
                y += GetElementHeight(el) + ItemSpacing;
            }
            return y + Padding;
        }

        private float GetElementHeight(HudElement el) => el switch
        {
            HudButton => ButtonHeight,
            HudCheckbox => CheckboxHeight,
            HudSlider s => SliderHeight + (s.ShowLabel ? 14f : 0f),
            HudDropdown => DropdownHeight,
            HudLabel l => l.IsSection ? 22f : 16f,
            HudSeparator => 6f,
            HudButtonGroup g => ButtonHeight,
            _ => 20f
        };

        private void RenderPanel(GLTextRenderer r, HudPanel panel, int vw, int vh)
        {
            float px = panel.X, py = panel.Y;
            float pw = panel.Width, ph = panel.ComputedHeight;

            // Panel background
            r.DrawRect(px, py, pw, ph, PanelBg.R, PanelBg.G, PanelBg.B, PanelBg.A);

            // Border highlight (top edge)
            r.DrawRect(px, py, pw, 1f, AccentColor.R, AccentColor.G, AccentColor.B, 0.4f);

            // Title bar
            float tx = px + Padding;
            float ty = py + 6f;

            // Collapse indicator
            string collapseIcon = panel.Collapsed ? "▶" : "▼";
            r.DrawText(collapseIcon, tx, ty, TextDim.R, TextDim.G, TextDim.B, TextDim.A);
            tx += 16f;

            // Panel title
            r.DrawText(panel.Title, tx, ty, TextPrimary.R, TextPrimary.G, TextPrimary.B, TextPrimary.A);

            if (panel.Collapsed) return;

            // Render child elements
            float ey = py + PanelTitleHeight + ItemSpacing;
            float contentX = px + Padding;
            float contentW = pw - Padding * 2;

            foreach (var el in panel.Elements)
            {
                if (!el.Visible) continue;

                float elH = GetElementHeight(el);
                el.ComputedBounds = new RectangleF(contentX, ey, contentW, elH);

                bool isHovered = el == _hoveredElement;
                bool isPressed = el == _pressedElement;

                switch (el)
                {
                    case HudButton btn:
                        RenderButton(r, btn, contentX, ey, contentW, elH, isHovered, isPressed);
                        break;
                    case HudCheckbox chk:
                        RenderCheckbox(r, chk, contentX, ey, contentW, elH, isHovered);
                        break;
                    case HudSlider sld:
                        RenderSlider(r, sld, contentX, ey, contentW, elH, isHovered);
                        break;
                    case HudDropdown dd:
                        RenderDropdown(r, dd, contentX, ey, contentW, elH, isHovered);
                        break;
                    case HudLabel lbl:
                        RenderLabel(r, lbl, contentX, ey, contentW, elH);
                        break;
                    case HudSeparator:
                        r.DrawRect(contentX, ey + 2f, contentW, 1f, SeparatorColor.R, SeparatorColor.G, SeparatorColor.B, SeparatorColor.A);
                        break;
                    case HudButtonGroup grp:
                        RenderButtonGroup(r, grp, contentX, ey, contentW, elH, isHovered);
                        break;
                }

                ey += elH + ItemSpacing;
            }
        }

        private void RenderButton(GLTextRenderer r, HudButton btn, float x, float y, float w, float h, bool hover, bool pressed)
        {
            var bg = pressed ? AccentColor : hover ? ButtonHover : btn.IsAccent ? ButtonActive : ButtonBg;
            r.DrawRect(x, y, w, h, bg.R, bg.G, bg.B, bg.A);
            float tw = r.MeasureTextWidth(btn.Text);
            float tx = x + (w - tw) / 2f;
            float ty2 = y + (h - r.LineHeight) / 2f;
            var fg = (btn.IsAccent || pressed) ? TextPrimary : hover ? TextPrimary : TextSecondary;
            r.DrawText(btn.Text, tx, ty2, fg.R, fg.G, fg.B, fg.A);
        }

        private void RenderCheckbox(GLTextRenderer r, HudCheckbox chk, float x, float y, float w, float h, bool hover)
        {
            // Checkbox box
            float boxSize = 14f;
            float boxX = x;
            float boxY = y + (h - boxSize) / 2f;
            var boxBg = hover ? ButtonHover : ButtonBg;
            r.DrawRect(boxX, boxY, boxSize, boxSize, boxBg.R, boxBg.G, boxBg.B, boxBg.A);

            if (chk.Checked)
            {
                // Fill with accent color
                r.DrawRect(boxX + 2f, boxY + 2f, boxSize - 4f, boxSize - 4f,
                    CheckActive.R, CheckActive.G, CheckActive.B, CheckActive.A);
            }

            // Label text
            float tx = boxX + boxSize + 6f;
            float ty = y + (h - r.LineHeight) / 2f;
            var fg = chk.Checked ? TextPrimary : TextSecondary;
            r.DrawText(chk.Text, tx, ty, fg.R, fg.G, fg.B, fg.A);
        }

        private void RenderSlider(GLTextRenderer r, HudSlider sld, float x, float y, float w, float h, bool hover)
        {
            float labelY = y;
            float sliderY = y;

            if (sld.ShowLabel)
            {
                // Label + value
                string label = $"{sld.Text}: {sld.Value:F0}%";
                r.DrawText(label, x, labelY, TextSecondary.R, TextSecondary.G, TextSecondary.B, TextSecondary.A);
                sliderY = y + 14f;
            }

            float trackH = 6f;
            float trackY = sliderY + (SliderHeight - trackH) / 2f;

            // Track background
            r.DrawRect(x, trackY, w, trackH, SliderTrack.R, SliderTrack.G, SliderTrack.B, SliderTrack.A);

            // Filled portion
            float fillW = w * ((sld.Value - sld.Min) / (sld.Max - sld.Min));
            r.DrawRect(x, trackY, fillW, trackH, AccentColor.R, AccentColor.G, AccentColor.B, 0.7f);

            // Thumb
            float thumbW = 12f, thumbH = 14f;
            float thumbX = x + fillW - thumbW / 2f;
            float thumbY = trackY - (thumbH - trackH) / 2f;
            var thumbColor = (hover || sld == _draggingSlider) ? TextPrimary : SliderThumb;
            r.DrawRect(thumbX, thumbY, thumbW, thumbH, thumbColor.R, thumbColor.G, thumbColor.B, thumbColor.A);

            // Store track bounds for hit-testing
            sld.TrackBounds = new RectangleF(x, trackY - 4f, w, trackH + 8f);
        }

        private void RenderDropdown(GLTextRenderer r, HudDropdown dd, float x, float y, float w, float h, bool hover)
        {
            var bg = hover ? ButtonHover : ButtonBg;
            r.DrawRect(x, y, w, h, bg.R, bg.G, bg.B, bg.A);

            // Current selection text
            string text = dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Options.Count
                ? dd.Options[dd.SelectedIndex] : dd.Text;
            float tx = x + 8f;
            float ty = y + (h - r.LineHeight) / 2f;
            r.DrawText(text, tx, ty, TextPrimary.R, TextPrimary.G, TextPrimary.B, TextPrimary.A);

            // Dropdown arrow
            string arrow = dd.IsOpen ? "▲" : "▼";
            float aw = r.MeasureTextWidth(arrow);
            r.DrawText(arrow, x + w - aw - 8f, ty, TextDim.R, TextDim.G, TextDim.B, TextDim.A);
        }

        private void RenderDropdownOverlay(GLTextRenderer r, HudDropdown dd, int vw, int vh)
        {
            if (dd.ComputedBounds == RectangleF.Empty) return;

            float x = dd.ComputedBounds.X;
            float y = dd.ComputedBounds.Y + dd.ComputedBounds.Height + 2f;
            float w = dd.ComputedBounds.Width;
            float itemH = DropdownHeight;
            float totalH = dd.Options.Count * itemH;

            // Ensure dropdown stays within viewport
            if (y + totalH > vh) y = dd.ComputedBounds.Y - totalH - 2f;

            // Background
            r.DrawRect(x, y, w, totalH, PanelBgLight.R, PanelBgLight.G, PanelBgLight.B, PanelBgLight.A);
            // Border
            r.DrawRect(x, y, w, 1f, AccentColor.R, AccentColor.G, AccentColor.B, 0.5f);

            dd.OptionBounds.Clear();
            for (int i = 0; i < dd.Options.Count; i++)
            {
                float iy = y + i * itemH;
                var optBounds = new RectangleF(x, iy, w, itemH);
                dd.OptionBounds.Add(optBounds);

                bool isHoverOpt = optBounds.Contains(_hoveredElement?.ComputedBounds.Location ?? PointF.Empty)
                    || (dd._hoveredOptionIndex == i);
                bool isSelected = i == dd.SelectedIndex;

                if (isHoverOpt)
                    r.DrawRect(x, iy, w, itemH, ButtonHover.R, ButtonHover.G, ButtonHover.B, ButtonHover.A);
                else if (isSelected)
                    r.DrawRect(x, iy, w, itemH, AccentColor.R, AccentColor.G, AccentColor.B, 0.3f);

                float tx = x + 8f;
                float ty = iy + (itemH - r.LineHeight) / 2f;
                var fg = isSelected ? AccentColor : TextPrimary;
                r.DrawText(dd.Options[i], tx, ty, fg.R, fg.G, fg.B, fg.A);
            }
        }

        private void RenderLabel(GLTextRenderer r, HudLabel lbl, float x, float y, float w, float h)
        {
            var color = lbl.IsSection ? AccentColor : lbl.IsDim ? TextDim : TextSecondary;
            float ty = y + (h - r.LineHeight) / 2f;

            if (lbl.IsSection)
            {
                // Section header with icon
                r.DrawText(lbl.Text, x, ty, color.R, color.G, color.B, color.A);
            }
            else
            {
                r.DrawText(lbl.Text, x, ty, color.R, color.G, color.B, color.A);
            }
        }

        private void RenderButtonGroup(GLTextRenderer r, HudButtonGroup grp, float x, float y, float w, float h, bool hover)
        {
            if (grp.Options.Count == 0) return;
            float btnW = (w - (grp.Options.Count - 1) * 2f) / grp.Options.Count;

            for (int i = 0; i < grp.Options.Count; i++)
            {
                float bx = x + i * (btnW + 2f);
                bool isSelected = i == grp.SelectedIndex;
                bool isHover = hover && grp._hoveredIndex == i;

                var bg = isSelected ? AccentColor : isHover ? ButtonHover : ButtonBg;
                r.DrawRect(bx, y, btnW, h, bg.R, bg.G, bg.B, bg.A);

                float tw = r.MeasureTextWidth(grp.Options[i]);
                float tx = bx + (btnW - tw) / 2f;
                float ty2 = y + (h - r.LineHeight) / 2f;
                var fg = isSelected ? TextPrimary : TextSecondary;
                r.DrawText(grp.Options[i], tx, ty2, fg.R, fg.G, fg.B, fg.A);
            }

            // Store individual button bounds for hit testing
            grp.ButtonBounds.Clear();
            for (int i = 0; i < grp.Options.Count; i++)
            {
                float bx = x + i * (btnW + 2f);
                grp.ButtonBounds.Add(new RectangleF(bx, y, btnW, h));
            }
        }

        // ═══ Mouse event processing ═══

        /// <summary>
        /// Process a mouse click. Returns true if the HUD consumed the event (suppress map interaction).
        /// </summary>
        public bool ProcessMouseDown(float mx, float my)
        {
            // Check if clicking on open dropdown overlay first
            if (_openDropdown != null && _openDropdown.IsOpen)
            {
                for (int i = 0; i < _openDropdown.OptionBounds.Count; i++)
                {
                    if (_openDropdown.OptionBounds[i].Contains(mx, my))
                    {
                        _openDropdown.SelectedIndex = i;
                        _openDropdown.OnSelectionChanged?.Invoke(i);
                        _openDropdown.IsOpen = false;
                        _openDropdown = null;
                        return true;
                    }
                }
                // Clicked outside dropdown → close it
                _openDropdown.IsOpen = false;
                _openDropdown = null;
                // Don't consume — let it fall through to panel check
            }

            // Check panels (reverse order = topmost first)
            for (int pi = _panels.Count - 1; pi >= 0; pi--)
            {
                var panel = _panels[pi];
                if (!panel.Visible) continue;

                var panelBounds = new RectangleF(panel.X, panel.Y, panel.Width, panel.ComputedHeight);
                if (!panelBounds.Contains(mx, my)) continue;

                // Title bar click → toggle collapse
                if (my < panel.Y + PanelTitleHeight)
                {
                    if (panel.Collapsible)
                        panel.Collapsed = !panel.Collapsed;
                    return true;
                }

                if (panel.Collapsed) return true;

                // Check child elements
                foreach (var el in panel.Elements)
                {
                    if (!el.Visible) continue;
                    if (!el.ComputedBounds.Contains(mx, my)) continue;

                    _pressedElement = el;

                    switch (el)
                    {
                        case HudButton btn:
                            btn.OnClick?.Invoke();
                            return true;

                        case HudCheckbox chk:
                            chk.Checked = !chk.Checked;
                            chk.OnChanged?.Invoke(chk.Checked);
                            return true;

                        case HudDropdown dd:
                            if (dd.IsOpen)
                            {
                                dd.IsOpen = false;
                                _openDropdown = null;
                            }
                            else
                            {
                                dd.IsOpen = true;
                                _openDropdown = dd;
                            }
                            return true;

                        case HudSlider sld:
                            _draggingSlider = sld;
                            UpdateSliderValue(sld, mx);
                            return true;

                        case HudButtonGroup grp:
                            for (int i = 0; i < grp.ButtonBounds.Count; i++)
                            {
                                if (grp.ButtonBounds[i].Contains(mx, my))
                                {
                                    grp.SelectedIndex = i;
                                    grp.OnSelectionChanged?.Invoke(i);
                                    return true;
                                }
                            }
                            return true;
                    }

                    return true;
                }

                // Clicked inside panel but not on any element — still consume
                return true;
            }

            return false;
        }

        /// <summary>
        /// Process mouse move. Returns true if cursor is over HUD (change cursor hint).
        /// </summary>
        public bool ProcessMouseMove(float mx, float my)
        {
            // Handle slider dragging
            if (_draggingSlider != null)
            {
                UpdateSliderValue(_draggingSlider, mx);
                return true;
            }

            _hoveredElement = null;

            // Check open dropdown options
            if (_openDropdown != null && _openDropdown.IsOpen)
            {
                _openDropdown._hoveredOptionIndex = -1;
                for (int i = 0; i < _openDropdown.OptionBounds.Count; i++)
                {
                    if (_openDropdown.OptionBounds[i].Contains(mx, my))
                    {
                        _openDropdown._hoveredOptionIndex = i;
                        return true;
                    }
                }
            }

            // Check panels
            for (int pi = _panels.Count - 1; pi >= 0; pi--)
            {
                var panel = _panels[pi];
                if (!panel.Visible) continue;

                var panelBounds = new RectangleF(panel.X, panel.Y, panel.Width, panel.ComputedHeight);
                if (!panelBounds.Contains(mx, my)) continue;

                if (panel.Collapsed) return true; // Over collapsed panel title

                foreach (var el in panel.Elements)
                {
                    if (!el.Visible) continue;
                    if (el.ComputedBounds.Contains(mx, my))
                    {
                        _hoveredElement = el;

                        // Track hovered button in group
                        if (el is HudButtonGroup grp)
                        {
                            grp._hoveredIndex = -1;
                            for (int i = 0; i < grp.ButtonBounds.Count; i++)
                            {
                                if (grp.ButtonBounds[i].Contains(mx, my))
                                {
                                    grp._hoveredIndex = i;
                                    break;
                                }
                            }
                        }

                        return true;
                    }
                }

                return true; // Over panel body
            }

            return false;
        }

        /// <summary>
        /// Process mouse up. Returns true if HUD consumed it.
        /// </summary>
        public bool ProcessMouseUp(float mx, float my)
        {
            bool consumed = false;
            if (_draggingSlider != null)
            {
                _draggingSlider = null;
                consumed = true;
            }
            if (_pressedElement != null)
            {
                _pressedElement = null;
                consumed = true;
            }
            if (_isDraggingPanel)
            {
                _isDraggingPanel = false;
                _draggedPanel = null;
                consumed = true;
            }
            return consumed;
        }

        /// <summary>
        /// Process mouse wheel. Returns true if HUD consumed it (e.g., slider adjustment).
        /// </summary>
        public bool ProcessMouseWheel(float mx, float my, int delta)
        {
            // Check if mouse is over any panel
            for (int pi = _panels.Count - 1; pi >= 0; pi--)
            {
                var panel = _panels[pi];
                if (!panel.Visible) continue;

                var panelBounds = new RectangleF(panel.X, panel.Y, panel.Width, panel.ComputedHeight);
                if (!panelBounds.Contains(mx, my)) continue;

                // If over a slider, adjust its value
                foreach (var el in panel.Elements)
                {
                    if (!el.Visible || !(el is HudSlider sld)) continue;
                    if (el.ComputedBounds.Contains(mx, my))
                    {
                        float step = (sld.Max - sld.Min) * 0.05f;
                        sld.Value = Math.Clamp(sld.Value + (delta > 0 ? step : -step), sld.Min, sld.Max);
                        sld.OnChanged?.Invoke(sld.Value);
                        return true;
                    }
                }

                // Consume wheel if over panel to prevent map zoom
                return true;
            }

            return false;
        }

        private void UpdateSliderValue(HudSlider sld, float mx)
        {
            if (sld.TrackBounds == RectangleF.Empty) return;
            float relative = (mx - sld.TrackBounds.X) / sld.TrackBounds.Width;
            relative = Math.Clamp(relative, 0f, 1f);
            float newVal = sld.Min + relative * (sld.Max - sld.Min);
            // Snap to integer
            newVal = (float)Math.Round(newVal);
            if (Math.Abs(newVal - sld.Value) > 0.01f)
            {
                sld.Value = newVal;
                sld.OnChanged?.Invoke(sld.Value);
            }
        }

        /// <summary>Check if the point is over any HUD panel (for cursor changes)</summary>
        public bool IsOverHud(float mx, float my)
        {
            // Check dropdown overlay first
            if (_openDropdown != null && _openDropdown.IsOpen)
            {
                foreach (var optBounds in _openDropdown.OptionBounds)
                {
                    if (optBounds.Contains(mx, my)) return true;
                }
            }

            foreach (var panel in _panels)
            {
                if (!panel.Visible) continue;
                var bounds = new RectangleF(panel.X, panel.Y, panel.Width, panel.ComputedHeight);
                if (bounds.Contains(mx, my)) return true;
            }
            return false;
        }
    }

    // ═══ HUD Element Types ═══

    public struct HudColor
    {
        public float R, G, B, A;
        public HudColor(int r, int g, int b, float a)
        {
            R = r / 255f; G = g / 255f; B = b / 255f; A = a;
        }
        public HudColor(float r, float g, float b, float a)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    public enum HudAnchor
    {
        TopLeft, TopRight, TopCenter,
        BottomLeft, BottomRight, BottomCenter,
        RightCenter,
        Manual
    }

    public class HudPanel
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 220f;
        public float ComputedHeight { get; set; }
        public float MarginX { get; set; } = 10f;
        public float MarginY { get; set; } = 10f;
        public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;
        public bool Visible { get; set; } = true;
        public bool Collapsed { get; set; } = false;
        public bool Collapsible { get; set; } = true;
        public List<HudElement> Elements { get; } = new();
    }

    public abstract class HudElement
    {
        public string Id { get; set; } = "";
        public bool Visible { get; set; } = true;
        public RectangleF ComputedBounds { get; set; }
    }

    public class HudButton : HudElement
    {
        public string Text { get; set; } = "";
        public bool IsAccent { get; set; } = false;
        public Action? OnClick { get; set; }
    }

    public class HudCheckbox : HudElement
    {
        public string Text { get; set; } = "";
        public bool Checked { get; set; }
        public Action<bool>? OnChanged { get; set; }
    }

    public class HudSlider : HudElement
    {
        public string Text { get; set; } = "";
        public float Value { get; set; } = 50f;
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 100f;
        public bool ShowLabel { get; set; } = true;
        public RectangleF TrackBounds { get; set; }
        public Action<float>? OnChanged { get; set; }
    }

    public class HudDropdown : HudElement
    {
        public string Text { get; set; } = "";
        public List<string> Options { get; set; } = new();
        public int SelectedIndex { get; set; } = 0;
        public bool IsOpen { get; set; }
        public List<RectangleF> OptionBounds { get; } = new();
        internal int _hoveredOptionIndex = -1;
        public Action<int>? OnSelectionChanged { get; set; }
    }

    public class HudLabel : HudElement
    {
        public string Text { get; set; } = "";
        public bool IsSection { get; set; } = false;
        public bool IsDim { get; set; } = false;
        /// <summary>Allows updating label text dynamically (e.g., for status display)</summary>
        public Action<HudLabel>? OnUpdate { get; set; }
    }

    public class HudSeparator : HudElement { }

    public class HudButtonGroup : HudElement
    {
        public List<string> Options { get; set; } = new();
        public int SelectedIndex { get; set; } = 0;
        public List<RectangleF> ButtonBounds { get; } = new();
        internal int _hoveredIndex = -1;
        public Action<int>? OnSelectionChanged { get; set; }
    }
}
