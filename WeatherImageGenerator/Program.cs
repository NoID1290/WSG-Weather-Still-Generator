#nullable enable
using System;
using System.Diagnostics;
using System.Drawing; // NuGet: System.Drawing.Common
using System.IO;
using System.Threading.Tasks;
using OpenMeteo; // NuGet: OpenMeteo
using CommonStrings; // Your custom namespace

namespace WeatherImageGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            // Entry point: Start the async task and wait for it indefinitely
            RunAsync().GetAwaiter().GetResult();
        }

        static async Task RunAsync()
        {
            // Initialize the API client
            OpenMeteoClient client = new OpenMeteoClient();

            // Load locations into an array for easier handling
            string[] locations = new string[] 
            {
                CommonSettings.LOCATION0, 
                CommonSettings.LOCATION1, 
                CommonSettings.LOCATION2,
                CommonSettings.LOCATION3, 
                CommonSettings.LOCATION4, 
                CommonSettings.LOCATION5,
                CommonSettings.LOCATION6
            };

            // Infinite loop to keep the program running
            while (true)
            {
                try
                {
                    Console.WriteLine($"\n--- Starting Update Cycle: {DateTime.Now} ---");
                    Console.WriteLine($"Fetching weather data...");
                    
                    // Array to store results for all 7 locations
                    WeatherForecast?[] allForecasts = new WeatherForecast?[7];
                    bool dataFetchSuccess = true;

                    // Fetch weather data for each location
                    for (int i = 0; i < locations.Length; i++)
                    {
                        string loc = locations[i];
                        try 
                        {
                            // CORRECTED METHOD: Uses QueryAsync
                            allForecasts[i] = await client.QueryAsync(loc);
                            Console.WriteLine($"✓ Fetched weather data for {loc}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"X Failed to fetch for {loc}: {ex.Message}");
                            dataFetchSuccess = false; 
                        }
                    }

                    // Check if we have at least the primary location data to proceed
                    if (allForecasts[0] == null) 
                    {
                        Console.WriteLine("Primary location data missing. Retrying in 1 minute...");
                        await Task.Delay(60000);
                        continue;
                    }

                    // Create output directory
                    string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "WeatherImages");
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    Console.WriteLine("Generating still images...");

                    // --- IMAGE GENERATION ---
                    
                    // 1. Current Weather (Uses Location 0)
                    if(allForecasts[0] != null)
                        GenerateCurrentWeatherImage(allForecasts[0]!, outputDir);

                    // 2. Forecast Summary (Uses Location 0)
                    if (allForecasts[0] != null)
                        GenerateForecastSummaryImage(allForecasts[0]!, outputDir);

                    // 3. Detailed Weather (Uses Location 0)
                    if (allForecasts[0] != null)
                        GenerateDetailedWeatherImage(allForecasts[0]!, outputDir);

                    // 4. Maps Image (Uses ALL locations)
                    GenerateMapsImage(allForecasts, locations, outputDir);

                    // 5. APNG Helper (Uses Location 0)
                    if (allForecasts[0] != null)
                        GenerateAPNGcurrentTemperature(allForecasts[0]!, outputDir);

                    // 6. Video Generation (Optional)
                    StartMakeVideo(outputDir);    

                    Console.WriteLine($"✓ Cycle Complete. Images saved to: {outputDir}");

                    // Wait Logic
                    if (int.TryParse(CommonSettings.REFRESHTIME, out int minutes))
                    {
                        Console.WriteLine($"Sleeping for {minutes} minutes...");
                        await Task.Delay(minutes * 60000);
                    }
                    else
                    {
                        // Fallback if setting is invalid
                        Console.WriteLine("Invalid refresh time setting. Defaulting to 15 minutes.");
                        await Task.Delay(15 * 60000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Critical Global Error: {ex.Message}");
                    Console.WriteLine("Retrying in 1 minute...");
                    await Task.Delay(60000);
                }
            }
        }

        static void GenerateMapsImage(WeatherForecast?[] allData, string[] locationNames, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // 1. Background / Map Layer
                string staticMapPath = Path.Combine(outputDir, "STATIC_MAP.IGNORE");
                if (File.Exists(staticMapPath))
                {
                    using (var importedMap = Image.FromFile(staticMapPath))
                    {
                        graphics.DrawImage(importedMap, 0, 0, width, height);
                    }
                }
                else
                {
                    graphics.Clear(Color.DarkBlue); // Fallback if map image missing
                }

                // 2. Overlay Data
                using (Font cityFont = new Font("Arial", 24, FontStyle.Bold))
                using (Font tempFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    // Loop through up to 5 locations for the map
                    for (int i = 0; i < 5 && i < allData.Length; i++)
                    {
                        var data = allData[i];
                        string locName = locationNames[i];

                        // Calculate positions (You may need to tweak these coordinates)
                        PointF cityPosition = new PointF(100 + i * 350, 200);
                        PointF tempPosition = new PointF(100 + i * 350, 250);

                        string tempText = "N/A";

                        if (data?.Current != null)
                        {
                            tempText = $"{data.Current.Temperature}{data.CurrentUnits?.Temperature}";
                        }

                        graphics.DrawString(locName, cityFont, whiteBrush, cityPosition);
                        graphics.DrawString(tempText, tempFont, whiteBrush, tempPosition);
                    }

                    // Title
                    graphics.DrawString("Regional Overview", titleFont, whiteBrush, new PointF(50, 50));
                }

                string filename = Path.Combine(outputDir, "4_WeatherMaps.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");
            }
        }

        static void GenerateCurrentWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                // Gradient Background
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(70, 130, 180), Color.FromArgb(100, 150, 200)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font("Arial", 24, FontStyle.Regular))
                using (Font dataFont = new Font("Arial", 32, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    // Title
                    graphics.DrawString("Current Weather", titleFont, whiteBrush, new PointF(50, 50));

                    // Data Extraction
                    string tempText = weatherData.Current?.Temperature != null
                        ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                        : "N/A";
                    
                    string conditionText = weatherData.Current?.Weathercode != null
                        ? GetWeatherDescription((int)weatherData.Current.Weathercode)
                        : "N/A";

                    string windText = weatherData.Current?.Windspeed_10m != null
                        ? $"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m}"
                        : "N/A";

                    // Draw Text
                    graphics.DrawString("Temperature:", labelFont, whiteBrush, new PointF(50, 150));
                    graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(50, 200));

                    graphics.DrawString("Condition:", labelFont, whiteBrush, new PointF(50, 280));
                    graphics.DrawString(conditionText, dataFont, whiteBrush, new PointF(50, 330));

                    graphics.DrawString("Wind Speed:", labelFont, whiteBrush, new PointF(50, 410));
                    graphics.DrawString(windText, dataFont, whiteBrush, new PointF(50, 460));
                }

                string filename = Path.Combine(outputDir, "1_CurrentWeather.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");
            }
        }

        static void GenerateForecastSummaryImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(60, 120, 150), Color.FromArgb(90, 140, 180)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font("Arial", 20, FontStyle.Regular))
                using (Font dataFont = new Font("Arial", 24, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    graphics.DrawString("Daily Forecast Summary", titleFont, whiteBrush, new PointF(50, 50));

                    if (weatherData.Daily != null && weatherData.Daily.Time != null && weatherData.Daily.Time.Length > 0)
                    {
                        int yPosition = 150;
                        int daysToShow = Math.Min(3, weatherData.Daily.Time.Length);

                        for (int i = 0; i < daysToShow; i++)
                        {
                            // Safe parsing
                            if(DateTime.TryParse(weatherData.Daily.Time[i], out DateTime dateTime))
                            {
                                string date = dateTime.ToString("ddd, MMM d");
                                string maxTemp = weatherData.Daily.Temperature_2m_max != null ? weatherData.Daily.Temperature_2m_max[i].ToString() : "-";
                                string minTemp = weatherData.Daily.Temperature_2m_min != null ? weatherData.Daily.Temperature_2m_min[i].ToString() : "-";
                                string condition = weatherData.Daily.Weathercode != null 
                                    ? GetWeatherDescription((int)weatherData.Daily.Weathercode[i])
                                    : "";

                                graphics.DrawString($"{date}", labelFont, whiteBrush, new PointF(50, yPosition));
                                graphics.DrawString($"High: {maxTemp}° | Low: {minTemp}° | {condition}", dataFont, whiteBrush,
                                    new PointF(50, yPosition + 35));

                                yPosition += 120;
                            }
                        }
                    }
                    else
                    {
                        graphics.DrawString("No forecast data available", labelFont, whiteBrush, new PointF(50, 200));
                    }
                }

                string filename = Path.Combine(outputDir, "2_DailyForecast.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");
            }
        }

        static void GenerateDetailedWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(80, 140, 160), Color.FromArgb(110, 160, 190)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                using (Font titleFont = new Font("Arial", 48, FontStyle.Bold))
                using (Font labelFont = new Font("Arial", 18, FontStyle.Regular))
                using (Font dataFont = new Font("Arial", 22, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    graphics.DrawString($"Détail pour {CommonSettings.LOCATION0}", titleFont, whiteBrush, new PointF(50, 30));

                    int yPosition = 120;
                    int lineHeight = 50;

                    if (weatherData.Current != null)
                    {
                        graphics.DrawString("Temperature:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}",
                            dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Humidity:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Relativehumidity_2m}%", dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Wind Speed:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m}",
                            dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Wind Direction:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Winddirection_10m}°", dataFont, whiteBrush, new PointF(300, yPosition));
                        yPosition += lineHeight;

                        graphics.DrawString("Pressure:", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"{weatherData.Current.Surface_pressure} hPa", dataFont, whiteBrush, new PointF(300, yPosition));
                    }
                }

                string filename = Path.Combine(outputDir, "3_DetailedWeather.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");
            }
        }

        static void GenerateAPNGcurrentTemperature(WeatherForecast weatherData, string outputDir)
        {
            int width = 400;
            int height = 100;
            
            using (Bitmap bitmap = new Bitmap(width, height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);

                using (Font dataFont = new Font("Arial", 72, FontStyle.Bold))
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    string tempText = weatherData.Current?.Temperature != null
                        ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                        : "N/A";
                    
                    // Draw at 0,0 for simple watermark use
                    graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(0, 0));
                }

                // .IGNORE extension preserved per your original code
                string filename = Path.Combine(outputDir, "temp_watermark_alpha.png.IGNORE");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");
            }
        }

        static void StartMakeVideo(string outputDir)
        {
            string scriptPath = Path.Combine(outputDir, "make_video.ps1");

            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"Error: Script not found at {scriptPath}");
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false
            };

            try
            {
                using (Process? process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to execute script: {ex.Message}");
            }
        }

        static string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear sky",
                1 => "Mainly clear",
                2 => "Partly cloudy",
                3 => "Overcast",
                45 => "Fog",
                48 => "Depositing rime Fog",
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
                71 => "Slight snow fall",
                73 => "Moderate snow fall",
                75 => "Heavy snow fall",
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
    }
}