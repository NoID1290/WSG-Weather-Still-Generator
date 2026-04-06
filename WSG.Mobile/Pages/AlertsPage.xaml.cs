using WSG.Mobile.Services;
using WSG.Mobile.ViewModels;

namespace WSG.Mobile.Pages;

public partial class AlertsPage : ContentPage
{
    private readonly WeatherAppState _state;

    public AlertsPage()
    {
        InitializeComponent();
        _state = ServiceHelper.GetRequiredService<WeatherAppState>();
        BindingContext = _state;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await _state.RefreshAsync();
    }
}
