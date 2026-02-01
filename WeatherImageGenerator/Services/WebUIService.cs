using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net;
using System.Reflection;
using WeatherImageGenerator.Utilities;
using WeatherImageGenerator.Models;
using OpenMeteo;

namespace WeatherImageGenerator.Services
{
    /// <summary>
    /// Manages the Web UI/API server for remote access
    /// </summary>
    public class WebUIService
    {
        private HttpListener? _httpListener;
        public event EventHandler? ServerStarted;
        public event EventHandler? ServerStopped;
        public event EventHandler<string>? ServerError;
        
        // Action handlers for controlling the desktop app
        public event EventHandler? StartCycleRequested;
        public event EventHandler? StopCycleRequested;
        public event EventHandler? GenerateStillRequested;
        public event EventHandler? GenerateVideoRequested;

        public int Port { get; set; } = 5000;
        public bool IsRunning { get; private set; } = false;

        // Cached weather data
        private WeatherForecast?[]? _cachedForecasts;
        private string[]? _cachedLocationNames;
        private DateTime _lastWeatherUpdate = DateTime.MinValue;

        /// <summary>
        /// Updates the cached weather data from the main application
        /// </summary>
        public void UpdateWeatherData(WeatherForecast?[] forecasts, string[] locationNames)
        {
            _cachedForecasts = forecasts;
            _cachedLocationNames = locationNames;
            _lastWeatherUpdate = DateTime.Now;
            Logger.Log($"WebUI: Weather data updated for {locationNames.Length} locations", Logger.LogLevel.Debug);
        }

        public WebUIService(int port = 5000)
        {
            Port = port;
        }

        /// <summary>
        /// Starts the web server with the configured port
        /// </summary>
        public void Start()
        {
            try
            {
                _httpListener = new HttpListener();
                // Use localhost (127.0.0.1) which doesn't require admin rights
                _httpListener.Prefixes.Add($"http://localhost:{Port}/");
                _httpListener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _httpListener.Start();
                IsRunning = true;
                Logger.Log($"Web UI server started on localhost:{Port}", Logger.LogLevel.Info);
                ServerStarted?.Invoke(this, EventArgs.Empty);

                // Start listening for requests asynchronously
                _ = ListenForRequests();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Logger.Log($"Failed to start Web UI server: {ex.Message}", Logger.LogLevel.Error);
                ServerError?.Invoke(this, ex.Message);
            }
        }

        private static bool IsListeningPort5000OrHigher(int port)
        {
            // Check if port is 5000 or higher, which may require admin rights for +
            return port >= 5000;
        }

        /// <summary>
        /// Starts the web server asynchronously with the configured port
        /// </summary>
        public async Task StartAsync()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://+:{Port}/");
                _httpListener.Start();
                IsRunning = true;
                Logger.Log($"Web UI server started on http://0.0.0.0:{Port}", Logger.LogLevel.Info);
                ServerStarted?.Invoke(this, EventArgs.Empty);

