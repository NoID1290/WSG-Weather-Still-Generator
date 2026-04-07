using WSG.Mobile.Models;

namespace WSG.Mobile.Services;

public sealed class GpsLocationService
{
    private CancellationTokenSource? _pollCts;
    private bool _isTracking;

    public bool IsTracking => _isTracking;

    public async Task<SavedLocation?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                    return null;
            }

            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request, cancellationToken);

            if (location is null)
                return null;

            var name = await ReverseGeocodeAsync(location.Latitude, location.Longitude);

            return new SavedLocation
            {
                Name = name ?? $"{location.Latitude:F2}, {location.Longitude:F2}",
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Region = "Both",
                IsFollowing = true,
                Order = -1
            };
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            return null;
        }
    }

    public void StartTracking(LocationStorageService locationStorage, Action? onLocationUpdated = null)
    {
        if (_isTracking)
            return;

        _isTracking = true;
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), token);
                    var loc = await GetCurrentLocationAsync(token);
                    if (loc is not null)
                    {
                        locationStorage.SetFollowingLocation(loc);
                        MainThread.BeginInvokeOnMainThread(() => onLocationUpdated?.Invoke());
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch
                {
                    // Silently retry on next interval
                }
            }
        }, token);
    }

    public void StopTracking()
    {
        _isTracking = false;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private static async Task<string?> ReverseGeocodeAsync(double latitude, double longitude)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={latitude:F4},{longitude:F4}&count=1&language=en&format=json";
            // Open-Meteo geocoding doesn't support reverse, so we use the MAUI geocoder
            var placemarks = await Geocoding.Default.GetPlacemarksAsync(latitude, longitude);
            var place = placemarks?.FirstOrDefault();
            if (place is not null)
            {
                var city = place.Locality ?? place.SubAdminArea ?? place.AdminArea;
                var region = place.AdminArea;
                if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(region) && city != region)
                    return $"{city}, {region}";
                return city ?? region ?? place.CountryName;
            }
        }
        catch
        {
            // Geocoding failure is non-critical
        }
        return null;
    }
}
