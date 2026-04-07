namespace WSG.Mobile.Services;

public sealed class WeatherIconService
{
    /// <summary>
    /// Maps a WMO weather code to a Unicode weather emoji for display.
    /// The WMO codes come from Open-Meteo's weathercode field.
    /// </summary>
    public string GetWeatherIcon(int wmoCode, bool isNight = false)
    {
        return wmoCode switch
        {
            0 => isNight ? "🌙" : "☀️",                    // Clear sky
            1 => isNight ? "🌙" : "🌤️",                   // Mainly clear
            2 => isNight ? "☁️" : "⛅",                     // Partly cloudy
            3 => "☁️",                                       // Overcast
            45 or 48 => "🌫️",                               // Fog / Depositing rime fog
            51 or 53 or 55 => "🌦️",                         // Drizzle light/moderate/dense
            56 or 57 => "🌧️",                               // Freezing drizzle
            61 or 63 => "🌧️",                               // Rain slight/moderate
            65 => "🌧️",                                     // Rain heavy
            66 or 67 => "🌧️",                               // Freezing rain
            71 or 73 => "🌨️",                               // Snow slight/moderate
            75 or 77 => "❄️",                                // Snow heavy / Snow grains
            80 or 81 or 82 => "🌦️",                         // Rain showers
            85 or 86 => "🌨️",                               // Snow showers
            95 => "⛈️",                                      // Thunderstorm
            96 or 99 => "⛈️",                                // Thunderstorm with hail
            _ => "🌡️"                                       // Default
        };
    }

    /// <summary>
    /// Gets an accent color hex string based on the WMO weather code.
    /// </summary>
    public string GetConditionColor(int wmoCode)
    {
        return wmoCode switch
        {
            0 or 1 => "#F59E0B",        // Clear/Sunny → Amber
            2 or 3 => "#6B7280",         // Cloudy → Gray
            45 or 48 => "#9CA3AF",       // Fog → Light gray
            51 or 53 or 55 or 56 or 57 => "#60A5FA", // Drizzle → Light blue
            61 or 63 or 65 or 66 or 67 => "#3B82F6", // Rain → Blue
            71 or 73 or 75 or 77 => "#67E8F9",       // Snow → Cyan
            80 or 81 or 82 => "#2563EB",              // Showers → Dark blue
            85 or 86 => "#06B6D4",                     // Snow showers → Teal
            95 or 96 or 99 => "#8B5CF6",              // Thunder → Purple
            _ => "#6B7280"                             // Default gray
        };
    }

    /// <summary>
    /// Gets a color for temperature display. Blue for cold, red for hot.
    /// </summary>
    public string GetTemperatureColor(float tempCelsius)
    {
        return tempCelsius switch
        {
            < -20 => "#1E40AF",   // Deep blue
            < -10 => "#2563EB",   // Blue
            < 0 => "#3B82F6",     // Medium blue
            < 10 => "#60A5FA",    // Light blue
            < 20 => "#34D399",    // Green
            < 25 => "#FBBF24",    // Yellow
            < 30 => "#F97316",    // Orange
            < 35 => "#EF4444",    // Red
            _ => "#DC2626"        // Deep red
        };
    }
}
