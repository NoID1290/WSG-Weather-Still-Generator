using System;
using System.Drawing;
using System.Windows.Forms;
using WeatherImageGenerator.Rendering.Common;

namespace WeatherImageGenerator.Rendering.DirectX
{
    /// <summary>
    /// DirectX 11 map renderer using Silk.NET.
    /// Provides full feature parity with the OpenGL renderer via the IMapRenderer interface.
    /// 
    /// Current status: Stub implementation — infrastructure scaffolding.
    /// TODO: Implement D3D11 device/swapchain setup, HLSL shader compilation,
    ///       tile texture management via ID3D11Texture2D, render pipeline,
    ///       staging textures for async tile upload, constant buffers.
    /// </summary>
    public class DXMapRenderer : IMapRenderer
    {
        private Panel _hostPanel;
        private bool _disposed;

        public DXMapRenderer()
        {
            _hostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 30)
            };

            var infoLabel = new Label
            {
                Text = "\u26A0 DirectX 11 renderer is not yet implemented.\nThe map will use OpenGL as a fallback.",
                ForeColor = Color.FromArgb(200, 200, 210),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12f, FontStyle.Regular),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _hostPanel.Controls.Add(infoLabel);
        }

        // ═══ IMapRenderer ═══

        public Control HostControl => _hostPanel;

        public HudSystem? HudSystem { get; set; }
        public string HudStatusBarText { get; set; } = "";
        public string HudAttributionText { get; set; } = "";
        public string HudStatusText { get; set; } = "";

        public bool ShowCrosshair { get; set; } = true;
        public bool UseCrosshairAsMouse { get; set; } = true;
        public bool ShowCoordinatesHUD { get; set; } = true;
        public double UserMarkerLat { get; set; }
        public double UserMarkerLon { get; set; }
        public bool ShowUserMarker { get; set; }

        public bool EnableTileSaturation { get; set; } = true;
        public bool EnableTileContrast { get; set; } = true;
        public bool EnableTileVignette { get; set; } = true;
        public bool EnableTileAtmosphere { get; set; } = true;
        public bool EnableRadarGlow { get; set; } = true;
        public bool EnableCrosshairPulse { get; set; } = true;

        public float OverlayOpacity { get; set; } = 0.7f;
        public float Overlay2Opacity { get; set; } = 0.6f;
        public bool DebugOverlayBounds { get; set; }

        public bool UsePboUploads { get; set; } = true;
        public bool IsSmoothZooming => false;
        public float CurrentFps => 0f;
        public int VramTextureCount => 0;
        public long VramEstimatedBytes => 0;
        public float Zoom => 1f;
        public TileProvider? ActiveTileProvider => null;

        public void SetImageBytes(byte[] data)
        {
            Console.WriteLine("[DXMapRenderer] SetImageBytes (stub)");
        }

        public void SetImageBytes(byte[] data, double? sourceCenterLat, double? sourceCenterLon, int? sourceZoom)
        {
            Console.WriteLine("[DXMapRenderer] SetImageBytes with metadata (stub)");
        }

        public void SetImageBytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            Console.WriteLine("[DXMapRenderer] SetImageBytes with bbox (stub)");
        }

        public void SetOverlay2Bytes(byte[] data, double minLat, double minLon, double maxLat, double maxLon, int sourceZoom)
        {
            Console.WriteLine("[DXMapRenderer] SetOverlay2Bytes (stub)");
        }

        public void ClearOverlay() { }
        public void ClearPositionedOverlay() { }
        public void ClearPositionedOverlay2() { }

        public void SetCenterLatLon(double lat, double lon)
        {
            Console.WriteLine($"[DXMapRenderer] SetCenterLatLon({lat:F4}, {lon:F4}) (stub)");
        }

        public void SetMapZoom(int z)
        {
            Console.WriteLine($"[DXMapRenderer] SetMapZoom({z}) (stub)");
        }

        public void SetMapStyle(OpenMap.MapStyle style)
        {
            Console.WriteLine($"[DXMapRenderer] SetMapStyle({style}) (stub)");
        }

        public void SetLocalTilesFolder(string? folder) { }

        public void InvalidateView()
        {
            _hostPanel.Invalidate();
        }

        public event Action<int>? MapZoomChanged;
        public event Action<double, double>? MapPositionChanged;
        public event Action<string, Color>? TileStatusChanged;
        public event Action<bool>? BackgroundTextureChanged;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _hostPanel?.Dispose();
            }
        }
    }
}
