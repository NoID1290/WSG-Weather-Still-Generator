namespace WeatherImageGenerator.Models
{
    public class AlertEntry
    {
        public string City { get; set; } = "";
        public string Type { get; set; } = ""; // WARNING, WATCH, STATEMENT
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string SeverityColor { get; set; } = "Gray"; // Red, Yellow, Gray
    }
}
