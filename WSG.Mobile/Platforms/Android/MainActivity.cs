using Android.App;
using Android.Content.PM;
using Android.OS;

namespace WSG.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CreateNotificationChannels();
    }

    private void CreateNotificationChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
            return;

        var alertChannel = new NotificationChannel(
            "weather_alerts",
            "Weather Alerts",
            NotificationImportance.High)
        {
            Description = "Active weather alerts for your saved locations"
        };
        manager.CreateNotificationChannel(alertChannel);

        var thresholdChannel = new NotificationChannel(
            "weather_thresholds",
            "Threshold Alerts",
            NotificationImportance.Default)
        {
            Description = "Notifications when weather conditions exceed your custom thresholds"
        };
        manager.CreateNotificationChannel(thresholdChannel);
    }
}
