using WSG.Mobile.ViewModels;

namespace WSG.Mobile.Pages;

public partial class ForecastPage : ContentPage
{
    private readonly WeatherAppState _state;

    public ForecastPage(WeatherAppState state)
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
