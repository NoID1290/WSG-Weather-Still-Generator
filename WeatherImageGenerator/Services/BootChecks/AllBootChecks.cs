#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
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

    // ═══════════════════════════════════════════════════════════════════
    //  10. App update check (GitHub releases)
    // ═══════════════════════════════════════════════════════════════════
    public class AppUpdateCheck : BootCheck
    {
        public override string Name => "App Update";
        public override string Description => "Check for newer application versions on GitHub";

        /// <summary>Update info if a newer version is available.</summary>
        public UpdateService.UpdateInfo? UpdateInfo { get; private set; }

        public override async Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                if (!config.CheckForUpdatesOnStartup)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Update check disabled in settings"
                    };
                }

                // Use a short timeout so the boot doesn't hang on slow networks
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                var info = await UpdateService.CheckForUpdatesAsync();
                UpdateInfo = info;

                if (!string.IsNullOrEmpty(info.Error))
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = $"Could not check: {info.Error}"
                    };
                }

                var currentVersion = UpdateService.GetCurrentVersion();

                if (info.IsUpdateAvailable)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = $"Update available: v{info.LatestVersion} (current: v{currentVersion})",
                        Detail = info.ReleaseName ?? info.ReleaseNotes
                    };
                }

                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = $"Up to date (v{currentVersion})"
                };
            }
            catch (OperationCanceledException)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = "Update check timed out"
                };
            }
            catch (Exception ex)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Update check failed: {ex.Message}"
                };
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  11. NAAD TCP stream connectivity
    // ═══════════════════════════════════════════════════════════════════
    public class NaadConnectionCheck : BootCheck
    {
        public override string Name => "NAAD Connection";
        public override string Description => "Verify TCP connectivity to NAAD alert streaming servers";

        public override async Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var ar = config.AlertReady;

                if (ar == null || !ar.Enabled)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Alert Ready is disabled"
                    };
                }

                var feedUrls = ar.FeedUrls;
                if (feedUrls == null || feedUrls.Count == 0)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = "No NAAD feed URLs configured"
                    };
                }

                // Try to connect to at least one NAAD TCP server
                var reachable = new List<string>();
                var unreachable = new List<string>();

                foreach (var url in feedUrls)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    try
                    {
                        var uri = new Uri(url);
                        var host = uri.Host;
                        var port = uri.Port > 0 ? uri.Port : 8080;

                        using var tcp = new TcpClient();
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                        await tcp.ConnectAsync(host, port, connectCts.Token);
                        tcp.Close();
                        reachable.Add($"{host}:{port}");
                    }
                    catch
                    {
                        try
                        {
                            var uri = new Uri(url);
                            unreachable.Add($"{uri.Host}:{uri.Port}");
                        }
                        catch
                        {
                            unreachable.Add(url);
                        }
                    }
                }

                if (reachable.Count > 0)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = $"{reachable.Count}/{reachable.Count + unreachable.Count} NAAD server(s) reachable",
                        Detail = string.Join(", ", reachable)
                    };
                }

                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"No NAAD servers reachable ({unreachable.Count} tried)",
                    Detail = string.Join(", ", unreachable)
                };
            }
            catch (Exception ex)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"NAAD check error: {ex.Message}",
                    Error = ex
                };
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  12. DLL dependency verification
    // ═══════════════════════════════════════════════════════════════════
    public class DependencyCheck : BootCheck
    {
        public override string Name => "Dependencies";
        public override string Description => "Verify all required DLL files are present";

        /// <summary>
        /// Project DLLs that must always be present (built as part of the solution).
        /// </summary>
        private static readonly string[] RequiredProjectDlls = new[]
        {
            "EAS.dll",
            "ECCC.dll",
            "OpenMap.dll",
            "OpenMeteo.dll",
            "WeatherShared.dll",
            "WSG.dll"
        };

        /// <summary>
        /// Third-party / NuGet DLLs that must be present at runtime.
        /// </summary>
        private static readonly string[] RequiredNuGetDlls = new[]
        {
            "SkiaSharp.dll",
            "Xabe.FFmpeg.dll",
            "Xabe.FFmpeg.Downloader.dll"
        };

        /// <summary>
        /// Native DLLs that must exist somewhere under runtimes/ for the current platform.
        /// </summary>
        private static readonly string[] RequiredNativeDlls = new[]
        {
            "libSkiaSharp.dll"
        };

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var missing = new List<string>();
                var found = 0;

                // Check project DLLs
                foreach (var dll in RequiredProjectDlls)
                {
                    if (File.Exists(Path.Combine(baseDir, dll)))
                        found++;
                    else
                        missing.Add(dll);
                }

                // Check NuGet DLLs
                foreach (var dll in RequiredNuGetDlls)
                {
                    if (File.Exists(Path.Combine(baseDir, dll)))
                        found++;
                    else
                        missing.Add(dll);
                }

                // Check native DLLs (search under runtimes/ for any matching file)
                var runtimesDir = Path.Combine(baseDir, "runtimes");
                foreach (var dll in RequiredNativeDlls)
                {
                    bool nativeFound = false;
                    if (Directory.Exists(runtimesDir))
                    {
                        var matches = Directory.GetFiles(runtimesDir, dll, SearchOption.AllDirectories);
                        if (matches.Length > 0)
                            nativeFound = true;
                    }
                    // Also check base directory as fallback
                    if (!nativeFound && File.Exists(Path.Combine(baseDir, dll)))
                        nativeFound = true;

                    if (nativeFound)
                        found++;
                    else
                        missing.Add($"{dll} (native)");
                }

                int total = RequiredProjectDlls.Length + RequiredNuGetDlls.Length + RequiredNativeDlls.Length;

                if (missing.Count == 0)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = $"All {total} required libraries present",
                        Detail = baseDir
                    });
                }

                // Determine severity — project DLLs missing is critical, NuGet/native is a warning
                bool hasCritical = false;
                foreach (var m in missing)
                {
                    foreach (var pdll in RequiredProjectDlls)
                    {
                        if (m == pdll) { hasCritical = true; break; }
                    }
                    if (hasCritical) break;
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = hasCritical ? BootCheckStatus.Failed : BootCheckStatus.Warning,
                    StatusMessage = $"{missing.Count} missing: {string.Join(", ", missing)}",
                    Detail = $"Found {found}/{total} — base: {baseDir}"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"DLL check error: {ex.Message}",
                    Error = ex
                });
            }
        }
    }
}
