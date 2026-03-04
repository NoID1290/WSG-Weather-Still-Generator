#nullable enable
using System;
using System.Collections.Generic;

namespace Grib2.Models
{
    /// <summary>
    /// WMO Code Table 4.2 parameter mapping: (discipline, category, number) → name + unit.
    /// Covers all essential meteorological parameters used by ECCC GDPS/RDPS/HRDPS models.
    /// </summary>
    public static class ParameterTable
    {
        /// <summary>
        /// Information about a GRIB2 parameter.
        /// </summary>
        public readonly record struct ParameterInfo(string Name, string ShortName, string Unit);

        private static readonly Dictionary<(byte Discipline, byte Category, byte Number), ParameterInfo> _table = new()
        {
            // Discipline 0: Meteorological
            // Category 0: Temperature
            [(0, 0, 0)] = new("Temperature", "TMP", "K"),
            [(0, 0, 1)] = new("Virtual Temperature", "VTMP", "K"),
            [(0, 0, 2)] = new("Potential Temperature", "POT", "K"),
            [(0, 0, 3)] = new("Pseudo-Adiabatic Potential Temperature", "EPOT", "K"),
            [(0, 0, 4)] = new("Maximum Temperature", "TMAX", "K"),
            [(0, 0, 5)] = new("Minimum Temperature", "TMIN", "K"),
            [(0, 0, 6)] = new("Dew Point Temperature", "DPT", "K"),
            [(0, 0, 7)] = new("Dew Point Depression", "DEPR", "K"),
            [(0, 0, 8)] = new("Lapse Rate", "LAPR", "K/m"),
            [(0, 0, 9)] = new("Temperature Anomaly", "TMPA", "K"),
            [(0, 0, 10)] = new("Latent Heat Net Flux", "LHTFL", "W/m²"),
            [(0, 0, 11)] = new("Sensible Heat Net Flux", "SHTFL", "W/m²"),
            [(0, 0, 12)] = new("Heat Index", "HEATX", "K"),
            [(0, 0, 13)] = new("Wind Chill Factor", "WCF", "K"),
            [(0, 0, 15)] = new("Virtual Potential Temperature", "VPTMP", "K"),
            [(0, 0, 17)] = new("Skin Temperature", "SKINT", "K"),
            [(0, 0, 21)] = new("Apparent Temperature", "APTMP", "K"),

            // Category 1: Moisture
            [(0, 1, 0)] = new("Specific Humidity", "SPFH", "kg/kg"),
            [(0, 1, 1)] = new("Relative Humidity", "RH", "%"),
            [(0, 1, 2)] = new("Humidity Mixing Ratio", "MIXR", "kg/kg"),
            [(0, 1, 3)] = new("Precipitable Water", "PWAT", "kg/m²"),
            [(0, 1, 4)] = new("Vapor Pressure", "VAPP", "Pa"),
            [(0, 1, 5)] = new("Saturation Deficit", "SATD", "Pa"),
            [(0, 1, 6)] = new("Evaporation", "EVP", "kg/m²"),
            [(0, 1, 7)] = new("Precipitation Rate", "PRATE", "kg/m²/s"),
            [(0, 1, 8)] = new("Total Precipitation", "APCP", "kg/m²"),
            [(0, 1, 9)] = new("Large-Scale Precipitation", "NCPCP", "kg/m²"),
            [(0, 1, 10)] = new("Convective Precipitation", "ACPCP", "kg/m²"),
            [(0, 1, 11)] = new("Snow Depth", "SNOD", "m"),
            [(0, 1, 12)] = new("Snowfall Rate Water Equivalent", "SRWEQ", "kg/m²/s"),
            [(0, 1, 13)] = new("Water Equivalent of Accumulated Snow Depth", "WEASD", "kg/m²"),
            [(0, 1, 14)] = new("Convective Snow", "CSNOW", "kg/m²"),
            [(0, 1, 15)] = new("Large-Scale Snow", "LSSNOW", "kg/m²"),
            [(0, 1, 16)] = new("Snow Melt", "SNMLT", "kg/m²"),
            [(0, 1, 22)] = new("Cloud Mixing Ratio", "CLMR", "kg/kg"),
            [(0, 1, 24)] = new("Ice Water Mixing Ratio", "ICMR", "kg/kg"),
            [(0, 1, 49)] = new("Total Snowfall", "TSNOW", "m"),
            [(0, 1, 52)] = new("Categorical Snow", "CSNOW_CAT", "categorical"),
            [(0, 1, 53)] = new("Categorical Freezing Rain", "CFRZR", "categorical"),
            [(0, 1, 54)] = new("Categorical Ice Pellets", "CICEP", "categorical"),
            [(0, 1, 55)] = new("Categorical Rain", "CRAIN", "categorical"),

            // Category 2: Momentum (Wind)
            [(0, 2, 0)] = new("Wind Direction", "WDIR", "°"),
            [(0, 2, 1)] = new("Wind Speed", "WIND", "m/s"),
            [(0, 2, 2)] = new("U-Component of Wind", "UGRD", "m/s"),
            [(0, 2, 3)] = new("V-Component of Wind", "VGRD", "m/s"),
            [(0, 2, 4)] = new("Stream Function", "STRM", "m²/s"),
            [(0, 2, 5)] = new("Velocity Potential", "VPOT", "m²/s"),
            [(0, 2, 8)] = new("Vertical Velocity (Pressure)", "VVEL", "Pa/s"),
            [(0, 2, 9)] = new("Vertical Velocity (Geometric)", "DZDT", "m/s"),
            [(0, 2, 10)] = new("Absolute Vorticity", "ABSV", "1/s"),
            [(0, 2, 12)] = new("Relative Vorticity", "RELV", "1/s"),
            [(0, 2, 22)] = new("Wind Speed (Gust)", "GUST", "m/s"),
            [(0, 2, 33)] = new("Wind Fetch", "WFETCH", "m"),

            // Category 3: Mass (Pressure/Geopotential)
            [(0, 3, 0)] = new("Pressure", "PRES", "Pa"),
            [(0, 3, 1)] = new("Pressure Reduced to MSL", "PRMSL", "Pa"),
            [(0, 3, 2)] = new("Pressure Tendency", "PTEND", "Pa/s"),
            [(0, 3, 3)] = new("ICAO Standard Atmosphere Reference Height", "ICAHT", "m"),
            [(0, 3, 4)] = new("Geopotential", "GP", "m²/s²"),
            [(0, 3, 5)] = new("Geopotential Height", "HGT", "gpm"),
            [(0, 3, 6)] = new("Geometric Height", "DIST", "m"),
            [(0, 3, 9)] = new("Geopotential Height Anomaly", "GPA", "gpm"),

            // Category 4: Short-wave Radiation
            [(0, 4, 0)] = new("Net Short-Wave Radiation Flux (Surface)", "NSWRS", "W/m²"),
            [(0, 4, 1)] = new("Net Short-Wave Radiation Flux (Top)", "NSWRT", "W/m²"),
            [(0, 4, 7)] = new("Downward Short-Wave Radiation Flux", "DSWRF", "W/m²"),
            [(0, 4, 8)] = new("Upward Short-Wave Radiation Flux", "USWRF", "W/m²"),
            [(0, 4, 9)] = new("Net Short-Wave Radiation Flux", "NSWRF", "W/m²"),
            [(0, 4, 10)] = new("Direct Short-Wave Radiation Flux", "DIRSWRF", "W/m²"),
            [(0, 4, 11)] = new("Diffuse Short-Wave Radiation Flux", "DIFSWRF", "W/m²"),

            // Category 5: Long-wave Radiation
            [(0, 5, 0)] = new("Net Long-Wave Radiation Flux (Surface)", "NLWRS", "W/m²"),
            [(0, 5, 1)] = new("Net Long-Wave Radiation Flux (Top)", "NLWRT", "W/m²"),
            [(0, 5, 3)] = new("Downward Long-Wave Radiation Flux", "DLWRF", "W/m²"),
            [(0, 5, 4)] = new("Upward Long-Wave Radiation Flux", "ULWRF", "W/m²"),

            // Category 6: Cloud
            [(0, 6, 0)] = new("Cloud Ice", "CICE", "kg/m²"),
            [(0, 6, 1)] = new("Total Cloud Cover", "TCDC", "%"),
            [(0, 6, 2)] = new("Convective Cloud Cover", "CDCON", "%"),
            [(0, 6, 3)] = new("Low Cloud Cover", "LCDC", "%"),
            [(0, 6, 4)] = new("Medium Cloud Cover", "MCDC", "%"),
            [(0, 6, 5)] = new("High Cloud Cover", "HCDC", "%"),
            [(0, 6, 6)] = new("Cloud Water", "CWAT", "kg/m²"),
            [(0, 6, 11)] = new("Cloud Base", "CBASE", "m"),
            [(0, 6, 12)] = new("Cloud Top", "CTOP", "m"),

            // Category 7: Thermodynamic Stability
            [(0, 7, 0)] = new("Parcel Lifted Index", "PLI", "K"),
            [(0, 7, 6)] = new("Convective Available Potential Energy", "CAPE", "J/kg"),
            [(0, 7, 7)] = new("Convective Inhibition", "CIN", "J/kg"),
            [(0, 7, 8)] = new("Storm Relative Helicity", "HLCY", "m²/s²"),

            // Category 19: Physical Atmospheric Properties
            [(0, 19, 0)] = new("Visibility", "VIS", "m"),
            [(0, 19, 1)] = new("Albedo", "ALBDO", "%"),
            [(0, 19, 3)] = new("Icing Severity", "ICSEV", "categorical"),
            [(0, 19, 11)] = new("Thunder Probability", "TSTM", "%"),

            // Discipline 2: Land Surface
            // Category 0: Vegetation/Biomass
            [(2, 0, 0)] = new("Land Cover", "LAND", "proportion"),
            [(2, 0, 1)] = new("Surface Roughness", "SFCR", "m"),
            [(2, 0, 2)] = new("Soil Temperature", "TSOIL", "K"),
            [(2, 0, 3)] = new("Soil Moisture Content", "SOILM", "kg/m²"),
            [(2, 0, 4)] = new("Vegetation", "VEG", "%"),
            [(2, 0, 5)] = new("Water Runoff", "RUNOFF", "kg/m²"),
            [(2, 0, 7)] = new("Evaporation", "EVBS", "W/m²"),

            // Category 3: Soil
            [(2, 3, 0)] = new("Soil Type", "SOTYP", "code"),
            [(2, 3, 18)] = new("Soil Porosity", "SOILP", "proportion"),
            [(2, 3, 20)] = new("Volumetric Soil Moisture", "SOILL", "m³/m³"),

            // Discipline 10: Oceanographic
            // Category 0: Waves
            [(10, 0, 3)] = new("Significant Wave Height", "HTSGW", "m"),
            [(10, 0, 4)] = new("Wind Wave Direction", "WVDIR", "°"),
            [(10, 0, 5)] = new("Wind Wave Peak Period", "WVPER", "s"),
        };

        /// <summary>
        /// Look up a parameter by its discipline, category, and number.
        /// Returns null if the parameter is not in the table.
        /// </summary>
        public static ParameterInfo? TryGet(byte discipline, byte category, byte number)
        {
            return _table.TryGetValue((discipline, category, number), out var info) ? info : null;
        }

        /// <summary>
        /// Look up a parameter; returns a fallback with "Unknown" name if not found.
        /// </summary>
        public static ParameterInfo Get(byte discipline, byte category, byte number)
        {
            return _table.TryGetValue((discipline, category, number), out var info)
                ? info
                : new ParameterInfo($"Unknown ({discipline}.{category}.{number})", $"D{discipline}C{category}N{number}", "?");
        }

        /// <summary>
        /// Check if a parameter exists in the table.
        /// </summary>
        public static bool Contains(byte discipline, byte category, byte number)
            => _table.ContainsKey((discipline, category, number));

        /// <summary>
        /// Get all registered parameters.
        /// </summary>
        public static IReadOnlyDictionary<(byte Discipline, byte Category, byte Number), ParameterInfo> All => _table;
    }
}
