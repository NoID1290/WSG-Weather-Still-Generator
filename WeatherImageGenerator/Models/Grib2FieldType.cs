namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// GRIB2 forecast field types available for the interactive weather map overlay.
    /// Each field corresponds to specific WMO discipline/category/parameter codes.
    /// </summary>
    public enum Grib2FieldType
    {
        /// <summary>Temperature at 2m (WMO 0/0/0)</summary>
        Temperature,

        /// <summary>Wind speed computed from U/V at 10m (WMO 0/2/2 + 0/2/3)</summary>
        Wind,

        /// <summary>Total precipitation (WMO 0/1/52) or rain/snow rate</summary>
        Precipitation,

        /// <summary>Total cloud cover (WMO 0/6/1)</summary>
        CloudCover,

        /// <summary>Mean sea level pressure (WMO 0/3/0 or 0/3/1)</summary>
        Pressure,

        /// <summary>Convective Available Potential Energy (WMO 0/7/6)</summary>
        CAPE
    }

    /// <summary>
    /// GRIB2 model source to download from ECCC Datamart.
    /// </summary>
    public enum Grib2ModelSource
    {
        /// <summary>Global Deterministic Prediction System — 25 km, 240-hour forecast</summary>
        GDPS,

        /// <summary>High Resolution Deterministic Prediction System — 2.5 km, 48-hour forecast</summary>
        HRDPS
    }
}
