// Example: Fetching and displaying radar image for a location
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ECCC;
using ECCC.Services;

namespace RadarExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using var httpClient = new HttpClient();
            
            // Example 1: Fetch radar image using ECCCApi
            Console.WriteLine("Example 1: Using ECCCApi.GetRadarImageAsync()");
            Console.WriteLine("=".PadRight(50, '='));
            
            var imageData = await ECCCApi.GetRadarImageAsync(
                httpClient,
                latitude: 45.5017,   // Montreal
                longitude: -73.5673,
                radiusKm: 150,       // 150km radius
                width: 800,
                height: 600
            );
            
            if (imageData != null)
            {
                await File.WriteAllBytesAsync("montreal_radar.png", imageData);
                Console.WriteLine($"✓ Radar image saved: montreal_radar.png ({imageData.Length} bytes)");
            }
            else
            {
                Console.WriteLine("✗ Failed to fetch radar image");
            }
            
            Console.WriteLine();
            
            // Example 2: Fetch radar image using RadarImageService directly
            Console.WriteLine("Example 2: Using RadarImageService directly");
            Console.WriteLine("=".PadRight(50, '='));
            
            var radarService = new RadarImageService(httpClient);
            
            // Fetch for multiple locations
            var locations = new[]
            {
                (Name: "Quebec City", Lat: 46.8139, Lon: -71.2080),
                (Name: "Toronto", Lat: 43.6532, Lon: -79.3832),
                (Name: "Vancouver", Lat: 49.2827, Lon: -123.1207)
            };
            
            foreach (var location in locations)
            {
                Console.WriteLine($"\nFetching radar for {location.Name}...");
                
                var radarData = await radarService.FetchRadarImageAsync(
                    location.Lat,
                    location.Lon,
                    radiusKm: 100
                );
                
                if (radarData != null)
                {
                    var filename = $"{location.Name.Replace(" ", "_").ToLower()}_radar.png";
                    await File.WriteAllBytesAsync(filename, radarData);
                    Console.WriteLine($"  ✓ Saved: {filename}");
                }
                else
                {
                    Console.WriteLine($"  ✗ Failed to fetch radar for {location.Name}");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine("Example 3: Radar layer information");
            Console.WriteLine("=".PadRight(50, '='));
            Console.WriteLine($"Default layer: {RadarImageService.GetRadarLayerDescription()}");
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
