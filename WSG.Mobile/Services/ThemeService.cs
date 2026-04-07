namespace WSG.Mobile.Services;

public sealed class ThemeService
{
    private const string ThemeKey = "app_theme";

    public string CurrentTheme => Preferences.Default.Get(ThemeKey, "System");

    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "System", "Light", "Dark" };

    public void ApplyTheme(string theme)
    {
        Preferences.Default.Set(ThemeKey, theme);
        ApplyToApp(theme);
    }

    public void ApplyCurrentTheme()
    {
        ApplyToApp(CurrentTheme);
    }

    private static void ApplyToApp(string theme)
    {
        if (Application.Current is null)
            return;

        Application.Current.UserAppTheme = theme switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}
