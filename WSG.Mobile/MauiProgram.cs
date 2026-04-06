using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WSG.Mobile.Pages;
using WSG.Mobile.Services;
using WSG.Mobile.ViewModels;

namespace WSG.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<WeatherAggregatorService>();
		builder.Services.AddSingleton<WeatherAppState>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<ForecastPage>();
		builder.Services.AddSingleton<AlertsPage>();
		builder.Services.AddSingleton<SettingsPage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
