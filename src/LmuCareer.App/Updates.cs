using System.Net.Http;
using System.Text.Json;

namespace LmuCareer.App;

/// <summary>The newest published release on GitHub, for the "new version" notice.</summary>
public static class Updates
{
    /// <summary>Where releases are published: owner/repository on GitHub.</summary>
    public const string Repository = "factoryseatlmuapp/factory-seat";

    /// <param name="Highlights">
    /// What's new, by language code ("en", "de", "pt-BR"), from the release's highlights.json; empty
    /// when the release has none.
    /// </param>
    public sealed record Release(Version Version, string Url, IReadOnlyDictionary<string, IReadOnlyList<string>> Highlights)
    {
        /// <summary>Up to three highlights in the language, else its base language, else English.</summary>
        public IReadOnlyList<string> HighlightsIn(string language)
        {
            var dash = language.IndexOf('-');
            foreach (var code in new[] { language, dash > 0 ? language[..dash] : language, "en" })
                if (Highlights.TryGetValue(code, out var list) && list.Count > 0) return list.Take(3).ToList();
            return [];
        }
    }

    /// <summary>The release asset with the update card's highlights.</summary>
    public const string HighlightsAsset = "highlights.json";

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
            return new Release(version, url, await Highlights(http, doc.RootElement));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The release's highlights.json: <c>{"en": ["...", "..."], "de": [...]}</c>. Only read from this
    /// repository's release downloads; a release without it, or one that can't be read, has none.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> Highlights(HttpClient http, JsonElement release)
    {
        var none = new Dictionary<string, IReadOnlyList<string>>();
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return none;

        var asset = assets.EnumerateArray().FirstOrDefault(a =>
            a.TryGetProperty("name", out var name) && name.GetString() == HighlightsAsset);
        if (asset.ValueKind != JsonValueKind.Object
            || !asset.TryGetProperty("browser_download_url", out var link)
            || link.GetString() is not { } download
            || !download.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            return none;

        try
        {
            var text = await http.GetStringAsync(download);
            return text.Length > 64 * 1024 ? none : ParseHighlights(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return none;
        }
    }

    /// <summary>Language code to its lines; anything that isn't a list of short strings is left out.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseHighlights(string json)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var language in doc.RootElement.EnumerateObject())
        {
            if (language.Value.ValueKind != JsonValueKind.Array) continue;
            var lines = language.Value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!.Trim())
                .Where(v => v.Length is > 0 and <= 200)
                .ToList();
            if (lines.Count > 0) result[language.Name] = lines;
        }
        return result;
    }
}
