using WSG.Mobile.ViewModels;

namespace WSG.Mobile.Pages;

public partial class AlertsPage : ContentPage
{
    private readonly WeatherAppState _state;

    public AlertsPage(WeatherAppState state)
    {
        InitializeComponent();
        _state = state;
        BindingContext = _state;
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await _state.RefreshAsync();
    }
}
