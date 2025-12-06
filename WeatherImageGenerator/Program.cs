#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using OpenMeteo;
using CommonStrings;

    

namespace WeatherImageGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            RunAsync().GetAwaiter().GetResult();
        }

        static async Task RunAsync()
        {
            try
            {
                OpenMeteoClient client = new OpenMeteoClient();

                // Query weathers from COMMONSTRINGS
                string location0 = CommonSettings.LOCATION0;
                string location1 = CommonSettings.LOCATION1;
                string location2 = CommonSettings.LOCATION2;
                string location3 = CommonSettings.LOCATION3;
                string location4 = CommonSettings.LOCATION4;
                string location5 = CommonSettings.LOCATION5;
                string location6 = CommonSettings.LOCATION6;
            



                Console.WriteLine($"Fetching weathers data...");
                
                // Fetch weathers data asynchronously
                WeatherForecast? weatherData0 = await client.QueryAsync(location0);
                WeatherForecast? weatherData1 = await client.QueryAsync(location1);
                WeatherForecast? weatherData2 = await client.QueryAsync(location2);
                WeatherForecast? weatherData3 = await client.QueryAsync(location3);
                WeatherForecast? weatherData4 = await client.QueryAsync(location4);
                WeatherForecast? weatherData5 = await client.QueryAsync(location5);
                WeatherForecast? weatherData6 = await client.QueryAsync(location6);
                WeatherForecast? weatherData = weatherData0; // Currently only using LOCATION0 for image generation

            
                
             
                if (weatherData == null)
                {
                    Console.WriteLine("Failed to fetch weather data.");
                    return;
                }

                // Create output directory
                string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "WeatherImages");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                Console.WriteLine("Generating still images..."); 

                // MAIN IMAGE GENERATION CALLS, ENABLE OR DISABLE AS NEEDED

                // Generate still images
                //GenerateCurrentWeatherImage(weatherData, outputDir); 
                //GenerateForecastSummaryImage(weatherData, outputDir); 
                //GenerateDetailedWeatherImage(weatherData, outputDir); 
                GenerateMapsImage(weatherData, outputDir);
                //GenerateAPNGcurrentTemperature(weatherData, outputDir);
                //StartMakeVideo(outputDir);    
               // WaitAndRefresh(); 


                Console.WriteLine($"\n✓ Successfully generated 3 weather images in: {outputDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }


        static void GenerateMapsImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            Bitmap bitmap = new Bitmap(width, height);
            Graphics graphics = Graphics.FromImage(bitmap);

            try
            {
                // Import map images and draw temperature results
                // Placeholder implementation - in real code, you would load actual map images and overlay data


                Font cityFont = new Font("Arial", 24, FontStyle.Bold);
                Font tempFont = new Font("Arial", 48, FontStyle.Bold);
                Font titleFont = new Font("Arial", 48, FontStyle.Bold);
                Brush whiteBrush = Brushes.White;

                for (int i = 0; i < 5; i++)
                {
                    // Placeholder positions and data
                    PointF cityPosition = new PointF(100 + i * 300, 200);
                    PointF tempPosition = new PointF(100 + i * 300, 250);

                    //need to handle all the location
                    
                   // string cityName = $"City {i + 1}"; // need to handle all the location

                    string cityName = i switch
                    {
                        0 => CommonSettings.LOCATION0,
                        1 => CommonSettings.LOCATION1,
                        2 => CommonSettings.LOCATION2,
                        3 => CommonSettings.LOCATION3,
                        4 => CommonSettings.LOCATION4,
                        _ => "Unknown"
                    };



                    string tempText = weatherData.Current?.Temperature != null 
                        ? $"{weatherData.Current.Temperature + i * 2}{weatherData.CurrentUnits?.Temperature}"
                        : "N/A";

                    graphics.DrawString(cityName, cityFont, whiteBrush, cityPosition);
                    graphics.DrawString(tempText, tempFont, whiteBrush, tempPosition);
                }




                // Title
                graphics.DrawString("Weather Maps Placeholder", titleFont, whiteBrush, new PointF(50, 50));

                string filename = Path.Combine(outputDir, "4_WeatherMaps.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");

                titleFont.Dispose();
            }
            finally
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }
        static void GenerateCurrentWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            Bitmap bitmap = new Bitmap(width, height);
            Graphics graphics = Graphics.FromImage(bitmap);

            try
            {
                // Fill background with gradient
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(70, 130, 180), Color.FromArgb(100, 150, 200)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                Font titleFont = new Font("Arial", 48, FontStyle.Bold);
                Font labelFont = new Font("Arial", 24, FontStyle.Regular);
                Font dataFont = new Font("Arial", 32, FontStyle.Bold);
                Brush whiteBrush = Brushes.White;

                // Title
                graphics.DrawString("Current Weather", titleFont, whiteBrush, new PointF(50, 50));

                // Current temperature
                string tempText = weatherData.Current?.Temperature != null 
                    ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                    : "N/A";
                graphics.DrawString("Temperature:", labelFont, whiteBrush, new PointF(50, 150));
                graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(50, 200));

                // Weather condition
                string conditionText = weatherData.Current?.Weathercode != null
                    ? GetWeatherDescription((int)weatherData.Current.Weathercode)
                    : "N/A";
                graphics.DrawString("Condition:", labelFont, whiteBrush, new PointF(50, 280));
                graphics.DrawString(conditionText, dataFont, whiteBrush, new PointF(50, 330));

                // Wind speed
                string windText = weatherData.Current?.Windspeed_10m != null
                    ? $"{weatherData.Current.Windspeed_10m} {weatherData.CurrentUnits?.Windspeed_10m}"
                    : "N/A";
                graphics.DrawString("Wind Speed:", labelFont, whiteBrush, new PointF(50, 410));
                graphics.DrawString(windText, dataFont, whiteBrush, new PointF(50, 460));

                string filename = Path.Combine(outputDir, "1_CurrentWeather.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");

                titleFont.Dispose();
                labelFont.Dispose();
                dataFont.Dispose();
            }
            finally
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }

        static void GenerateForecastSummaryImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            Bitmap bitmap = new Bitmap(width, height);
            Graphics graphics = Graphics.FromImage(bitmap);

            try
            {
                // Fill background with gradient
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(60, 120, 150), Color.FromArgb(90, 140, 180)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                Font titleFont = new Font("Arial", 48, FontStyle.Bold);
                Font labelFont = new Font("Arial", 20, FontStyle.Regular);
                Font dataFont = new Font("Arial", 24, FontStyle.Bold);
                Brush whiteBrush = Brushes.White;

                graphics.DrawString("Daily Forecast Summary", titleFont, whiteBrush, new PointF(50, 50));

                if (weatherData.Daily != null && weatherData.Daily.Time != null && weatherData.Daily.Time.Length > 0)
                {
                    int yPosition = 150;
                    int daysToShow = Math.Min(3, weatherData.Daily.Time.Length);

                    for (int i = 0; i < daysToShow; i++)
                    {
                        DateTime dateTime = DateTime.Parse(weatherData.Daily.Time[i]);
                        string date = dateTime.ToString("ddd, MMM d");
                        string maxTemp = weatherData.Daily.Temperature_2m_max != null ? weatherData.Daily.Temperature_2m_max[i].ToString() : "N/A";
                        string minTemp = weatherData.Daily.Temperature_2m_min != null ? weatherData.Daily.Temperature_2m_min[i].ToString() : "N/A";
                        string condition = weatherData.Daily.Weathercode != null && weatherData.Daily.Weathercode[i] != null
                            ? GetWeatherDescription((int)weatherData.Daily.Weathercode[i])
                            : "N/A";

                        graphics.DrawString($"{date}", labelFont, whiteBrush, new PointF(50, yPosition));
                        graphics.DrawString($"High: {maxTemp}° | Low: {minTemp}° | {condition}", dataFont, whiteBrush, 
                            new PointF(50, yPosition + 35));

                        yPosition += 120;
                    }
                }
                else
                {
                    graphics.DrawString("No forecast data available", labelFont, whiteBrush, new PointF(50, 200)); // NO DATA FOR CANADA, NEED TO CHANGE FORCAST INFO
                }

                string filename = Path.Combine(outputDir, "2_DailyForecast.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");

                titleFont.Dispose();
                labelFont.Dispose();
                dataFont.Dispose();
            }
            finally
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }

        static void GenerateDetailedWeatherImage(WeatherForecast weatherData, string outputDir)
        {
            int width = 1920;
            int height = 1080;
            Bitmap bitmap = new Bitmap(width, height);
            Graphics graphics = Graphics.FromImage(bitmap);

            try
            {
                // Fill background with gradient
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(80, 140, 160), Color.FromArgb(110, 160, 190)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }

                Font titleFont = new Font("Arial", 48, FontStyle.Bold);
                Font labelFont = new Font("Arial", 18, FontStyle.Regular);
                Font dataFont = new Font("Arial", 22, FontStyle.Bold);
                Brush whiteBrush = Brushes.White;

                graphics.DrawString($"Détail pour {CommonSettings.LOCATION0}", titleFont, whiteBrush, new PointF(50, 30));

                int yPosition = 120;
                int lineHeight = 50;

                // Display various weather details
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

                string filename = Path.Combine(outputDir, "3_DetailedWeather.png");
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");

                titleFont.Dispose();
                labelFont.Dispose();
                dataFont.Dispose();
            }
            finally
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
        }

        static void GenerateAPNGcurrentTemperature(WeatherForecast weatherData, string outputDir)
        {
            int width = 400; // leave higher buffer for high temp
            int height = 100;
            Bitmap bitmap = new Bitmap(width, height);
            Graphics graphics = Graphics.FromImage(bitmap);

            try
            {
                // Not needed for APNG

                // Fill background with gradient
                /*
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, height),
                    Color.FromArgb(0, 0, 0), Color.FromArgb(0, 0, 0))) 
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }
                */

                 graphics.Clear(Color.Transparent);


                Font titleFont = new Font("Arial", 48, FontStyle.Bold);
                Font dataFont = new Font("Arial", 72, FontStyle.Bold);
                Brush whiteBrush = Brushes.White;

                // Title
                //graphics.DrawString("Current Temperature", titleFont, whiteBrush, new PointF(50, 50)); // Removed title for APNG

                // Current temperature
                string tempText = weatherData.Current?.Temperature != null 
                    ? $"{weatherData.Current.Temperature}{weatherData.CurrentUnits?.Temperature}"
                    : "N/A";
                graphics.DrawString(tempText, dataFont, whiteBrush, new PointF(0, 0)); // Centered in APNG

                string filename = Path.Combine(outputDir, "temp_watermark_alpha.png.IGNORE"); // Marked to be ignored for now
                // Save as APNG - Placeholder function
                bitmap.Save(filename);
                Console.WriteLine($"✓ Generated: {filename}");

                titleFont.Dispose();
                dataFont.Dispose();
            }
            finally
            {
                graphics.Dispose();
                bitmap.Dispose();
            }
            // Placeholder for APNG generation logic
            // This would involve creating multiple frames and compiling them into an APNG file
            // For simplicity, this function is left unimplemented
        }

        static void StartMakeVideo(string outputDir)
        {
            string scriptPath = Path.Combine(outputDir, "make_video.ps1");

            // Check if script exists before trying to run it
            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"Error: Script not found at {scriptPath}");
                return;
            }

            // Prepare the process to run PowerShell.exe
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                // Arguments explained:
                // -NoProfile: Loads faster by not loading user profile
                // -ExecutionPolicy Bypass: Temporarily allows the script to run even if restricted
                // -File: Specifies the script to run (wrapped in quotes for paths with spaces)
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = false // Set to true if you want it to run invisibly
            };

            try 
            {
                using (Process process = Process.Start(startInfo))
                {
                    // Optional: Wait for the script to finish before continuing C# code
                    process.WaitForExit(); 
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to execute script: {ex.Message}");
            }
        }
        // wait fot 5 minutes before and repeat
        static void WaitAndRefresh()
        {    
            Console.WriteLine("Waiting for 5 minutes before refresh...");
            System.Threading.Thread.Sleep(int.Parse(CommonStrings.CommonSettings.REFRESHTIME) * 60000); 
            RunAsync().GetAwaiter().GetResult();
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
