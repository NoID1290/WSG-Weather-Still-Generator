using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace WeatherImageGenerator.Utilities
{
    internal static class EmbeddedResourceLoader
    {
        private const string RootNamespace = "WeatherImageGenerator";

        public static bool TryReadText(string relativePath, out string content)
        {
            if (TryReadBytes(relativePath, out var bytes))
            {
                content = Encoding.UTF8.GetString(bytes);
                return true;
            }

            content = string.Empty;
            return false;
        }

        public static bool TryReadBytes(string relativePath, out byte[] content)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = BuildResourceName(relativePath);

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? FindResourceBySuffix(assembly, resourceName);

            if (stream == null)
            {
                content = Array.Empty<byte>();
                return false;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            content = ms.ToArray();
            return true;
        }

        private static Stream? FindResourceBySuffix(Assembly assembly, string resourceName)
        {
            var all = assembly.GetManifestResourceNames();
            var match = all.FirstOrDefault(n =>
                string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

            return match != null ? assembly.GetManifestResourceStream(match) : null;
        }

        private static string BuildResourceName(string relativePath)
        {
            var normalized = relativePath
                .Replace('\\', '.')
                .Replace('/', '.')
                .Trim('.');

            if (normalized.StartsWith(RootNamespace + ".", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return $"{RootNamespace}.{normalized}";
        }
    }
}
