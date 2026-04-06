using WSG.Mobile.ViewModels;

namespace WSG.Mobile;

public partial class MainPage : ContentPage
{
	private readonly WeatherAppState _state;
	private bool _hasLoaded;

	public MainPage(WeatherAppState state)
	{
		InitializeComponent();
		_state = state;
		BindingContext = _state;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (_hasLoaded)
		{
			return;
		}

		_hasLoaded = true;
		await _state.RefreshAsync();
	}

	private async void OnRefreshClicked(object? sender, EventArgs e)
	{
		await _state.RefreshAsync();
	}
}
