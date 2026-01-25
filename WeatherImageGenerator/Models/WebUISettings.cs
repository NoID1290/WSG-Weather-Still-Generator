using System.Collections.Generic;

namespace WeatherImageGenerator.Models
{
    public class WebUISettings
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 5000;
        public bool AllowRemoteAccess { get; set; } = true;
        public bool EnableCORS { get; set; } = true;
        public List<string> CORSOrigins { get; set; } = new() { "*" };
    }
}
