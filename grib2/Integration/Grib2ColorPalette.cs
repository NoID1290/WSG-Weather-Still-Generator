using System;
using System.Collections.Generic;
using System.Drawing;

namespace Grib2.Integration
{
    /// <summary>
    /// Color palettes for GRIB2 forecast field types.
    /// Each field gets a distinct, scientifically-appropriate color ramp
    /// with smooth linear interpolation between color stops.
    /// </summary>
    public static class Grib2ColorPalette
    {
        // ═══════════════════════════════════════════════════════════════════
        // Temperature (°C)  — purple → blue → cyan → green → yellow → red
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] TemperatureStops =
        {
            (-40, 40,   0, 100),   // deep purple
            (-30,  0,   0, 180),   // dark blue
            (-20,  0,  80, 220),   // blue
            (-10, 60, 160, 240),   // sky blue
            (  0,130, 210, 255),   // light cyan
            ( 10,120, 220, 120),   // green
            ( 20,240, 230,  50),   // yellow
            ( 30,255, 160,  20),   // orange
            ( 40,220,  30,  20),   // red
            ( 50,140,   0,  50),   // dark crimson
        };

        // ═══════════════════════════════════════════════════════════════════
        // Wind speed (km/h) — calm blue → green → yellow → orange → red → magenta
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] WindStops =
        {
            (  0,  80, 130, 190),   // calm blue
            ( 10,  60, 180, 170),   // teal
            ( 20, 100, 210, 100),   // green
            ( 40, 200, 220,  50),   // yellow-green
            ( 60, 255, 200,   0),   // gold
            ( 80, 255, 130,   0),   // orange
            (100, 230,  50,  20),   // red
            (130, 180,  20, 100),   // magenta
            (160, 120,   0, 120),   // deep purple
        };

        // ═══════════════════════════════════════════════════════════════════
        // Precipitation (mm/h) — transparent → light blue → blue → yellow → red
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] PrecipStops =
        {
            (  0,   0,   0,   0),   // transparent/none
            (0.1f, 100, 180, 255),   // very light blue
            (  1,  50, 130, 230),   // light blue
            (  5,  30,  90, 200),   // medium blue
            ( 10,  50, 200,  80),   // green
            ( 25, 240, 230,  40),   // yellow
            ( 50, 255, 140,   0),   // orange
            (100, 220,  30,  30),   // red
            (150, 160,  20, 100),   // magenta
        };

        // ═══════════════════════════════════════════════════════════════════
        // Cloud cover (%) — clear → translucent white → dense gray
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] CloudStops =
        {
            (  0,   0,   0,   0),   // clear (transparent)
            ( 10, 200, 210, 220),   // very faint
            ( 30, 180, 190, 200),   // light cloud
            ( 50, 160, 170, 180),   // partial
            ( 70, 140, 150, 160),   // significant
            ( 90, 120, 125, 130),   // dense
            (100, 100, 105, 110),   // overcast
        };

        // ═══════════════════════════════════════════════════════════════════
        // Pressure (hPa) — low purple → mid green → high orange
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] PressureStops =
        {
            ( 960,  80,  20, 140),   // deep low (purple)
            ( 980,  60,  80, 200),   // low (blue)
            ( 995, 100, 180, 200),   // slightly low (cyan)
            (1010, 120, 200, 120),   // average (green)
            (1020, 200, 200,  60),   // slightly high (yellow)
            (1035, 220, 150,  30),   // high (orange)
            (1050, 180,  60,  30),   // very high (red)
        };

        // ═══════════════════════════════════════════════════════════════════
        // CAPE (J/kg) — none → green → yellow → orange → red → magenta
        // ═══════════════════════════════════════════════════════════════════
        private static readonly (float val, int r, int g, int b)[] CapeStops =
        {
            (   0,   0,   0,   0),   // none
            ( 100,  80, 180,  80),   // marginal (green)
            ( 500, 160, 220,  60),   // weak (yellow-green)
            (1000, 240, 230,  40),   // moderate (yellow)
            (2000, 255, 160,  20),   // strong (orange)
            (3000, 220,  50,  20),   // severe (red)
            (4000, 160,  20, 100),   // extreme (magenta)
            (5000, 100,   0, 120),   // violent (purple)
        };

        /// <summary>
        /// Gets the color for a given field type and value using smooth linear interpolation.
        /// </summary>
        /// <param name="fieldType">The GRIB2 field type (determines the palette)</param>
        /// <param name="value">The field value in its natural unit (°C, km/h, mm/h, %, hPa, J/kg)</param>
        /// <param name="alpha">Alpha for the returned color (0–255). Default 160 for semi-transparent overlay.</param>
        /// <returns>Interpolated ARGB color</returns>
        public static Color GetColor(string fieldType, float value, int alpha = 160)
        {
            var stops = fieldType switch
            {
                "Temperature" => TemperatureStops,
                "Wind" => WindStops,
                "Precipitation" => PrecipStops,
                "CloudCover" => CloudStops,
                "Pressure" => PressureStops,
                "CAPE" => CapeStops,
                _ => TemperatureStops
            };

            return InterpolateColor(stops, value, alpha);
        }

        /// <summary>
        /// Gets the display unit string for a field type.
        /// </summary>
        public static string GetUnit(string fieldType)
        {
            return fieldType switch
            {
                "Temperature" => "°C",
                "Wind" => "km/h",
                "Precipitation" => "mm/h",
                "CloudCover" => "%",
                "Pressure" => "hPa",
                "CAPE" => "J/kg",
                _ => ""
            };
        }

        /// <summary>
        /// Gets the display name for a field type.
        /// </summary>
        public static string GetDisplayName(string fieldType)
        {
            return fieldType switch
            {
                "Temperature" => "Temperature",
                "Wind" => "Wind Speed",
                "Precipitation" => "Precipitation",
                "CloudCover" => "Cloud Cover",
                "Pressure" => "MSLP",
                "CAPE" => "CAPE",
                _ => fieldType
            };
        }

        /// <summary>
        /// Performs smooth linear interpolation between color stops.
        /// </summary>
        private static Color InterpolateColor((float val, int r, int g, int b)[] stops, float value, int alpha)
        {
            if (stops.Length == 0) return Color.Transparent;
            if (value <= stops[0].val) return Color.FromArgb(alpha, stops[0].r, stops[0].g, stops[0].b);
            if (value >= stops[^1].val) return Color.FromArgb(alpha, stops[^1].r, stops[^1].g, stops[^1].b);

            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (value >= stops[i].val && value <= stops[i + 1].val)
                {
                    float f = (value - stops[i].val) / (stops[i + 1].val - stops[i].val);
                    int cr = (int)(stops[i].r + (stops[i + 1].r - stops[i].r) * f);
                    int cg = (int)(stops[i].g + (stops[i + 1].g - stops[i].g) * f);
                    int cb = (int)(stops[i].b + (stops[i + 1].b - stops[i].b) * f);
                    return Color.FromArgb(alpha,
                        Math.Clamp(cr, 0, 255),
                        Math.Clamp(cg, 0, 255),
                        Math.Clamp(cb, 0, 255));
                }
            }

            return Color.FromArgb(alpha, 128, 128, 128);
        }

        /// <summary>
        /// Gets the value range for legend rendering.
        /// Returns (min, max) for the given field type.
        /// </summary>
        public static (float Min, float Max) GetValueRange(string fieldType)
        {
            return fieldType switch
            {
                "Temperature" => (-40f, 50f),
                "Wind" => (0f, 160f),
                "Precipitation" => (0f, 150f),
                "CloudCover" => (0f, 100f),
                "Pressure" => (960f, 1050f),
                "CAPE" => (0f, 5000f),
                _ => (0f, 100f)
            };
        }

        /// <summary>
        /// Generates a 1D RGBA palette texture (256 texels) for GPU-based color mapping.
        /// The returned byte array is 256×4 bytes (R,G,B,A per texel), sampled by
        /// normalizing a field value to [0,1] within its range.
        /// </summary>
        /// <param name="fieldType">Field name (Temperature, Wind, Precipitation, CloudCover, Pressure, CAPE)</param>
        /// <param name="alpha">Alpha for non-transparent stops (0–255). Default 200.</param>
        /// <returns>byte[1024] — 256 texels × 4 channels (RGBA)</returns>
        public static byte[] GeneratePaletteTexture(string fieldType, int alpha = 200)
        {
            var (min, max) = GetValueRange(fieldType);
            var texture = new byte[256 * 4];

            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                float val = min + t * (max - min);
                var c = GetColor(fieldType, val, alpha);

                int offset = i * 4;
                texture[offset]     = c.R;
                texture[offset + 1] = c.G;
                texture[offset + 2] = c.B;
                texture[offset + 3] = c.A;
            }

            return texture;
        }

        /// <summary>
        /// Gets the raw color stops for a field type as arrays suitable for GPU uniform upload.
        /// Returns parallel arrays of (values, r, g, b) normalized to [0,1] for colors.
        /// </summary>
        public static (float[] values, float[] r, float[] g, float[] b) GetStopsNormalized(string fieldType)
        {
            var stops = fieldType switch
            {
                "Temperature" => TemperatureStops,
                "Wind" => WindStops,
                "Precipitation" => PrecipStops,
                "CloudCover" => CloudStops,
                "Pressure" => PressureStops,
                "CAPE" => CapeStops,
                _ => TemperatureStops
            };

            var values = new float[stops.Length];
            var r = new float[stops.Length];
            var g = new float[stops.Length];
            var b = new float[stops.Length];

            for (int i = 0; i < stops.Length; i++)
            {
                values[i] = stops[i].val;
                r[i] = stops[i].r / 255f;
                g[i] = stops[i].g / 255f;
                b[i] = stops[i].b / 255f;
            }

            return (values, r, g, b);
        }

        /// <summary>
        /// All supported field type names.
        /// </summary>
        public static readonly string[] AllFieldTypes = { "Temperature", "Wind", "Precipitation", "CloudCover", "Pressure", "CAPE" };
    }
}
