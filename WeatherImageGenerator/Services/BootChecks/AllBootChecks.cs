#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    //  0. Single instance check — prevent multiple WSG instances
    // ═══════════════════════════════════════════════════════════════════
    public class SingleInstanceCheck : BootCheck
    {
        public override string Name => "Single Instance";
        public override string Description => "Ensure no other instance of WeatherImageGenerator is running";

        /// <summary>True if another instance was detected (fatal — app must exit).</summary>
        public bool IsDuplicate { get; private set; }

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentId = currentProcess.Id;
                var processName = currentProcess.ProcessName;

                var allProcesses = Process.GetProcessesByName(processName);
                var otherInstances = new List<Process>();

                foreach (var p in allProcesses)
                {
                    try
                    {
                        if (p.Id != currentId && !p.HasExited)
                            otherInstances.Add(p);
                    }
                    catch
                    {
                        // Access denied or process exited between check — ignore
                    }
                }

                if (otherInstances.Count > 0)
                {
                    IsDuplicate = true;
                    var pids = string.Join(", ", otherInstances.ConvertAll(p => p.Id.ToString()));
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Failed,
                        StatusMessage = $"Another instance is already running (PID: {pids})",
                        Detail = "Only one instance of WeatherImageGenerator can run at a time. Please close the other instance first.",
                        IsFatal = true
                    });
                }

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = "No other instance detected"
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Could not verify: {ex.Message}"
                });
            }
        }
    }

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
    //  3. Cache verification (all app caches in %LOCALAPPDATA%/WSG)
    // ═══════════════════════════════════════════════════════════════════
    public class CacheCheck : BootCheck
    {
        public override string Name => "Cache";
        public override string Description => "Verify application cache directories exist and report total usage";

        /// <summary>Root cache path: %LOCALAPPDATA%/WSG</summary>
        public static string GetCacheRoot() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG");

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var cacheRoot = GetCacheRoot();

                // Ensure root exists
                if (!Directory.Exists(cacheRoot))
                {
                    Directory.CreateDirectory(cacheRoot);
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Repaired,
                        StatusMessage = "Created cache directory",
                        Detail = cacheRoot
                    });
                }

                // Scan all subdirectories for total size
                long totalFiles = 0;
                long totalBytes = 0;
                var subdirs = new List<string>();

                try
                {
                    foreach (var dir in Directory.GetDirectories(cacheRoot))
                    {
                        var dirName = Path.GetFileName(dir);
                        long dirBytes = 0;
                        long dirFiles = 0;
                        try
                        {
                            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                            dirFiles = files.Length;
                            foreach (var f in files)
                            {
                                try { dirBytes += new FileInfo(f).Length; } catch { }
                            }
                        }
                        catch { /* skip inaccessible dirs */ }

                        totalFiles += dirFiles;
                        totalBytes += dirBytes;

                        if (dirFiles > 0)
                        {
                            var sizeMb = (dirBytes / 1024.0 / 1024.0).ToString("F1");
                            subdirs.Add($"{dirName}: {dirFiles} files ({sizeMb} MB)");
                        }
                    }

                    // Also count files directly in root
                    try
                    {
                        var rootFiles = Directory.GetFiles(cacheRoot);
                        totalFiles += rootFiles.Length;
                        foreach (var f in rootFiles)
                        {
                            try { totalBytes += new FileInfo(f).Length; } catch { }
                        }
                    }
                    catch { }
                }
                catch { /* ignore */ }

                var totalSizeMb = (totalBytes / 1024.0 / 1024.0).ToString("F1");
                var statusMsg = totalFiles > 0
                    ? $"Cache OK ({totalFiles} files, {totalSizeMb} MB)"
                    : "Cache OK (empty)";

                var detail = subdirs.Count > 0
                    ? $"{cacheRoot}\n{string.Join("\n", subdirs)}"
                    : cacheRoot;

                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Passed,
                    StatusMessage = statusMsg,
                    Detail = detail
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
    //  6b. NWS NOAA (US National Weather Service) connectivity
    // ═══════════════════════════════════════════════════════════════════
    public class NwsNoaaCheck : BootCheck
    {
        public override string Name => "NWS NOAA";
        public override string Description => "Verify NWS (National Weather Service) alert configuration and API connectivity";

        public override async Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var nws = config.Nws;

                if (nws == null || !nws.Enabled)
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "NWS alerts disabled in settings"
                    };
                }

                // Verify API base URL is configured
                var apiUrl = nws.ApiBaseUrl;
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Warning,
                        StatusMessage = "NWS enabled but no API base URL configured"
                    };
                }

                // Try a lightweight connectivity test against the NWS API
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(nws.UserAgent ?? "WSG/1.0");

                var testUrl = apiUrl.TrimEnd('/') + "/alerts/active?status=actual&limit=1";
                var response = await http.GetAsync(testUrl, ct);

                if (response.IsSuccessStatusCode)
                {
                    var detail = new List<string>();
                    if (nws.States != null && nws.States.Count > 0)
                        detail.Add($"States: {string.Join(", ", nws.States)}");
                    if (nws.Zones != null && nws.Zones.Count > 0)
                        detail.Add($"Zones: {string.Join(", ", nws.Zones)}");
                    if (!string.IsNullOrEmpty(nws.Point))
                        detail.Add($"Point: {nws.Point}");

                    return new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = "NWS API is reachable",
                        Detail = detail.Count > 0 ? string.Join(" | ", detail) : apiUrl
                    };
                }

                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"NWS API responded with HTTP {(int)response.StatusCode}"
                };
            }
            catch (TaskCanceledException)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = "NWS API timed out — will retry during cycle"
                };
            }
            catch (Exception ex)
            {
                return new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"NWS check error: {ex.Message}",
                    Error = ex
                };
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
    //  8b. WebUI Network Access (URL ACL, Firewall, UAC)
    // ═══════════════════════════════════════════════════════════════════
    public class WebUINetworkAccessCheck : BootCheck
    {
        public override string Name => "Web UI Network";
        public override string Description => "Verify URL ACL reservation, Windows Firewall rule, and elevation for remote Web UI access";

        public override Task<BootCheckResult> RunAsync(CancellationToken ct)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var webui = config.WebUI;

                // Skip entirely if Web UI is disabled
                if (webui == null || !webui.Enabled)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Web UI is disabled"
                    });
                }

                // Skip if remote access is not enabled — no ACL/firewall needed for localhost
                if (!webui.AllowRemoteAccess)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Skipped,
                        StatusMessage = "Remote access is disabled — localhost only"
                    });
                }

                int port = webui.Port > 0 ? webui.Port : 5000;
                string prefix = $"http://*:{port}/";
                string firewallRuleName = $"WeatherStillAPI_WebUI_{port}";

                bool hasUrlAcl = CheckUrlAcl(prefix);
                bool hasFirewall = CheckFirewallRule(firewallRuleName);
                bool isElevated = IsRunningAsAdmin();

                var issues = new List<string>();
                if (!hasUrlAcl) issues.Add("URL ACL missing");
                if (!hasFirewall) issues.Add("Firewall rule missing");

                if (issues.Count == 0)
                {
                    return Task.FromResult(new BootCheckResult
                    {
                        Name = Name,
                        Status = BootCheckStatus.Passed,
                        StatusMessage = $"Remote access ready (port {port})",
                        Detail = $"URL ACL: ✓ | Firewall: ✓ | Elevated: {(isElevated ? "Yes" : "No")}"
                    });
                }

                // Issues found — try auto-repair
                string detail = $"URL ACL: {(hasUrlAcl ? "✓" : "✗")} | Firewall: {(hasFirewall ? "✓" : "✗")} | Elevated: {(isElevated ? "Yes" : "No")}";

                if (isElevated)
                {
                    // We have admin rights — repair silently
                    bool repaired = TryRepairRemoteAccess(prefix, port, firewallRuleName, !hasUrlAcl, !hasFirewall);
                    if (repaired)
                    {
                        return Task.FromResult(new BootCheckResult
                        {
                            Name = Name,
                            Status = BootCheckStatus.Repaired,
                            StatusMessage = $"Configured remote access for port {port}",
                            Detail = $"Fixed: {string.Join(", ", issues)}"
                        });
                    }
                }

                // Not elevated or repair failed — report as warning
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"{string.Join(" & ", issues)} — UAC prompt on first start",
                    Detail = detail
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootCheckResult
                {
                    Name = Name,
                    Status = BootCheckStatus.Warning,
                    StatusMessage = $"Network check error: {ex.Message}",
                    Error = ex
                });
            }
        }

        /// <summary>
        /// Checks whether a URL ACL reservation exists for the given prefix.
        /// </summary>
        private static bool CheckUrlAcl(string prefix)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "http show urlacl",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                // netsh output includes lines like: "Reserved URL : http://*:5000/"
                return output.Contains(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Checks whether a Windows Firewall inbound rule exists with the given name.
        /// </summary>
        private static bool CheckFirewallRule(string ruleName)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return false;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                // If rule exists, output contains the rule name; if not, it says "No rules match"
                return output.Contains(ruleName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Checks whether the current process is running with administrator privileges.
        /// </summary>
        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>
        /// Attempts to repair missing URL ACL and/or firewall rule (requires elevation).
        /// </summary>
        private static bool TryRepairRemoteAccess(string prefix, int port, string firewallRuleName, bool fixAcl, bool fixFirewall)
        {
            try
            {
                bool success = true;

                if (fixAcl)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"http add urlacl url={prefix} user=Everyone",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(10000);
                        if (proc.ExitCode != 0) success = false;
                    }
                    else success = false;
                }

                if (fixFirewall)
                {
                    // Delete old rule first (ignore errors)
                    try
                    {
                        var delPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"advfirewall firewall delete rule name=\"{firewallRuleName}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        var delProc = System.Diagnostics.Process.Start(delPsi);
                        delProc?.WaitForExit(5000);
                    }
                    catch { }

                    var addPsi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"{firewallRuleName}\" dir=in action=allow protocol=TCP localport={port}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var proc = System.Diagnostics.Process.Start(addPsi);
                    if (proc != null)
                    {
                        proc.WaitForExit(10000);
                        if (proc.ExitCode != 0) success = false;
                    }
                    else success = false;
                }

                return success;
            }
            catch
            {
                return false;
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
