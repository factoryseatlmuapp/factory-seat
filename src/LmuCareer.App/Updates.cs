using System.Net.Http;
using System.Text.Json;

namespace LmuCareer.App;

/// <summary>The newest published release on GitHub, for the "new version" notice.</summary>
public static class Updates
{
    /// <summary>Where releases are published: owner/repository on GitHub.</summary>
    public const string Repository = "factoryseatlmuapp/factory-seat";

    public sealed record Release(Version Version, string Url);

    public static async Task<Release?> LatestRelease()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FactorySeat-update-check");
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest"));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            // Only ever link to this repository's release pages.
            if (!Version.TryParse(tag, out var version) || !url.StartsWith($"https://github.com/{Repository}/releases/", StringComparison.OrdinalIgnoreCase))
                return null;
            return new Release(version, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
