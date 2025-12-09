using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WeatherAlertMonitor
{
    class Program
    {
        // DICTIONARY OF CITIES & CODES
        // We use the 'City' specific feeds (qc-XX) because they exist for every town.
        // The 'Battleboard' (qcrm) feeds are regional and harder to map to specific towns like Amos.
        private static readonly Dictionary<string, string> CityFeeds = new Dictionary<string, string>
        {
            { "Montreal",    "https://weather.gc.ca/rss/city/qc-147_f.xml" },
            { "Quebec City", "https://weather.gc.ca/rss/city/qc-133_f.xml" },
            { "Gatineau",    "https://weather.gc.ca/rss/city/qc-59_f.xml" },
            { "Amos","https://weather.gc.ca/rss/alerts/48.574_-78.116_f.xml" } // No city feed, using lat/lon
        };

        private const int RefreshMinutes = 5;

        static async Task Main(string[] args)
        {
            Console.Title = "Quebec Weather Alert Monitor";
            Console.WriteLine($"--- MONITORING {CityFeeds.Count} CITIES ---");

            using (HttpClient client = new HttpClient())
            {
                // Essential to avoid 403 Forbidden
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                while (true)
                {
                    Console.Clear(); // Optional: Clears console on every refresh
                    Console.WriteLine($"--- UPDATING: {DateTime.Now:HH:mm:ss} ---");

                    foreach (var city in CityFeeds)
                    {
                        try
                        {
                            await CheckFeed(client, city.Key, city.Value);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[{city.Key}] Error: {ex.Message}");
                            Console.ResetColor();
                        }
                        
                        // Small delay to be polite to the server
                        await Task.Delay(500); 
                    }

                    Console.WriteLine($"\nNext check in {RefreshMinutes} minutes...");
                    await Task.Delay(TimeSpan.FromMinutes(RefreshMinutes));
                }
            }
        }

        static async Task CheckFeed(HttpClient client, string cityName, string url)
        {
            string xmlContent = await client.GetStringAsync(url);
            XDocument doc = XDocument.Parse(xmlContent);
            XNamespace atom = "http://www.w3.org/2005/Atom";

            var entries = doc.Root.Elements(atom + "entry");
            bool alertFound = false;

            // Loop through entries to find alerts
            foreach (var entry in entries)
            {
                string title = entry.Element(atom + "title")?.Value ?? "";
                string summary = entry.Element(atom + "summary")?.Value ?? "";
                string category = entry.Element(atom + "category")?.Attribute("term")?.Value ?? "";

                // FILTER: We only care about Warnings/Watches/Statements.
                // In City feeds, entries also include "Current Conditions" and "Forecast" which we skip.
                if (category.Equals("Veilles et avertissements", StringComparison.OrdinalIgnoreCase) || 
                    category.Equals("Warnings and Watches", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if it says "No watches or warnings"
                    if (title.Contains("Aucune veille ou alerte", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("No watches or warnings", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // If we are here, we have a REAL alert
                    alertFound = true;

                    // Determine Color based on severity keyword
                    ConsoleColor msgColor = ConsoleColor.White;
                    string typeLabel = "NOTICE";

                    if (title.Contains("avertissement", StringComparison.OrdinalIgnoreCase) || 
                        title.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        msgColor = ConsoleColor.Red;
                        typeLabel = "WARNING";
                    }
                    else if (title.Contains("veille", StringComparison.OrdinalIgnoreCase) || 
                             title.Contains("watch", StringComparison.OrdinalIgnoreCase))
                    {
                        msgColor = ConsoleColor.Yellow;
                        typeLabel = "WATCH";
                    }
                    else if (title.Contains("bulletin", StringComparison.OrdinalIgnoreCase) || 
                             title.Contains("statement", StringComparison.OrdinalIgnoreCase))
                    {
                        msgColor = ConsoleColor.Gray;
                        typeLabel = "STATEMENT";
                    }

                    PrintAlert(msgColor, cityName, typeLabel, title, summary);
                }
            }

            if (!alertFound)
            {
                // Optional: Print green "OK" if you want to see status for clear cities
                // Console.ForegroundColor = ConsoleColor.Green;
                // Console.WriteLine($"[{cityName}] All Clear");
                // Console.ResetColor();
            }
        }

        static void PrintAlert(ConsoleColor color, string city, string type, string title, string summary)
        {
            Console.ForegroundColor = color;
            Console.WriteLine("\n" + new string('=', 40));
            Console.WriteLine($" >>> {city.ToUpper()} : {type} <<<");
            Console.WriteLine(new string('=', 40));
            Console.ResetColor();
            Console.WriteLine($"Headline: {title}");
            Console.WriteLine($"Details:  {CleanSummary(summary)}");
        }

        static string CleanSummary(string summary)
        {
            // Simple HTML tag stripper
            return summary.Replace("<br/>", "\n")
                          .Replace("<b>", "")
                          .Replace("</b>", "")
                          .Trim();
        }
    }
}