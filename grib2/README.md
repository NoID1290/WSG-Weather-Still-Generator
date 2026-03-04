# Grib2 — GRIB2 Decoder with ILGPU Compute

A pure C# GRIB2 binary decoder with ILGPU-accelerated forecast computation, targeting `net10.0`.

## Features

- **Pure C# GRIB2 Decoder** — Zero native dependencies, parses sections 0–8 per WMO spec
- **Packing Templates** — Simple (5.0), Complex (5.2), Complex+Spatial Differencing (5.3), PNG (5.40), JPEG2000 (5.41 stub)
- **Grid Templates** — Regular lat/lon (3.0), Rotated lat/lon (3.1)
- **GPU-Accelerated Compute** — ILGPU kernels for interpolation, derived fields, unit conversion, and aggregation
- **OpenMeteo Integration** — Direct mapping from GRIB2 fields to `OpenMeteo.WeatherForecast` / `Hourly` model
- **WMO Parameter Table** — 100+ parameters mapped from `(discipline, category, number)` to human-readable names

## Project Structure

```
Grib2/
├── Grib2.csproj
├── README.md
├── Decoder/
│   ├── Grib2Reader.cs            ← Entry point: parse GRIB2 bytes → messages
│   ├── BitReader.cs              ← Low-level bit-stream reader
│   └── Sections/
│       ├── IndicatorSection.cs      (Section 0)
│       ├── IdentificationSection.cs (Section 1)
│       ├── LocalUseSection.cs       (Section 2)
│       ├── GridDefinitionSection.cs (Section 3)
│       ├── ProductDefinitionSection.cs (Section 4)
│       ├── DataRepresentationSection.cs (Section 5)
│       ├── BitmapSection.cs         (Section 6)
│       ├── DataSection.cs           (Section 7)
│       └── EndSection.cs            (Section 8)
├── Templates/
│   ├── Grid/
│   │   ├── LatLonGridTemplate.cs         (3.0)
│   │   └── RotatedLatLonGridTemplate.cs  (3.1)
│   └── Packing/
│       ├── SimplePackingTemplate.cs      (5.0)
│       ├── ComplexPackingTemplate.cs     (5.2)
│       ├── ComplexSpatialPackingTemplate.cs (5.3)
│       ├── PngPackingTemplate.cs         (5.40)
│       └── Jpeg2000PackingTemplate.cs    (5.41 — stub)
├── Models/
│   ├── Grib2Message.cs
│   ├── Grib2Grid.cs
│   ├── Grib2Field.cs
│   ├── Grib2Metadata.cs
│   └── ParameterTable.cs
├── Compute/
│   ├── GpuContext.cs                ← ILGPU singleton device manager
│   ├── ForecastComputer.cs          ← High-level kernel orchestration
│   └── Kernels/
│       ├── InterpolationKernels.cs   (bilinear, nearest-neighbor)
│       ├── DerivedFieldKernels.cs    (wind speed/dir, wind chill, humidex, etc.)
│       ├── AggregationKernels.cs     (spatial min/max/mean)
│       └── UnitConversionKernels.cs  (K→°C, Pa→hPa, m/s→km/h, etc.)
└── Integration/
    └── Grib2ToOpenMeteoConverter.cs  ← GRIB2 → WeatherForecast/Hourly mapping
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| ILGPU | 1.5.1 | GPU compute (CUDA/OpenCL/CPU) |
| ILGPU.Algorithms | 1.5.1 | Math intrinsics for kernels |
| OpenMeteo (project) | — | Output model types |
| OpenMap (project) | — | SkiaSharp transitive dependency for PNG template |

## Usage

### Decode a GRIB2 File

```csharp
using Grib2.Decoder;

byte[] data = File.ReadAllBytes("CMC_glb_TMP_ISBL_500_latlon.24x.24_2024030100_P003.grib2");
var reader = new Grib2Reader(data);

foreach (var message in reader.ReadMessages())
{
    Console.WriteLine($"Parameter: {message.Field.ParameterName}");
    Console.WriteLine($"Unit: {message.Field.ParameterUnit}");
    Console.WriteLine($"Grid: {message.Grid.Ni}x{message.Grid.Nj}");
    Console.WriteLine($"Forecast hour: {message.Field.ForecastTimeHours}");
    Console.WriteLine($"Values: {message.Field.ValidPointCount} points");
}
```

### GPU-Accelerated Computation

```csharp
using Grib2.Compute;

