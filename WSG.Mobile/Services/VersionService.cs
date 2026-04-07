using System.Net.Http.Json;

namespace WSG.Mobile.Services;

public sealed class VersionService
{
    private const string CurrentVersion = "1.1.0";
    private const string GitHubApiUrl = "https://api.github.com/repos/{owner}/{repo}/releases/latest";
    private const string GitHubReleasesUrl = "https://github.com/{owner}/{repo}/releases/latest";

    // Update these with your actual GitHub repo details
    private const string Owner = "noidsoftwork";
    private const string Repo = "weather-still-api";

    public string GetCurrentVersion() => CurrentVersion;

    public async Task<(bool HasUpdate, string LatestVersion, string? ReleaseUrl)> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-Mobile/1.1");
            http.Timeout = TimeSpan.FromSeconds(15);

            var url = GitHubApiUrl.Replace("{owner}", Owner).Replace("{repo}", Repo);
            var response = await http.GetFromJsonAsync<GitHubRelease>(url, cancellationToken);

            if (response?.TagName is null)
                return (false, CurrentVersion, null);

            var latestVersion = response.TagName.TrimStart('v', 'V');
            var releaseUrl = response.HtmlUrl ?? GitHubReleasesUrl.Replace("{owner}", Owner).Replace("{repo}", Repo);

            if (IsNewerVersion(latestVersion, CurrentVersion))
                return (true, latestVersion, releaseUrl);

            return (false, CurrentVersion, null);
        }
        catch
        {
            return (false, CurrentVersion, null);
        }
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(NormalizeSemVer(latest), out var latestVer) &&
            Version.TryParse(NormalizeSemVer(current), out var currentVer))
        {
            return latestVer > currentVer;
        }
        return false;
    }

    private static string NormalizeSemVer(string version)
    {
        // Handle versions like "1.20.48.0406" by taking first 3 parts
        var parts = version.Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => $"{parts[0]}.{parts[1]}.{parts[2]}"
        };
    }

    private sealed class GitHubRelease
    {
        public string? TagName { get; set; }
        public string? HtmlUrl { get; set; }
        public string? Name { get; set; }
    }
}
