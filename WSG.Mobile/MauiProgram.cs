using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WSG.Mobile.Controls;
using WSG.Mobile.Pages;
using WSG.Mobile.Platforms.Android;
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
			})
			.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<RadarGLView, RadarGLViewHandler>();
			});

		// Core services
		builder.Services.AddSingleton<WeatherAggregatorService>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<LocationStorageService>();
		builder.Services.AddSingleton<GpsLocationService>();
		builder.Services.AddSingleton<ThemeService>();
		builder.Services.AddSingleton<WeatherIconService>();
		builder.Services.AddSingleton<RadarService>();
		builder.Services.AddSingleton<VersionService>();
		builder.Services.AddSingleton<AlertPollingService>();
		builder.Services.AddSingleton<ThresholdMonitorService>();
		builder.Services.AddSingleton<TileCacheService>();

		// ViewModel
		builder.Services.AddSingleton<WeatherAppState>();

		// Pages
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<ForecastPage>();
		builder.Services.AddSingleton<AlertsPage>();
		builder.Services.AddSingleton<RadarPage>();
		builder.Services.AddSingleton<SettingsPage>();

		// Shell
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
