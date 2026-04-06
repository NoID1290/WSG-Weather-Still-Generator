using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace WSG.Mobile.Services;

public static class ServiceHelper
{
    public static IServiceProvider Services =>
        IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI service provider is not available yet.");

    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();
}
