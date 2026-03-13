using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using EKCA;
using EKCA.Models;
using OpenMap;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator.Rendering.Common
{
    /// <summary>
    /// Interactive seismogram map control.
    /// Renders CNSN seismic stations as glowing colour-coded dots on a slippy map,
    /// animates earthquake epicentres, and displays real MiniSEED waveform data
    /// in a docked bottom panel with scrolling playback.
    /// </summary>
    public class SeismogramMapControl : UserControl
    {
        // ── Core map infrastructure ──────────────────────────────────────────
        private IMapRenderer _glControl;
        private BinaryTileCache _tileCache;
        private HudSystem _hudSystem;
        private HttpClient _httpClient;

        // ── Map state ────────────────────────────────────────────────────────
        private double _currentLat = 56.1304;
        private double _currentLon = -106.3468;
        private int _currentZoom = 4;
        private bool _suppressOverlayUpdates;

        // ── EKCA live data ───────────────────────────────────────────────────
        private List<SeismicStation> _stations = new();
        private List<EarthquakeEvent> _events = new();
        private SeismicStation? _selectedStation;
        private EarthquakeEvent? _selectedEvent;
        private SeismogramData? _currentWaveform;
        private bool _isLoadingWaveform;
        private int _waveformWindowHours = 1;

        // ── Timers ───────────────────────────────────────────────────────────
        private Timer _dataRefreshTimer;   // 60 s  – refresh stations + events
        private Timer _epicenterAnimTimer; // 50 ms – animate expanding rings
        private Timer _waveformScrollTimer;// 30 ms – advance waveform playback
        private Timer _statusUpdateTimer;  // 500 ms – refresh status-bar labels
        private bool _isShuttingDown;
        private bool _isDisposed;

        // ── Animation state ──────────────────────────────────────────────────
        private float _epicenterRingPhase; // 0..1 cycling
        private float _waveformScrollPos;  // 0..1 — playback head position
        private bool _waveformPlaying;

        // ── HUD element refs ─────────────────────────────────────────────────
        private HudDropdown? _ddStation;
        private HudDropdown? _ddEvent;
        private HudLabel? _lblZoom;
        private HudLabel? _lblStatus;
        private HudLabel? _lblEventInfo;
        private HudButtonGroup? _grpMapStyle;

        // ── Overlay bitmap constants (Mercator, zoom-5 reference) ────────────
        private const double OV_MIN_LAT = 40.0;
        private const double OV_MAX_LAT = 84.0;
        private const double OV_MIN_LON = -142.5;
        private const double OV_MAX_LON = -50.5;
        private const int OV_ZOOM = 5;
        private const int OV_W = 1024;
        private const int OV_H = 700;

        // Pre-computed Mercator pixel origin for the overlay bbox at OV_ZOOM
        private double _ovOriginX;
        private double _ovOriginY;
        private double _ovScaleX; // pixels-per-OV_W
        private double _ovScaleY; // pixels-per-OV_H

        // ── Waveform panel ───────────────────────────────────────────────────
        private WaveformPanel _waveformPanel;

        // ────────────────────────────────────────────────────────────────────
        public SeismogramMapControl()
        {
            PrecomputeOverlayOrigin();
            InitializeComponents();
            InitializeMapControl();
            SetupEventHandlers();
            ApplyTheme();
            LoadMapSettings();
            StartTimers();
            // Initial EKCA data load (fire-and-forget)
            _ = LoadEkcaDataAsync();
        }

        // ════════════════════════════════════════════════════════════════════
        // Initialisation
        // ════════════════════════════════════════════════════════════════════

        private void PrecomputeOverlayOrigin()
        {
            _ovOriginX = LonToMercX(OV_MIN_LON, OV_ZOOM);
            _ovOriginY = LatToMercY(OV_MAX_LAT, OV_ZOOM); // top = max-lat (Mercator Y flipped)
            double xRight = LonToMercX(OV_MAX_LON, OV_ZOOM);
            double yBottom = LatToMercY(OV_MIN_LAT, OV_ZOOM);
            _ovScaleX = (xRight - _ovOriginX) / OV_W;
            _ovScaleY = (yBottom - _ovOriginY) / OV_H;
        }

        private void InitializeComponents()
        {
            this.Size = new Size(1200, 800);
            this.BackColor = ThemeManager.Current.Background;

            // Waveform panel docked at the bottom (carved out first so Fill leaves it alone)
            _waveformPanel = new WaveformPanel { Height = 165, Dock = DockStyle.Bottom };
            this.Controls.Add(_waveformPanel);
        }

        private void InitializeMapControl()
        {
            var config = ConfigManager.LoadConfig();
            var apiStr = config.OpenMap?.RenderingApi ?? "OpenGL";
            var api = RenderingFactory.ParseFromString(apiStr);

            _glControl = RenderingFactory.CreateMapRenderer(api);
            _glControl.HostControl.Dock = DockStyle.Fill;
            _glControl.HostControl.BackColor = ThemeManager.Current.Background;
            this.Controls.Add(_glControl.HostControl);

            // WinForms docks controls back-to-front (highest index first).
            // BringToFront on the Fill control puts it at index 0 (docked last),
            // pushing the Bottom waveform panel to index 1 (docked first → reserves 165px).
            _glControl.HostControl.BringToFront();

            _hudSystem = new HudSystem();
            _glControl.HudSystem = _hudSystem;

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var cacheDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WSG", "seismo_cache");
            _tileCache = new BinaryTileCache(cacheDir);

            EKCAApi.Log = msg => Console.WriteLine($"[EKCA] {msg}");

            _glControl.HudAttributionText = "Data: Earthquakes Canada / CNSN  |  Map: \u00a9 OpenStreetMap contributors";

            BuildHudPanels();
        }

        private void SetupEventHandlers()
        {
            _glControl.MapZoomChanged += zoom =>
            {
                _currentZoom = zoom;
                UpdateStatusLabels();
                if (!_suppressOverlayUpdates && !_glControl.IsSmoothZooming)
                    RebuildAndPushStationOverlay();
            };

            _glControl.MapPositionChanged += (lat, lon) =>
            {
                _currentLat = lat;
                _currentLon = lon;
                UpdateStatusLabels();
                if (!_suppressOverlayUpdates)
                    RebuildAndPushStationOverlay();
            };
        }

        private void StartTimers()
        {
            _dataRefreshTimer = new Timer { Interval = 60_000 };
            _dataRefreshTimer.Tick += (_, _) => _ = LoadEkcaDataAsync();
            _dataRefreshTimer.Start();

            _epicenterAnimTimer = new Timer { Interval = 50 };
            _epicenterAnimTimer.Tick += EpicenterAnimTick;
            _epicenterAnimTimer.Start();

            _waveformScrollTimer = new Timer { Interval = 33 };
            _waveformScrollTimer.Tick += WaveformScrollTick;
            // Started/stopped by ToggleWaveformPlayback

            _statusUpdateTimer = new Timer { Interval = 500 };
            _statusUpdateTimer.Tick += (_, _) => UpdateStatusLabels();
            _statusUpdateTimer.Start();
        }

        // ════════════════════════════════════════════════════════════════════
        // HUD panels
        // ════════════════════════════════════════════════════════════════════

        private void BuildHudPanels()
        {
            // ── Zoom strip (bottom-right, no title) ──────────────────────────
            var zoomPanel = new HudPanel
            {
                Id = "zoom",
                Title = "",
                Anchor = HudAnchor.BottomRight,
                Width = 40f,
                MarginX = 12, MarginY = 36,
                Collapsible = false,
                TitleVisible = false
            };
            zoomPanel.Elements.Add(new HudButton { Id = "zoomIn",  Text = "+", IsCompact = true, OnClick = () => SetZoom(_currentZoom + 1) });
            _lblZoom = new HudLabel { Id = "zoomLvl", Text = $"{_currentZoom}", IsDim = true };
            zoomPanel.Elements.Add(_lblZoom);
            zoomPanel.Elements.Add(new HudButton { Id = "zoomOut", Text = "\u2212", IsCompact = true, OnClick = () => SetZoom(_currentZoom - 1) });
            zoomPanel.Elements.Add(new HudSeparator());
            zoomPanel.Elements.Add(new HudButton { Id = "center",  Text = "\u25CE", IsCompact = true, OnClick = CenterCanada });
            _hudSystem.AddPanel(zoomPanel);

            // ── Seismograph controls (top-right) ─────────────────────────────
            var ctrlPanel = new HudPanel
            {
                Id = "seismo",
                Title = "Seismograph",
                Anchor = HudAnchor.TopRight,
                Width = 295f,
                MarginX = 10, MarginY = 10,
                Collapsible = true
            };

            ctrlPanel.Elements.Add(new HudLabel { Id = "lblStationHdr", Text = "Station", IsSection = true });
            _ddStation = new HudDropdown
            {
                Id = "ddStation",
                Text = "Station",
                Options = new List<string> { "— loading —" },
                SelectedIndex = 0,
                OnSelectionChanged = OnStationSelectionChanged
            };
            ctrlPanel.Elements.Add(_ddStation);
            ctrlPanel.Elements.Add(new HudButton
            {
                Id = "btnLoadWaveform",
                Text = "Load Waveform",
                IsAccent = true,
                OnClick = () => _ = LoadWaveformForSelectedStation()
            });

            ctrlPanel.Elements.Add(new HudSeparator());
            ctrlPanel.Elements.Add(new HudLabel { Id = "lblEventHdr", Text = "Earthquake", IsSection = true });
            _ddEvent = new HudDropdown
            {
                Id = "ddEvent",
                Text = "Earthquake",
                Options = new List<string> { "— loading —" },
                SelectedIndex = 0,
                OnSelectionChanged = OnEventSelectionChanged
            };
            ctrlPanel.Elements.Add(_ddEvent);
            ctrlPanel.Elements.Add(new HudButton
            {
                Id = "btnGoEvent",
                Text = "Go to Epicenter",
                OnClick = GoToSelectedEvent
            });
            ctrlPanel.Elements.Add(new HudButton
            {
                Id = "btnFindNearestStation",
                Text = "Find Nearest Station",
                OnClick = SelectNearestStationToEvent
            });

            ctrlPanel.Elements.Add(new HudSeparator());
            ctrlPanel.Elements.Add(new HudLabel { Id = "lblWaveformHdr", Text = "Waveform Window", IsSection = true });
            ctrlPanel.Elements.Add(new HudButtonGroup
            {
                Id = "grpWaveWindow",
                Options = new List<string> { "1 h", "6 h", "24 h" },
                SelectedIndex = 0,
                OnSelectionChanged = idx =>
                {
                    _waveformWindowHours = idx == 0 ? 1 : idx == 1 ? 6 : 24;
                    SaveMapSettings();
                    if (_selectedStation != null)
                        _ = LoadWaveformForSelectedStation();
                }
            });

            ctrlPanel.Elements.Add(new HudSeparator());
            ctrlPanel.Elements.Add(new HudLabel { Id = "lblMapStyleHdr", Text = "Map Style", IsSection = true });
            _grpMapStyle = new HudButtonGroup
            {
                Id = "grpMapStyle",
                Options = new List<string> { "Std", "Dark", "Topo", "Sat" },
                SelectedIndex = 1,
                OnSelectionChanged = idx => ApplyMapStyle(idx)
            };
            ctrlPanel.Elements.Add(_grpMapStyle);

            ctrlPanel.Elements.Add(new HudSeparator());
            ctrlPanel.Elements.Add(new HudButton
            {
                Id = "btnRefresh",
                Text = "Refresh Data",
                OnClick = RefreshData
            });

            _hudSystem.AddPanel(ctrlPanel);

            // ── Event info panel (top-left) ───────────────────────────────────
            var infoPanel = new HudPanel
            {
                Id = "eventInfo",
                Title = "Event Info",
                Anchor = HudAnchor.TopLeft,
                Width = 320f,
                MarginX = 10, MarginY = 10,
                Collapsible = true,
                Collapsed = false
            };

            _lblEventInfo = new HudLabel { Id = "lblEventInfo", Text = "No event selected" };
            infoPanel.Elements.Add(_lblEventInfo);
            infoPanel.Elements.Add(new HudSeparator());
            _lblStatus = new HudLabel { Id = "lblStatus", Text = "Loading stations\u2026", IsDim = true };
            infoPanel.Elements.Add(_lblStatus);

            _hudSystem.AddPanel(infoPanel);

            // Apply dark map style by default
            ApplyMapStyle(1);
        }

        // ════════════════════════════════════════════════════════════════════
        // EKCA data loading
        // ════════════════════════════════════════════════════════════════════

        private async Task LoadEkcaDataAsync()
        {
            if (_isShuttingDown || _isDisposed) return;
            try
            {
                SetStatus("Fetching seismic stations…");

                var stations = await EKCAApi.GetStationsAsync(_httpClient);
                var events   = await EKCAApi.GetRecentEventsAsync(_httpClient);

                EKCAApi.CorrelateEventsToStations(stations, events);

                if (_isShuttingDown || _isDisposed) return;

                _stations = stations;
                _events   = events;

                // Update HUD dropdowns from UI thread
                if (_glControl.HostControl.IsHandleCreated)
                {
                    _glControl.HostControl.BeginInvoke(new Action(() =>
                    {
                        if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
                        PopulateStationDropdown();
                        PopulateEventDropdown();
                        RebuildAndPushStationOverlay();
                        SetStatus($"Loaded {stations.Count} stations · {events.Count} events");
                    }));
                }
            }
            catch (Exception ex)
            {
                if (!_isShuttingDown)
                    SetStatus($"Data load failed: {ex.Message}");
            }
        }

        private void PopulateStationDropdown()
        {
            if (_ddStation == null) return;
            var sorted = _stations.OrderBy(s => s.StationCode).ToList();
            _ddStation.Options = sorted.Select(s => $"{s.StationCode,-5} {TruncateStr(s.SiteName, 22)}").ToList();
            if (_ddStation.Options.Count == 0)
                _ddStation.Options.Add("— no stations —");
            _ddStation.SelectedIndex = 0;
            _selectedStation = sorted.Count > 0 ? sorted[0] : null;
        }

        private void PopulateEventDropdown()
        {
            if (_ddEvent == null) return;
            var recent = _events.Take(40).ToList();
            _ddEvent.Options = recent.Select(e =>
            {
                var age = DateTime.UtcNow - e.OriginTime;
                string ageStr = age.TotalDays >= 1 ? $"{(int)age.TotalDays}d"
                              : age.TotalHours >= 1 ? $"{(int)age.TotalHours}h"
                              : $"{(int)age.TotalMinutes}m";
                return $"M{e.Magnitude:F1} {TruncateStr(e.Location, 22)} · {ageStr}";
            }).ToList();
            if (_ddEvent.Options.Count == 0)
                _ddEvent.Options.Add("— no events —");
            _ddEvent.SelectedIndex = 0;
            _selectedEvent = recent.Count > 0 ? recent[0] : null;
            UpdateEventInfoLabel();
        }

        private static string TruncateStr(string s, int max) =>
            s.Length <= max ? s : s[..max].TrimEnd() + "…";

        private async Task LoadWaveformForSelectedStation()
        {
            if (_isShuttingDown || _isDisposed) return;
            if (_selectedStation == null || _isLoadingWaveform) return;
            _isLoadingWaveform = true;
            SetStatus($"Loading waveform for {_selectedStation.StationCode}…");
            try
            {
                var waveform = await EKCAApi.GetRecentWaveformAsync(_httpClient, _selectedStation.StationCode, lastHours: _waveformWindowHours);
                if (_isShuttingDown || _isDisposed) return;
                _currentWaveform = waveform;
                _waveformScrollPos = 0f;

                // Capture locals so the marshalled callback cannot hit a null _selectedStation.
                var capturedWaveform = waveform;
                var capturedStation  = _selectedStation;

                if (_glControl.HostControl.IsHandleCreated)
                    _glControl.HostControl.BeginInvoke(new Action(() =>
                    {
                        if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
                        if (capturedWaveform == null) return;
                        _waveformPanel.SetWaveform(capturedWaveform, capturedStation);
                        if (capturedStation != null)
                            SetStatus(capturedWaveform.HasData
                                ? $"{capturedStation.StationCode} · {capturedWaveform.Samples.Length:N0} samples · {capturedWaveform.SampleRateHz:F1} sps"
                                : $"No waveform data for {capturedStation.StationCode}");
                    }));
            }
            catch (Exception ex)
            {
                if (!_isShuttingDown)
                    SetStatus($"Waveform error: {ex.Message}");
            }
            finally
            {
                _isLoadingWaveform = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // HUD callbacks
        // ════════════════════════════════════════════════════════════════════

        private void OnStationSelectionChanged(int idx)
        {
            var sorted = _stations.OrderBy(s => s.StationCode).ToList();
            _selectedStation = idx >= 0 && idx < sorted.Count ? sorted[idx] : null;
            RebuildAndPushStationOverlay();
        }

        private void OnEventSelectionChanged(int idx)
        {
            var recent = _events.Take(40).ToList();
            _selectedEvent = idx >= 0 && idx < recent.Count ? recent[idx] : null;
            UpdateEventInfoLabel();
            _epicenterRingPhase = 0f;
        }

        private void GoToSelectedEvent()
        {
            if (_selectedEvent == null) return;
            SetLocationAndZoom(_selectedEvent.Latitude, _selectedEvent.Longitude, Math.Max(6, _currentZoom));
        }

        private void SelectNearestStationToEvent()
        {
            if (_selectedEvent == null || _stations.Count == 0) return;

            var nearest = _stations
                .OrderBy(s => EKCAApi.CalculateDistanceKm(s.Latitude, s.Longitude, _selectedEvent.Latitude, _selectedEvent.Longitude))
                .First();

            _selectedStation = nearest;
            var sorted = _stations.OrderBy(s => s.StationCode).ToList();
            int idx = sorted.IndexOf(nearest);
            if (_ddStation != null && idx >= 0)
                _ddStation.SelectedIndex = idx;

            _ = LoadWaveformForSelectedStation();
        }

        private void CenterCanada()
        {
            SetLocationAndZoom(56.1304, -106.3468, 4);
        }

        private void UpdateEventInfoLabel()
        {
            if (_lblEventInfo == null) return;
            if (_selectedEvent == null)
            {
                _lblEventInfo.Text = "No event selected";
                return;
            }
            var age = DateTime.UtcNow - _selectedEvent.OriginTime;
            string ageStr = age.TotalDays >= 1
                ? $"{(int)age.TotalDays}d ago"
                : age.TotalHours >= 1
                    ? $"{(int)age.TotalHours}h ago"
                    : $"{(int)age.TotalMinutes}m ago";
            _lblEventInfo.Text =
                $"M{_selectedEvent.Magnitude:F2}  \u2014  {_selectedEvent.Location}\n" +
                $"{_selectedEvent.OriginTime:yyyy-MM-dd HH:mm:ss} UTC  \u00b7  {ageStr}\n" +
                $"Depth: {_selectedEvent.DepthKm:F0} km";
        }

        private void SetStatus(string msg)
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            if (_lblStatus != null) _lblStatus.Text = msg;
            _glControl?.InvalidateView();
        }

        private void UpdateStatusLabels()
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            if (_lblZoom != null)
                _lblZoom.Text = $"{_currentZoom}";

            var fps  = _glControl?.CurrentFps ?? 0f;
            var vram = (_glControl?.VramEstimatedBytes ?? 0) / 1_048_576.0;
            var api  = _glControl?.ActiveApi.ToString() ?? "Unknown";
            _glControl!.HudStatusBarText = $"{api}   FPS: {fps:F0}   VRAM: {vram:F0} MB   Zoom: {_currentZoom}";
            _glControl.InvalidateView();
        }

        // ════════════════════════════════════════════════════════════════════
        // Station / epicentre overlay rendering
        // ════════════════════════════════════════════════════════════════════

        private void RebuildAndPushStationOverlay()
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            if (_stations.Count == 0) return;

            using var bmp = new Bitmap(OV_W, OV_H, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var station in _stations)
            {
                bool selected = station == _selectedStation;
                var color = GetStationColor(station.ActivityLevel, selected);
                var (px, py) = StationToOverlayPixel(station.Latitude, station.Longitude);
                if (px < -20 || px > OV_W + 20 || py < -20 || py > OV_H + 20) continue;

                DrawGlowingDot(g, px, py, color, selected ? 6f : 3.5f);

                if (selected)
                {
                    // White ring around selected station
                    using var ringPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
                    g.DrawEllipse(ringPen, px - 9f, py - 9f, 18f, 18f);
                }
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            _glControl.SetImageBytes(ms.ToArray(), OV_MIN_LAT, OV_MIN_LON, OV_MAX_LAT, OV_MAX_LON, OV_ZOOM);
        }

        private void EpicenterAnimTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            _epicenterRingPhase = (_epicenterRingPhase + 0.018f) % 1.0f;
            if (_selectedEvent != null)
                PushEpicenterOverlay();
        }

        private void PushEpicenterOverlay()
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            if (_selectedEvent == null)
            {
                _glControl.ClearPositionedOverlay2();
                return;
            }

            var mag = _selectedEvent.Magnitude;
            Color ringColor = mag >= 5.0 ? Color.FromArgb(255, 80, 0)
                            : mag >= 3.0 ? Color.FromArgb(255, 200, 0)
                            : Color.FromArgb(40, 200, 255);

            using var bmp = new Bitmap(OV_W, OV_H, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var (px, py) = StationToOverlayPixel(_selectedEvent.Latitude, _selectedEvent.Longitude);

            // Three phase-staggered expanding rings
            for (int ring = 0; ring < 3; ring++)
            {
                float phase  = (_epicenterRingPhase + ring * 0.333f) % 1.0f;
                float radius = phase * 85f;
                if (radius < 1f) continue; // GDI+ rejects zero/near-zero ellipse dimensions
                float alpha  = (1f - phase) * 190f;
                float width  = Math.Max(0.5f, 2.2f - phase * 2f);
                using var pen = new Pen(Color.FromArgb((int)alpha, ringColor.R, ringColor.G, ringColor.B), width);
                g.DrawEllipse(pen, px - radius, py - radius, radius * 2, radius * 2);
            }

            // Centre dot with magnitude-based size
            float dotR = Math.Min(10f, 3f + (float)mag * 1.2f);
            DrawGlowingDot(g, px, py, ringColor, dotR);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            _glControl.SetOverlay2Bytes(ms.ToArray(), OV_MIN_LAT, OV_MIN_LON, OV_MAX_LAT, OV_MAX_LON, OV_ZOOM);
        }

        // ════════════════════════════════════════════════════════════════════
        // Waveform playback
        // ════════════════════════════════════════════════════════════════════

        private void WaveformScrollTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            if (_currentWaveform == null || !_currentWaveform.HasData) return;
            _waveformScrollPos = (_waveformScrollPos + 0.001f) % 1.0f;
            _waveformPanel.SetScrollPosition(_waveformScrollPos);
        }

        public void ToggleWaveformPlayback()
        {
            if (_isShuttingDown || _isDisposed) return;
            _waveformPlaying = !_waveformPlaying;
            if (_waveformPlaying)
                _waveformScrollTimer.Start();
            else
                _waveformScrollTimer.Stop();
            _waveformPanel.SetPlaying(_waveformPlaying);
        }

        // ════════════════════════════════════════════════════════════════════
        // Map style helpers
        // ════════════════════════════════════════════════════════════════════

        private static readonly MapStyle[] _mapStyles =
        {
            MapStyle.Standard,
            MapStyle.TerrainDark,
            MapStyle.Terrain,
            MapStyle.Satellite
        };
        private int _mapStyleIndex = 1;

        private void ApplyMapStyle(int idx)
        {
            _mapStyleIndex = Math.Clamp(idx, 0, _mapStyles.Length - 1);
            _glControl.SetMapStyle(_mapStyles[_mapStyleIndex]);
            if (_grpMapStyle != null)
                _grpMapStyle.SelectedIndex = _mapStyleIndex;
            SaveMapSettings();
        }

        public void CycleMapStyle() => ApplyMapStyle((_mapStyleIndex + 1) % _mapStyles.Length);

        // ════════════════════════════════════════════════════════════════════
        // Public API (mirrors WeatherMapControl)
        // ════════════════════════════════════════════════════════════════════

        public int CurrentZoom => _currentZoom;

        public void SetLocation(double lat, double lon)
        {
            if (_isShuttingDown || _isDisposed) return;
            _currentLat = lat;
            _currentLon = lon;
            _glControl.SetCenterLatLon(lat, lon);
            UpdateStatusLabels();
        }

        public void SetLocationAndZoom(double lat, double lon, int zoom)
        {
            if (_isShuttingDown || _isDisposed) return;
            _currentLat = lat;
            _currentLon = lon;
            _currentZoom = Math.Clamp(zoom, 1, 20);
            _suppressOverlayUpdates = true;
            try
            {
                _glControl.SetCenterLatLon(lat, lon);
                _glControl.SetMapZoom(_currentZoom);
            }
            finally { _suppressOverlayUpdates = false; }
            UpdateStatusLabels();
            RebuildAndPushStationOverlay();
        }

        public void SetZoom(int zoom)
        {
            if (_isShuttingDown || _isDisposed) return;
            _currentZoom = Math.Clamp(zoom, 1, 20);
            _glControl.SetMapZoom(_currentZoom);
            UpdateStatusLabels();
        }

        public void RefreshData()
        {
            if (_isShuttingDown || _isDisposed) return;
            _ = LoadEkcaDataAsync();
        }

        public void BeginShutdown()
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;

            try { _dataRefreshTimer?.Stop(); } catch { }
            try { _epicenterAnimTimer?.Stop(); } catch { }
            try { _waveformScrollTimer?.Stop(); } catch { }
            try { _statusUpdateTimer?.Stop(); } catch { }

            _waveformPlaying = false;
            _selectedEvent = null;

            try { _glControl?.ClearPositionedOverlay2(); } catch { }
        }

        public void ApplyTheme()
        {
            this.BackColor = ThemeManager.Current.Background;
            if (_glControl != null)
                _glControl.HostControl.BackColor = ThemeManager.Current.Background;
            _waveformPanel?.ApplyTheme();
        }

        // ════════════════════════════════════════════════════════════════════
        // Settings persistence
        // ════════════════════════════════════════════════════════════════════

        private void SaveMapSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.SeismogramMapView ??= new SeismogramMapViewSettings();
                config.SeismogramMapView.ZoomLevel = _currentZoom;
                config.SeismogramMapView.Latitude  = _currentLat;
                config.SeismogramMapView.Longitude = _currentLon;
                config.SeismogramMapView.MapStyleIndex = _mapStyleIndex;
                config.SeismogramMapView.WaveformWindowHours = _waveformWindowHours;
                ConfigManager.SaveConfig(config, silent: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SeismoMap] Save settings failed: {ex.Message}");
            }
        }

        private void LoadMapSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var s = config.SeismogramMapView;
                if (s == null) return;

                _currentZoom = Math.Clamp(s.ZoomLevel, 1, 20);
                _currentLat  = s.Latitude;
                _currentLon  = s.Longitude;
                _waveformWindowHours = s.WaveformWindowHours > 0 ? s.WaveformWindowHours : 1;
                ApplyMapStyle(s.MapStyleIndex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SeismoMap] Load settings failed: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Mercator projection helpers
        // ════════════════════════════════════════════════════════════════════

        private static double LonToMercX(double lon, int zoom) =>
            (lon + 180.0) / 360.0 * (1 << zoom) * 256.0;

        private static double LatToMercY(double lat, int zoom)
        {
            double rad    = lat * Math.PI / 180.0;
            double sinLat = Math.Sin(rad);
            return (0.5 - Math.Log((1 + sinLat) / (1 - sinLat)) / (4.0 * Math.PI)) * (1 << zoom) * 256.0;
        }

        private (float x, float y) StationToOverlayPixel(double lat, double lon)
        {
            double mercX = LonToMercX(lon, OV_ZOOM);
            double mercY = LatToMercY(lat, OV_ZOOM);
            float px = (float)((mercX - _ovOriginX) / _ovScaleX);
            float py = (float)((mercY - _ovOriginY) / _ovScaleY);
            return (px, py);
        }

        // ════════════════════════════════════════════════════════════════════
        // GDI+ drawing helpers
        // ════════════════════════════════════════════════════════════════════

        private static Color GetStationColor(ActivityLevel level, bool selected = false)
        {
            if (selected) return Color.FromArgb(255, 255, 255);
            return level switch
            {
                ActivityLevel.Active   => Color.FromArgb(255, 80, 0),
                ActivityLevel.Elevated => Color.FromArgb(255, 200, 0),
                ActivityLevel.Normal   => Color.FromArgb(0, 210, 210),
                _                      => Color.FromArgb(120, 130, 150)
            };
        }

        private static void DrawGlowingDot(Graphics g, float cx, float cy, Color color, float radius)
        {
            // Outer glow rings
            for (int i = 4; i >= 1; i--)
            {
                float r = radius + i * 2.8f;
                int alpha = 20 * (5 - i);
                using var brush = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
            }
            // Core dot
            using var core = new SolidBrush(Color.FromArgb(220, color.R, color.G, color.B));
            g.FillEllipse(core, cx - radius, cy - radius, radius * 2, radius * 2);
            // Bright specular highlight
            float hr = radius * 0.38f;
            using var hi = new SolidBrush(Color.FromArgb(255,
                Math.Min(255, color.R + 60),
                Math.Min(255, color.G + 60),
                Math.Min(255, color.B + 60)));
            g.FillEllipse(hi, cx - hr, cy - hr, hr * 2, hr * 2);
        }

        // ════════════════════════════════════════════════════════════════════
        // Disposal
        // ════════════════════════════════════════════════════════════════════

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                BeginShutdown();
                _dataRefreshTimer?.Dispose();
                _epicenterAnimTimer?.Dispose();
                _waveformScrollTimer?.Dispose();
                _statusUpdateTimer?.Dispose();
                _glControl?.Dispose();
                _tileCache?.Dispose();
                _httpClient?.Dispose();
                try { SaveMapSettings(); } catch { }
                _isDisposed = true;
            }
            base.Dispose(disposing);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // WaveformPanel — GDI+ seismogram rendering
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dockable bottom panel that renders an animated seismogram trace with grid, scale, and playback cursor.
    /// </summary>
    internal class WaveformPanel : Panel
    {
        private SeismogramData? _waveform;
        private SeismicStation? _station;
        private float _scrollPos;   // 0..1 — normalised playback head
        private bool _playing;

        // Visible window: fraction of total samples shown at once
        private const float WindowFraction = 0.25f;

        private static readonly Color BgColor      = Color.FromArgb(18, 22, 30);
        private static readonly Color GridColor     = Color.FromArgb(32, 45, 62);
        private static readonly Color BaselineColor = Color.FromArgb(55, 72, 95);
        private static readonly Color TraceColor    = Color.FromArgb(0, 205, 225);
        private static readonly Color HighColor     = Color.FromArgb(255, 140, 20);
        private static readonly Color CursorColor   = Color.FromArgb(230, 240, 255);
        private static readonly Color TextColor     = Color.FromArgb(165, 185, 210);

        internal WaveformPanel()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint  |
                          ControlStyles.UserPaint, true);
            this.BackColor = BgColor;
        }

        internal void SetWaveform(SeismogramData? data, SeismicStation? station)
        {
            _waveform = data;
            _station  = station;
            _scrollPos = 0f;
            Invalidate();
        }

        internal void SetScrollPosition(float pos)
        {
            _scrollPos = pos;
            Invalidate();
        }

        internal void SetPlaying(bool playing)
        {
            _playing = playing;
            Invalidate();
        }

        internal void ApplyTheme()
        {
            this.BackColor = BgColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BgColor);
            var rc = this.ClientRectangle;

            if (_waveform == null || !_waveform.HasData)
            {
                DrawEmptyState(g, rc);
                return;
            }

            DrawGrid(g, rc);
            DrawBaseline(g, rc);
            DrawTrace(g, rc);
            DrawCursor(g, rc);
            DrawMeta(g, rc);
            DrawBorder(g, rc);
        }

        private static void DrawEmptyState(Graphics g, Rectangle rc)
        {
            using var font = new Font("Segoe UI", 11f, FontStyle.Regular);
            using var brush = new SolidBrush(Color.FromArgb(90, 110, 140));
            string msg = "No waveform data  ·  Select a station and click Load Waveform";
            var size = g.MeasureString(msg, font);
            g.DrawString(msg, font, brush,
                (rc.Width - size.Width) / 2f,
                (rc.Height - size.Height) / 2f);
            using var borderPen = new Pen(Color.FromArgb(35, 50, 70), 1f);
            g.DrawLine(borderPen, 0, 0, rc.Width, 0);
        }

        private static void DrawGrid(Graphics g, Rectangle rc)
        {
            using var pen = new Pen(GridColor, 1f);
            int gridCols = 12;
            int gridRows = 4;
            for (int c = 1; c < gridCols; c++)
            {
                int x = rc.Width * c / gridCols;
                g.DrawLine(pen, x, 0, x, rc.Height);
            }
            for (int r = 1; r < gridRows; r++)
            {
                int y = rc.Height * r / gridRows;
                g.DrawLine(pen, 0, y, rc.Width, y);
            }
        }

        private static void DrawBaseline(Graphics g, Rectangle rc)
        {
            int mid = rc.Height / 2;
            using var pen = new Pen(BaselineColor, 1f);
            g.DrawLine(pen, 0, mid, rc.Width, mid);
        }

        private void DrawTrace(Graphics g, Rectangle rc)
        {
            float[] samples = _waveform!.NormalizedSamples();
            int total  = samples.Length;
            int winLen = Math.Max(1, (int)(total * WindowFraction));
            int start  = (int)(_scrollPos * (total - winLen));
            start = Math.Clamp(start, 0, total - winLen);

            // Decimate to screen-width pixels for performance
            int targetCount = rc.Width;
            var span = new ArraySegment<float>(samples, start, winLen);
            float[] disp = Decimate(span, targetCount);

            float mid    = rc.Height / 2f;
            float hRange = rc.Height * 0.44f;
            float ampThreshold = 0.65f;

            var points = new PointF[disp.Length];
            for (int i = 0; i < disp.Length; i++)
                points[i] = new PointF(i, mid - disp[i] * hRange);

            // Draw trace in two passes: normal amplitude (cyan), high amplitude (orange)
            using var tracePen = new Pen(TraceColor, 1.2f) { LineJoin = LineJoin.Round };
            using var highPen  = new Pen(HighColor,  1.5f) { LineJoin = LineJoin.Round };

            var normalPts = new List<PointF>(disp.Length);
            var highPts   = new List<PointF>(disp.Length);
            foreach (var pt in points)
            {
                float normAmp = Math.Abs((pt.Y - mid) / hRange);
                if (normAmp > ampThreshold) highPts.Add(pt); else normalPts.Add(pt);
            }

            if (normalPts.Count > 1) g.DrawLines(tracePen, normalPts.ToArray());
            if (highPts.Count   > 1) g.DrawLines(highPen,  highPts.ToArray());
        }

        private void DrawCursor(Graphics g, Rectangle rc)
        {
            if (!_playing) return;
            float x = _scrollPos * rc.Width;
            using var glow = new Pen(Color.FromArgb(50, CursorColor.R, CursorColor.G, CursorColor.B), 5f);
            using var line = new Pen(Color.FromArgb(200, CursorColor.R, CursorColor.G, CursorColor.B), 1.5f);
            g.DrawLine(glow, x, 0, x, rc.Height);
            g.DrawLine(line, x, 0, x, rc.Height);
        }

        private void DrawMeta(Graphics g, Rectangle rc)
        {
            if (_waveform == null || _station == null) return;
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            using var brush = new SolidBrush(TextColor);

            string left = $"{_station.StationCode} · {_waveform.Channel} · {_waveform.SampleRateHz:F1} sps";
            string mid  = _waveform.HasData
                ? $"{_waveform.StartTime:yyyy-MM-dd HH:mm}  +{_waveform.DurationSeconds / 3600.0:F1} h"
                : "No data";
            string right = _playing ? "▶ PLAYING" : "⏸ PAUSED  [SPACE]";

            g.DrawString(left,  font, brush, 6f, 3f);

            var midSize = g.MeasureString(mid, font);
            g.DrawString(mid, font, brush, (rc.Width - midSize.Width) / 2f, 3f);

            var rightSize = g.MeasureString(right, font);
            g.DrawString(right, font, brush, rc.Width - rightSize.Width - 6f, 3f);
        }

        private static void DrawBorder(Graphics g, Rectangle rc)
        {
            using var pen = new Pen(Color.FromArgb(42, 58, 80), 1f);
            g.DrawLine(pen, 0, 0, rc.Width, 0);
        }

        /// <summary>Peak-preserving decimation — keeps the min and max in each bucket.</summary>
        private static float[] Decimate(ArraySegment<float> src, int targetCount)
        {
            if (src.Count <= targetCount)
                return src.ToArray();

            float[] result = new float[targetCount];
            double buckSize = (double)src.Count / targetCount;
            for (int i = 0; i < targetCount; i++)
            {
                int s = (int)(i * buckSize);
                int e = Math.Min(src.Count, (int)((i + 1) * buckSize));
                float mn = float.MaxValue, mx = float.MinValue;
                for (int j = s; j < e; j++)
                {
                    float v = src.Array![src.Offset + j];
                    if (v < mn) mn = v;
                    if (v > mx) mx = v;
                }
                result[i] = Math.Abs(mx) > Math.Abs(mn) ? mx : mn;
            }
            return result;
        }
    }
}
