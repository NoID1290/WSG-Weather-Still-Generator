﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// GPU-rendered HUD overlay system for the weather map viewport.
    /// All controls (buttons, checkboxes, dropdowns, sliders) are rendered via IHudRenderer
    /// as semi-transparent floating panels directly in the GPU viewport.
    /// </summary>
    public class HudSystem
    {
        // â•â•â• Color constants â•â•â•
        private static readonly HudColor PanelBg = new(15, 15, 20, 0.75f);
        private static readonly HudColor PanelBgLight = new(25, 25, 35, 0.80f);
        private static readonly HudColor ButtonBg = new(45, 45, 60, 0.85f);
        private static readonly HudColor ButtonHover = new(65, 65, 90, 0.90f);
        private static readonly HudColor ButtonActive = new(40, 120, 255, 0.85f);
        private static readonly HudColor AccentColor = new(40, 120, 255, 1.0f);
        private static readonly HudColor DangerColor = new(200, 60, 50, 1.0f);
        private static readonly HudColor TextPrimary = new(240, 240, 240, 0.95f);
        private static readonly HudColor TextSecondary = new(170, 170, 180, 0.75f);
        private static readonly HudColor TextDim = new(165, 165, 175, 0.80f);
        private static readonly HudColor SliderTrack = new(80, 80, 100, 0.6f);
        private static readonly HudColor SliderThumb = new(40, 120, 255, 1.0f);
        private static readonly HudColor SeparatorColor = new(80, 80, 100, 0.35f);
        private static readonly HudColor CheckActive = new(50, 200, 120, 1.0f);

        private readonly List<HudPanel> _panels = new();
        private IHudRenderer? _currentRenderer = null;
        private float _currentPanelContentWidth = 400f;
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
        private const float PanelTitleHeight = 22f;
        private const float InlineItemSpacing = 4f;
        private const float InlineButtonSize = 28f;

        // UI opacity multipliers
        public float PanelOpacityMultiplier { get; set; } = 1.0f;
        public float AnimationPanelOpacity { get; set; } = 1.0f;
        private const float PanelCornerRadius = 6f;

        /// <summary>
        /// Set to a non-null string to show a centered loading overlay on the viewport.
        /// Set to null to hide it.
        /// </summary>
        public string? LoadingMessage { get; set; }

        public HudSystem() { }

        public void AddPanel(HudPanel panel) => _panels.Add(panel);
        public HudPanel? GetPanel(string id) => _panels.FirstOrDefault(p => p.Id == id);
        public IReadOnlyList<HudPanel> Panels => _panels;

        /// <summary>
        /// Render all HUD panels and their child elements.
        /// Call within the GL paint loop after map rendering, with blend enabled.
        /// </summary>
        public void Render(IHudRenderer renderer, int viewportW, int viewportH)
        {
            if (renderer == null || !renderer.IsInitialized) return;

            _currentRenderer = renderer;

            // First pass: layout panels that use anchored positioning
            // Compute heights first, then auto-stack panels sharing the same anchor
            foreach (var panel in _panels)
            {
                if (!panel.Visible) continue;
                _currentPanelContentWidth = panel.Width - Padding * 2;
                panel.ComputedHeight = CalculatePanelHeight(panel);
            }

            // Auto-stack: accumulate Y offset for panels sharing the same anchor
            var anchorOffsets = new Dictionary<HudAnchor, float>();
            // Horizontal layout groups: accumulate X offset for panels sharing the same LayoutGroup
            var groupOffsets = new Dictionary<string, float>();
            var groupStartY = new Dictionary<string, float>();
            foreach (var panel in _panels)
            {
                if (!panel.Visible) continue;

                float w = panel.Width;
                float h = panel.ComputedHeight;
                var anchor = panel.Anchor;

                // Horizontal layout group takes priority
                if (!string.IsNullOrEmpty(panel.LayoutGroup))
                {
                    string grp = panel.LayoutGroup;
                    if (!groupOffsets.ContainsKey(grp))
                    {
                        groupOffsets[grp] = panel.MarginX;
                        groupStartY[grp] = panel.MarginY;
                    }

                    float useWidth = panel.Collapsed ? (panel.CompactWidth > 0 ? panel.CompactWidth : w) : w;
                    panel.X = groupOffsets[grp];
                    panel.Y = groupStartY[grp];
                    groupOffsets[grp] = panel.X + useWidth + 6f; // 6px gap between horizontal panels
                }
                // For stackable anchors (TopLeft, TopRight), use accumulated offset
                else if (anchor == HudAnchor.TopLeft || anchor == HudAnchor.TopRight)
                {
                    if (!anchorOffsets.ContainsKey(anchor))
                        anchorOffsets[anchor] = panel.MarginY;

                    float y = anchorOffsets[anchor];
                    panel.Y = y;

                    if (anchor == HudAnchor.TopLeft)
                        panel.X = panel.MarginX;
                    else
                        panel.X = viewportW - w - panel.MarginX;

                    anchorOffsets[anchor] = y + h + 6f; // 6px gap between stacked panels
                }
                else
                {
                    LayoutPanel(panel, viewportW, viewportH);
                }
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

            // Render loading overlay on top of everything
            if (!string.IsNullOrEmpty(LoadingMessage))
            {
                RenderLoadingOverlay(renderer, viewportW, viewportH, LoadingMessage);
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
            float topPad = panel.TitleVisible ? PanelTitleHeight : 6f;
            if (panel.Collapsed)
                return topPad + (panel.TitleVisible ? 4f : 0f);

            float y = topPad + ItemSpacing;
            foreach (var el in panel.Elements)
            {
                if (!el.Visible) continue;
                y += GetElementHeight(el) + ItemSpacing;
            }
            return y + (panel.TitleVisible ? Padding : 4f);
        }

        private float GetElementHeight(HudElement el) => el switch
        {
            HudInlineRow row => InlineButtonSize,
            HudButton => ButtonHeight,
            HudCheckbox => CheckboxHeight,
            HudSlider s => SliderHeight + (s.ShowLabel ? 14f : 0f) + (s.ShowTicks ? 18f : 0f),
            HudDropdown => DropdownHeight,
            HudLabel l => l.IsSection ? 22f : 16f * Math.Max(1, CountWrappedLines(l.Text, _currentPanelContentWidth)),
            HudSeparator => 6f,
            HudProgressLine => 6f,
            HudButtonGroup g => ButtonHeight,
            _ => 20f
        };

        private void RenderPanel(IHudRenderer r, HudPanel panel, int vw, int vh)
        {
            float px = panel.X, py = panel.Y;
            // For collapsed panels with LayoutGroup, use CompactWidth if available
            float pw = (!string.IsNullOrEmpty(panel.LayoutGroup) && panel.Collapsed && panel.CompactWidth > 0)
                ? panel.CompactWidth : panel.Width;
            float ph = panel.ComputedHeight;

            // Panel background with opacity multiplier
            float panelAlpha = PanelBg.A * (panel.Id == "animation" ? AnimationPanelOpacity : PanelOpacityMultiplier);
            r.DrawRect(px, py, pw, ph, PanelBg.R, PanelBg.G, PanelBg.B, panelAlpha);

            // Border highlight (top edge)
            r.DrawRect(px, py, pw, 1f, AccentColor.R, AccentColor.G, AccentColor.B, 0.4f);

            // Title bar (skip if TitleVisible = false)
            if (panel.TitleVisible)
            {
                float tx = px + Padding;
                float ty = py + 4f;

                // Collapse indicator
                if (panel.Collapsible)
                {
                    string collapseIcon = panel.Collapsed ? "▶" : "▼";
                    r.DrawText(collapseIcon, tx, ty, TextDim.R, TextDim.G, TextDim.B, TextDim.A);
                    tx += 16f;
                }

                // Panel title
                r.DrawText(panel.Title, tx, ty, TextPrimary.R, TextPrimary.G, TextPrimary.B, TextPrimary.A);
            }

            if (panel.Collapsed) return;

            // Render child elements
            float topPad = panel.TitleVisible ? PanelTitleHeight : 6f;
            float ey = py + topPad + ItemSpacing;
            float contentX = px + Padding;
            float contentW = pw - Padding * 2;
            _currentPanelContentWidth = contentW;

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
                    case HudInlineRow row:
                        RenderInlineRow(r, row, contentX, ey, contentW, elH);
                        break;
                    case HudProgressLine prog:
                        RenderProgressLine(r, prog, contentX, ey, contentW, elH);
                        break;
                }

                ey += elH + ItemSpacing;
            }
        }

        private void RenderButton(IHudRenderer r, HudButton btn, float x, float y, float w, float h, bool hover, bool pressed)
        {
            if (btn.IsDisabled)
            {
                r.DrawRect(x, y, w, h, ButtonBg.R, ButtonBg.G, ButtonBg.B, ButtonBg.A * 0.55f);
                float tw2 = r.MeasureTextWidth(btn.Text);
                float tx2 = x + (w - tw2) / 2f;
                float ty3 = y + (h - r.LineHeight) / 2f;
                r.DrawText(btn.Text, tx2, ty3, TextDim.R, TextDim.G, TextDim.B, TextDim.A * 0.55f);
                return;
            }
            var successColor = new HudColor(CheckActive.R, CheckActive.G, CheckActive.B, CheckActive.A * 0.85f);
            var bg = pressed ? AccentColor : hover ? ButtonHover : btn.IsSuccess ? successColor : btn.IsAccent ? ButtonActive : ButtonBg;
            r.DrawRect(x, y, w, h, bg.R, bg.G, bg.B, bg.A);
            float tw = r.MeasureTextWidth(btn.Text);
            float tx = x + (w - tw) / 2f;
            float ty2 = y + (h - r.LineHeight) / 2f;
            var fg = (btn.IsAccent || btn.IsSuccess || pressed) ? TextPrimary : hover ? TextPrimary : TextSecondary;
            r.DrawText(btn.Text, tx, ty2, fg.R, fg.G, fg.B, fg.A);
        }

        private void RenderCheckbox(IHudRenderer r, HudCheckbox chk, float x, float y, float w, float h, bool hover)
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

        private void RenderSlider(IHudRenderer r, HudSlider sld, float x, float y, float w, float h, bool hover)
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

            // Reserve space for tick labels above the track
            if (sld.ShowTicks && sld.TickLabels.Count > 0)
                sliderY += 18f;

            float trackH = sld.ShowLabel ? 6f : 4f;
            float trackY = sliderY + (SliderHeight - trackH) / 2f;

            // --- Tick marks and HH:MM labels ---
            if (sld.ShowTicks && sld.TickLabels.Count > 1)
            {
                int count = sld.TickLabels.Count;
                // Determine which tick is the active (current) one
                float range2 = sld.Max - sld.Min;
                int activeTick = range2 > 0f
                    ? (int)Math.Round((sld.Value - sld.Min) / range2 * (count - 1))
                    : 0;

                float labelLineH = r.LineHeight;
                float labelAreaY = sliderY - 18f;  // area above the track

                for (int i = 0; i < count; i++)
                {
                    float tickRatio = (float)i / (count - 1);
                    float tickX = x + tickRatio * w;

                    bool isActive = (i == activeTick);

                    // Tick line (2px tall, sitting just above the track)
                    var tickColor = isActive ? AccentColor : SliderTrack;
                    float tickAlpha = isActive ? 1.0f : 0.8f;
                    r.DrawRect(tickX - 0.5f, trackY - 5f, 1f, 5f, tickColor.R, tickColor.G, tickColor.B, tickAlpha);

                    // HH:MM label above the tick
                    string lbl = sld.TickLabels[i];
                    float lblW = r.MeasureTextWidth(lbl);
                    // Clamp label X so it stays within slider bounds
                    float lblX = Math.Max(x, Math.Min(tickX - lblW / 2f, x + w - lblW));
                    float lblY = labelAreaY + (18f - labelLineH) / 2f;
                    var lblColor = isActive ? AccentColor : TextDim;
                    r.DrawText(lbl, lblX, lblY, lblColor.R, lblColor.G, lblColor.B, isActive ? 1.0f : 0.75f);
                }
            }

            // Track background
            r.DrawRect(x, trackY, w, trackH, SliderTrack.R, SliderTrack.G, SliderTrack.B, SliderTrack.A);

            // Filled portion
            float range = sld.Max - sld.Min;
            float ratio = range > 0f ? (sld.Value - sld.Min) / range : 0f;
            float fillW = w * ratio;
            r.DrawRect(x, trackY, fillW, trackH, AccentColor.R, AccentColor.G, AccentColor.B, 0.85f);

            // Thumb
            float thumbW = sld.ShowLabel ? 12f : 10f;
            float thumbH = sld.ShowLabel ? 14f : 12f;
            float thumbX = x + fillW - thumbW / 2f;
            float thumbY = trackY - (thumbH - trackH) / 2f;
            var thumbColor = (hover || sld == _draggingSlider) ? TextPrimary : SliderThumb;
            r.DrawRect(thumbX, thumbY, thumbW, thumbH, thumbColor.R, thumbColor.G, thumbColor.B, thumbColor.A);

            // Store track bounds for hit-testing
            sld.TrackBounds = new RectangleF(x, trackY - 4f, w, trackH + 8f);
        }

        private void RenderProgressLine(IHudRenderer r, HudProgressLine prog, float x, float y, float w, float h)
        {
            // Slim 3px track
            float trackH = 3f;
            float trackY = y + (h - trackH) / 2f;
            r.DrawRect(x, trackY, w, trackH, SliderTrack.R, SliderTrack.G, SliderTrack.B, SliderTrack.A * 0.7f);

            // Filled accent portion
            float fillW = w * Math.Max(0f, Math.Min(1f, prog.Value));
            if (fillW > 0f)
            {
                r.DrawRect(x, trackY, fillW, trackH, AccentColor.R, AccentColor.G, AccentColor.B, 0.9f);
                // Bright cap at leading edge
                r.DrawRect(x + fillW - 1f, trackY - 1f, 2f, trackH + 2f, 1f, 1f, 1f, 0.55f);
            }
        }

        private void RenderDropdown(IHudRenderer r, HudDropdown dd, float x, float y, float w, float h, bool hover)
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

        private void RenderDropdownOverlay(IHudRenderer r, HudDropdown dd, int vw, int vh)
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

        private void RenderLoadingOverlay(IHudRenderer r, int vw, int vh, string message)
        {
            // Semi-transparent dark scrim over entire viewport
            r.DrawRect(0, 0, vw, vh, 0, 0, 0, 0.55f);

            // Centered pill-shaped panel
            float textW = r.MeasureTextWidth(message);
            float panelW = textW + 60f;
            float panelH = 44f;
            float px = (vw - panelW) / 2f;
            float py = (vh - panelH) / 2f;

            // Panel background
            r.DrawRect(px, py, panelW, panelH, PanelBg.R, PanelBg.G, PanelBg.B, 0.92f);
            // Accent top edge
            r.DrawRect(px, py, panelW, 2f, AccentColor.R, AccentColor.G, AccentColor.B, 0.8f);

            // Centered text
            float tx = px + (panelW - textW) / 2f;
            float ty = py + (panelH - r.LineHeight) / 2f;
            r.DrawText(message, tx, ty, TextPrimary.R, TextPrimary.G, TextPrimary.B, TextPrimary.A);
        }

        private void RenderLabel(IHudRenderer r, HudLabel lbl, float x, float y, float w, float h)
        {
            var color = lbl.IsSection ? AccentColor : lbl.IsDim ? TextDim : TextSecondary;

            if (lbl.IsSection)
            {
                float ty = y + (h - r.LineHeight) / 2f;
                r.DrawText(lbl.Text, x, ty, color.R, color.G, color.B, color.A);
            }
            else
            {
                // Word-wrap label text within the available width
                string wrapped = WrapText(lbl.Text, w, r);
                float lineH = r.LineHeight;
                int lineCount = CountNewlines(wrapped) + 1;
                float totalTextH = lineH * lineCount;
                float ty = y + (h - totalTextH) / 2f;
                r.DrawText(wrapped, x, ty, color.R, color.G, color.B, color.A);
            }
        }

        /// <summary>
        /// Counts wrapped lines for a label, using the renderer if available for accurate measurement.
        /// </summary>
        private int CountWrappedLines(string text, float maxWidth)
        {
            if (_currentRenderer == null || maxWidth <= 0)
                return Math.Max(1, text.Split('\n').Length);

            string wrapped = WrapText(text, maxWidth, _currentRenderer);
            return CountNewlines(wrapped) + 1;
        }

        private static int CountNewlines(string s)
        {
            int count = 0;
            foreach (char c in s)
                if (c == '\n') count++;
            return count;
        }

        /// <summary>
        /// Word-wrap text to fit within maxWidth pixels, breaking at ' ' and '|' characters.
        /// </summary>
        private static string WrapText(string text, float maxWidth, IHudRenderer r)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return text;
            if (r.MeasureTextWidth(text) <= maxWidth) return text;

            var result = new System.Text.StringBuilder();
            float lineWidth = 0f;
            int lastBreak = -1;
            int lineStart = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    result.Append(text, lineStart, i - lineStart + 1);
                    lineStart = i + 1;
                    lineWidth = 0f;
                    lastBreak = -1;
                    continue;
                }

                float charW = r.MeasureTextWidth(c.ToString());
                lineWidth += charW;

                if (c == ' ' || c == '|')
                    lastBreak = i;

                if (lineWidth > maxWidth && i > lineStart)
                {
                    if (lastBreak > lineStart)
                    {
                        result.Append(text, lineStart, lastBreak - lineStart + 1);
                        result.Append('\n');
                        lineStart = lastBreak + 1;
                        // Recalculate width from new line start
                        lineWidth = 0f;
                        for (int j = lineStart; j <= i; j++)
                            lineWidth += r.MeasureTextWidth(text[j].ToString());
                        lastBreak = -1;
                    }
                    else
                    {
                        // No break point found, force break at current position
                        result.Append(text, lineStart, i - lineStart);
                        result.Append('\n');
                        lineStart = i;
                        lineWidth = charW;
                        lastBreak = -1;
                    }
                }
            }

            if (lineStart < text.Length)
                result.Append(text, lineStart, text.Length - lineStart);

            return result.ToString();
        }

        private void RenderButtonGroup(IHudRenderer r, HudButtonGroup grp, float x, float y, float w, float h, bool hover)
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

        private void RenderInlineRow(IHudRenderer r, HudInlineRow row, float x, float y, float w, float h)
        {
            // Render children horizontally within the row
            float cx = x;
            float rowH = InlineButtonSize;
            row.ChildBounds.Clear();

            foreach (var child in row.Children)
            {
                if (!child.Visible) continue;

                bool isChildHovered = child == _hoveredElement;
                bool isChildPressed = child == _pressedElement;

                switch (child)
                {
                    case HudButton btn:
                    {
                        float btnTextW = r.MeasureTextWidth(btn.Text);
                        float btnW = btn.IsCompact ? InlineButtonSize : Math.Max(btnTextW + 16f, InlineButtonSize);
                        if (btn.IsDisabled)
                        {
                            r.DrawRect(cx, y, btnW, rowH, ButtonBg.R, ButtonBg.G, ButtonBg.B, ButtonBg.A * 0.55f);
                            float dtx = cx + (btnW - btnTextW) / 2f;
                            float dty = y + (rowH - r.LineHeight) / 2f;
                            r.DrawText(btn.Text, dtx, dty, TextDim.R, TextDim.G, TextDim.B, TextDim.A * 0.5f);
                        }
                        else
                        {
                            var successColor2 = new HudColor(CheckActive.R, CheckActive.G, CheckActive.B, CheckActive.A * 0.85f);
                            var bg = isChildPressed ? AccentColor : isChildHovered ? ButtonHover : btn.IsSuccess ? successColor2 : btn.IsAccent ? ButtonActive : ButtonBg;
                            r.DrawRect(cx, y, btnW, rowH, bg.R, bg.G, bg.B, bg.A);
                            float tx = cx + (btnW - btnTextW) / 2f;
                            float ty = y + (rowH - r.LineHeight) / 2f;
                            var fg = (btn.IsAccent || btn.IsSuccess || isChildPressed) ? TextPrimary : isChildHovered ? TextPrimary : TextSecondary;
                            r.DrawText(btn.Text, tx, ty, fg.R, fg.G, fg.B, fg.A);
                        }
                        child.ComputedBounds = new RectangleF(cx, y, btnW, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, btnW, rowH));
                        cx += btnW + InlineItemSpacing;
                        break;
                    }
                    case HudLabel lbl:
                    {
                        float lblW = r.MeasureTextWidth(lbl.Text) + 8f;
                        float ty = y + (rowH - r.LineHeight) / 2f;
                        var color = lbl.IsDim ? TextDim : TextSecondary;
                        r.DrawText(lbl.Text, cx + 4f, ty, color.R, color.G, color.B, color.A);
                        child.ComputedBounds = new RectangleF(cx, y, lblW, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, lblW, rowH));
                        cx += lblW + InlineItemSpacing;
                        break;
                    }
                    case HudCheckbox chk:
                    {
                        float boxSize = 14f;
                        float totalW = boxSize + 6f + r.MeasureTextWidth(chk.Text);
                        float boxX = cx;
                        float boxY = y + (rowH - boxSize) / 2f;
                        var boxBg = isChildHovered ? ButtonHover : ButtonBg;
                        r.DrawRect(boxX, boxY, boxSize, boxSize, boxBg.R, boxBg.G, boxBg.B, boxBg.A);
                        if (chk.Checked)
                            r.DrawRect(boxX + 2f, boxY + 2f, boxSize - 4f, boxSize - 4f, CheckActive.R, CheckActive.G, CheckActive.B, CheckActive.A);
                        float labelX = boxX + boxSize + 6f;
                        float labelY = y + (rowH - r.LineHeight) / 2f;
                        var fg = chk.Checked ? TextPrimary : TextSecondary;
                        r.DrawText(chk.Text, labelX, labelY, fg.R, fg.G, fg.B, fg.A);
                        child.ComputedBounds = new RectangleF(cx, y, totalW, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, totalW, rowH));
                        cx += totalW + InlineItemSpacing;
                        break;
                    }
                    case HudSeparator:
                    {
                        // Vertical divider
                        float sepX = cx + 2f;
                        r.DrawRect(sepX, y + 4f, 1f, rowH - 8f, SeparatorColor.R, SeparatorColor.G, SeparatorColor.B, SeparatorColor.A);
                        child.ComputedBounds = new RectangleF(cx, y, 6f, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, 6f, rowH));
                        cx += 6f + InlineItemSpacing;
                        break;
                    }
                    case HudSlider sld:
                    {
                        // Inline slider: fixed width
                        float sliderW = Math.Min(120f, w - (cx - x) - 4f);
                        if (sliderW < 40f) sliderW = 40f;
                        float trackH = 6f;
                        float trackY = y + (rowH - trackH) / 2f;
                        r.DrawRect(cx, trackY, sliderW, trackH, SliderTrack.R, SliderTrack.G, SliderTrack.B, SliderTrack.A);
                        float fillW = sliderW * ((sld.Value - sld.Min) / (sld.Max - sld.Min));
                        r.DrawRect(cx, trackY, fillW, trackH, AccentColor.R, AccentColor.G, AccentColor.B, 0.7f);
                        float thumbW = 10f, thumbH = 14f;
                        float thumbX = cx + fillW - thumbW / 2f;
                        float thumbY = trackY - (thumbH - trackH) / 2f;
                        var thumbColor = (isChildHovered || sld == _draggingSlider) ? TextPrimary : SliderThumb;
                        r.DrawRect(thumbX, thumbY, thumbW, thumbH, thumbColor.R, thumbColor.G, thumbColor.B, thumbColor.A);
                        sld.TrackBounds = new RectangleF(cx, trackY - 4f, sliderW, trackH + 8f);
                        child.ComputedBounds = new RectangleF(cx, y, sliderW, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, sliderW, rowH));
                        cx += sliderW + InlineItemSpacing;
                        break;
                    }
                    default:
                    {
                        child.ComputedBounds = new RectangleF(cx, y, 0, rowH);
                        row.ChildBounds.Add(new RectangleF(cx, y, 0, rowH));
                        break;
                    }
                }
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

                var panelW = (!string.IsNullOrEmpty(panel.LayoutGroup) && panel.Collapsed && panel.CompactWidth > 0)
                    ? panel.CompactWidth : panel.Width;
                var panelBounds = new RectangleF(panel.X, panel.Y, panelW, panel.ComputedHeight);
                if (!panelBounds.Contains(mx, my)) continue;

                // Title bar click → toggle collapse
                float titleH = panel.TitleVisible ? PanelTitleHeight : Padding;
                if (panel.TitleVisible && my < panel.Y + titleH)
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

                        case HudInlineRow row:
                            // Check children of the inline row
                            foreach (var child in row.Children)
                            {
                                if (!child.Visible) continue;
                                if (!child.ComputedBounds.Contains(mx, my)) continue;
                                _pressedElement = child;
                                switch (child)
                                {
                                    case HudButton btn2:
                                        if (!btn2.IsDisabled)
                                            btn2.OnClick?.Invoke();
                                        return true;
                                    case HudCheckbox chk2:
                                        chk2.Checked = !chk2.Checked;
                                        chk2.OnChanged?.Invoke(chk2.Checked);
                                        return true;
                                    case HudSlider sld2:
                                        _draggingSlider = sld2;
                                        UpdateSliderValue(sld2, mx);
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

                var panelW2 = (!string.IsNullOrEmpty(panel.LayoutGroup) && panel.Collapsed && panel.CompactWidth > 0)
                    ? panel.CompactWidth : panel.Width;
                var panelBounds = new RectangleF(panel.X, panel.Y, panelW2, panel.ComputedHeight);
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

                        // Track hovered child in inline row
                        if (el is HudInlineRow row)
                        {
                            foreach (var child in row.Children)
                            {
                                if (!child.Visible) continue;
                                if (child.ComputedBounds.Contains(mx, my))
                                {
                                    _hoveredElement = child;
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
            // Only quantise to integers for large-range sliders (e.g. 0-100 opacity).
            // Normalised sliders like the animation timeline use the raw fraction.
            if (sld.Max - sld.Min >= 2f)
                newVal = (float)Math.Round(newVal);
            if (Math.Abs(newVal - sld.Value) > 0.001f)
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
                float pw = (!string.IsNullOrEmpty(panel.LayoutGroup) && panel.Collapsed && panel.CompactWidth > 0)
                    ? panel.CompactWidth : panel.Width;
                var bounds = new RectangleF(panel.X, panel.Y, pw, panel.ComputedHeight);
                if (bounds.Contains(mx, my)) return true;
            }
            return false;
        }
    }

    // â•â•â• HUD Element Types â•â•â•

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
        /// <summary>When false, the panel has no title bar (good for minimal/borderless panels like zoom strip).</summary>
        public bool TitleVisible { get; set; } = true;
        /// <summary>Panels sharing the same non-null LayoutGroup are laid out horizontally instead of vertically.</summary>
        public string? LayoutGroup { get; set; } = null;
        /// <summary>Width to use when collapsed in a horizontal LayoutGroup (title text width only).</summary>
        public float CompactWidth { get; set; } = 0f;
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
        /// <summary>When true, compact square button (used in inline rows).</summary>
        public bool IsCompact { get; set; } = false;
        /// <summary>Renders the button greyed-out and ignores clicks.</summary>
        public bool IsDisabled { get; set; } = false;
        /// <summary>Renders the button with a green success color (e.g. after loading completes).</summary>
        public bool IsSuccess { get; set; } = false;
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
        /// <summary>When true, draws tick marks and labels above the slider track.</summary>
        public bool ShowTicks { get; set; } = false;
        /// <summary>Labels for each tick (one per frame). Shown above the track when ShowTicks is true.</summary>
        public List<string> TickLabels { get; set; } = new();
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

    /// <summary>
    /// A thin horizontal progress bar — purely decorative, shows playback progress.
    /// Rendered as a slim accent-colored fill strip.
    /// </summary>
    public class HudProgressLine : HudElement
    {
        /// <summary>Fill fraction 0.0–1.0.</summary>
        public float Value { get; set; } = 0f;
    }

    public class HudButtonGroup : HudElement
    {
        public List<string> Options { get; set; } = new();
        public int SelectedIndex { get; set; } = 0;
        public List<RectangleF> ButtonBounds { get; } = new();
        internal int _hoveredIndex = -1;
        public Action<int>? OnSelectionChanged { get; set; }
    }

    /// <summary>
    /// Container element that renders its children horizontally in a single row.
    /// Used for compact toolbars (e.g., animation transport controls).
    /// </summary>
    public class HudInlineRow : HudElement
    {
        public List<HudElement> Children { get; } = new();
        public List<RectangleF> ChildBounds { get; } = new();
    }
}
