# Grib2 Library — GRIB2 Decoder with ILGPU Compute

A new `Grib2` class library project targeting `net10.0`, providing pure C# GRIB2 binary decoding and ILGPU-accelerated forecast computation. The decoder parses GRIB2 sections 0–8 with support for all common data representation templates (Simple Packing 5.0, Complex Packing 5.2/5.3, PNG 5.40, JPEG2000 5.41). ILGPU kernels handle grid interpolation, derived-field computation (e.g., wind chill, humidex, apparent temperature), and spatial aggregation on the GPU. Output integrates with the existing `OpenMeteo.WeatherForecast` model via the `Hourly` pressure-level arrays.

## Steps

1. **Create grib2/Grib2.csproj** — SDK-style class library, `net10.0`, `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (needed for ILGPU memory views). Add NuGet references: `ILGPU` and `ILGPU.Algorithms`. Add project reference to `OpenMeteo` (for output types) and `WeatherShared` if alert-related forecast data is needed.

2. **Register in WSG.sln** — Add the `grib2\Grib2.csproj` with a new project GUID. Add build configurations for Debug/Release × AnyCPU/x64/x86 matching existing projects.

3. **Create core directory structure:**
   ```
   grib2/
   ├── Grib2.csproj
   ├── README.md
   ├── Decoder/           ← Pure C# binary GRIB2 parsing
   │   ├── Grib2Reader.cs
   │   ├── BitReader.cs
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
   ├── Templates/         ← Grid & packing template implementations
   │   ├── Grid/
   │   │   ├── LatLonGridTemplate.cs         (Template 3.0)
   │   │   └── RotatedLatLonGridTemplate.cs  (Template 3.1)
   │   └── Packing/
   │       ├── SimplePackingTemplate.cs      (Template 5.0)
   │       ├── ComplexPackingTemplate.cs     (Template 5.2)
   │       ├── ComplexSpatialPackingTemplate.cs (Template 5.3)
   │       ├── PngPackingTemplate.cs         (Template 5.40)
   │       └── Jpeg2000PackingTemplate.cs    (Template 5.41)
   ├── Models/            ← Decoded data structures
   │   ├── Grib2Message.cs
   │   ├── Grib2Grid.cs
   │   ├── Grib2Field.cs
   │   ├── Grib2Metadata.cs
   │   └── ParameterTable.cs   (WMO code table 4.2 mappings)
   ├── Compute/           ← ILGPU GPU-accelerated computation
   │   ├── GpuContext.cs
   │   ├── Kernels/
   │   │   ├── InterpolationKernels.cs   (bilinear/bicubic grid interpolation)
   │   │   ├── DerivedFieldKernels.cs    (wind chill, humidex, apparent temp, etc.)
   │   │   ├── AggregationKernels.cs     (spatial avg, min, max over regions)
   │   │   └── UnitConversionKernels.cs  (K→°C, Pa→hPa, m/s→km/h, etc.)
   │   └── ForecastComputer.cs           (orchestrates kernel dispatch)
   └── Integration/       ← Bridge to existing weather model
       └── Grib2ToOpenMeteoConverter.cs  (decoded fields → WeatherForecast/Hourly)
   ```

4. **Create grib2/Decoder/Grib2Reader.cs** — Entry point class. Accepts `ReadOnlyMemory<byte>` or `Stream`. Parses the binary structure by scanning for the `GRIB` magic bytes (ASCII `0x47524942`), reading each section sequentially, and yielding `Grib2Message` objects. Supports multi-message files (GRIB2 files can contain multiple messages concatenated).

5. **Create grib2/Decoder/BitReader.cs** — Low-level bit-stream reader for unpacking arbitrary-width fields from the GRIB2 binary. GRIB2 uses non-byte-aligned integers frequently (e.g., 12-bit packed values). Must support signed/unsigned reads, IEEE 754 floats in the GRIB2 format (which uses a custom representation: `value = R × 10^(-D) × 2^(-E)`), and the scaled-value representation `V = (R + X × 2^E) × 10^(-D)`.

6. **Create Section parsers** in grib2/Decoder/Sections/ — Each section class reads its binary layout per the WMO GRIB2 spec:
   - `IndicatorSection` (Section 0): Discipline code, edition number, total message length
   - `IdentificationSection` (Section 1): Originating center (54 = CMC/ECCC), reference time, production status, data type
   - `GridDefinitionSection` (Section 3): Grid template number, Ni×Nj dimensions, lat/lon bounds, resolution
   - `ProductDefinitionSection` (Section 4): Parameter category/number (maps to TMP, UGRD, VGRD, etc.), forecast time, surface level
   - `DataRepresentationSection` (Section 5): Packing template number, reference value, binary/decimal scale factors, bit depth
   - `BitmapSection` (Section 6): Presence bitmap for sparse grids
   - `DataSection` (Section 7): Raw packed data bytes (delegated to packing templates for unpacking)

7. **Create packing templates** in grib2/Templates/Packing/ — Each template unpacks Section 7 data bytes into `float[]` grid values:
   - **Simple Packing (5.0)**: Direct `Y = R + X × 2^E × 10^(-D)` for each grid point
   - **Complex Packing (5.2)**: Group-based packing with variable group widths, lengths, and reference values
   - **Complex Spatial Differencing (5.3)**: 5.2 + first/second-order spatial differencing for improved compression
   - **PNG (5.40)**: Decompress PNG payload → raw integers → apply scale formula
   - **JPEG2000 (5.41)**: Decompress JPEG2000 payload → raw integers → apply scale formula (will need `OpenJpeg` or managed J2K decoder — suggest NuGet `CSJ2K` or `System.Drawing` fallback)

8. **Create grib2/Models/ParameterTable.cs** — WMO Code Table 4.2 mapping: `(discipline, category, number)` → human-readable name + unit. Essential mappings: `(0,0,0)` = Temperature [K], `(0,2,2)` = U-component wind [m/s], `(0,2,3)` = V-component wind [m/s], `(0,1,1)` = Relative Humidity [%], `(0,3,0)` = Pressure [Pa], `(0,1,8)` = Total Precipitation [kg/m²], `(0,6,1)` = Total Cloud Cover [%], etc.

9. **Create grib2/Compute/GpuContext.cs** — ILGPU device management. Initialize `Context`, select best `Accelerator` (prefer CUDA → OpenCL → CPU fallback). Expose `Accelerator` for kernel compilation. Implement `IDisposable` for clean GPU resource teardown. Singleton/pooled pattern so multiple forecast computations share one GPU context.

10. **Create ILGPU kernels** in grib2/Compute/Kernels/:
    - `InterpolationKernels`: Bilinear interpolation kernel — given a source grid (Ni×Nj) and target coordinates, compute interpolated values on GPU. Used for extracting point forecasts from gridded GRIB2 data and regridding between model resolutions.
    - `DerivedFieldKernels`: Element-wise kernels computing wind chill (from T + V), humidex (from T + Td), apparent temperature, wind speed/direction from U/V components (`speed = sqrt(u² + v²)`, `dir = atan2(-u, -v)`), dew point from T + RH.
    - `AggregationKernels`: Parallel reduction kernels for spatial min/max/mean over rectangular sub-grids or radius-based regions.
    - `UnitConversionKernels`: Bulk element-wise conversions: K→°C, Pa→hPa, m/s→km/h. These are simple but benefit from GPU parallelism on large grids (GDPS = 1800×901 = 1.6M points).

11. **Create grib2/Compute/ForecastComputer.cs** — Orchestration layer. Takes decoded `Grib2Field[]` (from the decoder), uploads grid data to GPU, dispatches kernel sequences (unit conversion → derived fields → interpolation → aggregation), downloads results. Provides high-level methods like `ComputePointForecast(lat, lon, fields)` and `ComputeGridDerivedFields(fields)`.

12. **Create grib2/Integration/Grib2ToOpenMeteoConverter.cs** — Maps decoded+computed GRIB2 fields to `OpenMeteo.WeatherForecast`. Populates `Hourly` arrays: `Temperature_2m`, `Windspeed_10m`, `Winddirection_10m`, `Pressure_msl`, `Relativehumidity_2m`, `Precipitation`, `Snowfall`, `Cloudcover`, and the pressure-level arrays (already defined in OpenMeteo/Hourly.cs) for upper-air data. Maps ECCC model runs (00Z/06Z/12Z/18Z) to hourly time steps.

13. **Create grib2/README.md** — Document the project purpose, supported GRIB2 templates, GPU requirements, usage examples, and ECCC Datamart URL patterns for common model outputs (GDPS, RDPS, HRDPS).

14. **Add project reference** from ECCC/ECCC.csproj to grib2/Grib2.csproj — so the existing `FetchDataAsync` → `EcccDataType.Datamart` pipeline can decode fetched GRIB2 bytes.

## Verification

- Build the solution: `dotnet build WSG.sln` should compile the new `Grib2` project with zero errors
- Unit test: Download a small GRIB2 file from `https://dd.weather.gc.ca/model_gem_global/25km/grib2/lat_lon/00/003/` and verify `Grib2Reader` parses all sections correctly
- GPU test: `GpuContext` should detect available accelerator and print device name; run a trivial kernel (e.g., unit conversion on a 100-element array) to validate ILGPU pipeline
- Integration test: Parse a GDPS temperature GRIB2 file → extract a point forecast for a known city → compare against ECCC website values

