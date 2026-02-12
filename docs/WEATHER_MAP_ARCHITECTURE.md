# Weather Interactive Map - Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    WeatherMapControl (UI)                        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Control Panel (Right Side)                               │  │
│  │  • Zoom Controls (➕ ➖ 🎯)                               │  │
│  │  • Radar Toggle & Opacity Slider                          │  │
│  │  • Temperature Toggle & Opacity Slider                    │  │
│  │  • Refresh Button / Clear Cache                           │  │
│  │  • Status Display (Zoom, Position, Cache)                │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │             GLRadarControl (OpenGL Viewport)              │  │
│  │  • Tile Rendering Layer                                   │  │
│  │  • Weather Overlay Layer                                  │  │
│  │  • Pan & Zoom Controls                                    │  │
│  │  • Mouse Interaction                                      │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                                 │
                ┌────────────────┼────────────────┐
                │                │                │
                ▼                ▼                ▼
    ┌──────────────────┐ ┌────────────────┐ ┌──────────────────┐
    │  TileProvider    │ │WeatherOverlay  │ │BinaryTileCache   │
    │                  │ │   Manager      │ │                  │
    │ • Tile Fetching  │ │ • Radar Data   │ │ • tiles.bin      │
    │ • URL Templates  │ │ • Temperature  │ │ • tiles.idx      │
    │ • Cache Check    │ │ • Compositing  │ │ • Fast Lookup    │
    └──────────────────┘ └────────────────┘ └──────────────────┘
            │                    │                    │
            │                    │                    │
    ┌───────┴────────────────────┴────────────────────┴───────┐
    │              Cache & Network Layer                       │
    └──────────────────────────────────────────────────────────┘
            │                    │                    │
    ┌───────▼────────┐  ┌────────▼─────────┐  ┌──────▼────────┐
    │ OpenStreetMap  │  │  ECCC GeoMet     │  │  OpenMeteo    │
    │   Tile Server  │  │  WMS (Radar)     │  │     API       │
    │                │  │                  │  │ (Temperature) │
    └────────────────┘  └──────────────────┘  └───────────────┘
```

## Data Flow

### 1. Tile Loading Flow
```
User Pans/Zooms Map
    │
    ▼
GLRadarControl calculates visible tiles
    │
    ▼
TileProvider.GetTileBytesAsync(z, x, y)
    │
    ├─► Check LocalTilesRoot (offline)
    │   └─► Found? Return immediately
    │
    ├─► Check BinaryTileCache (FASTEST)
    │   └─► Found? Return immediately
    │
    ├─► Check Old File Cache
    │   └─► Found? Migrate to binary + Return
    │
    └─► Download from OpenStreetMap
        └─► Save to BinaryTileCache + Return
```

### 2. Weather Overlay Flow
```
User Enables Radar/Temperature
    │
    ▼
WeatherOverlayManager.GetCompositedOverlaysAsync()
    │
    ├─► If Radar Enabled:
    │   └─► UpdateRadarOverlayAsync()
    │       └─► RadarImageService.FetchRadarImageAsync()
    │           └─► ECCC GeoMet WMS API
    │
    ├─► If Temperature Enabled:
    │   └─► UpdateTemperatureOverlayAsync()
    │       └─► GenerateTemperatureGridAsync()
    │           └─► OpenMeteoClient.QueryAsync() (x25 points)
    │
    └─► CompositeOverlays()
        └─► Blend layers with opacity
            └─► Return PNG bytes
                │
                ▼
        GLRadarControl.SetImageBytes()
            └─► Upload to GPU texture
                └─► Render in OpenGL
