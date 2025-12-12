using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using WeatherImageGenerator;

namespace QuebecWeatherAlertMonitor
{
    // A simple model to hold alert data
    public class AlertEntry
    {
        public string City { get; set; } = "";
        public string Type { get; set; } = ""; // WARNING, WATCH, STATEMENT
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string SeverityColor { get; set; } = "Gray"; // Red, Yellow, Gray
    }

    public static class ECCC
    {
        // Call this method from Program.cs
        public static async Task<List<AlertEntry>> FetchAllAlerts(HttpClient client)
        {
            var config = ConfigManager.LoadConfig();
            var ecccConfig = config.ECCC ?? new ECCCSettings();
            var cityFeeds = ecccConfig.CityFeeds ?? new Dictionary<string, string>();

            List<AlertEntry> allAlerts = new List<AlertEntry>();

            // Ensure we have a User-Agent or ECCC will block the request
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", ecccConfig.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            }

            foreach (var city in cityFeeds)
            {
                try
                {
                    var cityAlerts = await CheckFeed(client, city.Key, city.Value);
                    allAlerts.AddRange(cityAlerts);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ECCC] Error fetching {city.Key}: {ex.Message}");
                }
                
                // Be polite to the server
                await Task.Delay(ecccConfig.DelayBetweenRequestsMs); 
            }

            return allAlerts;
        }

        private static async Task<List<AlertEntry>> CheckFeed(HttpClient client, string cityName, string url)
        {
            List<AlertEntry> foundAlerts = new List<AlertEntry>();

            string xmlContent = await client.GetStringAsync(url);
            XDocument doc = XDocument.Parse(xmlContent);
            XNamespace atom = "http://www.w3.org/2005/Atom";

            var entries = doc.Root.Elements(atom + "entry");

            foreach (var entry in entries)
            {
                string title = entry.Element(atom + "title")?.Value ?? "";
                string summary = entry.Element(atom + "summary")?.Value ?? "";
                string category = entry.Element(atom + "category")?.Attribute("term")?.Value ?? "";

                // Filter for Warnings/Watches/Statements
                if (category.Equals("Veilles et avertissements", StringComparison.OrdinalIgnoreCase) || 
                    category.Equals("Warnings and Watches", StringComparison.OrdinalIgnoreCase))
                {
                    // Skip "No watches or warnings"
                    if (title.Contains("Aucune veille ou alerte", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("No watches or warnings", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AlertEntry newAlert = new AlertEntry
                    {
                        City = cityName,
                        Title = title,
                        Summary = CleanSummary(summary)
                    };

                    // Determine Type and Color Logic
                    if (title.Contains("avertissement", StringComparison.OrdinalIgnoreCase) || 
                        title.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        newAlert.Type = "WARNING";
                        newAlert.SeverityColor = "Red";
                    }
                    else if (title.Contains("veille", StringComparison.OrdinalIgnoreCase) || 
                             title.Contains("watch", StringComparison.OrdinalIgnoreCase))
                    {
                        newAlert.Type = "WATCH";
                        newAlert.SeverityColor = "Yellow";
                    }
                    else
                    {
                        newAlert.Type = "STATEMENT";
                        newAlert.SeverityColor = "Gray";
                    }

                    foundAlerts.Add(newAlert);
                }
            }

            return foundAlerts;
        }

        private static string CleanSummary(string summary)
        {
            return summary.Replace("<br/>", "\n")
                          .Replace("<b>", "")
                          .Replace("</b>", "")
                          .Trim();
        }
    }
}