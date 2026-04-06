using WSG.Mobile.Pages;

namespace WSG.Mobile;

public partial class AppShell : Shell
{
	public AppShell(MainPage mainPage, ForecastPage forecastPage, AlertsPage alertsPage, SettingsPage settingsPage)
	{
		InitializeComponent();

		Items.Add(new TabBar
		{
			Items =
			{
				new ShellContent
				{
					Title = "Home",
					Route = "home",
					Content = mainPage
				},
				new ShellContent
				{
					Title = "Forecast",
					Route = "forecast",
					Content = forecastPage
				},
				new ShellContent
				{
					Title = "Alerts",
					Route = "alerts",
					Content = alertsPage
				},
				new ShellContent
				{
					Title = "Settings",
					Route = "settings",
					Content = settingsPage
				}
			}
		});
	}
}
