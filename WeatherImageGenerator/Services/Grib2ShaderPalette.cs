using System;
using System.Collections.Concurrent;
using Grib2.Integration;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Manages 1D RGBA palette textures for GPU-based GRIB2 data color mapping.
    /// Caches palette textures per field type to avoid regeneration each frame.
    /// The palette is sampled in the fragment shader by normalizing a raw data value
    /// to [0,1] within the field's range and using it as a texture coordinate.
    /// </summary>
    public sealed class Grib2ShaderPalette : IDisposable
    {
        /// <summary>Palette texture width in texels. 256 gives smooth gradients with low memory.</summary>
        public const int PaletteWidth = 256;

        /// <summary>Bytes per palette = PaletteWidth × 4 (RGBA).</summary>
        public const int PaletteBytes = PaletteWidth * 4;

        private readonly ConcurrentDictionary<string, PaletteEntry> _cache = new();

        /// <summary>
        /// Gets or creates a cached RGBA palette texture for the given field type.
        /// </summary>
        /// <param name="fieldType">Field name (Temperature, Wind, Precipitation, CloudCover, Pressure, CAPE)</param>
        /// <param name="alpha">Alpha channel intensity (0–255). Default 200.</param>
        /// <returns>RGBA byte array (256×4 = 1024 bytes) suitable for GPU texture upload</returns>
        public byte[] GetPalette(string fieldType, int alpha = 200)
        {
            string key = $"{fieldType}_{alpha}";
            return _cache.GetOrAdd(key, _ => new PaletteEntry
            {
                Data = Grib2ColorPalette.GeneratePaletteTexture(fieldType, alpha),
                FieldType = fieldType,
                Alpha = alpha
            }).Data;
        }

        /// <summary>
        /// Gets the data normalization range for the given field type.
        /// Shader uses: normalizedValue = (rawValue - min) / (max - min)
        /// </summary>
        public static (float Min, float Max) GetNormalizationRange(string fieldType)
        {
            return Grib2ColorPalette.GetValueRange(fieldType);
        }

        /// <summary>
        /// Gets all palette textures for all field types at once (useful for pre-loading).
        /// </summary>
        public void PreloadAll(int alpha = 200)
        {
            foreach (var ft in Grib2ColorPalette.AllFieldTypes)
                GetPalette(ft, alpha);
        }

        /// <summary>
        /// Invalidates cached palettes (e.g., when alpha changes).
        /// </summary>
        public void ClearCache() => _cache.Clear();

        public void Dispose() => _cache.Clear();

        private sealed class PaletteEntry
        {
            public byte[] Data = Array.Empty<byte>();
            public string FieldType = "";
            public int Alpha;
        }
    }
}
