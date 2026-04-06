using WSG.Mobile.Services;
using WSG.Mobile.ViewModels;

namespace WSG.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly WeatherAppState _state;

    public SettingsPage()
    {
        InitializeComponent();
        _state = ServiceHelper.GetRequiredService<WeatherAppState>();
        BindingContext = _state;
    }

    private async void OnSaveAndRefreshClicked(object? sender, EventArgs e)
    {
        _state.SaveSettings();
        await _state.RefreshAsync();
    }
}
