using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;

namespace MontrealWeatherAlerts
{
    class Program
    {
        // The URL you found (Montreal/Laval Regional Summary)
        private const string RssUrl = "https://weather.gc.ca/rss/battleboard/qcrm2_e.xml";
        
        // How often to check (in minutes)
        private const int RefreshMinutes = 5;

        static async Task Main(string[] args)
        {
            Console.Title = "Montreal Weather Alert Monitor (qcrm2)";
            Console.WriteLine($"--- MONITORING: {RssUrl} ---");
            Console.WriteLine($"Checks every {RefreshMinutes} minutes.\n");

            using (HttpClient client = new HttpClient())
            {
                // Essential to avoid 403 Forbidden errors
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                while (true)
                {
                    try
                    {
                        await CheckFeed(client);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[{DateTime.Now:HH:mm}] Connection Error: {ex.Message}");
                        Console.ResetColor();
                    }

                    // Wait before next check
                    await Task.Delay(TimeSpan.FromMinutes(RefreshMinutes));
                }
            }
        }

        static async Task CheckFeed(HttpClient client)
        {
            // 1. Download XML
            string xmlContent = await client.GetStringAsync(RssUrl);
            XDocument doc = XDocument.Parse(xmlContent);
            XNamespace atom = "http://www.w3.org/2005/Atom";

            var entries = doc.Root.Elements(atom + "entry");
            bool alertFound = false;

            Console.Write($"[{DateTime.Now:HH:mm}] Scanning... ");

            foreach (var entry in entries)
            {
                string title = entry.Element(atom + "title")?.Value ?? "";
                string summary = entry.Element(atom + "summary")?.Value ?? "";

                // Skip "No watches or warnings"
                if (title.Contains("No watches or warnings", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If we get here, we have an actual alert
                alertFound = true;
                Console.WriteLine(""); // New line for cleanliness

                // 2. Formatting based on Severity
                if (title.Contains("Special weather statement", StringComparison.OrdinalIgnoreCase))
                {
                    PrintAlert(ConsoleColor.Yellow, "SPECIAL STATEMENT", title, summary);
                }
                else if (title.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                {
                    PrintAlert(ConsoleColor.Red, "WARNING", title, summary);
                }
                else if (title.Contains("Watch", StringComparison.OrdinalIgnoreCase))
                {
                    PrintAlert(ConsoleColor.DarkYellow, "WATCH", title, summary);
                }
                else
                {
                    PrintAlert(ConsoleColor.White, "NOTICE", title, summary);
                }
            }

            if (!alertFound)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("All Clear.");
                Console.ResetColor();
            }
        }

        static void PrintAlert(ConsoleColor color, string type, string title, string summary)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"\n>>> {type} DETECTED <<<");
            Console.ResetColor();
            Console.WriteLine($"Headline: {title}");
            Console.WriteLine($"Details:  {CleanSummary(summary)}");
            Console.WriteLine(new string('-', 50));
        }

        static string CleanSummary(string summary)
        {
            // Remove HTML <br/> tags from the summary
            return summary.Replace("<br/>", "\n").Trim();
        }
    }
}