```

### 3. Binary Cache Structure
```
%LocalAppData%\WSG\map_cache\
│
├─ tiles.bin              [All tile data sequentially]
│  │
│  ├─► Tile 1 Data (z=4, x=5, y=10) @ offset 0
│  ├─► Tile 2 Data (z=5, x=3, y=7)  @ offset 15234
│  ├─► Tile 3 Data (z=4, x=6, y=10) @ offset 28901
│  └─► ...
│
└─ tiles.idx              [Fast lookup index]
   │
   ├─► Version: 1
   ├─► Count: 1250 tiles
   │
   └─► Index Entries:
       ├─► (z=4, x=5, y=10) → Offset: 0, Length: 15234, Timestamp
       ├─► (z=5, x=3, y=7)  → Offset: 15234, Length: 13667, Timestamp
       └─► ...
```

## Component Interactions

```
┌─────────────────────────────────────────────────────────────────┐
│                         User Interface                           │
│  Mouse Events → GLRadarControl → TileProvider + OverlayManager  │
│  UI Controls  → WeatherMapControl → Enable/Disable Features     │
└─────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Rendering Pipeline                          │
│  1. Render base map tiles (from cache or download)              │
│  2. Composite weather overlays (radar + temperature)            │
│  3. Blend layers with GPU acceleration                          │
│  4. Display at 60 FPS                                           │
└─────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Caching Strategy                           │
│  • BinaryTileCache: Fast O(1) lookups, 7-day expiry            │
│  • Weather Data: 5-minute (radar) / 30-minute (temp) updates   │
│  • Automatic compaction and cleanup                             │
└─────────────────────────────────────────────────────────────────┘
```

## Performance Optimizations

### GPU Acceleration
- Tile textures uploaded to GPU memory
- Hardware-accelerated blending
- Viewport culling (only render visible tiles)
- Efficient texture atlas management

### Network Efficiency
- Binary cache reduces downloads by ~95%
- Proper HTTP headers (User-Agent, caching)
- Parallel tile downloads (when needed)
- Rate limiting and retry logic

### Memory Management
- LRU eviction (max 300 tile textures)
- Automatic cleanup of expired tiles
- Lazy loading of weather overlays
- Double-buffered rendering

## Threading Model

```
┌─────────────────────────────────────────────────────────────────┐
│  UI Thread (Main)                                                │
│  • User input handling                                           │
│  • OpenGL rendering (MakeCurrent)                               │
│  • UI control updates                                            │
└─────────────────────────────────────────────────────────────────┘
                                 │
                    ┌────────────┼────────────┐
                    │            │            │
        ┌───────────▼─────┐ ┌───▼────────┐ ┌─▼──────────────┐
        │Background Thread│ │ HTTP Worker│ │Cache I/O Worker│
        │• Tile prefetch  │ │• Downloads │ │• Disk reads    │
        │• Weather update │ │• API calls │ │• Disk writes   │
        └─────────────────┘ └────────────┘ └────────────────┘
                    │            │            │
                    └────────────┼────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │   BeginInvoke (UI)      │
                    │   • Update textures     │
                    │   • Trigger repaint     │
                    └─────────────────────────┘
```

## API Integration Points

### External APIs
1. **OpenStreetMap Tile Server**
   - URL: `https://tile.openstreetmap.org/{z}/{x}/{y}.png`
   - Rate Limit: Respectful (cached)
   - Attribution: Required

2. **ECCC GeoMet WMS**
   - URL: `https://geo.weather.gc.ca/geomet`
   - Layer: `RADAR_1KM_RRAI`
   - Update: Real-time

3. **OpenMeteo API**
   - URL: `https://api.open-meteo.com/v1/forecast`
   - Parameters: Current temperature
   - Coverage: Global

### Internal APIs
- `GLRadarControl` → Public events and methods
- `WeatherMapControl` → High-level control API
- `BinaryTileCache` → Cache management API
- `WeatherOverlayManager` → Weather data API

---

**This architecture provides:**
- ✅ Separation of concerns
- ✅ Efficient caching at multiple levels
- ✅ Async/await for non-blocking operations
- ✅ GPU acceleration for rendering
- ✅ Extensible design for new overlays

---
