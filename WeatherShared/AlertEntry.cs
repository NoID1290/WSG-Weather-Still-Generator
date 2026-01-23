using System;

namespace WeatherImageGenerator.Models
{
    public class AlertEntry
    {
        public string City { get; set; } = "";
        public string Type { get; set; } = ""; // WARNING, WATCH, STATEMENT
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string SeverityColor { get; set; } = "Gray"; // Red, Yellow, Gray
        
        // Extended fields from ECCC API
        /// <summary>Impact level: modéré, élevé, etc.</summary>
        public string? Impact { get; set; }
        /// <summary>Forecast confidence: élevée, modérée, etc.</summary>
        public string? Confidence { get; set; }
        /// <summary>When the alert was issued</summary>
        public DateTimeOffset? IssuedAt { get; set; }
        /// <summary>When the alert expires</summary>
        public DateTimeOffset? ExpiresAt { get; set; }
        /// <summary>Full detailed description text</summary>
        public string? Description { get; set; }
        /// <summary>Safety instructions</summary>
        public string? Instructions { get; set; }
        /// <summary>URL to detailed report</summary>
        public string? DetailUrl { get; set; }
        /// <summary>Region/area affected</summary>
        public string? Region { get; set; }
    }
}