# GRIB2 Forecast Overlay — Integration Guide

## Overview

The GRIB2 Forecast Overlay integrates Environment and Climate Change Canada (ECCC) numerical weather prediction model data into the Interactive Weather Radar map. It downloads GRIB2-encoded forecast grids from the ECCC Datamart (`dd.weather.gc.ca`), decodes them with the in-house `Grib2` library, and rasterizes them as a semi-transparent overlay on the GPU-composited map.

## Architecture

```
┌──────────────────┐    ┌──────────────────┐    ┌────────────────────┐
│ WeatherMapControl │───▶│WeatherOverlay    │───▶│ Grib2DataService   │
│  (HUD controls)  │    │  Manager         │    │ (ECCC download +   │
│  BuildHudPanels() │    │ UpdateGrib2Async │    │  parse + cache)    │
│  UpdateOverlays() │    │                  │    └────────────────────┘
│  Save/LoadSettings│    │                  │             │
└──────────────────┘    │                  │    ┌────────────────────┐
         │               │                  │───▶│Grib2OverlayRenderer│
         ▼               └──────────────────┘    │ (rasterize to PNG) │
┌──────────────────┐                              └────────────────────┘
│  IMapRenderer    │                                        │
│ Overlay Slot 3   │◀───────────────── PNG bytes ───────────┘
│ (GL/Vk/DX)      │
└──────────────────┘
```

## Supported Models

| Model | Resolution | Coverage | Forecast Range | Runs |
|-------|-----------|----------|---------------|------|
| **GDPS** | 15 km | Global | 0–240 h (3-hourly) | 00Z, 12Z |
| **HRDPS** | 2.5 km | Continental Canada | 0–48 h (hourly) | 00Z, 06Z, 12Z, 18Z |

## Supported Field Types

| Field | Parameter | Units (display) | Color Palette |
|-------|-----------|-----------------|---------------|
| Temperature | TMP_TGL_2 (2m) | °C | Blue → Cyan → Green → Yellow → Red |
| Wind | UGRD/VGRD_TGL_10 | km/h | Green → Yellow → Orange → Red → Purple |
| Precipitation | APCP_SFC_0 | mm/h | Blue → Green → Yellow → Orange → Red |
| Cloud Cover | TCDC_SFC_0 | % | White → Light grey → Medium grey → Dark grey |
| Pressure | PRMSL_MSL_0 | hPa | Green → Yellow → Orange → Red (with isobar contours) |
| CAPE | CAPE_SFC_0 | J/kg | Yellow → Orange → Red → Magenta → Purple |

## Files Added / Modified

### New Files

| File | Purpose |
|------|---------|
| `WeatherImageGenerator/Models/Grib2FieldType.cs` | `Grib2FieldType` and `Grib2ModelSource` enums |
| `Grib2/Integration/Grib2ColorPalette.cs` | Color stop arrays and `GetColor()` interpolation for 6 field types |
| `WeatherImageGenerator/Services/Grib2DataService.cs` | ECCC Datamart downloader with disk+memory cache, run detection |
| `WeatherImageGenerator/Services/Grib2OverlayRenderer.cs` | Rasterizes GRIB2 grids → transparent PNG (heatmap, wind barbs, isobars, labels) |

### Modified Files

| File | Changes |
|------|---------|
| `WeatherImageGenerator/Rendering/Common/IMapRenderer.cs` | Added `Overlay3Opacity`, `SetOverlay3Bytes()`, `ClearPositionedOverlay3()` |
| `WeatherImageGenerator/Rendering/OpenGL/GLRadarControl.cs` | Overlay3 fields, property, paint loop, texture upload methods |
| `WeatherImageGenerator/Rendering/Vulkan/VulkanMapRenderer.cs` | Overlay3 fields, property, render call, upload methods |
| `WeatherImageGenerator/Rendering/DirectX/DXMapRenderer.cs` | Overlay3 fields, property, render call, upload methods |
| `WeatherImageGenerator/Rendering/Common/WeatherOverlayManager.cs` | GRIB2 fields/properties, `UpdateGrib2OverlayAsync()`, `InvalidateGrib2Cache()` |
| `WeatherImageGenerator/Rendering/Common/WeatherMapControl.cs` | GRIB2 HUD panel (checkbox, dropdowns, sliders), `UpdateOverlays()` wiring, Save/Load |
| `WeatherImageGenerator/Services/ConfigManager.cs` | `WeatherMapViewSettings` extended with 8 GRIB2 properties |

## HUD Controls

The GRIB2 controls appear in the **Overlays** panel under a separator after the Temperature section:

- **GRIB2 Forecast** — enable/disable checkbox
- **Field** — dropdown: Temperature, Wind, Precipitation, Cloud Cover, Pressure, CAPE
- **Model** — dropdown: GDPS (15 km), HRDPS (2.5 km)
- **Forecast Opacity** — slider 0–100%
- **Forecast Hour** — slider 0–240 (GDPS) or 0–48 (HRDPS)
- **Show Labels** — value badges on the map
- **Wind Barbs** — directional arrows (Wind mode only)
- **Isobars** — contour lines (Pressure mode only)

## Data Flow

1. User enables "GRIB2 Forecast" checkbox and selects field/model/hour
2. `WeatherMapControl.UpdateOverlays()` sets `_overlayManager.Grib2Enabled = true` and calls `UpdateGrib2OverlayAsync()`
3. `WeatherOverlayManager` lazy-inits `Grib2DataService`, calls `SetModelAsync()` to detect latest run
4. `Grib2DataService.FetchFieldAsync()` checks memory cache → disk cache → downloads from ECCC Datamart
5. Downloaded `.grib2` bytes are parsed by `Grib2Reader` → returns `Grib2Message` (which contains `.Grid` and `.Field`)
6. `Grib2OverlayRenderer.RenderOverlay()` rasterizes the grid values into a transparent PNG using bilinear interpolation
7. PNG bytes flow back to `WeatherMapControl`, which calls `_glControl.SetOverlay3Bytes(data, bbox...)` 
8. The GPU compositor alpha-blends Overlay Slot 3 on top of Slots 1 (Radar) and 2 (Temperature)

## Caching

- **Memory cache**: `ConcurrentDictionary<string, CachedGrib2Message>` — keyed by `{model}_{date}_{run}_{field}_{fh}`
- **Disk cache**: `MapCache/Grib2/` directory — raw `.grib2` files with configurable expiry (default 6 hours)
- **Overlay cache**: `WeatherOverlayManager._grib2Overlay` — invalidated on position/zoom/field/hour change

## Overlay Slot Architecture

| Slot | Layer | Source |
|------|-------|--------|
| 1 | Radar (WMS) | ECCC GeoMet / NWS |
| 2 | Temperature Grid | OpenMeteo API |
| **3** | **GRIB2 Forecast** | **ECCC Datamart** |

All slots are GPU-composited with independent opacity via the renderer's alpha blending pipeline.

## Settings Persistence

All GRIB2 settings are saved/loaded via `ConfigManager.WeatherMapViewSettings`:

```json
{
  "Grib2Enabled": false,
  "Grib2FieldTypeIndex": 0,
  "Grib2ModelIndex": 0,
  "Grib2Opacity": 60,
  "Grib2ForecastHour": 0,
  "Grib2ShowLabels": true,
  "Grib2ShowWindBarbs": true,
  "Grib2ShowIsobars": false
}
```
