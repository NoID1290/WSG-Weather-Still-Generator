using System;
using Grib2.Models;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Prepares decoded GRIB2 grid data for GPU texture upload.
    /// Converts raw field values into R32F (single-channel float) texture data
    /// and computes the metadata uniforms needed by the grib2_data shader.
    /// 
    /// The actual GPU texture creation (glTexImage2D / vkCreateImage / CreateTexture2D)
    /// is performed by the rendering backend; this class only prepares the CPU-side data.
    /// </summary>
    public sealed class Grib2TextureUploader
    {
        private readonly Grib2ShaderPalette _palette;

        public Grib2TextureUploader(Grib2ShaderPalette palette)
        {
            _palette = palette;
        }

        /// <summary>
        /// Prepares a GRIB2 message's grid data for GPU upload.
        /// Returns all data needed by the rendering backend to create the texture and set uniforms.
        /// </summary>
        public Grib2GpuData? PrepareForUpload(Grib2Message message, Grib2FieldType fieldType)
        {
            var field = message?.Field;
            var grid = message?.Grid;
            if (field?.Values == null || field.Values.Length == 0 || grid == null)
                return null;

            if (grid.Ni <= 0 || grid.Nj <= 0)
                return null;

            string fieldName = fieldType.ToString();
            var (dataMin, dataMax) = Grib2ShaderPalette.GetNormalizationRange(fieldName);

            // Convert units on CPU so shader works in display units
            float[] displayValues = ConvertToDisplayUnits(field.Values, fieldType);

            // Replace NaN with a sentinel below min range so shader renders transparent
            float sentinel = dataMin - 1000f;
            for (int i = 0; i < displayValues.Length; i++)
            {
                if (float.IsNaN(displayValues[i]))
                    displayValues[i] = sentinel;
            }

            // Get palette texture
            byte[] paletteData = _palette.GetPalette(fieldName);

            return new Grib2GpuData
            {
                // Grid data as R32F
                GridData = displayValues,
                GridWidth = grid.Ni,
                GridHeight = grid.Nj,

                // Palette
                PaletteData = paletteData,
                PaletteWidth = Grib2ShaderPalette.PaletteWidth,

                // Normalization range for shader
                DataMin = dataMin,
                DataMax = dataMax,

                // Grid geographic bounds
                MinLat = Math.Min(grid.FirstLatitude, grid.LastLatitude),
                MaxLat = Math.Max(grid.FirstLatitude, grid.LastLatitude),
                MinLon = grid.FirstLongitude,
                MaxLon = grid.LastLongitude,

                // Grid scanning
                IDirectionPositive = grid.IDirectionPositive,
                JDirectionPositive = grid.JDirectionPositive,

                // Rotated grid support (HRDPS)
                IsRotated = grid.TemplateNumber == 1,
                RotatedPoleLat = grid.RotatedPoleLat ?? 0.0,
                RotatedPoleLon = grid.RotatedPoleLon ?? 0.0,
                RotationAngle = grid.RotationAngle ?? 0.0,

                // Metadata
                FieldType = fieldType,
                FieldName = fieldName,
            };
        }

        /// <summary>
        /// Prepares wind field data from U and V components.
        /// Returns speed grid + direction grid for the wind shader.
        /// </summary>
        public Grib2WindGpuData? PrepareWindUpload(Grib2Message uMessage, Grib2Message vMessage)
        {
            var uField = uMessage?.Field;
            var vField = vMessage?.Field;
            var grid = uMessage?.Grid;
            if (uField?.Values == null || vField?.Values == null || grid == null)
                return null;

            int len = Math.Min(uField.Values.Length, vField.Values.Length);
            float[] uData = new float[len];
            float[] vData = new float[len];
            float[] speedData = new float[len];

            for (int i = 0; i < len; i++)
            {
                float u = uField.Values[i];
                float v = vField.Values[i];
                uData[i] = u;
                vData[i] = v;
                speedData[i] = MathF.Sqrt(u * u + v * v) * 3.6f; // m/s → km/h
            }

            var (dataMin, dataMax) = Grib2ShaderPalette.GetNormalizationRange("Wind");
            byte[] paletteData = _palette.GetPalette("Wind");

            return new Grib2WindGpuData
            {
                UComponentData = uData,
                VComponentData = vData,
                SpeedData = speedData,
                GridWidth = grid.Ni,
                GridHeight = grid.Nj,

                PaletteData = paletteData,
                PaletteWidth = Grib2ShaderPalette.PaletteWidth,
                DataMin = dataMin,
                DataMax = dataMax,

                MinLat = Math.Min(grid.FirstLatitude, grid.LastLatitude),
                MaxLat = Math.Max(grid.FirstLatitude, grid.LastLatitude),
                MinLon = grid.FirstLongitude,
                MaxLon = grid.LastLongitude,

                IDirectionPositive = grid.IDirectionPositive,
                JDirectionPositive = grid.JDirectionPositive,
                IsRotated = grid.TemplateNumber == 1,
                RotatedPoleLat = grid.RotatedPoleLat ?? 0.0,
                RotatedPoleLon = grid.RotatedPoleLon ?? 0.0,
                RotationAngle = grid.RotationAngle ?? 0.0,
            };
        }

        private static float[] ConvertToDisplayUnits(float[] values, Grib2FieldType fieldType)
        {
            float[] result = new float[values.Length];

            switch (fieldType)
            {
                case Grib2FieldType.Temperature:
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] > 200 ? values[i] - 273.15f : values[i];
                    break;

                case Grib2FieldType.Wind:
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] * 3.6f;
                    break;

                case Grib2FieldType.Pressure:
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] > 10000 ? values[i] / 100f : values[i];
                    break;

                case Grib2FieldType.CloudCover:
                    for (int i = 0; i < values.Length; i++)
                        result[i] = values[i] <= 1 ? values[i] * 100f : values[i];
                    break;

                default:
                    Array.Copy(values, result, values.Length);
                    break;
            }

            return result;
        }
    }

    /// <summary>
    /// GPU-ready data for a single GRIB2 field layer.
    /// </summary>
    public sealed class Grib2GpuData
    {
        /// <summary>Flat grid data in display units, row-major (Nj rows × Ni columns). Upload as R32F texture.</summary>
        public float[] GridData = Array.Empty<float>();
        public int GridWidth;
        public int GridHeight;

        /// <summary>1D RGBA palette (256 texels × 4 bytes). Upload as RGBA8 1D texture.</summary>
        public byte[] PaletteData = Array.Empty<byte>();
        public int PaletteWidth;

        /// <summary>Normalization: shader computes t = (value - DataMin) / (DataMax - DataMin) to sample palette.</summary>
        public float DataMin;
        public float DataMax;

        /// <summary>Geographic bounding box of the grid.</summary>
        public double MinLat, MaxLat, MinLon, MaxLon;

        /// <summary>Grid scanning directions.</summary>
        public bool IDirectionPositive;
        public bool JDirectionPositive;

        /// <summary>Rotated grid parameters (HRDPS Template 3.1).</summary>
        public bool IsRotated;
        public double RotatedPoleLat, RotatedPoleLon, RotationAngle;

        /// <summary>Field metadata.</summary>
        public Grib2FieldType FieldType;
        public string FieldName = "";
    }

    /// <summary>
    /// GPU-ready data for wind field visualization (U/V components + derived speed).
    /// </summary>
    public sealed class Grib2WindGpuData
    {
        /// <summary>U wind component (m/s), upload as R32F texture.</summary>
        public float[] UComponentData = Array.Empty<float>();
        /// <summary>V wind component (m/s), upload as R32F texture.</summary>
        public float[] VComponentData = Array.Empty<float>();
        /// <summary>Wind speed (km/h), upload as R32F texture for palette lookup.</summary>
        public float[] SpeedData = Array.Empty<float>();

        public int GridWidth, GridHeight;
        public byte[] PaletteData = Array.Empty<byte>();
        public int PaletteWidth;
        public float DataMin, DataMax;
        public double MinLat, MaxLat, MinLon, MaxLon;
        public bool IDirectionPositive, JDirectionPositive;
        public bool IsRotated;
        public double RotatedPoleLat, RotatedPoleLon, RotationAngle;
    }
}
