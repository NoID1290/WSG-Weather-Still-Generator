using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using WeatherImageGenerator.Services;

class TestAlertFix
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing ECCC Alert Detection Fix");
        Console.WriteLine("==================================\n");

        using var httpClient = new HttpClient();
        
        // Test with the cities configured in appsettings.json
        var locations = new[] { "Montreal", "Quebec City", "Gatineau", "Amos", "Saint-Rémi" };
        
        try
        {
            var alerts = await ECCC.FetchAllAlerts(httpClient, locations);
            
            Console.WriteLine($"Total alerts found: {alerts.Count}\n");
            
            if (alerts.Count > 0)
            {
                foreach (var alert in alerts)
                {
                    Console.WriteLine($"City: {alert.City}");
                    Console.WriteLine($"Title: {alert.Title}");
                    Console.WriteLine($"Summary: {alert.Summary.Substring(0, Math.Min(150, alert.Summary.Length))}...");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("No active alerts found for the configured cities.");
                Console.WriteLine("This is expected if there are no current weather alerts.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
