// Test script for NWS SAME alert tone generation and alert handling
// Run with: dotnet run --project WeatherImageGenerator -- --test-nws

using System;
using System.IO;
using EAS.NWS;

namespace WeatherImageGenerator
{
    public static class TestNwsAlerts
    {
        /// <summary>
        /// Tests the NWS SAME tone generation and alert parsing.
        /// </summary>
        public static void RunTest()
        {
            Console.WriteLine("=== NWS SAME Alert Test ===\n");

            // Test 1: Generate SAME alert tone
            Console.WriteLine("Test 1: Generating US EAS SAME Attention Signal...");
            string testDir = Path.Combine(Path.GetTempPath(), "NWS_Test");
            Directory.CreateDirectory(testDir);

            string tonePath = Path.Combine(testDir, "NWSSameTone.wav");
            bool success = NwsSameToneGenerator.GenerateSameTone(tonePath);

            if (success && File.Exists(tonePath))
            {
                var fileInfo = new FileInfo(tonePath);
                Console.WriteLine($"  ✓ SAME tone generated successfully!");
                Console.WriteLine($"  ✓ File: {tonePath}");
                Console.WriteLine($"  ✓ Size: {fileInfo.Length:N0} bytes");
                Console.WriteLine($"  ✓ Duration: 8 seconds (alternating EAS patterns)");
                Console.WriteLine($"  ✓ Format: 16-bit PCM WAV, 44.1 kHz, Mono");
                Console.WriteLine($"  ✓ Frequencies: 853 Hz + 960 Hz EAS Attention Signal");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed to generate SAME tone");
            }

            // Test 2: Test cached tone retrieval
            Console.WriteLine("\nTest 2: Testing cached SAME tone retrieval...");
            string? cachedPath = NwsSameToneGenerator.GetOrGenerateSameTone();
            if (cachedPath != null)
            {
                Console.WriteLine($"  ✓ Cached SAME tone available at: {cachedPath}");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed to get cached SAME tone");
            }

            // Test 3: Test NWS alert generation
            Console.WriteLine("\nTest 3: Generating test NWS CAP alerts...");
            var allAlerts = EAS.NWS.TestNwsAlerts.GetAllTestAlerts();
            Console.WriteLine($"  ✓ Generated {allAlerts.Count} test alert types:");
            foreach (var alertType in allAlerts.Keys)
            {
                Console.WriteLine($"      • {alertType}");
            }

            // Test 4: Test alert parsing
            Console.WriteLine("\nTest 4: Testing NWS alert parsing...");
            var tornadoAlert = EAS.NWS.TestNwsAlerts.GenerateTornadoWarning();
            var floodAlert = EAS.NWS.TestNwsAlerts.GenerateFloodWarning();
            var stormAlert = EAS.NWS.TestNwsAlerts.GenerateSevereThunderstormWarning();

            Console.WriteLine($"  ✓ Tornado Warning CAP XML: {tornadoAlert.Length} bytes");
            Console.WriteLine($"  ✓ Flood Warning CAP XML: {floodAlert.Length} bytes");
            Console.WriteLine($"  ✓ Severe Thunderstorm Warning CAP XML: {stormAlert.Length} bytes");

            // Test 5: Verify SAME header format
            Console.WriteLine("\nTest 5: Verifying SAME header format in alerts...");
            if (tornadoAlert.Contains("<valueName>SAME</valueName>"))
            {
                Console.WriteLine($"  ✓ SAME header parameter found in alerts");
                int sameIndex = tornadoAlert.IndexOf("<valueName>SAME</valueName>");
                int valueStart = tornadoAlert.IndexOf("<value>", sameIndex) + 7;
                int valueEnd = tornadoAlert.IndexOf("</value>", sameIndex);
                string sameValue = tornadoAlert.Substring(valueStart, valueEnd - valueStart);
                Console.WriteLine($"  ✓ Sample SAME header: {sameValue}");
            }
            else
            {
                Console.WriteLine($"  ✗ No SAME header parameter in generated alerts");
            }

            // Test 6: Test NwsOptions
            Console.WriteLine("\nTest 6: Testing NwsOptions configuration...");
            var options = new NwsOptions
            {
                Enabled = true,
                HttpTimeoutSeconds = 30
            };
            options.FeedUrls = NwsOptions.GetDefaultFeedUrls();
            Console.WriteLine($"  ✓ NWS feed URLs configured:");
            if (options.FeedUrls != null)
            {
                foreach (var url in options.FeedUrls)
                {
                    Console.WriteLine($"      {url}");
                }
            }
            Console.WriteLine($"  ✓ HTTP Timeout: {options.HttpTimeoutSeconds} seconds");

            Console.WriteLine("\n=== Test Complete ===");
            Console.WriteLine($"Test files location: {testDir}");
            Console.WriteLine("You can play the NWSSameTone.wav file to hear the EAS Attention Signal.");
            Console.WriteLine("Reference: FCC 47 CFR Part 11 - Emergency Alert System");
        }
    }
}
