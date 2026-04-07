using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace WSG.Mobile.Platforms.Android.Widget;

[BroadcastReceiver(Label = "WSG Weather", Exported = true)]
[IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
[MetaData("android.appwidget.provider", Resource = "@xml/weather_widget_info")]
public class WeatherWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null)
            return;

        foreach (var widgetId in appWidgetIds)
        {
            UpdateWidget(context, appWidgetManager, widgetId);
        }
    }

    public static void UpdateWidget(Context context, AppWidgetManager appWidgetManager, int appWidgetId)
    {
        var prefs = context.GetSharedPreferences("wsg_widget", FileCreationMode.Private);
        var location = prefs?.GetString("location", "—") ?? "—";
        var temperature = prefs?.GetString("temperature", "--") ?? "--";
        var condition = prefs?.GetString("condition", "") ?? "";
        var updated = prefs?.GetString("updated", "") ?? "";

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
        views.SetTextViewText(Resource.Id.widget_location, location);
        views.SetTextViewText(Resource.Id.widget_temperature, temperature);
        views.SetTextViewText(Resource.Id.widget_condition, condition);
        views.SetTextViewText(Resource.Id.widget_updated, updated);

        // Open app when widget is tapped
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is not null)
        {
            var pendingIntent = PendingIntent.GetActivity(
                context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            views.SetOnClickPendingIntent(Resource.Id.widget_root, pendingIntent);
        }

        appWidgetManager.UpdateAppWidget(appWidgetId, views);
    }

    /// <summary>
    /// Called from the main app to push updated weather data to the widget.
    /// </summary>
    public static void PushWeatherData(Context context, string location, string temperature, string condition)
    {
        var prefs = context.GetSharedPreferences("wsg_widget", FileCreationMode.Private);
        var editor = prefs?.Edit();
        if (editor is null) return;

        editor.PutString("location", location);
        editor.PutString("temperature", temperature);
        editor.PutString("condition", condition);
        editor.PutString("updated", DateTime.Now.ToString("HH:mm"));
        editor.Apply();

        // Trigger widget update
        var appWidgetManager = AppWidgetManager.GetInstance(context);
        var componentName = new ComponentName(context, Java.Lang.Class.FromType(typeof(WeatherWidgetProvider)));
        var widgetIds = appWidgetManager?.GetAppWidgetIds(componentName);
        if (widgetIds is not null && appWidgetManager is not null)
        {
            foreach (var id in widgetIds)
            {
                UpdateWidget(context, appWidgetManager, id);
            }
        }
    }
}
