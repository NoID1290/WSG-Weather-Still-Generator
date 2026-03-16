using System;
using System.Drawing;
using System.Windows.Forms;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Abstraction for the interactive weather map rendering control.
    /// Each rendering backend (OpenGL, Vulkan, DirectX) must provide an implementation
    /// that acts as a WinForms Control hosting a GPU-accelerated map viewport.
    /// 
    /// The implementation is expected to:
    ///  - Render slippy-map tiles with configurable shader effects
    ///  - Composite weather overlays (radar, temperature) with geo-positioning
    ///  - Draw crosshair/markers and in-viewport HUD panels via IHudRenderer
    ///  - Handle mouse interaction (pan, zoom, HUD click-through)
    ///  - Expose map state (zoom level, center lat/lon, FPS, VRAM stats)
    /// </summary>
    public interface IMapRenderer : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════
        // Control hosting — the implementation must expose a WinForms Control
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>The WinForms Control that hosts the rendered viewport.</summary>
        Control HostControl { get; }

        /// <summary>The rendering API backend used by this renderer.</summary>
        RenderingApi ActiveApi { get; }

        // ═══════════════════════════════════════════════════════════════════
        // HUD system
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>The interactive HUD overlay system for in-viewport controls.</summary>
        HudSystem? HudSystem { get; set; }

        /// <summary>Bottom-right status bar text (e.g., FPS, VRAM).</summary>
        string HudStatusBarText { get; set; }

        /// <summary>Bottom-left attribution text (e.g., map source credits).</summary>
        string HudAttributionText { get; set; }

        /// <summary>Bottom-center status text (e.g., loading status).</summary>
        string HudStatusText { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        // Display options
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Whether to show the crosshair/aiming dot at the viewport center.</summary>
        bool ShowCrosshair { get; set; }

        /// <summary>Whether the crosshair follows the mouse cursor.</summary>
        bool UseCrosshairAsMouse { get; set; }

        /// <summary>Whether to show coordinate HUD overlay at the top of the viewport.</summary>
        bool ShowCoordinatesHUD { get; set; }

        /// <summary>User marker latitude (for "my location" pin).</summary>
        double UserMarkerLat { get; set; }

        /// <summary>User marker longitude (for "my location" pin).</summary>
        double UserMarkerLon { get; set; }

        /// <summary>Whether to display the user location marker on the map.</summary>
        bool ShowUserMarker { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        // Shader effect toggles
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Enable saturation enhancement on map tiles.</summary>
        bool EnableTileSaturation { get; set; }

        /// <summary>Enable contrast curve on map tiles.</summary>
        bool EnableTileContrast { get; set; }

        /// <summary>Enable screen-space vignette effect on map tiles.</summary>
        bool EnableTileVignette { get; set; }

        /// <summary>Enable atmospheric tint on map tiles.</summary>
        bool EnableTileAtmosphere { get; set; }

        /// <summary>Enable glow effect on high-intensity radar areas.</summary>
        bool EnableRadarGlow { get; set; }

        /// <summary>Enable pulsing animation on the crosshair.</summary>
        bool EnableCrosshairPulse { get; set; }

        /// <summary>Whether to display the bottom-right status bar.</summary>
        bool ShowStatusBar { get; set; }

        /// <summary>Whether to display the scale bar (ruler).</summary>
        bool ShowRuler { get; set; }

        /// <summary>Opacity multiplier for the status bar background (0.0-1.0).</summary>
        float StatusBarOpacity { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        // Overlay management
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Primary overlay opacity (0.0-1.0) for radar layer.</summary>
        float OverlayOpacity { get; set; }

        /// <summary>Secondary overlay opacity (0.0-1.0) for temperature layer.</summary>
        float Overlay2Opacity { get; set; }

        /// <summary>Tertiary overlay opacity (0.0-1.0) for GRIB2 forecast layer.</summary>
        float Overlay3Opacity { get; set; }

        /// <summary>Show debug bounding boxes for overlays.</summary>
        bool DebugOverlayBounds { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        // Performance / diagnostics
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Whether to use PBO (Pixel Buffer Object) async uploads for tiles.</summary>
        bool UsePboUploads { get; set; }

        /// <summary>Whether a smooth zoom animation is currently in progress.</summary>
        bool IsSmoothZooming { get; }

        /// <summary>Current rendering frame rate.</summary>
        float CurrentFps { get; }

        /// <summary>Number of tile textures currently in VRAM.</summary>
        int VramTextureCount { get; }

        /// <summary>Estimated VRAM usage in bytes for tile textures.</summary>
        long VramEstimatedBytes { get; }

        /// <summary>Current smooth zoom factor.</summary>
        float Zoom { get; }

        /// <summary>The active tile provider for this renderer.</summary>
        TileProvider? ActiveTileProvider { get; }

        // ═══════════════════════════════════════════════════════════════════
        // Image/overlay data
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Set the primary overlay from raw image bytes (simple; no geo-positioning).</summary>
        void SetImageBytes(byte[] data);

        /// <summary>Set the primary overlay from raw image bytes with optional center/zoom metadata.</summary>
        void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom);

        /// <summary>Set the primary overlay from raw image bytes with explicit bounding box.</summary>
        void SetImageBytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom);

        /// <summary>Set the secondary overlay from raw image bytes with explicit bounding box.</summary>
        void SetOverlay2Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom);

        /// <summary>Set the tertiary overlay (GRIB2 forecast) from raw image bytes with explicit bounding box.</summary>
        void SetOverlay3Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom);

        /// <summary>Clear all overlays.</summary>
        void ClearOverlay();

        /// <summary>Clear only the primary positioned overlay.</summary>
        void ClearPositionedOverlay();

        /// <summary>Clear only the secondary positioned overlay.</summary>
        void ClearPositionedOverlay2();

        /// <summary>Clear only the tertiary positioned overlay (GRIB2).</summary>
        void ClearPositionedOverlay3();

        // ═══════════════════════════════════════════════════════════════════
        // GPU GRIB2 data pipeline
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Upload raw GRIB2 float grid data for GPU shader-based visualization.</summary>
        void SetGrib2GpuData(Grib2GpuRenderData data) { }

        /// <summary>Clear GPU GRIB2 data and disable the GPU overlay pipeline.</summary>
        void ClearGrib2GpuData() { }

        /// <summary>Whether the GPU GRIB2 overlay pipeline is active.</summary>
        bool Grib2GpuActive { get => false; }

        // ═══════════════════════════════════════════════════════════════════
        // Station / epicenter GPU vector markers
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upload station marker data for GPU vector rendering.
        /// Called when station list or selection changes.
        /// </summary>
        void SetStationMarkers(StationMarkerEntry[] markers) { }

        /// <summary>
        /// Upload epicenter marker data for GPU vector rendering.
        /// Called when event selection changes.
        /// </summary>
        void SetEpicenterMarkers(EpicenterMarkerEntry[] epicenters) { }

        /// <summary>
        /// Update the animation phase clocks for epicenter ring animation.
        /// Called ~20 times/sec by the seismogram animation timer.
        /// </summary>
        void SetMarkerAnimPhase(float epicenterPhase, float mostRecentPhase) { }

        /// <summary>
        /// Upload lightning strike marker data for GPU vector rendering.
        /// Age-fading and CG/IC colours are applied by the lightning_marker shader.
        /// Called whenever the active time window changes.
        /// </summary>
        void SetLightningMarkers(LightningStrikeEntry[] markers) { }

        // ═══════════════════════════════════════════════════════════════════
        // Map navigation
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Set the map center to the given lat/lon.</summary>
        void SetCenterLatLon(double lat, double lon);

        /// <summary>Set the tile zoom level.</summary>
        void SetMapZoom(int z);

        /// <summary>Set the map tile style (Standard, Minimal, Terrain, Satellite, etc.).</summary>
        void SetMapStyle(OpenMap.MapStyle style);

        /// <summary>Set the local tiles folder path for offline tile loading.</summary>
        void SetLocalTilesFolder(string? folder);

        /// <summary>Request a repaint of the viewport.</summary>
        void InvalidateView();

        // ═══════════════════════════════════════════════════════════════════
        // Events
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Fired when the tile zoom level changes.</summary>
        event Action<int>? MapZoomChanged;

        /// <summary>Fired when the map center position changes.</summary>
        event Action<double, double>? MapPositionChanged;

        /// <summary>Fired when tile loading status changes (message + color).</summary>
        event Action<string, Color>? TileStatusChanged;

        /// <summary>Fired when the background texture availability changes.</summary>
        event Action<bool>? BackgroundTextureChanged;
    }
}
