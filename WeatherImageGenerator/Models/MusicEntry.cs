using System;
using System.IO;
using System.Text.Json.Serialization;

namespace WeatherImageGenerator.Models
{
    /// <summary>
    /// Represents a music track that can be used in video generation
    /// </summary>
    public class MusicEntry
    {
        /// <summary>
        /// Display name of the music track
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// Full path to the music file
        /// </summary>
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Whether this is a built-in demo track
        /// </summary>
        [JsonPropertyName("isDemo")]
        public bool IsDemo { get; set; } = false;

        /// <summary>
        /// Creates a new music entry
        /// </summary>
        public MusicEntry()
        {
        }

        /// <summary>
        /// Creates a new music entry with the specified parameters
        /// </summary>
        public MusicEntry(string name, string filePath, bool isDemo = false)
        {
            Name = name;
            FilePath = filePath;
            IsDemo = isDemo;
        }

        /// <summary>
        /// Checks if the music file exists
        /// </summary>
        public bool FileExists()
        {
            return !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        }

        /// <summary>
        /// Returns a display-friendly string representation
        /// </summary>
        public override string ToString()
        {
            string demo = IsDemo ? " [Demo]" : "";
            string exists = FileExists() ? "" : " [Missing]";
            return $"{Name}{demo}{exists}";
        }

        /// <summary>
        /// Gets the file extension
        /// </summary>
        public string GetFileExtension()
        {
            return Path.GetExtension(FilePath).ToLowerInvariant();
        }
    }
}