                // Start listening for requests
                await ListenForRequests();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Logger.Log($"Failed to start Web UI server: {ex.Message}", Logger.LogLevel.Error);
                ServerError?.Invoke(this, ex.Message);
            }
        }

        /// <summary>
        /// Stops the web server
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                if (_httpListener != null)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    IsRunning = false;
                    Logger.Log("Web UI server stopped", Logger.LogLevel.Info);
                    ServerStopped?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error stopping Web UI server: {ex.Message}", Logger.LogLevel.Error);
                ServerError?.Invoke(this, ex.Message);
            }
        }

        private async Task ListenForRequests()
        {
            while (IsRunning && _httpListener?.IsListening == true)
            {
                try
                {
                    HttpListenerContext context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex)
                {
                    if (IsRunning)
                    {
                        Logger.Log($"Error listening for requests: {ex.Message}", Logger.LogLevel.Debug);
                    }
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                string method = context.Request.HttpMethod;

                if (path == "/" || path == "/index.html")
                {
                    await ServeFile(context, "wwwroot/index.html", "text/html");
                }
                else if (path.StartsWith("/api/status"))
                {
                    GetStatus(context);
                }
                else if (path.StartsWith("/api/weather/current"))
                {
                    GetCurrentWeather(context);
                }
                else if (path.StartsWith("/api/weather/forecast"))
                {
                    GetWeatherForecast(context);
                }
                else if (path.StartsWith("/api/images/list"))
                {
                    GetImagesList(context);
                }
                else if (path.StartsWith("/api/images/"))
                {
                    string filename = path.Substring("/api/images/".Length);
                    await GetImage(context, filename);
                }
                else if (path.StartsWith("/api/settings/web") && method == "GET")
                {
                    GetWebSettings(context);
                }
                else if (path.StartsWith("/api/settings/web") && method == "POST")
                {
                    UpdateWebSettings(context);
                }
                else if (path.StartsWith("/api/config/full") && method == "GET")
                {
                    GetFullConfig(context);
                }
                else if (path.StartsWith("/api/config/locations") && method == "GET")
                {
                    GetLocations(context);
                }
                else if (path.StartsWith("/api/config/locations") && method == "POST")
                {
                    UpdateLocations(context);
                }
                else if (path.StartsWith("/api/config/general") && method == "POST")
                {
                    UpdateGeneralConfig(context);
                }
                else if (path.StartsWith("/api/config/image") && method == "POST")
                {
                    UpdateImageConfig(context);
                }
                else if (path.StartsWith("/api/config/video") && method == "POST")
                {
                    UpdateVideoConfig(context);
                }
                else if (path.StartsWith("/api/config/music") && method == "POST")
                {
                    UpdateMusicConfig(context);
                }
                else if (path.StartsWith("/api/config/alerts") && method == "POST")
                {
                    UpdateAlertConfig(context);
                }
                else if (path.StartsWith("/api/config/radar") && method == "POST")
                {
                    UpdateRadarConfig(context);
                }
                else if (path.StartsWith("/api/actions/start-cycle") && method == "POST")
                {
                    StartCycle(context);
                }
                else if (path.StartsWith("/api/actions/stop-cycle") && method == "POST")
                {
                    StopCycle(context);
                }
                else if (path.StartsWith("/api/actions/generate-still") && method == "POST")
                {
                    GenerateStill(context);
                }
                else if (path.StartsWith("/api/actions/generate-video") && method == "POST")
                {
                    GenerateVideo(context);
                }
                else if (path.StartsWith("/css/"))
                {
                    string filename = "wwwroot" + path;
                    await ServeFile(context, filename, "text/css");
                }
                else if (path.StartsWith("/js/"))
                {
                    string filename = "wwwroot" + path;
                    await ServeFile(context, filename, "application/javascript");
                }
                else
                {
                    RespondWithJson(context, new { error = "Not found" }, 404);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error handling request: {ex.Message}", Logger.LogLevel.Debug);
                try
                {
                    RespondWithJson(context, new { error = "Internal server error" }, 500);
                }
                catch { }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task ServeFile(HttpListenerContext context, string filePath, string contentType)
        {
            try
            {
                // Try multiple possible locations for wwwroot
                string? fullPath = null;
                string[] possibleLocations = new[]
                {
                    // Look in the application base directory
                    Path.Combine(AppContext.BaseDirectory, filePath),
                    // Look in the project directory (up from bin/Debug/net10.0-windows)
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", filePath),
                    // Look in current working directory
                    Path.Combine(Directory.GetCurrentDirectory(), filePath),
                    // Absolute path if file starts with /
                    filePath.TrimStart('/')
                };

                foreach (var location in possibleLocations)
                {
                    var normalizedPath = Path.GetFullPath(location);
                    if (File.Exists(normalizedPath))
                    {
                        fullPath = normalizedPath;
                        break;
                    }
                }

                if (fullPath != null && File.Exists(fullPath))
                {
                    context.Response.ContentType = contentType;
                    byte[] buffer = File.ReadAllBytes(fullPath);
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    Logger.Log($"Served file: {filePath}", Logger.LogLevel.Debug);
                }
                else
                {
                    Logger.Log($"File not found: {filePath}", Logger.LogLevel.Debug);
                    RespondWithJson(context, new { error = "File not found", requestedPath = filePath }, 404);
                }
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void RespondWithJson(HttpListenerContext context, object data, int statusCode = 200)
        {
            try
            {
                string json = JsonSerializer.Serialize(data);
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error responding with JSON: {ex.Message}", Logger.LogLevel.Debug);
            }
        }

        // Endpoint handlers
        private void GetCurrentWeather(HttpListenerContext context)
        {
            try
            {
                if (_cachedForecasts == null || _cachedForecasts.Length == 0)
                {
                    RespondWithJson(context, new
                    {
                        timestamp = DateTime.UtcNow,
                        status = "no_data",
                        message = "No weather data available. Start a cycle to fetch weather.",
                        locations = new List<object>()
                    });
                    return;
                }

                var locations = new List<object>();
                for (int i = 0; i < _cachedForecasts.Length; i++)
                {
                    var forecast = _cachedForecasts[i];
                    var name = _cachedLocationNames != null && i < _cachedLocationNames.Length
                        ? _cachedLocationNames[i]
                        : $"Location {i + 1}";

                    if (forecast?.Current != null)
                    {
                        var current = forecast.Current;
                        var units = forecast.CurrentUnits;
                        locations.Add(new
                        {
                            name,
                            temperature = current.Temperature_2m,
                            temperatureUnit = units?.Temperature_2m ?? "°C",
                            feelsLike = current.Apparent_temperature,
                            humidity = current.Relativehumidity_2m,
                            weatherCode = current.Weathercode,
                            weatherDescription = GetWeatherDescription(current.Weathercode ?? 0),
                            windSpeed = current.Windspeed_10m,
                            windSpeedUnit = units?.Windspeed_10m ?? "km/h",
                            windDirection = current.Winddirection_10m,
                            windDirectionCardinal = DegreesToCardinal(current.Winddirection_10m),
                            precipitation = current.Precipitation,
                            cloudCover = current.Cloudcover,
                            pressure = current.Pressure_msl,
                            isDay = current.Is_day == 1,
                            time = current.Time
                        });
                    }
                    else
                    {
                        locations.Add(new
                        {
                            name,
                            error = "No current data available"
                        });
                    }
                }

                RespondWithJson(context, new
                {
                    timestamp = DateTime.UtcNow,
                    lastUpdate = _lastWeatherUpdate,
                    status = "ok",
                    locationCount = locations.Count,
                    locations
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in GetCurrentWeather: {ex.Message}", Logger.LogLevel.Error);
                RespondWithJson(context, new { status = "error", message = ex.Message }, 500);
            }
        }

        private void GetWeatherForecast(HttpListenerContext context)
        {
            try
            {
                if (_cachedForecasts == null || _cachedForecasts.Length == 0)
                {
                    RespondWithJson(context, new
                    {
                        timestamp = DateTime.UtcNow,
                        status = "no_data",
                        message = "No forecast data available. Start a cycle to fetch weather.",
                        locations = new List<object>()
                    });
                    return;
                }

                var locations = new List<object>();
                for (int i = 0; i < _cachedForecasts.Length; i++)
                {
                    var forecast = _cachedForecasts[i];
                    var name = _cachedLocationNames != null && i < _cachedLocationNames.Length
                        ? _cachedLocationNames[i]
                        : $"Location {i + 1}";

                    if (forecast?.Daily != null && forecast.Daily.Time != null)
                    {
                        var daily = forecast.Daily;
                        var dailyForecasts = new List<object>();

                        for (int d = 0; d < Math.Min(7, daily.Time.Length); d++)
                        {
                            dailyForecasts.Add(new
                            {
                                date = daily.Time[d],
                                weatherCode = daily.Weathercode?[d],
                                weatherDescription = GetWeatherDescription((int)(daily.Weathercode?[d] ?? 0)),
                                tempMax = daily.Temperature_2m_max?[d],
                                tempMin = daily.Temperature_2m_min?[d],
                                precipitationSum = daily.Precipitation_sum?[d],
                                windSpeedMax = daily.Windspeed_10m_max?[d],
                                sunrise = daily.Sunrise?[d],
                                sunset = daily.Sunset?[d]
                            });
                        }

                        locations.Add(new
                        {
                            name,
                            dailyForecasts
                        });
                    }
                    else
                    {
                        locations.Add(new
                        {
                            name,
                            error = "No forecast data available"
                        });
                    }
                }

                RespondWithJson(context, new
                {
                    timestamp = DateTime.UtcNow,
                    lastUpdate = _lastWeatherUpdate,
                    status = "ok",
                    locationCount = locations.Count,
                    locations
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in GetWeatherForecast: {ex.Message}", Logger.LogLevel.Error);
                RespondWithJson(context, new { status = "error", message = ex.Message }, 500);
            }
        }

        // Weather helper methods
        private static string DegreesToCardinal(int? degrees)
        {
            if (!degrees.HasValue) return "N/A";
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            return cardinals[(int)Math.Round(((double)degrees % 360) / 45)];
        }

        private static string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear sky",
                1 => "Mainly clear",
                2 => "Partly cloudy",
                3 => "Overcast",
                45 => "Fog",
                48 => "Depositing rime fog",
                51 => "Light drizzle",
                53 => "Moderate drizzle",
                55 => "Dense drizzle",
                56 => "Light freezing drizzle",
                57 => "Dense freezing drizzle",
                61 => "Slight rain",
                63 => "Moderate rain",
                65 => "Heavy rain",
                66 => "Light freezing rain",
                67 => "Heavy freezing rain",
                71 => "Slight snow",
                73 => "Moderate snow",
                75 => "Heavy snow",
                77 => "Snow grains",
                80 => "Slight rain showers",
                81 => "Moderate rain showers",
                82 => "Violent rain showers",
                85 => "Slight snow showers",
                86 => "Heavy snow showers",
                95 => "Thunderstorm",
                96 => "Thunderstorm with light hail",
                99 => "Thunderstorm with heavy hail",
                _ => "Unknown"
            };
        }

        private void GetWebSettings(HttpListenerContext context)
        {
            RespondWithJson(context, new
            {
                port = Port,
                isRunning = IsRunning,
                allowRemoteAccess = true
            });
        }

        private void UpdateWebSettings(HttpListenerContext context)
        {
            RespondWithJson(context, new
            {
                status = "success",
                message = "Settings updated (placeholder)"
            });
        }

        private void GetStatus(HttpListenerContext context)
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly?.GetName().Version?.ToString()
                          ?? "unknown";

            RespondWithJson(context, new
            {
                timestamp = DateTime.UtcNow,
                status = IsRunning ? "running" : "stopped",
                version
            });
        }

        private void GetImagesList(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var imageDir = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                
                if (!Directory.Exists(imageDir))
                {
                    RespondWithJson(context, new { images = new List<object>() });
                    return;
                }

                var files = Directory.GetFiles(imageDir, "*.png");
                var images = new List<object>();
                
                foreach (var file in files)
                {
                    images.Add(new { filename = Path.GetFileName(file), path = file });
                }

                RespondWithJson(context, new { images });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private async Task GetImage(HttpListenerContext context, string filename)
        {
            try
            {
                if (string.IsNullOrEmpty(filename))
                {
                    RespondWithJson(context, new { error = "Filename not specified" }, 400);
                    return;
                }

                var config = ConfigManager.LoadConfig();
                var imageDir = config.ImageGeneration?.OutputDirectory ?? "WeatherImages";
                var filePath = Path.Combine(imageDir, Path.GetFileName(filename)); // Security: prevent directory traversal

                if (!File.Exists(filePath))
                {
                    RespondWithJson(context, new { error = "Image not found" }, 404);
                    return;
                }

                context.Response.ContentType = "image/png";
                byte[] buffer = File.ReadAllBytes(filePath);
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        // NEW ENDPOINT HANDLERS

        private void GetFullConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                RespondWithJson(context, config);
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void GetLocations(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var locations = new List<object>();
                
                if (config.Locations != null)
                {
                    // Use reflection to get all Location properties
                    for (int i = 0; i < 9; i++)
                    {
                        var locationProp = config.Locations.GetType().GetProperty($"Location{i}");
                        var apiProp = config.Locations.GetType().GetProperty($"Location{i}Api");
                        
                        var name = locationProp?.GetValue(config.Locations) as string;
                        var api = apiProp?.GetValue(config.Locations) ?? 0;
                        
                        var location = new
                        {
                            index = i,
                            name = name,
                            api = (int)api
                        };
                        locations.Add(location);
                    }
                }

                RespondWithJson(context, new { locations });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateGeneralConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (updates != null && updates.ContainsKey("refreshTimeMinutes"))
                {
                    if (int.TryParse(updates["refreshTimeMinutes"]?.ToString() ?? "", out int refreshMinutes))
                    {
                        config.RefreshTimeMinutes = refreshMinutes;
                    }
                }
                if (updates != null && updates.ContainsKey("theme"))
                {
                    config.Theme = updates["theme"]?.ToString() ?? "Blue";
                }

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "General settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateImageConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (config.ImageGeneration == null) config.ImageGeneration = new();

                if (updates != null && updates.ContainsKey("imageWidth") && int.TryParse(updates["imageWidth"]?.ToString() ?? "", out int width))
                    config.ImageGeneration.ImageWidth = width;
                if (updates != null && updates.ContainsKey("imageHeight") && int.TryParse(updates["imageHeight"]?.ToString() ?? "", out int height))
                    config.ImageGeneration.ImageHeight = height;
                if (updates != null && updates.ContainsKey("imageFormat"))
                    config.ImageGeneration.ImageFormat = updates["imageFormat"]?.ToString();
                if (updates != null && updates.ContainsKey("marginPixels") && int.TryParse(updates["marginPixels"]?.ToString() ?? "", out int margin))
                    config.ImageGeneration.MarginPixels = margin;

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "Image settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateVideoConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (config.Video == null) config.Video = new();

                if (updates != null && updates.ContainsKey("qualityPreset"))
                    config.Video.QualityPreset = updates["qualityPreset"]?.ToString();
                if (updates != null && updates.ContainsKey("resolutionMode"))
                    config.Video.ResolutionMode = updates["resolutionMode"]?.ToString();
                if (updates != null && updates.ContainsKey("frameRate") && int.TryParse(updates["frameRate"]?.ToString() ?? "", out int fps))
                    config.Video.FrameRate = fps;
                if (updates != null && updates.ContainsKey("videoCodec"))
                    config.Video.VideoCodec = updates["videoCodec"]?.ToString();
                if (updates != null && updates.ContainsKey("enableHardwareEncoding") && bool.TryParse(updates["enableHardwareEncoding"]?.ToString() ?? "", out bool hwenc))
                    config.Video.EnableHardwareEncoding = hwenc;
                if (updates != null && updates.ContainsKey("videoBitrate"))
                    config.Video.VideoBitrate = updates["videoBitrate"]?.ToString();
                if (updates != null && updates.ContainsKey("staticDurationSeconds") && double.TryParse(updates["staticDurationSeconds"]?.ToString() ?? "", out double staticDuration))
                    config.Video.StaticDurationSeconds = staticDuration;
                if (updates != null && updates.ContainsKey("useTotalDuration") && bool.TryParse(updates["useTotalDuration"]?.ToString() ?? "", out bool useTotalDuration))
                    config.Video.UseTotalDuration = useTotalDuration;
                if (updates != null && updates.ContainsKey("totalDurationSeconds") && int.TryParse(updates["totalDurationSeconds"]?.ToString() ?? "", out int totalDuration))
                    config.Video.TotalDurationSeconds = totalDuration;
                if (updates != null && updates.ContainsKey("enableFadeTransitions") && bool.TryParse(updates["enableFadeTransitions"]?.ToString() ?? "", out bool fade))
                    config.Video.EnableFadeTransitions = fade;
                if (updates != null && updates.ContainsKey("fadeDurationSeconds") && double.TryParse(updates["fadeDurationSeconds"]?.ToString() ?? "", out double fadeDuration))
                    config.Video.FadeDurationSeconds = fadeDuration;

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "Video settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateMusicConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (config.Music == null) config.Music = new();

                if (updates != null && updates.ContainsKey("enableMusicInVideo") && bool.TryParse(updates["enableMusicInVideo"]?.ToString() ?? "", out bool enableMusic))
                    config.Music.EnableMusicInVideo = enableMusic;
                if (updates != null && updates.ContainsKey("useRandomMusic") && bool.TryParse(updates["useRandomMusic"]?.ToString() ?? "", out bool useRandom))
                    config.Music.UseRandomMusic = useRandom;
                if (updates != null && updates.ContainsKey("selectedMusicIndex") && int.TryParse(updates["selectedMusicIndex"]?.ToString() ?? "", out int musicIndex))
                    config.Music.SelectedMusicIndex = musicIndex;

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "Music settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateAlertConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (config.AlertReady == null) config.AlertReady = new();

                if (updates != null && updates.ContainsKey("enabled") && bool.TryParse(updates["enabled"]?.ToString() ?? "", out bool enabled))
                    config.AlertReady.Enabled = enabled;
                if (updates != null && updates.ContainsKey("preferredLanguage"))
                    config.AlertReady.PreferredLanguage = updates["preferredLanguage"]?.ToString() ?? "f";
                if (updates != null && updates.ContainsKey("highRiskOnly") && bool.TryParse(updates["highRiskOnly"]?.ToString() ?? "", out bool highRisk))
                    config.AlertReady.HighRiskOnly = highRisk;
                if (updates != null && updates.ContainsKey("excludeWeatherAlerts") && bool.TryParse(updates["excludeWeatherAlerts"]?.ToString() ?? "", out bool excludeWeather))
                    config.AlertReady.ExcludeWeatherAlerts = excludeWeather;
                if (updates != null && updates.ContainsKey("includeTests") && bool.TryParse(updates["includeTests"]?.ToString() ?? "", out bool includeTests))
                    config.AlertReady.IncludeTests = includeTests;
                if (updates != null && updates.ContainsKey("maxAgeHours") && int.TryParse(updates["maxAgeHours"]?.ToString() ?? "", out int maxAge))
                    config.AlertReady.MaxAgeHours = maxAge;

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "Alert settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateRadarConfig(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (config.ImageGeneration == null) config.ImageGeneration = new();
                if (config.ECCC == null) config.ECCC = new();

                if (updates != null && updates.ContainsKey("enableProvinceRadar") && bool.TryParse(updates["enableProvinceRadar"]?.ToString() ?? "", out bool enableProvinceRadar))
                    config.ECCC.EnableProvinceRadar = enableProvinceRadar;
                if (updates != null && updates.ContainsKey("enableWeatherMaps") && bool.TryParse(updates["enableWeatherMaps"]?.ToString() ?? "", out bool enableWeatherMaps))
                    config.ImageGeneration.EnableWeatherMaps = enableWeatherMaps;
                if (updates != null && updates.ContainsKey("provinceFrames") && int.TryParse(updates["provinceFrames"]?.ToString() ?? "", out int frames))
                    config.ECCC.ProvinceFrames = frames;
                if (updates != null && updates.ContainsKey("provinceFrameStepMinutes") && int.TryParse(updates["provinceFrameStepMinutes"]?.ToString() ?? "", out int step))
                    config.ECCC.ProvinceFrameStepMinutes = step;

                ConfigManager.SaveConfig(config);
                RespondWithJson(context, new { status = "success", message = "Radar settings updated" });
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void UpdateLocations(HttpListenerContext context)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                if (config.Locations == null) config.Locations = new LocationSettings();
                var body = ReadRequestBody(context);
                var updates = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();

                if (updates != null && updates.ContainsKey("index") && int.TryParse(updates["index"]?.ToString() ?? "", out int index)
                    && index >= 0 && index <= 8)
                {
                    // Remove
                    if (updates.ContainsKey("action") && updates["action"]?.ToString() == "remove")
                    {
                        var prop = config.Locations?.GetType().GetProperty($"Location{index}");
                        if (prop != null) prop.SetValue(config.Locations, null);
                    }

                    // Update name
                    if (updates.ContainsKey("name"))
                    {
                        var prop = config.Locations?.GetType().GetProperty($"Location{index}");
                        if (prop != null) prop.SetValue(config.Locations, updates["name"]?.ToString());
                    }

                    // Update api
                    if (updates.ContainsKey("api"))
                    {
                        if (int.TryParse(updates["api"]?.ToString() ?? "", out int apiInt))
                        {
                            if (Enum.IsDefined(typeof(Models.WeatherApiType), apiInt))
                            {
                                config.Locations?.SetApiPreference(index, (Models.WeatherApiType)apiInt);
                            }
                        }
                    }

                    ConfigManager.SaveConfig(config);
                    RespondWithJson(context, new { status = "success", message = "Location updated" });
                }
                else
                {
                    RespondWithJson(context, new { error = "Invalid or missing index" }, 400);
                }
            }
            catch (Exception ex)
            {
                RespondWithJson(context, new { error = ex.Message }, 500);
            }
        }

        private void StartCycle(HttpListenerContext context)
        {
            StartCycleRequested?.Invoke(this, EventArgs.Empty);
            RespondWithJson(context, new { status = "success", message = "Cycle start request sent to application" });
        }

        private void StopCycle(HttpListenerContext context)
        {
            StopCycleRequested?.Invoke(this, EventArgs.Empty);
            RespondWithJson(context, new { status = "success", message = "Cycle stop request sent to application" });
        }

        private void GenerateStill(HttpListenerContext context)
        {
            GenerateStillRequested?.Invoke(this, EventArgs.Empty);
            RespondWithJson(context, new { status = "success", message = "Still generation request sent to application" });
        }

        private void GenerateVideo(HttpListenerContext context)
        {
            GenerateVideoRequested?.Invoke(this, EventArgs.Empty);
            RespondWithJson(context, new { status = "success", message = "Video generation request sent to application" });
        }

        private string ReadRequestBody(HttpListenerContext context)
        {
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }    }
}