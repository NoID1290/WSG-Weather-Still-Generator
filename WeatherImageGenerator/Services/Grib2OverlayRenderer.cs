using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Grib2.Integration;
using Grib2.Models;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Utilities;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Rasterizes decoded GRIB2 grid data into transparent PNG images suitable for
    /// map overlay compositing. Supports heatmaps, wind barbs, isobar contours,
    /// and labeled value badges.
    /// </summary>
    public class Grib2OverlayRenderer
    {
        /// <summary>Whether to draw value labels on the overlay.</summary>
        public bool ShowLabels { get; set; } = true;

        /// <summary>Whether to draw wind barb arrows (only used for Wind field type).</summary>
        public bool ShowWindBarbs { get; set; } = true;

        /// <summary>Whether to draw isobar contour lines (only used for Pressure field type).</summary>
        public bool ShowIsobars { get; set; } = true;

        /// <summary>Base alpha for heatmap pixels (0–255). Default 140 for semi-transparent overlay.</summary>
        public int HeatmapAlpha { get; set; } = 140;

        /// <summary>
        /// Renders a GRIB2 field into a transparent PNG overlay for the given viewport.
        /// </summary>
        /// <param name="field">Decoded GRIB2 field with grid values</param>
        /// <param name="fieldType">Which field type (determines palette and rendering style)</param>
        /// <param name="viewportBBox">Viewport bounding box (MinLat, MinLon, MaxLat, MaxLon)</param>
        /// <param name="width">Output image width in pixels</param>
        /// <param name="height">Output image height in pixels</param>
        /// <returns>PNG bytes, or null on failure</returns>
        public byte[]? RenderOverlay(
            Grib2Message message,
            Grib2FieldType fieldType,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewportBBox,
            int width,
            int height)
        {
            var field = message?.Field;
            var grid = message?.Grid;
            if (field?.Values == null || field.Values.Length == 0 || grid == null)
                return null;

            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                string fieldName = fieldType.ToString();

                // Convert values to display units
                float[] displayValues = ConvertToDisplayUnits(field.Values, fieldType);

                // Step 1: Render heatmap raster
                RenderHeatmap(g, grid, displayValues, fieldName, viewportBBox, width, height);

                // Step 2: Render field-specific features
                switch (fieldType)
                {
                    case Grib2FieldType.Wind:
                        if (ShowWindBarbs)
                            RenderWindBarbs(g, grid, displayValues, viewportBBox, width, height);
                        break;

                    case Grib2FieldType.Pressure:
                        if (ShowIsobars)
                            RenderIsobars(g, grid, displayValues, viewportBBox, width, height);
                        break;
                }

                // Step 3: Render labels
                if (ShowLabels)
                    RenderLabels(g, grid, displayValues, fieldName, viewportBBox, width, height);

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Grib2OverlayRenderer] Render error: {ex.Message}", Logger.LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// Renders a wind speed overlay from U and V components.
        /// </summary>
        public byte[]? RenderWindOverlay(
            Grib2Message uMessage,
            Grib2Message vMessage,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewportBBox,
            int width,
            int height)
        {
            var uField = uMessage?.Field;
            var vField = vMessage?.Field;
            var grid = uMessage?.Grid;
            if (uField?.Values == null || vField?.Values == null || grid == null)
                return null;

            int len = Math.Min(uField.Values.Length, vField.Values.Length);
            float[] windSpeed = new float[len];
            float[] windDir = new float[len];

            for (int i = 0; i < len; i++)
            {
                float u = uField.Values[i];
                float v = vField.Values[i];
                windSpeed[i] = MathF.Sqrt(u * u + v * v) * 3.6f; // m/s → km/h
                windDir[i] = (MathF.Atan2(-u, -v) * 180f / MathF.PI + 360f) % 360f;
            }

            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // Heatmap
                RenderHeatmap(g, grid, windSpeed, "Wind", viewportBBox, width, height);

                // Wind barbs
                if (ShowWindBarbs)
                {
                    RenderWindBarbsFromComponents(g, grid, windSpeed, windDir, viewportBBox, width, height);
                }

                // Labels
                if (ShowLabels)
                    RenderLabels(g, grid, windSpeed, "Wind", viewportBBox, width, height);

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Grib2OverlayRenderer] Wind render error: {ex.Message}", Logger.LogLevel.Error);
                return null;
            }
        }

        #region Heatmap rendering

        private void RenderHeatmap(
            Graphics g,
            Grib2Grid grid,
            float[] displayValues,
            string fieldName,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewBBox,
            int width,
            int height)
        {
            int ni = grid.Ni;
            int nj = grid.Nj;

            if (ni <= 0 || nj <= 0 || displayValues.Length < ni * nj)
                return;

            // Create a raster bitmap at grid resolution (clamped for perf)
            int rasterW = Math.Min(ni, 512);
            int rasterH = Math.Min(nj, 512);

            using var raster = new Bitmap(rasterW, rasterH, PixelFormat.Format32bppArgb);

            // Lock bits for fast pixel writing
            var rect = new Rectangle(0, 0, rasterW, rasterH);
            var bits = raster.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                double gridFirstLat = grid.FirstLatitude;
                double gridFirstLon = grid.FirstLongitude;
                double gridDi = grid.DiDegrees;
                double gridDj = grid.DjDegrees;

                // Determine grid scanning direction
                bool scanNtoS = (grid.ScanningMode & 0x40) == 0; // bit 2 = 0 means N→S
                bool scanWtoE = (grid.ScanningMode & 0x80) == 0; // bit 1 = 0 means W→E

                for (int py = 0; py < rasterH; py++)
                {
                    // Map raster pixel to grid coordinate
                    double lat = viewBBox.MaxLat - (double)py / rasterH * (viewBBox.MaxLat - viewBBox.MinLat);
                    int rowPtr = py * bits.Stride;

                    for (int px = 0; px < rasterW; px++)
                    {
                        double lon = viewBBox.MinLon + (double)px / rasterW * (viewBBox.MaxLon - viewBBox.MinLon);

                        // Convert lat/lon to grid indices
                        double gi, gj;
                        if (scanWtoE)
                            gi = (lon - gridFirstLon) / gridDi;
                        else
                            gi = (gridFirstLon - lon) / gridDi;

                        if (scanNtoS)
                            gj = (gridFirstLat - lat) / gridDj;
                        else
                            gj = (lat - gridFirstLat) / gridDj;

                        // Bilinear interpolation
                        int i0 = (int)Math.Floor(gi);
                        int j0 = (int)Math.Floor(gj);
                        float fx = (float)(gi - i0);
                        float fy = (float)(gj - j0);

                        i0 = Math.Clamp(i0, 0, ni - 2);
                        j0 = Math.Clamp(j0, 0, nj - 2);

                        float v00 = GetGridValue(displayValues, i0, j0, ni);
                        float v10 = GetGridValue(displayValues, i0 + 1, j0, ni);
                        float v01 = GetGridValue(displayValues, i0, j0 + 1, ni);
                        float v11 = GetGridValue(displayValues, i0 + 1, j0 + 1, ni);

                        float value = v00 * (1 - fx) * (1 - fy) +
                                      v10 * fx * (1 - fy) +
                                      v01 * (1 - fx) * fy +
                                      v11 * fx * fy;

                        var color = Grib2ColorPalette.GetColor(fieldName, value, HeatmapAlpha);

                        // Write BGRA
                        int offset = rowPtr + px * 4;
                        Marshal.WriteByte(bits.Scan0, offset, color.B);
                        Marshal.WriteByte(bits.Scan0, offset + 1, color.G);
                        Marshal.WriteByte(bits.Scan0, offset + 2, color.R);
                        Marshal.WriteByte(bits.Scan0, offset + 3, color.A);
                    }
                }
            }
            finally
            {
                raster.UnlockBits(bits);
            }

            // Draw interpolated raster scaled to viewport
            g.DrawImage(raster, 0, 0, width, height);
        }

        private static float GetGridValue(float[] values, int i, int j, int ni)
        {
            int idx = j * ni + i;
            if (idx >= 0 && idx < values.Length)
                return values[idx];
            return 0f;
        }

        #endregion

        #region Wind barbs

        private void RenderWindBarbs(
            Graphics g,
            Grib2Grid grid,
            float[] windSpeed,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewBBox,
            int width,
            int height)
        {
            // No direction info in a single field — just render speed labels
            // Full wind barbs require U/V components (use RenderWindBarbsFromComponents)
        }

        private void RenderWindBarbsFromComponents(
            Graphics g,
            Grib2Grid grid,
            float[] windSpeed,
            float[] windDir,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewBBox,
            int width,
            int height)
        {
            int ni = grid.Ni;
            int nj = grid.Nj;

            // Draw wind barbs at regular intervals (every ~60 pixels)
            int stepPx = Math.Max(50, Math.Min(80, width / 12));
            float barbLen = stepPx * 0.4f;

            using var pen = new Pen(Color.FromArgb(220, 40, 40, 40), 1.8f);
            pen.EndCap = LineCap.ArrowAnchor;

            for (int py = stepPx / 2; py < height; py += stepPx)
            {
                for (int px = stepPx / 2; px < width; px += stepPx)
                {
                    // Map pixel to lat/lon
                    double lat = viewBBox.MaxLat - (double)py / height * (viewBBox.MaxLat - viewBBox.MinLat);
                    double lon = viewBBox.MinLon + (double)px / width * (viewBBox.MaxLon - viewBBox.MinLon);

                    // Find nearest grid point
                    int gi = (int)Math.Round((lon - grid.FirstLongitude) / grid.DiDegrees);
                    int gj = (int)Math.Round((grid.FirstLatitude - lat) / grid.DjDegrees);

                    gi = Math.Clamp(gi, 0, ni - 1);
                    gj = Math.Clamp(gj, 0, nj - 1);

                    int idx = gj * ni + gi;
                    if (idx >= windSpeed.Length || idx >= windDir.Length) continue;

                    float speed = windSpeed[idx];
                    float dir = windDir[idx];

                    if (speed < 2f) continue; // skip calm winds

                    // Draw arrow from the point in the direction the wind is blowing FROM
                    float dirRad = dir * MathF.PI / 180f;
                    float dx = -MathF.Sin(dirRad) * barbLen;
                    float dy = MathF.Cos(dirRad) * barbLen;

                    // Scale barb length by speed (capped)
                    float scale = Math.Min(speed / 60f, 1.5f) + 0.5f;
                    dx *= scale;
                    dy *= scale;

                    g.DrawLine(pen, px, py, px + dx, py + dy);
                }
            }
        }

        #endregion

        #region Isobar contours

        private void RenderIsobars(
            Graphics g,
            Grib2Grid grid,
            float[] displayValues,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewBBox,
            int width,
            int height)
        {
            int ni = grid.Ni;
            int nj = grid.Nj;

            // Draw isobars every 4 hPa
            float isobarInterval = 4f;
            float minVal = 960f, maxVal = 1060f;

            using var pen = new Pen(Color.FromArgb(180, 60, 60, 60), 1.2f);
            using var font = new Font("Segoe UI", 8, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(200, 30, 30, 30));
            using var bgBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255));

            for (float isoVal = minVal; isoVal <= maxVal; isoVal += isobarInterval)
            {
                var contourPoints = new List<PointF>();

                // Simple marching: scan rows and find where value crosses isobar level
                for (int py = 0; py < height - 1; py += 3)
                {
                    for (int px = 0; px < width - 1; px += 3)
                    {
                        double lat = viewBBox.MaxLat - (double)py / height * (viewBBox.MaxLat - viewBBox.MinLat);
                        double lon = viewBBox.MinLon + (double)px / width * (viewBBox.MaxLon - viewBBox.MinLon);

                        double lat2 = viewBBox.MaxLat - (double)(py + 3) / height * (viewBBox.MaxLat - viewBBox.MinLat);
                        double lon2 = viewBBox.MinLon + (double)(px + 3) / width * (viewBBox.MaxLon - viewBBox.MinLon);

                        float v1 = SampleGrid(displayValues, lat, lon, grid, ni, nj);
                        float v2 = SampleGrid(displayValues, lat, lon2, grid, ni, nj);
                        float v3 = SampleGrid(displayValues, lat2, lon, grid, ni, nj);

                        // Check for crossing
                        if ((v1 <= isoVal && v2 > isoVal) || (v1 > isoVal && v2 <= isoVal) ||
                            (v1 <= isoVal && v3 > isoVal) || (v1 > isoVal && v3 <= isoVal))
                        {
                            contourPoints.Add(new PointF(px, py));
                        }
                    }
                }

                // Draw points as small segments
                foreach (var pt in contourPoints)
                {
                    g.FillRectangle(new SolidBrush(pen.Color), pt.X, pt.Y, 2, 2);
                }

                // Label every ~200 contour points
                if (contourPoints.Count > 0)
                {
                    int step = Math.Max(1, contourPoints.Count / 4);
                    for (int i = step / 2; i < contourPoints.Count; i += step)
                    {
                        var pt = contourPoints[i];
                        string label = $"{isoVal:F0}";
                        var sz = g.MeasureString(label, font);
                        g.FillRectangle(bgBrush, pt.X - 1, pt.Y - 1, sz.Width + 2, sz.Height + 2);
                        g.DrawString(label, font, brush, pt.X, pt.Y);
                    }
                }
            }
        }

        private float SampleGrid(float[] values, double lat, double lon, Grib2Grid grid, int ni, int nj)
        {
            double gi = (lon - grid.FirstLongitude) / grid.DiDegrees;
            double gj = (grid.FirstLatitude - lat) / grid.DjDegrees;

            int i0 = Math.Clamp((int)gi, 0, ni - 1);
            int j0 = Math.Clamp((int)gj, 0, nj - 1);

            return GetGridValue(values, i0, j0, ni);
        }

        #endregion

        #region Labels

        private void RenderLabels(
            Graphics g,
            Grib2Grid grid,
            float[] displayValues,
            string fieldName,
            (double MinLat, double MinLon, double MaxLat, double MaxLon) viewBBox,
            int width,
            int height)
        {
            int ni = grid.Ni;
            int nj = grid.Nj;

            string unit = Grib2ColorPalette.GetUnit(fieldName);
            int stepPx = Math.Max(80, Math.Min(120, width / 8));

            using var font = new Font("Segoe UI", 10, FontStyle.Bold);

            for (int py = stepPx / 2; py < height; py += stepPx)
            {
                for (int px = stepPx / 2; px < width; px += stepPx)
                {
                    double lat = viewBBox.MaxLat - (double)py / height * (viewBBox.MaxLat - viewBBox.MinLat);
                    double lon = viewBBox.MinLon + (double)px / width * (viewBBox.MaxLon - viewBBox.MinLon);

                    float value = SampleGrid(displayValues, lat, lon, grid, ni, nj);

                    // Format value
                    string text = fieldName switch
                    {
                        "Temperature" => $"{value:F1}°",
                        "Wind" => $"{value:F0}",
                        "Precipitation" => value < 0.1f ? "" : $"{value:F1}",
                        "CloudCover" => $"{value:F0}%",
                        "Pressure" => $"{value:F0}",
                        "CAPE" => value < 50 ? "" : $"{value:F0}",
                        _ => $"{value:F1}"
                    };

                    if (string.IsNullOrEmpty(text)) continue;

                    var textSize = g.MeasureString(text, font);
                    float bw = textSize.Width + 10;
                    float bh = textSize.Height + 4;
                    float bx = px - bw / 2;
                    float by = py - bh / 2;

                    var badgeColor = Grib2ColorPalette.GetColor(fieldName, value, 200);
                    using var bgPath = CreateRoundRect(bx, by, bw, bh, bh / 2);
                    using var bgBrush = new SolidBrush(badgeColor);
                    using var borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f);
                    g.FillPath(bgBrush, bgPath);
                    g.DrawPath(borderPen, bgPath);

                    float tx = bx + 5;
                    float ty = by + 2;
                    g.DrawString(text, font, Brushes.Black, tx + 0.8f, ty + 0.8f);
                    g.DrawString(text, font, Brushes.White, tx, ty);
                }
            }
        }

        #endregion

        #region Unit conversion

        private static float[] ConvertToDisplayUnits(float[] values, Grib2FieldType fieldType)
        {
            float[] result = new float[values.Length];

            switch (fieldType)
            {
                case Grib2FieldType.Temperature:
                    // Kelvin → Celsius
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] > 200 ? values[i] - 273.15f : values[i];
                    break;

                case Grib2FieldType.Wind:
                    // m/s → km/h
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] * 3.6f;
                    break;

                case Grib2FieldType.Pressure:
                    // Pa → hPa
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] > 10000 ? values[i] / 100f : values[i];
                    break;

                case Grib2FieldType.CloudCover:
                    // 0-1 fraction → 0-100%
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] <= 1 ? values[i] * 100f : values[i];
                    break;

                default:
                    Array.Copy(values, result, values.Length);
                    break;
            }

            return result;
        }

        #endregion

        #region Helpers

        private static GraphicsPath CreateRoundRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            r = Math.Min(r, Math.Min(w, h) / 2);
            path.AddArc(x, y, r * 2, r * 2, 180, 90);
            path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        #endregion
    }
}