using var computer = new ForecastComputer();

// Unit conversion
float[] tempCelsius = computer.KelvinToCelsius(tempKelvin);
float[] pressureHpa = computer.PascalToHpa(pressurePa);
float[] windKmh = computer.MsToKmh(windMs);

// Derived fields
float[] windSpeed = computer.ComputeWindSpeed(uWind, vWind);
float[] windDir = computer.ComputeWindDirection(uWind, vWind);
float[] windChill = computer.ComputeWindChill(tempCelsius, windKmh);

// Point forecast
var forecast = computer.ComputePointForecast(fields, grid, lat: 45.5f, lon: -73.6f);
Console.WriteLine($"Temperature: {forecast["temperature_2m"]:F1}°C");
```

### Convert to OpenMeteo Format

```csharp
using Grib2.Integration;

var converter = new Grib2ToOpenMeteoConverter();
var forecast = converter.Convert(messages, lat: 45.5f, lon: -73.6f, elevation: 36f);

// Access hourly data
Console.WriteLine($"Temperature 2m: {forecast.Hourly?.Temperature_2m?[0]}°C");
Console.WriteLine($"Wind: {forecast.Hourly?.Windspeed_10m?[0]} km/h");
```

## ECCC Datamart URL Patterns

Common GRIB2 file sources from Environment and Climate Change Canada:

### GDPS (Global Deterministic Prediction System) — 25km
```
https://dd.weather.gc.ca/model_gem_global/25km/grib2/lat_lon/{HH}/{FFF}/
  CMC_glb_{PARAM}_{LEVEL}_latlon.24x.24_{YYYYMMDD}{HH}_P{FFF}.grib2
```

### RDPS (Regional Deterministic Prediction System) — 10km
```
https://dd.weather.gc.ca/model_gem_regional/10km/grib2/{HH}/{FFF}/
  CMC_reg_{PARAM}_{LEVEL}_ps10km_{YYYYMMDD}{HH}_P{FFF}.grib2
```

### HRDPS (High Resolution Deterministic Prediction System) — 2.5km
```
https://dd.weather.gc.ca/model_hrdps/continental/2.5km/{HH}/{FFF}/
  CMC_hrdps_continental_{PARAM}_{LEVEL}_ps2.5km_{YYYYMMDD}{HH}_P{FFF}-00.grib2
```

Where:
- `{HH}` = Model run hour (00, 06, 12, 18)
- `{FFF}` = Forecast hour (000–240 for GDPS, 000–084 for RDPS, 000–048 for HRDPS)
- `{PARAM}` = Parameter short name (TMP, UGRD, VGRD, RH, PRES, APCP, TCDC, etc.)
- `{LEVEL}` = Level description (TGL_2m, ISBL_500, SFC_0, etc.)

## Supported GRIB2 Templates

### Data Representation (Packing)
| Template | Name | Status |
|----------|------|--------|
| 5.0 | Simple Packing | ✅ Full |
| 5.2 | Complex Packing | ✅ Full |
| 5.3 | Complex + Spatial Differencing | ✅ Full |
| 5.40 | PNG Packing | ✅ Full (via SkiaSharp) |
| 5.41 | JPEG2000 Packing | ⚠️ Stub (throws NotSupportedException) |

### Grid Definition
| Template | Name | Status |
|----------|------|--------|
| 3.0 | Regular Latitude/Longitude | ✅ Full |
| 3.1 | Rotated Latitude/Longitude | ✅ Full |

### Product Definition
| Template | Name | Status |
|----------|------|--------|
| 4.0 | Analysis/Forecast at a point in time | ✅ Full |
| 4.1 | Individual ensemble forecast | ✅ Full |
| 4.2 | Derived forecast based on all ensemble | ✅ Full |
| 4.8 | Average/accumulation over time interval | ✅ Full |
| 4.11 | Individual ensemble + time interval | ✅ Full |
| 4.12 | Derived forecast + time interval | ✅ Full |

## GPU Requirements

ILGPU automatically selects the best available accelerator:
1. **CUDA** — NVIDIA GPUs (best performance)
2. **OpenCL** — AMD/Intel GPUs
3. **CPU** — Fallback using CPU accelerator (always available)

No GPU drivers or SDKs are required at build time. The ILGPU CPU accelerator provides identical functionality for development and CI environments.

## License

Part of the WSG (Weather Still Generator) project.
