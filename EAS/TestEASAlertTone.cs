// Test script for EAS Alert Tone Generation
// Run with: dotnet run --project WeatherImageGenerator -- --test-eas

using System;
using System.IO;
using EAS;

namespace WeatherImageGenerator
{
    public static class TestEASAlertTone
    {
        /// <summary>
        /// Tests the Alert Ready Canada attention signal generation.
        /// </summary>
        public static void RunTest()
        {
            Console.WriteLine("=== EAS Alert Tone Test ===\n");

            // Test 1: Generate alert tone
            Console.WriteLine("Test 1: Generating Canadian Alerting Attention Signal...");
            string testDir = Path.Combine(Path.GetTempPath(), "EAS_Test");
            Directory.CreateDirectory(testDir);

            string tonePath = Path.Combine(testDir, "AlertReadyTone.wav");
            bool success = AlertToneGenerator.GenerateAlertTone(tonePath);

            if (success && File.Exists(tonePath))
            {
                var fileInfo = new FileInfo(tonePath);
                Console.WriteLine($"  ✓ Alert tone generated successfully!");
                Console.WriteLine($"  ✓ File: {tonePath}");
                Console.WriteLine($"  ✓ Size: {fileInfo.Length:N0} bytes");
                Console.WriteLine($"  ✓ Duration: 8 seconds (alternating tones)");
                Console.WriteLine($"  ✓ Format: 16-bit PCM WAV, 44.1 kHz, Mono");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed to generate alert tone");
            }

            // Test 2: Test cached tone retrieval
            Console.WriteLine("\nTest 2: Testing cached tone retrieval...");
            string? cachedPath = AlertToneGenerator.GetOrGenerateAlertTone();
            if (cachedPath != null)
            {
                Console.WriteLine($"  ✓ Cached tone available at: {cachedPath}");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed to get cached tone");
            }

            // Test 3: Test AlertReadyClient options
            Console.WriteLine("\nTest 3: Testing AlertReadyClient configuration...");
            var options = new AlertReadyOptions
            {
                Enabled = true,
                PreferredLanguage = "fr-CA",
                Jurisdictions = new() { "QC", "CA" },
                HighRiskOnly = true,
                ExcludeWeatherAlerts = true,
                GenerateAlertTone = true
            };

            // Add default NAAD URLs
            options.FeedUrls = AlertReadyOptions.GetDefaultNaadUrls();
            Console.WriteLine($"  ✓ Default NAAD TCP URLs:");
            foreach (var url in options.FeedUrls)
            {
                Console.WriteLine($"      {url}");
            }

            Console.WriteLine($"  ✓ Default HTTP URLs:");
            foreach (var url in AlertReadyOptions.GetDefaultHttpUrls())
            {
                Console.WriteLine($"      {url}");
            }

            // Test 4: Test ConnectionHealthStats
            Console.WriteLine("\nTest 4: Testing ConnectionHealthStats...");
            var stats = new ConnectionHealthStats
            {
                Status = NaadConnectionStatus.Disconnected,
                LastHeartbeat = null,
                TimeSinceLastHeartbeat = TimeSpan.MaxValue,
                IsHealthy = false,
                ActiveAlertCount = 0,
                CachedIdentifierCount = 0,
                StreamTasksRunning = 0
            };
            Console.WriteLine($"  ✓ {stats}");

            Console.WriteLine("\n=== Test Complete ===");
            Console.WriteLine($"Test files location: {testDir}");
            Console.WriteLine("You can play the AlertReadyTone.wav file to verify the tone.");
        }
    }
}
