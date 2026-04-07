using System.Text.Json;
using WSG.Mobile.Models;

namespace WSG.Mobile.Services;

public sealed class LocationStorageService
{
    private const string LocationsKey = "saved_locations_json";
    private const string ActiveIndexKey = "active_location_index";
    private const int MaxSavedLocations = 4;

    private List<SavedLocation>? _cached;

    public List<SavedLocation> LoadLocations()
    {
        if (_cached is not null)
            return _cached;

        var json = Preferences.Default.Get(LocationsKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _cached = JsonSerializer.Deserialize<List<SavedLocation>>(json) ?? CreateDefault();
            }
            catch
            {
                _cached = CreateDefault();
            }
        }
        else
        {
            _cached = CreateDefault();
        }

        return _cached;
    }

    public void SaveLocations(List<SavedLocation> locations)
    {
        _cached = locations;
        var json = JsonSerializer.Serialize(locations);
        Preferences.Default.Set(LocationsKey, json);
    }

    public bool AddLocation(SavedLocation location)
    {
        var locations = LoadLocations();
        var savedCount = locations.Count(l => !l.IsFollowing);
        if (savedCount >= MaxSavedLocations)
            return false;

        location.Order = savedCount;
        location.IsFollowing = false;
        locations.Add(location);
        SaveLocations(locations);
        return true;
    }

    public void RemoveLocation(int index)
    {
        var locations = LoadLocations();
        if (index >= 0 && index < locations.Count)
        {
            locations.RemoveAt(index);
            ReorderLocations(locations);
            SaveLocations(locations);
        }
    }

    public SavedLocation? GetFollowingLocation()
    {
        return LoadLocations().FirstOrDefault(l => l.IsFollowing);
    }

    public void SetFollowingLocation(SavedLocation? location)
    {
        var locations = LoadLocations();
        locations.RemoveAll(l => l.IsFollowing);
        if (location is not null)
        {
            location.IsFollowing = true;
            location.Order = -1;
            locations.Insert(0, location);
        }
        SaveLocations(locations);
    }

    public int GetActiveIndex()
    {
        return Preferences.Default.Get(ActiveIndexKey, 0);
    }

    public void SetActiveIndex(int index)
    {
        Preferences.Default.Set(ActiveIndexKey, index);
    }

    public SavedLocation GetActiveLocation()
    {
        var locations = LoadLocations();
        var index = GetActiveIndex();
        if (index >= 0 && index < locations.Count)
            return locations[index];
        return locations.Count > 0 ? locations[0] : CreateDefaultLocation();
    }

    private static List<SavedLocation> CreateDefault()
    {
        return new List<SavedLocation> { CreateDefaultLocation() };
    }

    private static SavedLocation CreateDefaultLocation()
    {
        return new SavedLocation
        {
            Name = "Montreal, QC",
            Latitude = 45.5017,
            Longitude = -73.5673,
            Region = "Canada",
            Order = 0
        };
    }

    private static void ReorderLocations(List<SavedLocation> locations)
    {
        var order = 0;
        foreach (var loc in locations.Where(l => !l.IsFollowing))
        {
            loc.Order = order++;
        }
    }
}
