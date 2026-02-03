using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EAS;
using WeatherImageGenerator.Models;
using WeatherImageGenerator.Services;

namespace WeatherImageGenerator
{
    /// <summary>
    /// Test program for NAAD/Alert Ready emergency alert system
    /// </summary>
    public static class TestEmergencyAlerts
    {
        public static async Task RunTest()
        {
            Console.WriteLine("=== NAAD/Alert Ready Emergency Alert System Test ===\n");

            // Create output directory
            string outputDir = "TestAlerts";
            if (!System.IO.Directory.Exists(outputDir))
            {
                System.IO.Directory.CreateDirectory(outputDir);
            }

            // Test 1: Generate test CAP-CP XML alerts
            Console.WriteLine("Step 1: Generating test CAP-CP XML alerts...");
            var testAlerts = TestAlertGenerator.GetAllTestAlerts();
            var xmlAlerts = new List<(string name, string xml)>();

            foreach (var kvp in testAlerts)
            {
                string xml = kvp.Value("fr-CA");
                xmlAlerts.Add((kvp.Key, xml));
                
                // Save XML for inspection
                string xmlFile = System.IO.Path.Combine(outputDir, $"{kvp.Key.Replace(" ", "_")}.xml");
                System.IO.File.WriteAllText(xmlFile, xml);
                Console.WriteLine($"  ✓ Generated: {kvp.Key} -> {xmlFile}");
            }

            // Test 2: Parse CAP-CP XML into AlertEntry objects
            Console.WriteLine("\nStep 2: Parsing CAP-CP alerts with AlertReadyClient...");
            var httpClient = new System.Net.Http.HttpClient();
            var options = new AlertReadyOptions
            {
                Enabled = true,
                ExcludeWeatherAlerts = true,
                PreferredLanguage = "fr-CA",
                Jurisdictions = new List<string> { "QC", "CA" },
                HighRiskOnly = false // Include all severity levels for testing
            };
            
            var client = new AlertReadyClient(httpClient, options);
            client.Log = (msg) => Console.WriteLine($"  [AlertReadyClient] {msg}");

            var parsedAlerts = new List<AlertEntry>();
            foreach (var (name, xml) in xmlAlerts)
            {
                try
                {
                    var alerts = client.GetType()
                        .GetMethod("ParseAlerts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.Invoke(client, new object[] { xml, new List<string>() }) as List<AlertEntry>;

                    if (alerts != null && alerts.Count > 0)
                    {
                        parsedAlerts.AddRange(alerts);
                        Console.WriteLine($"  ✓ Parsed: {name} -> {alerts.Count} alert(s)");
                    }
                    else
                    {
                        Console.WriteLine($"  ✗ Filtered out: {name} (likely weather alert)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Failed to parse {name}: {ex.Message}");
                }
            }

            // Test 3: Generate emergency alert images, audio, and video
            Console.WriteLine($"\nStep 3: Generating {parsedAlerts.Count} emergency alert image(s), audio, and video...");
            if (parsedAlerts.Count > 0)
            {
                try
                {
                    // Use the new method that generates both media AND video automatically
                    var (generatedFiles, videoPath) = EmergencyAlertGenerator.GenerateEmergencyAlertsWithVideo(
                        parsedAlerts, 
                        outputDir, 
                        "fr-CA"
                    );

                    Console.WriteLine($"\n✓ Successfully generated {generatedFiles.Count} file(s):");
                    foreach (var file in generatedFiles)
                    {
                        var fileInfo = new System.IO.FileInfo(file);
                        Console.WriteLine($"  • {fileInfo.Name} ({fileInfo.Length / 1024.0:F1} KB)");
                    }
                    
                    if (!string.IsNullOrEmpty(videoPath))
                    {
                        Console.WriteLine($"\n✓ Alert video generated: {videoPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to generate alerts: {ex.Message}");
                    Console.WriteLine($"   Stack trace: {ex.StackTrace}");
                }
            }
            else
            {
                Console.WriteLine("No alerts to generate (all were filtered out).");
            }

            // Summary
            Console.WriteLine("\n=== Test Complete ===");
            Console.WriteLine($"Output directory: {System.IO.Path.GetFullPath(outputDir)}");
            Console.WriteLine("\nCheck the output directory for:");
            Console.WriteLine("  • *.xml files (CAP-CP alert XML)");
            Console.WriteLine("  • EmergencyAlert_*.png files (alert images)");
            Console.WriteLine("  • EmergencyAlert_*.wav files (alert audio)");
            Console.WriteLine("  • alert_video.mp4 (alert video)");
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
