using System.Text.Json;
using WSG.Mobile.Models;

namespace WSG.Mobile.Services;

public sealed class SettingsService
{
    private const string SettingsKey = "app_settings_json";
    private AppSettings? _cached;

    public AppSettings Load()
    {
        if (_cached is not null)
            return _cached;

        var json = Preferences.Default.Get(SettingsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _cached = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                _cached = new AppSettings();
            }
        }
        else
        {
            _cached = new AppSettings();
        }

        return _cached;
    }

    public void Save(AppSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings);
        Preferences.Default.Set(SettingsKey, json);
    }

    public void Save()
    {
        if (_cached is not null)
            Save(_cached);
    }
}
