namespace WSG.Mobile.Models;

public sealed class AppSettings
{
    // Display
    public string Theme { get; set; } = "System";
    public string TemperatureUnit { get; set; } = "°C";
    public string WindSpeedUnit { get; set; } = "km/h";
    public string PressureUnit { get; set; } = "hPa";
    public string TimeFormat { get; set; } = "24h";

    // Radar
    public int RadarFrameCount { get; set; } = 15;
    public string RadarAnimationSpeed { get; set; } = "Normal";
    public int RadarOpacityPercent { get; set; } = 70;
    public bool RadarAutoCenter { get; set; } = true;

    // Alerts
    public string AlertRegion { get; set; } = "Canada";
    public bool HighRiskOnly { get; set; } = true;
    public bool NotificationsEnabled { get; set; }
    public int AlertPollIntervalMinutes { get; set; } = 30;

    // Thresholds
    public bool TempThresholdEnabled { get; set; }
    public float TempThresholdMin { get; set; } = -20f;
    public float TempThresholdMax { get; set; } = 35f;
    public bool WindThresholdEnabled { get; set; }
    public float WindThresholdMax { get; set; } = 80f;
    public bool PrecipThresholdEnabled { get; set; }
    public float PrecipThresholdMax { get; set; } = 25f;
}
