using WSG.Mobile.Services;

namespace WSG.Mobile;

public partial class App : Application
{
	private readonly AppShell _appShell;
	private readonly ThemeService _themeService;

	public App(AppShell appShell, ThemeService themeService)
	{
		InitializeComponent();
		_appShell = appShell;
		_themeService = themeService;
		_themeService.ApplyCurrentTheme();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_appShell);
	}
}