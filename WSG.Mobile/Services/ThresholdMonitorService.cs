using WSG.Mobile.Models;

namespace WSG.Mobile.Services;

public sealed class ThresholdMonitorService
{
    private DateTimeOffset _lastTempAlert = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWindAlert = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPrecipAlert = DateTimeOffset.MinValue;
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(3);

    public void CheckThresholds(WeatherSnapshot snapshot, AppSettings settings, Action<string, string>? onBreach)
    {
        if (onBreach is null)
            return;

        var now = DateTimeOffset.Now;

        // Temperature threshold
        if (settings.TempThresholdEnabled && TryParseTemp(snapshot.CurrentTemperatureDisplay, out var temp))
        {
            if (now - _lastTempAlert > Cooldown)
            {
                if (temp < settings.TempThresholdMin)
                {
                    _lastTempAlert = now;
                    onBreach($"Low Temperature Alert", $"Current temperature is {temp:F1}°C, below your {settings.TempThresholdMin:F0}°C threshold.");
                }
                else if (temp > settings.TempThresholdMax)
                {
                    _lastTempAlert = now;
                    onBreach($"High Temperature Alert", $"Current temperature is {temp:F1}°C, above your {settings.TempThresholdMax:F0}°C threshold.");
                }
            }
        }

        // Wind threshold
        if (settings.WindThresholdEnabled && TryParseValue(snapshot.WindDisplay, out var wind))
        {
            if (wind > settings.WindThresholdMax && now - _lastWindAlert > Cooldown)
            {
                _lastWindAlert = now;
                onBreach("High Wind Alert", $"Wind speed is {snapshot.WindDisplay}, above your {settings.WindThresholdMax:F0} threshold.");
            }
        }

        // Precipitation threshold
        if (settings.PrecipThresholdEnabled && TryParseValue(snapshot.PrecipitationDisplay, out var precip))
        {
            if (precip > settings.PrecipThresholdMax && now - _lastPrecipAlert > Cooldown)
            {
                _lastPrecipAlert = now;
                onBreach("Heavy Precipitation Alert", $"Precipitation is {snapshot.PrecipitationDisplay}, above your {settings.PrecipThresholdMax:F0} mm threshold.");
            }
        }
    }

    private static bool TryParseTemp(string display, out float value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(display) || display == "--" || display == "—")
            return false;
        // Extract numeric part from strings like "12.3 °C"
        var numericPart = new string(display.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return float.TryParse(numericPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseValue(string display, out float value)
    {
        return TryParseTemp(display, out value);
    }
}
