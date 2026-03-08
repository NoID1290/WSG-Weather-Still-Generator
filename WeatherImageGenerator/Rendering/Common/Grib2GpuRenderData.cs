using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// GPU-ready GRIB2 data package for shader-based weather visualization.
    /// Contains raw float grid(s), palette texels, and geo-positioning metadata.
    /// </summary>
    public sealed class Grib2GpuRenderData
    {
        /// <summary>Raw data grid as R32F float array (row-major).</summary>
        public required float[] GridData { get; init; }

        /// <summary>Grid width in texels.</summary>
        public required int GridWidth { get; init; }

        /// <summary>Grid height in texels.</summary>
        public required int GridHeight { get; init; }

        /// <summary>1D palette RGBA texture data (256 texels, 1024 bytes).</summary>
        public required byte[] PaletteData { get; init; }

        /// <summary>Minimum data value for normalization.</summary>
        public required float DataMin { get; init; }

        /// <summary>Maximum data value for normalization.</summary>
        public required float DataMax { get; init; }

        /// <summary>GRIB2 field type (Temperature, Wind, Precipitation, etc.)</summary>
        public required Grib2FieldType FieldType { get; init; }

        /// <summary>Bounding box for geo-positioning the overlay.</summary>
        public required double MinLat { get; init; }
        public required double MinLon { get; init; }
        public required double MaxLat { get; init; }
        public required double MaxLon { get; init; }

        /// <summary>Wind U-component grid (only for Wind field type).</summary>
        public float[]? WindU { get; init; }

        /// <summary>Wind V-component grid (only for Wind field type).</summary>
        public float[]? WindV { get; init; }

        /// <summary>Overall opacity multiplier.</summary>
        public float Opacity { get; init; } = 1.0f;

        /// <summary>Enable glow effect for high-intensity regions.</summary>
        public bool EnableGlow { get; init; } = true;

        /// <summary>Enable contour lines overlay.</summary>
        public bool EnableContours { get; init; } = false;

        /// <summary>Contour line interval in data units.</summary>
        public float ContourInterval { get; init; } = 4.0f;
    }
}
