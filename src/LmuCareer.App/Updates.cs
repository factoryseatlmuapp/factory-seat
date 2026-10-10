using System.Net.Http;
using System.Text.Json;

namespace LmuCareer.App;

/// <summary>The newest published release on GitHub, for the "new version" notice.</summary>
public static class Updates
{
    /// <summary>Where releases are published: owner/repository on GitHub.</summary>
    public const string Repository = "factoryseatlmuapp/factory-seat";

    public sealed record Release(Version Version, string Url);

    /// <summary>This build's version, numbers only (1.1.0 for a 1.1.0-beta build).</summary>
    public static Version Current { get; } = typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>This build's version as released: "1.1.0", or "1.1.0-beta" for a pre-release.</summary>
    public static string CurrentLabel { get; } = Label();

    /// <summary>
    /// Whether a published release is newer than this build. A pre-release is older than the release
    /// it leads up to, so a 1.1.0-beta build is told when 1.1.0 is out.
    /// </summary>
    public static bool IsNewer(Version latest)
    {
        var current = new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));
        var published = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
        return published > current || (published == current && CurrentLabel.Contains('-'));
    }

    private static string Label()
    {
        var info = typeof(Updates).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        // The SDK adds "+<commit>" to the informational version; that isn't part of the release name.
        var label = info?.Split('+')[0];
        return string.IsNullOrEmpty(label) ? Current.ToString(3) : label;
    }

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