## Decisions

- **ILGPU over Vulkan compute**: User chose ILGPU for C#-native GPU kernels with automatic CUDA/OpenCL/CPU fallback, rather than extending the existing Vulkan rendering pipeline with compute shaders
- **Pure C# decoder over eccodes/wgrib2**: Zero native dependencies, easier deployment, full control over parsing
- **AllowUnsafeBlocks**: Required for ILGPU `ArrayView<T>` and `MemoryBuffer` interop
- **JPEG2000 dependency**: Template 5.41 will need a managed J2K decoder (e.g., `CSJ2K` NuGet) — this is the only external dependency beyond ILGPU
- **Namespace**: `Grib2` (PascalCase) per user preference
- **Project structure**: Clear separation of concerns between decoding, GPU computation, and integration layers for maintainability and testability
- **Documentation**: Comprehensive README with usage instructions, supported templates, and ECCC data source patterns to assist future users and contributors
- **Implement into WSG**: The `Grib2` library will be integrated into the existing WSG architecture, allowing it to decode GRIB2 files fetched from ECCC Datamart and feed the data into the `OpenMeteo.WeatherForecast` model for use in image/video generation and alert systems.
- **Implement into the Interactive Map Radar with personal options**: The decoded GRIB2 data can also be used to enhance the interactive map radar feature by providing additional layers of forecast data (e.g., temperature, wind fields) that users can toggle on/off for a more comprehensive weather visualization experience.
