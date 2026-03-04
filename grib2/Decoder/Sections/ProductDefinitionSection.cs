#nullable enable
using System;
using Grib2.Models;

namespace Grib2.Decoder.Sections
{
    /// <summary>
    /// Section 4 — Product Definition Section.
    /// Defines what the data represents: parameter, forecast time, surface/level.
    /// Layout:
    ///   Octets 1–4:  Section length
    ///   Octet 5:     Section number (4)
    ///   Octets 6–7:  Number of coordinate values after template
    ///   Octets 8–9:  Product definition template number
    ///   Octets 10+:  Product definition template data
    /// </summary>
    public static class ProductDefinitionSection
    {
        /// <summary>Minimum section header length before template data.</summary>
        public const int MinHeaderLength = 9;

        /// <summary>
        /// Parse Section 4 and populate the field object.
        /// </summary>
        /// <param name="data">Section data span.</param>
        /// <param name="field">Field object to populate.</param>
        /// <param name="discipline">Discipline from Section 0 (needed for parameter lookup).</param>
        /// <returns>Total section length in bytes.</returns>
        public static int Parse(ReadOnlySpan<byte> data, Grib2Field field, byte discipline)
        {
            if (data.Length < MinHeaderLength)
                throw new InvalidOperationException($"Section 4 requires at least {MinHeaderLength} bytes");

            int sectionLength = (int)data.ReadUInt32BE(0);
            byte sectionNumber = data[4];

            if (sectionNumber != 4)
                throw new InvalidOperationException($"Expected section 4, got section {sectionNumber}");

            // ushort coordinateValues = data.ReadUInt16BE(5);
            field.ProductTemplateNumber = data.ReadUInt16BE(7);
            field.Discipline = discipline;

            // Parse template data
            ReadOnlySpan<byte> templateData = data.Slice(9);
            switch (field.ProductTemplateNumber)
            {
                case 0: // Analysis or forecast at a horizontal level/layer at a point in time
                case 1: // Individual ensemble forecast
                case 2: // Derived forecasts based on all ensemble members
                case 8: // Average, accumulation, extreme values at a point in time
                case 11: // Individual ensemble forecast with temporal extent
                case 12: // Derived forecasts based on all ensemble members with temporal extent
                    ParseTemplate0(templateData, field);
                    break;
                default:
                    // Try to parse common fields at the same offsets
                    TryParseCommonFields(templateData, field);
                    break;
            }

            // Resolve parameter name from table
            var paramInfo = ParameterTable.TryGet(field.Discipline, field.ParameterCategory, field.ParameterNumber);
            if (paramInfo.HasValue)
            {
                field.ParameterName = paramInfo.Value.Name;
                field.ParameterShortName = paramInfo.Value.ShortName;
                field.ParameterUnit = paramInfo.Value.Unit;
            }

            return sectionLength;
        }

        /// <summary>
        /// Parse Product Definition Template 4.0 (and variants 4.1, 4.2, 4.8, etc. that share the same base layout).
        /// Template 4.0 layout (0-indexed from template start):
        ///   Octet 0:     Parameter category
        ///   Octet 1:     Parameter number
        ///   Octet 2:     Type of generating process
        ///   Octet 3:     Background generating process identifier
        ///   Octet 4:     Analysis or forecast generating process identifier
        ///   Octets 5–6:  Hours of observational data cutoff after reference time
        ///   Octet 7:     Minutes of observational data cutoff
        ///   Octet 8:     Indicator of unit of time range (Code Table 4.4)
        ///   Octets 9–12: Forecast time in units defined by octet 8
        ///   Octet 13:    Type of first fixed surface (Code Table 4.5)
        ///   Octet 14:    Scale factor of first fixed surface
        ///   Octets 15–18: Scaled value of first fixed surface
        ///   Octet 19:    Type of second fixed surface
        ///   Octet 20:    Scale factor of second fixed surface
        ///   Octets 21–24: Scaled value of second fixed surface
        /// </summary>
        private static void ParseTemplate0(ReadOnlySpan<byte> template, Grib2Field field)
        {
            if (template.Length < 25)
                return;

            field.ParameterCategory = template[0];
            field.ParameterNumber = template[1];
            field.GeneratingProcess = template[2];
            // template[3] = background process, template[4] = generating process identifier

            field.ForecastTimeUnit = template[8];

            // Forecast time — convert to hours based on unit indicator
            int rawForecastTime = (int)template.ReadUInt32BE(9);
            field.ForecastHour = ConvertToHours(rawForecastTime, field.ForecastTimeUnit);

            // First fixed surface
            field.SurfaceType = template[13];
            field.SurfaceScaleFactor = template[14];
            int scaledSurfaceValue = (int)template.ReadUInt32BE(15);
            field.SurfaceValue = field.SurfaceScaleFactor == 0
                ? scaledSurfaceValue
                : scaledSurfaceValue / Math.Pow(10, field.SurfaceScaleFactor);

            // Second fixed surface
            field.SurfaceType2 = template[19];
            byte scaleFactor2 = template[20];
            int scaledValue2 = (int)template.ReadUInt32BE(21);
            field.SurfaceValue2 = scaleFactor2 == 0
                ? scaledValue2
                : scaledValue2 / Math.Pow(10, scaleFactor2);
        }

        /// <summary>
        /// Fallback: try to parse parameter category/number from the first 2 bytes of any template.
        /// Most product definition templates start with category + number.
        /// </summary>
        private static void TryParseCommonFields(ReadOnlySpan<byte> template, Grib2Field field)
        {
            if (template.Length >= 2)
            {
                field.ParameterCategory = template[0];
                field.ParameterNumber = template[1];
            }

            if (template.Length >= 25)
            {
                field.GeneratingProcess = template[2];
                field.ForecastTimeUnit = template[8];
                int rawForecastTime = (int)template.ReadUInt32BE(9);
                field.ForecastHour = ConvertToHours(rawForecastTime, field.ForecastTimeUnit);
                field.SurfaceType = template[13];
                field.SurfaceScaleFactor = template[14];
                int scaledSurfaceValue = (int)template.ReadUInt32BE(15);
                field.SurfaceValue = field.SurfaceScaleFactor == 0
                    ? scaledSurfaceValue
                    : scaledSurfaceValue / Math.Pow(10, field.SurfaceScaleFactor);
            }
        }

        /// <summary>
        /// Convert a forecast time value to hours based on the unit indicator (Code Table 4.4).
        /// </summary>
        private static int ConvertToHours(int value, byte unit) => unit switch
        {
            0 => value / 60,       // Minutes → hours
            1 => value,            // Hours
            2 => value * 24,       // Days → hours
            3 => value * 720,      // Months → hours (approximate 30 days)
            10 => value * 3,       // 3-hour periods
            11 => value * 6,       // 6-hour periods
            12 => value * 12,      // 12-hour periods
            13 => value / 3600,    // Seconds → hours
            _ => value             // Assume hours for unknown units
        };
    }
}
