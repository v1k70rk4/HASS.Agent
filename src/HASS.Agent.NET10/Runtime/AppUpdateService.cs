using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HASS.Agent.Companion.Runtime;

internal static class AppUpdateService
{
    public static async Task<AppUpdateState> CheckAsync(string installedVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.ExecutableName}/{installedVersion}");

            var latestUrl = $"https://api.github.com/repos/{AppIdentity.GitHubRepository}/releases/latest";
            var release = await http.GetFromJsonAsync<GitHubRelease>(latestUrl, cancellationToken);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return AppUpdateState.Failed(installedVersion, "latest_release_unavailable");
            }

            var asset = SelectReleaseAsset(release.Assets);
            return new AppUpdateState(
                AppIdentity.DisplayName,
                installedVersion,
                release.TagName,
                IsNewerVersion(release.TagName, installedVersion),
                release.HtmlUrl ?? $"{AppIdentity.GitHubRepositoryUrl}/releases/latest",
                asset?.DownloadUrl,
                asset?.Name,
                DateTimeOffset.UtcNow,
                null);
        }
        catch (Exception ex)
        {
            return AppUpdateState.Failed(installedVersion, ex.Message);
        }
    }

    public static async Task<string> DownloadAsync(AppUpdateState update, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl) || string.IsNullOrWhiteSpace(update.AssetName))
        {
            throw new InvalidOperationException("No downloadable update asset is available.");
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppIdentity.ExecutableName}/{update.InstalledVersion}");

        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, SanitizeFileName(update.AssetName));

        await using var source = await http.GetStreamAsync(update.DownloadUrl, cancellationToken);
        await using var destination = File.Create(targetPath);
        await source.CopyToAsync(destination, cancellationToken);
        return targetPath;
    }

    private static GitHubReleaseAsset? SelectReleaseAsset(IReadOnlyList<GitHubReleaseAsset>? assets)
    {
        return assets?
            .Where(asset => !string.IsNullOrWhiteSpace(asset.DownloadUrl))
            .OrderBy(asset => GetAssetPreference(asset.Name))
            .FirstOrDefault(asset => IsNet10Asset(asset.Name) && GetAssetPreference(asset.Name) < 100);
    }

    private static bool IsNet10Asset(string name)
    {
        return name.Contains("HASS.Agent.NET10", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetAssetPreference(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".msi", StringComparison.Ordinal)) return 0;
        if (lower.EndsWith(".exe", StringComparison.Ordinal)) return 1;
        if (lower.EndsWith(".zip", StringComparison.Ordinal)) return 2;
        return 100;
    }

    private static bool IsNewerVersion(string latestTag, string currentVersion)
    {
        var latest = NormalizeVersion(latestTag);
        var current = NormalizeVersion(currentVersion);
        if (latest is not null && current is not null)
        {
            return latest.CompareTo(current) > 0;
        }

        return !string.Equals(latestTag.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static Version? NormalizeVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}

internal sealed record AppUpdateState(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("installed_version")] string InstalledVersion,
    [property: JsonPropertyName("latest_version")] string LatestVersion,
    [property: JsonPropertyName("update_available")] bool UpdateAvailable,
    [property: JsonPropertyName("release_url")] string? ReleaseUrl,
    [property: JsonPropertyName("download_url")] string? DownloadUrl,
    [property: JsonPropertyName("asset_name")] string? AssetName,
    [property: JsonPropertyName("checked_at")] DateTimeOffset CheckedAt,
    [property: JsonPropertyName("error")] string? Error)
{
    public static AppUpdateState Failed(string installedVersion, string error)
    {
        return new AppUpdateState(
            AppIdentity.DisplayName,
            installedVersion,
            installedVersion,
            false,
            $"{AppIdentity.GitHubRepositoryUrl}/releases/latest",
            null,
            null,
            DateTimeOffset.UtcNow,
            error);
    }
}
