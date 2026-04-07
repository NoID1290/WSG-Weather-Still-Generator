using WSG.Mobile.Pages;

namespace WSG.Mobile;

public partial class AppShell : Shell
{
	public AppShell(MainPage mainPage, ForecastPage forecastPage, AlertsPage alertsPage, RadarPage radarPage, SettingsPage settingsPage)
	{
		InitializeComponent();

		Items.Add(new TabBar
		{
			Items =
			{
				new ShellContent
				{
					Title = "Home",
					Icon = "home.png",
					Route = "home",
					Content = mainPage
				},
				new ShellContent
				{
					Title = "Forecast",
					Icon = "forecast.png",
					Route = "forecast",
					Content = forecastPage
				},
				new ShellContent
				{
					Title = "Alerts",
					Icon = "alert.png",
					Route = "alerts",
					Content = alertsPage
				},
				new ShellContent
				{
					Title = "Radar",
					Icon = "radar.png",
					Route = "radar",
					Content = radarPage
				},
				new ShellContent
				{
					Title = "Settings",
					Icon = "settings.png",
					Route = "settings",
					Content = settingsPage
				}
			}
		});
	}
}
