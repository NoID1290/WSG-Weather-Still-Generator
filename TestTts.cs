using System;
using System.Threading.Tasks;

class TestTts
{
    static async Task Main()
    {
        Console.WriteLine("Testing EdgeTtsClient...");
        using var client = new EAS.EdgeTtsClient();
        var result = await client.SynthesizeToFileAsync(
            "Bonjour, ceci est un test de la synthèse vocale en français canadien.",
            "test_french.mp3",
            EAS.EdgeTtsClient.VOICE_FR_CA_SYLVIE
        );
        Console.WriteLine($"Result: {result}");
        if (System.IO.File.Exists("test_french.mp3"))
        {
            var fi = new System.IO.FileInfo("test_french.mp3");
            Console.WriteLine($"File size: {fi.Length} bytes");
        }
    }
}
