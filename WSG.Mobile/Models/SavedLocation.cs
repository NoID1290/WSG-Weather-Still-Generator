namespace WSG.Mobile.Models;

public sealed class SavedLocation
{
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Region { get; set; } = "Canada";
    public bool IsFollowing { get; set; }
    public int Order { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Latitude:F2}, {Longitude:F2}" : Name;
}
