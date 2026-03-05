#nullable enable
using System;

namespace Grib2.Models
{
    /// <summary>
    /// Represents a single decoded GRIB2 field — the parameter, forecast time, surface level,
    /// and unpacked floating-point grid values.
    /// </summary>
    public class Grib2Field
    {
        // --- Parameter identification (from Section 4) ---

        /// <summary>WMO discipline code (Section 0). 0 = Meteorological.</summary>
        public byte Discipline { get; set; }

        /// <summary>Parameter category within the discipline (Section 4, octet 10).</summary>
        public byte ParameterCategory { get; set; }

        /// <summary>Parameter number within the category (Section 4, octet 11).</summary>
        public byte ParameterNumber { get; set; }

        /// <summary>Composite lookup key: "D.C.N" (e.g., "0.0.0" for Temperature).</summary>
        public string ParameterKey => $"{Discipline}.{ParameterCategory}.{ParameterNumber}";

        /// <summary>Human-readable parameter name from ParameterTable (e.g., "Temperature").</summary>
        public string? ParameterName { get; set; }

        /// <summary>Human-readable parameter short name (e.g., "TMP").</summary>
        public string? ParameterShortName { get; set; }

        /// <summary>Unit string from ParameterTable (e.g., "K", "m/s", "%").</summary>
        public string? ParameterUnit { get; set; }

        // --- Forecast time (from Section 4) ---

        /// <summary>Product definition template number (Section 4, octets 8–9).</summary>
        public int ProductTemplateNumber { get; set; }

        /// <summary>Type of generating process (0 = analysis, 2 = forecast).</summary>
        public byte GeneratingProcess { get; set; }

        /// <summary>Forecast time in hours from the reference time.</summary>
        public int ForecastHour { get; set; }

        /// <summary>Unit of forecast time indicator (1 = hour, 0 = minute, etc.).</summary>
        public byte ForecastTimeUnit { get; set; }

        // --- Surface/level (from Section 4) ---

        /// <summary>Type of first fixed surface (e.g., 103 = height above ground, 100 = isobaric).</summary>
        public byte SurfaceType { get; set; }

        /// <summary>Value of first fixed surface (e.g., 2 for 2m above ground, 85000 for 850 hPa).</summary>
        public double SurfaceValue { get; set; }

        /// <summary>Scale factor of first fixed surface value.</summary>
        public byte SurfaceScaleFactor { get; set; }

        /// <summary>Type of second fixed surface (for layers). 255 = missing/not used.</summary>
        public byte SurfaceType2 { get; set; } = 255;

        /// <summary>Value of second fixed surface.</summary>
        public double SurfaceValue2 { get; set; }

        /// <summary>
        /// Human-readable surface type name.
        /// </summary>
        public string SurfaceTypeName => SurfaceType switch
        {
            1 => "Ground/Surface",
            100 => "Isobaric",
            101 => "Mean Sea Level",
            103 => "Height Above Ground",
            104 => "Sigma Level",
            105 => "Hybrid Level",
            106 => "Depth Below Land Surface",
            108 => "Pressure Difference",
            200 => "Entire Atmosphere",
            _ => $"Surface Type {SurfaceType}"
        };

        // --- Data representation (from Section 5) ---

        /// <summary>Data representation template number (e.g., 0=Simple, 2=Complex, 3=ComplexSpatial, 40=JPEG2000, 41=PNG).</summary>
        public int PackingTemplateNumber { get; set; }

        /// <summary>Reference value R (IEEE 754 float from Section 5).</summary>
        public float ReferenceValue { get; set; }

        /// <summary>Binary scale factor E (Section 5).</summary>
        public short BinaryScaleFactor { get; set; }

        /// <summary>Decimal scale factor D (Section 5).</summary>
        public short DecimalScaleFactor { get; set; }

        /// <summary>Number of bits per packed value.</summary>
        public byte BitsPerValue { get; set; }

        // --- Bitmap (from Section 6) ---

        /// <summary>True if a bitmap is present (Section 6 indicator = 0).</summary>
        public bool HasBitmap { get; set; }

        /// <summary>Bitmap array — true where data is present, false where missing. Null if no bitmap.</summary>
        public bool[]? Bitmap { get; set; }

        // --- Unpacked data values ---

        /// <summary>
        /// Unpacked floating-point grid values in grid-point order.
        /// NaN indicates a missing value (either via bitmap or all-bits-set convention).
        /// Length = Ni × Nj for full grids, or NumberOfDataPoints for sparse grids.
        /// </summary>
        public float[] Values { get; set; } = Array.Empty<float>();

        /// <summary>Number of valid (non-NaN) data points in Values.</summary>
        public int ValidPointCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Values.Length; i++)
                    if (!float.IsNaN(Values[i]))
                        count++;
                return count;
            }
        }

        /// <summary>Minimum value in the field (excluding NaN).</summary>
        public float MinValue
        {
            get
            {
                float min = float.MaxValue;
                for (int i = 0; i < Values.Length; i++)
                    if (!float.IsNaN(Values[i]) && Values[i] < min)
                        min = Values[i];
                return min == float.MaxValue ? float.NaN : min;
            }
        }

        /// <summary>Maximum value in the field (excluding NaN).</summary>
        public float MaxValue
        {
            get
            {
                float max = float.MinValue;
                for (int i = 0; i < Values.Length; i++)
                    if (!float.IsNaN(Values[i]) && Values[i] > max)
                        max = Values[i];
                return max == float.MinValue ? float.NaN : max;
            }
        }

        public override string ToString()
            => $"{ParameterShortName ?? ParameterKey} [{ParameterUnit}] " +
               $"@ {SurfaceTypeName} {SurfaceValue} | " +
               $"FH={ForecastHour}h | Packing={PackingTemplateNumber} | " +
               $"Points={Values.Length} | Range=[{MinValue:F2}, {MaxValue:F2}]";
    }
}
