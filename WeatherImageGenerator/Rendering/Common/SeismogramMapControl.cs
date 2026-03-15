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
        private float _epicenterRingPhase;    // 0..1 cycling
        private float _mostRecentRingPhase;   // 0..1 cycling, faster than selected
        private float _waveformScrollPos;     // 0..1 — playback head position
        private bool _waveformPlaying;

        // ── Most-recent-event tracking ────────────────────────────────────────
        private EarthquakeEvent? _mostRecentEvent;

        // ── HUD element refs ─────────────────────────────────────────────────
        private HudDropdown? _ddStation;
        private HudDropdown? _ddEvent;
        private HudLabel? _lblZoom;
        private HudLabel? _lblStatus;
        private HudLabel? _lblEventInfo;
        private HudButtonGroup? _grpMapStyle;

        // ── Waveform panel ───────────────────────────────────────────────────
        private WaveformPanel _waveformPanel;

        // ────────────────────────────────────────────────────────────────────
        public SeismogramMapControl()
        {
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

        private void InitializeComponents()
        {
            this.Size = new Size(1200, 800);
            this.BackColor = ThemeManager.Current.Background;

            // Waveform panel docked at the bottom (carved out first so Fill leaves it alone)
            _waveformPanel = new WaveformPanel { Height = 240, Dock = DockStyle.Bottom };
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
                // GPU markers recalculate per-frame from lat/lon — no rebuild needed
            };

            _glControl.MapPositionChanged += (lat, lon) =>
            {
                _currentLat = lat;
                _currentLon = lon;
                UpdateStatusLabels();
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

            // ── Legend panel (bottom-left) ────────────────────────────────────
            var legendPanel = new HudPanel
            {
                Id         = "legend",
                Title      = "Legend",
                Anchor     = HudAnchor.LeftCenter,
                Width      = 225f,
                MarginX    = 10,
                MarginY    = 14,
                Collapsible = true,
                Collapsed   = false
            };

            // Station colours
            legendPanel.Elements.Add(new HudLegendRow { IsHeader = true, Label = "Stations" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(0, 210, 210),   Label = "Normal \u2014 no recent events" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(255, 200, 0),   Label = "Elevated \u2014 M\u22652.0 / 24 h" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(255, 80, 0),    Label = "Active \u2014 M\u22653.0 / 24 h" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(120, 130, 150), Label = "Inactive" });
            legendPanel.Elements.Add(new HudSeparator());

            // Earthquake ring colours
            legendPanel.Elements.Add(new HudLegendRow { IsHeader = true, Label = "Earthquake rings" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(40, 200, 255),  Label = "M < 3.0" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(255, 200, 0),   Label = "M 3.0 \u2013 5.0" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(255, 80, 0),    Label = "M \u2265 5.0" });
            legendPanel.Elements.Add(new HudSeparator());

            // Marker symbols
            legendPanel.Elements.Add(new HudLegendRow { IsHeader = true, Label = "Markers" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(255, 255, 255),  Label = "\u25b2 Selected station" });
            legendPanel.Elements.Add(new HudLegendRow { DotColor = Color.FromArgb(200, 230, 255),  Label = "\u25cb Most recent event" });

            _hudSystem.AddPanel(legendPanel);

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
                        RebuildStationMarkers();
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

            // Track the most-recent event (by origin time) for the fast-pulse marker
            _mostRecentEvent = _events.Count > 0
                ? _events.OrderByDescending(e => e.OriginTime).First()
                : null;

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
                var capturedEvent    = _selectedEvent;

                if (_glControl.HostControl.IsHandleCreated)
                    _glControl.HostControl.BeginInvoke(new Action(() =>
                    {
                        if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
                        if (capturedWaveform == null) return;
                        _waveformPanel.SetWaveform(capturedWaveform, capturedStation);
                        _waveformPanel.SetEvent(capturedEvent);
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
            RebuildStationMarkers();
        }

        private void OnEventSelectionChanged(int idx)
        {
            var recent = _events.Take(40).ToList();
            _selectedEvent = idx >= 0 && idx < recent.Count ? recent[idx] : null;
            UpdateEventInfoLabel();
            _epicenterRingPhase = 0f;
            RebuildEpicenterMarkers();
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
            _waveformPanel.SetEvent(_selectedEvent);
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

        private void RebuildStationMarkers()
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            var entries = _stations.Select(s =>
            {
                var col = GetStationColor(s.ActivityLevel, s == _selectedStation);
                return new StationMarkerEntry(
                    (float)s.Latitude, (float)s.Longitude,
                    col.ToArgb(),
                    s == _selectedStation);
            }).ToArray();
            _glControl.SetStationMarkers(entries);
        }

        private void EpicenterAnimTick(object? sender, EventArgs e)
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            _epicenterRingPhase    = (_epicenterRingPhase    + 0.018f) % 1.0f;
            _mostRecentRingPhase   = (_mostRecentRingPhase   + 0.030f) % 1.0f;
            if (_selectedEvent != null || _mostRecentEvent != null)
                _glControl.SetMarkerAnimPhase(_epicenterRingPhase, _mostRecentRingPhase);
        }

        private void RebuildEpicenterMarkers()
        {
            if (_isShuttingDown || _isDisposed || IsDisposed || Disposing) return;
            var _epicenterEntries = new System.Collections.Generic.List<EpicenterMarkerEntry>();

            // Most-recent-event (when different from selected)
            if (_mostRecentEvent != null && _mostRecentEvent != _selectedEvent)
            {
                var mrColor = Color.FromArgb(200, 200, 230, 255);
                _epicenterEntries.Add(new EpicenterMarkerEntry(
                    (float)_mostRecentEvent.Latitude, (float)_mostRecentEvent.Longitude,
                    mrColor.ToArgb(), (float)_mostRecentEvent.Magnitude, true));
            }

            // Selected event
            if (_selectedEvent != null)
            {
                var mag = _selectedEvent.Magnitude;
                var col = mag >= 5.0 ? Color.FromArgb(255, 80, 0)
                        : mag >= 3.0 ? Color.FromArgb(255, 200, 0)
                                     : Color.FromArgb(40, 200, 255);
                _epicenterEntries.Add(new EpicenterMarkerEntry(
                    (float)_selectedEvent.Latitude, (float)_selectedEvent.Longitude,
                    col.ToArgb(), (float)mag, false));
            }

            _glControl.SetEpicenterMarkers(_epicenterEntries.ToArray());
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
            RebuildStationMarkers();
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

        // ════════════════════════════════════════════════════════════════════
        // Color helpers
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
        private SeismogramData?  _waveform;
        private SeismicStation?  _station;
        private EarthquakeEvent? _event;
        private float            _scrollPos;  // 0..1 — normalised playback head
        private bool             _playing;

        // Visible window: fraction of total samples shown at once
        private const float WindowFraction = 0.25f;

        // Layout zones (pixels)
        private const int TopHeaderH  = 28;
        private const int BottomAxisH = 22;
        private const int LeftAxisW   = 45;
        private const int RightMargin = 8;

        private static readonly Color BgColor       = Color.FromArgb(18, 22, 30);
        private static readonly Color GridColor      = Color.FromArgb(32, 45, 62);
        private static readonly Color BaselineColor  = Color.FromArgb(55, 72, 95);
        private static readonly Color TraceColor     = Color.FromArgb(0, 205, 225);
        private static readonly Color HighColor      = Color.FromArgb(255, 140, 20);
        private static readonly Color CursorColor    = Color.FromArgb(230, 240, 255);
        private static readonly Color TextColor      = Color.FromArgb(165, 185, 210);
        private static readonly Color LiveColor      = Color.FromArgb(60, 220, 100);
        private static readonly Color AxisColor      = Color.FromArgb(55, 72, 95);
        private static readonly Color HeaderSepColor = Color.FromArgb(36, 52, 72);

        internal WaveformPanel()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint  |
                          ControlStyles.UserPaint, true);
            this.BackColor = BgColor;
        }

        internal void SetWaveform(SeismogramData? data, SeismicStation? station)
        {
            _waveform  = data;
            _station   = station;
            _scrollPos = 0f;
            Invalidate();
        }

        internal void SetEvent(EarthquakeEvent? ev)
        {
            _event = ev;
            Invalidate();
        }

        // ── Visible-window helper ───────────────────────────────────────────────
        private (DateTime windowStart, TimeSpan windowDuration, int startSample, int winLen)?
            GetVisibleWindow()
        {
            if (_waveform == null || !_waveform.HasData || _waveform.SampleRateHz <= 0)
                return null;

            int total  = _waveform.Samples.Length;
            int winLen = Math.Max(1, (int)(total * WindowFraction));
            int start  = (int)(_scrollPos * (total - winLen));
            start = Math.Clamp(start, 0, Math.Max(0, total - winLen));

            var windowStart    = _waveform.StartTime.AddSeconds(start / _waveform.SampleRateHz);
            var windowDuration = TimeSpan.FromSeconds(winLen / _waveform.SampleRateHz);

            return (windowStart, windowDuration, start, winLen);
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
            var g  = e.Graphics;
            g.Clear(BgColor);
            var rc = this.ClientRectangle;

            var contentRect = new Rectangle(
                LeftAxisW,
                TopHeaderH + 2,
                Math.Max(1, rc.Width  - LeftAxisW   - RightMargin),
                Math.Max(1, rc.Height - TopHeaderH  - BottomAxisH - 4));

            DrawHeaderBar(g, rc, contentRect);

            if (_waveform == null || !_waveform.HasData)
            {
                DrawEmptyState(g, rc);
                DrawBorder(g, rc);
                return;
            }

            DrawGrid(g, contentRect);
            DrawBaseline(g, contentRect);
            DrawAmplitudeAxis(g, contentRect);
            DrawTimestampAxis(g, contentRect);
            DrawTrace(g, contentRect);
            DrawEventMarker(g, contentRect);
            DrawCursor(g, contentRect);
            DrawBorder(g, rc);
        }

        private static void DrawEmptyState(Graphics g, Rectangle rc)
        {
            using var font  = new Font("Segoe UI", 11f, FontStyle.Regular);
            using var brush = new SolidBrush(Color.FromArgb(90, 110, 140));
            string msg  = "No waveform data  ·  Select a station and click Load Waveform";
            var    size = g.MeasureString(msg, font);
            g.DrawString(msg, font, brush,
                (rc.Width  - size.Width)  / 2f,
                TopHeaderH + (rc.Height - TopHeaderH - size.Height) / 2f);
        }

        private static void DrawGrid(Graphics g, Rectangle contentRect)
        {
            using var pen = new Pen(GridColor, 1f);
            int gridCols = 12;
            int gridRows = 4;
            for (int c = 1; c < gridCols; c++)
            {
                int x = contentRect.Left + contentRect.Width * c / gridCols;
                g.DrawLine(pen, x, contentRect.Top, x, contentRect.Bottom);
            }
            for (int r = 1; r < gridRows; r++)
            {
                int y = contentRect.Top + contentRect.Height * r / gridRows;
                g.DrawLine(pen, contentRect.Left, y, contentRect.Right, y);
            }
        }

        private static void DrawBaseline(Graphics g, Rectangle contentRect)
        {
            int mid = contentRect.Top + contentRect.Height / 2;
            using var pen = new Pen(BaselineColor, 1f);
            g.DrawLine(pen, contentRect.Left, mid, contentRect.Right, mid);
        }

        private void DrawTrace(Graphics g, Rectangle contentRect)
        {
            var win = GetVisibleWindow();
            if (!win.HasValue) return;

            float[] samples    = _waveform!.NormalizedSamples();
            int     start      = win.Value.startSample;
            int     winLen     = win.Value.winLen;
            int     targetCount = contentRect.Width;
            var     span       = new ArraySegment<float>(samples, start, winLen);
            float[] disp       = Decimate(span, targetCount);

            float mid          = contentRect.Top + contentRect.Height / 2f;
            float hRange       = contentRect.Height * 0.44f;
            float ampThreshold = 0.65f;

            var points = new PointF[disp.Length];
            for (int i = 0; i < disp.Length; i++)
                points[i] = new PointF(contentRect.Left + i, mid - disp[i] * hRange);

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

        // ── Earthquake event origin marker ───────────────────────────────────

        private void DrawEventMarker(Graphics g, Rectangle contentRect)
        {
            if (_event == null || _waveform == null || !_waveform.HasData) return;

            var win = GetVisibleWindow();
            if (!win.HasValue) return;

            double frac = (_event.OriginTime - win.Value.windowStart).TotalSeconds
                          / win.Value.windowDuration.TotalSeconds;
            if (frac < 0.0 || frac > 1.0) return;

            float  x   = contentRect.Left + (float)(frac * contentRect.Width);
            double mag = _event.Magnitude;
            Color  mc  = mag >= 5.0 ? Color.FromArgb(255, 100, 40)
                       : mag >= 3.0 ? Color.FromArgb(255, 210, 40)
                       : Color.FromArgb(80, 210, 255);

            using var dashPen = new Pen(Color.FromArgb(210, mc.R, mc.G, mc.B), 1.5f)
                { DashStyle = DashStyle.Dash };
            g.DrawLine(dashPen, x, contentRect.Top, x, contentRect.Bottom);

            using var font  = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(230, mc.R, mc.G, mc.B));
            string lbl = $"M{mag:F1}";
            var    sz  = g.MeasureString(lbl, font);
            float  tx  = Math.Clamp(x - sz.Width / 2f,
                (float)contentRect.Left,
                Math.Max((float)contentRect.Left, contentRect.Right - sz.Width));
            g.DrawString(lbl, font, brush, tx, contentRect.Top + 2f);
        }

        // ── Playback cursor ───────────────────────────────────────────────────

        private void DrawCursor(Graphics g, Rectangle contentRect)
        {
            if (!_playing) return;
            float x = contentRect.Left + _scrollPos * contentRect.Width;
            using var glow = new Pen(Color.FromArgb(50,  CursorColor.R, CursorColor.G, CursorColor.B), 5f);
            using var line = new Pen(Color.FromArgb(200, CursorColor.R, CursorColor.G, CursorColor.B), 1.5f);
            g.DrawLine(glow, x, contentRect.Top, x, contentRect.Bottom);
            g.DrawLine(line, x, contentRect.Top, x, contentRect.Bottom);
        }

        // ── Header bar ───────────────────────────────────────────────────────

        private void DrawHeaderBar(Graphics g, Rectangle rc, Rectangle contentRect)
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(24, 30, 42));
            g.FillRectangle(bgBrush, 0, 0, rc.Width, TopHeaderH);

            using var sepPen = new Pen(HeaderSepColor, 1f);
            g.DrawLine(sepPen, 0, TopHeaderH, rc.Width, TopHeaderH);

            using var fontMain = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            using var fontDim  = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var fontBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var brushMain = new SolidBrush(TextColor);
            using var brushDim  = new SolidBrush(Color.FromArgb(100, 130, 165));

            float cy = (TopHeaderH - fontMain.GetHeight(g)) / 2f;

            // ── Left: station identity ──
            if (_station != null)
            {
                string stationLabel = $"{_station.Network}.{_station.StationCode}  \u2013  {_station.SiteName}";
                g.DrawString(stationLabel, fontBold, brushMain, LeftAxisW + 2f, cy - 1f);
                if (_waveform != null)
                {
                    var    stSize  = g.MeasureString(stationLabel, fontBold);
                    string chLabel = $"  {_waveform.Channel} \u00b7 {_waveform.SampleRateHz:F1} sps";
                    g.DrawString(chLabel, fontDim, brushDim, LeftAxisW + 2f + stSize.Width - 4f, cy + 1f);
                }
            }
            else if (_waveform == null)
            {
                g.DrawString("No waveform loaded", fontDim, brushDim, LeftAxisW + 2f, cy);
            }

            // ── Centre: visible UTC time window ──
            var win = GetVisibleWindow();
            if (win.HasValue)
            {
                var    ws       = win.Value.windowStart;
                var    we       = ws + win.Value.windowDuration;
                string datePart = ws.Date == we.Date
                    ? ws.ToString("yyyy-MM-dd")
                    : $"{ws:MM/dd} \u2013 {we:MM/dd}";
                string timeFmt    = win.Value.windowDuration.TotalHours < 1.0 ? "HH:mm:ss" : "HH:mm";
                string centreText = $"{datePart}  \u00b7  {ws.ToString(timeFmt)} \u2013 {we.ToString(timeFmt)} UTC";
                var    cSize      = g.MeasureString(centreText, fontMain);
                g.DrawString(centreText, fontMain, brushMain, (rc.Width - cSize.Width) / 2f, cy);
            }

            // ── Right: live/age badge + play state ──
            if (_waveform != null)
            {
                bool   isLive = _waveform.EndTime >= DateTime.UtcNow.AddMinutes(-5);
                string freshStr;
                Color  freshColor;
                if (isLive)
                {
                    freshStr   = "\u25cf LIVE";
                    freshColor = LiveColor;
                }
                else
                {
                    var age = DateTime.UtcNow - _waveform.EndTime;
                    freshStr = age.TotalDays  >= 1 ? $"\u23f0 {(int)age.TotalDays}d ago"
                             : age.TotalHours >= 1 ? $"\u23f0 {(int)age.TotalHours}h ago"
                             : $"\u23f0 {(int)age.TotalMinutes}m ago";
                    freshColor = Color.FromArgb(140, 160, 185);
                }

                string playStr = _playing ? "  \u25b6 PLAYING" : "  \u23f8 [SPACE]";
                using var freshBrush = new SolidBrush(freshColor);
                using var playBrush  = new SolidBrush(Color.FromArgb(180,
                    CursorColor.R, CursorColor.G, CursorColor.B));
                var   freshSize = g.MeasureString(freshStr, fontMain);
                var   playSize  = g.MeasureString(playStr,  fontMain);
                float rx        = rc.Width - freshSize.Width - playSize.Width - RightMargin - 4f;
                g.DrawString(freshStr, fontMain, freshBrush, rx, cy);
                g.DrawString(playStr,  fontMain, playBrush,  rx + freshSize.Width - 4f, cy);
            }
        }

        // ── Y-axis amplitude labels ───────────────────────────────────────────

        private static void DrawAmplitudeAxis(Graphics g, Rectangle contentRect)
        {
            using var font    = new Font("Segoe UI", 7f, FontStyle.Regular);
            using var brush   = new SolidBrush(Color.FromArgb(100, 130, 165));
            using var rulePen = new Pen(AxisColor, 1f);

            g.DrawLine(rulePen, contentRect.Left, contentRect.Top, contentRect.Left, contentRect.Bottom);

            string[] labels = { "+100", "+50", "0", "\u221250", "\u2212100" };
            for (int i = 0; i < labels.Length; i++)
            {
                float frac = i / (float)(labels.Length - 1);
                float y    = contentRect.Top + frac * contentRect.Height;
                var   sz   = g.MeasureString(labels[i], font);
                g.DrawString(labels[i], font, brush,
                    contentRect.Left - sz.Width - 3f, y - sz.Height / 2f);
                using var tickPen = new Pen(AxisColor, 1f);
                g.DrawLine(tickPen, contentRect.Left - 3, (int)y, contentRect.Left, (int)y);
            }
        }

        // ── X-axis timestamp labels ───────────────────────────────────────────

        private void DrawTimestampAxis(Graphics g, Rectangle contentRect)
        {
            var win = GetVisibleWindow();
            if (!win.HasValue) return;

            var    windowStart    = win.Value.windowStart;
            var    windowDuration = win.Value.windowDuration;
            string fmt = windowDuration.TotalHours >= 4.0 ? "MM/dd HH:mm"
                       : windowDuration.TotalHours >= 1.0 ? "HH:mm"
                       : "HH:mm:ss";

            using var font    = new Font("Segoe UI", 7f, FontStyle.Regular);
            using var brush   = new SolidBrush(Color.FromArgb(100, 130, 165));
            using var tickPen = new Pen(AxisColor, 1f);
            using var rulePen = new Pen(AxisColor, 1f);

            int axisY = contentRect.Bottom + 1;
            g.DrawLine(rulePen, contentRect.Left, axisY, contentRect.Right, axisY);

            const int ticks = 8;
            for (int i = 0; i <= ticks; i++)
            {
                float  frac = i / (float)ticks;
                float  x    = contentRect.Left + frac * contentRect.Width;
                var    ts   = windowStart.AddSeconds(frac * windowDuration.TotalSeconds);
                string lbl  = ts.ToString(fmt);

                g.DrawLine(tickPen, (int)x, axisY, (int)x, axisY + 4);

                var   sz = g.MeasureString(lbl, font);
                float tx = Math.Clamp(x - sz.Width / 2f,
                    (float)contentRect.Left,
                    Math.Max((float)contentRect.Left, contentRect.Right - sz.Width));
                g.DrawString(lbl, font, brush, tx, axisY + 5f);
            }
        }

        // ── Border ────────────────────────────────────────────────────────────

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
