using WSG.Mobile.Services;
using WSG.Mobile.ViewModels;

namespace WSG.Mobile;

public partial class MainPage : ContentPage
{
	private readonly WeatherAppState _state;
	private readonly LocationStorageService _locationStorage;
	private bool _hasLoaded;
	private bool _suppressPickerEvent;

	public MainPage(WeatherAppState state, LocationStorageService locationStorage)
	{
		InitializeComponent();
		_state = state;
		_locationStorage = locationStorage;
		BindingContext = _state;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		RefreshLocationPicker();

		if (_hasLoaded)
			return;

		_hasLoaded = true;
		await _state.RefreshAsync();
	}

	private void RefreshLocationPicker()
	{
		_suppressPickerEvent = true;
		var locations = _locationStorage.LoadLocations();
		var names = locations.Select(l => l.DisplayName).ToList();
		LocationPicker.ItemsSource = names;
		var activeIdx = _locationStorage.GetActiveIndex();
		if (activeIdx >= 0 && activeIdx < names.Count)
			LocationPicker.SelectedIndex = activeIdx;
		else if (names.Count > 0)
			LocationPicker.SelectedIndex = 0;
		_suppressPickerEvent = false;
	}

	private async void OnLocationChanged(object? sender, EventArgs e)
	{
		if (_suppressPickerEvent || LocationPicker.SelectedIndex < 0)
			return;

		_state.SwitchLocation(LocationPicker.SelectedIndex);
		await _state.RefreshAsync();
	}

	private async void OnRefreshClicked(object? sender, EventArgs e)
	{
		await _state.RefreshAsync();
	}
}
