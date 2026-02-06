#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;

namespace WeatherImageGenerator.Services.BootChecks
{
    // ═══════════════════════════════════════════════════════════════════
    //  1. AppSettings verification & repair
    // ═══════════════════════════════════════════════════════════════════
    public class AppSettingsCheck : BootCheck
    {
        public override string Name => "Configuration";
        public override string Description => "Verify appsettings.json exists and contains all required sections";

        /// <summary>The validated settings after the check passes.</summary>
        public AppSettings? LoadedSettings { get; private set; }

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            var (settings, repairs) = DefaultSettingsGenerator.EnsureValidSettings();
            LoadedSettings = settings;

            if (repairs.Count == 0)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = "appsettings.json is valid"
                });
            }

            return Task.FromResult(new BootCheckResult
            {
                Name = Name,
                Status = BootCheckStatus.Repaired,
                StatusMessage = $"Repaired {repairs.Count} issue(s)",
                Detail = string.Join("\n", repairs)
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  2. FFmpeg verification
    // ═══════════════════════════════════════════════════════════════════
    public class FFmpegCheck : BootCheck
    {
        public override string Name => "FFmpeg";
        public override string Description => "Verify FFmpeg binaries are available";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                FFmpegLocator.ConfigureFromSettings();
                bool valid = FFmpegLocator.ValidateConfiguration(out string message);

                if (valid && File.Exists(FFmpegLocator.GetFFmpegPath()))
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = "FFmpeg is available",
                        Detail = message
                    });
                }

                // FFmpeg not physically present but set to Bundled → it will auto-download
                if (FFmpegLocator.CurrentSource == FFmpegSource.Bundled && valid)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = "FFmpeg will be downloaded on first use",
                        Detail = message
                    });
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = "FFmpeg not found — video features will be limited",
                    Detail = message
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"FFmpeg check error: {ex.Message}",
                    Error = ex
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  3. Map tile cache verification
    // ═══════════════════════════════════════════════════════════════════
    public class MapTileCacheCheck : BootCheck
    {
        public override string Name => "Map Tile Cache";
        public override string Description => "Verify map tile cache directory exists and is accessible";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var cacheDir = config.OpenMap?.TileCacheDirectory ?? "MapCache";

                // Resolve relative paths
                if (!Path.IsPathRooted(cacheDir))
                    cacheDir = Path.Combine(Directory.GetCurrentDirectory(), cacheDir);

                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Repaired,
                        StatusMessage = "Created map cache directory",
                        Detail = cacheDir
                    });
                }

                // Count cached tiles for info
                var files = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories);
                var sizeMb = 0L;
                foreach (var f in files)
                {
                    try { sizeMb += new FileInfo(f).Length; } catch { }
                }
                var sizeMbStr = (sizeMb / 1024.0 / 1024.0).ToString("F1");

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"Cache OK ({files.Length} tiles, {sizeMbStr} MB)",
                    Detail = cacheDir
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Cache check failed: {ex.Message}",
                    Error = ex
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  4. Open Meteo API connectivity
    // ═══════════════════════════════════════════════════════════════════
    public class OpenMeteoCheck : BootCheck
    {
        public override string Name => "Open Meteo API";
        public override string Description => "Verify connectivity with the Open Meteo weather service";

        public override async Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // Hit the geocoding API with a simple test query
                var response = await http.GetAsync(
                    "https://geocoding-api.open-meteo.com/v1/search?name=Montreal&count=1",
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = "Open Meteo API is reachable"
                    };
                }

                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Open Meteo responded with HTTP {(int)response.StatusCode}"
                };
            }
            catch (TaskCanceledException)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = "Open Meteo API timed out — will retry during cycle"
                };
            }
            catch (Exception ex)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Open Meteo unreachable: {ex.Message}"
                };
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  5. ECCC (Environment & Climate Change Canada) connectivity
    // ═══════════════════════════════════════════════════════════════════
    public class ECCCCheck : BootCheck
    {
        public override string Name => "ECCC Weather";
        public override string Description => "Verify connectivity with Environment Canada weather service";

        public override async Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // Test with the GeoMet WMS capabilities endpoint (lightweight)
                var response = await http.GetAsync(
                    "https://geo.weather.gc.ca/geomet?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities&LAYERS=RADAR_1KM_RRAI",
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = "ECCC GeoMet service is reachable"
                    };
                }

                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"ECCC responded with HTTP {(int)response.StatusCode}"
                };
            }
            catch (TaskCanceledException)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = "ECCC timed out — radar features may be delayed"
                };
            }
            catch (Exception ex)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"ECCC unreachable: {ex.Message}"
                };
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  6. Alert Ready (NAAD) connectivity
    // ═══════════════════════════════════════════════════════════════════
    public class AlertReadyCheck : BootCheck
    {
        public override string Name => "Alert Ready (NAAD)";
        public override string Description => "Verify Alert Ready emergency alert feeds are configured";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var ar = config.AlertReady;

                if (ar == null || !ar.Enabled)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Alert Ready is disabled in settings"
                    });
                }

                if (ar.FeedUrls == null || ar.FeedUrls.Count == 0)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = "Alert Ready enabled but no feed URLs configured"
                    });
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"Alert Ready configured ({ar.FeedUrls.Count} feed(s))",
                    Detail = string.Join(", ", ar.FeedUrls)
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Alert Ready check error: {ex.Message}",
                    Error = ex
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  7. Output directories verification
    // ═══════════════════════════════════════════════════════════════════
    public class OutputDirectoriesCheck : BootCheck
    {
        public override string Name => "Output Directories";
        public override string Description => "Verify output directories for images, video, and logs exist";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            var created = new List<string>();

            try
            {
                var config = ConfigManager.LoadConfig();
                var basePath = Directory.GetCurrentDirectory();

                // Image output directory
                var imgDir = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                if (!Path.IsPathRooted(imgDir))
                    imgDir = Path.Combine(basePath, imgDir);
                if (!Directory.Exists(imgDir))
                {
                    Directory.CreateDirectory(imgDir);
                    created.Add("WeatherImages");
                }

                // Video output directory
                var vidDir = config.Video?.OutputDirectory ?? "WeatherImages";
                if (!Path.IsPathRooted(vidDir))
                    vidDir = Path.Combine(basePath, vidDir);
                if (!Directory.Exists(vidDir))
                {
                    Directory.CreateDirectory(vidDir);
                    created.Add("Video output");
                }

                // Logs directory
                var logDir = Path.Combine(basePath, "logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                    created.Add("logs");
                }

                // Icons directory
                var iconDir = Path.Combine(basePath, "WeatherImages", "Icons");
                if (!Directory.Exists(iconDir))
                {
                    Directory.CreateDirectory(iconDir);
                    created.Add("Icons");
                }

                // Music directory
                var musicDir = Path.Combine(basePath, "Music");
                if (!Directory.Exists(musicDir))
                {
                    Directory.CreateDirectory(musicDir);
                    created.Add("Music");
                }

                if (created.Count > 0)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Repaired,
                        StatusMessage = $"Created {created.Count} missing director{(created.Count == 1 ? "y" : "ies")}",
                        Detail = string.Join(", ", created)
                    });
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = "All output directories exist"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Failed,
                    StatusMessage = $"Directory check failed: {ex.Message}",
                    Error = ex
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  8. WebUI port check
    // ═══════════════════════════════════════════════════════════════════
    public class WebUICheck : BootCheck
    {
        public override string Name => "Web UI";
        public override string Description => "Verify Web UI configuration";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var webui = config.WebUI;

                if (webui == null || !webui.Enabled)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Web UI is disabled"
                    });
                }

                if (webui.Port < 1 || webui.Port > 65535)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = $"Invalid port {webui.Port} — will use default 5000"
                    });
                }

                // Check if wwwroot exists
                var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                if (!Directory.Exists(wwwroot))
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = "wwwroot directory missing — Web UI may not work"
                    });
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"Web UI configured on port {webui.Port}"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Web UI check error: {ex.Message}",
                    Error = ex
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  9. .NET runtime & environment check
    // ═══════════════════════════════════════════════════════════════════
    public class EnvironmentCheck : BootCheck
    {
        public override string Name => "Environment";
        public override string Description => "Verify .NET runtime and system environment";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
                var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
                var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"{runtime} ({arch})",
                    Detail = os
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"Environment info unavailable: {ex.Message}"
                });
            }
        }
    }
}
