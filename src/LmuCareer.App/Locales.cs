using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.App;

/// <summary>
/// The languages the app speaks. Each is one JSON file mapping English text to its translation,
/// e.g. <c>"Next race": "다음 레이스"</c>; text with no translation shows in English. Files ship in
/// the exe (locales\*.json), and players can drop their own into the locales folder next to the
/// saves: a new language, or fixes to a shipped one (theirs win, string by string).
/// </summary>
/// <remarks>
/// A file may group its strings in objects for readability ("Settings": { … }); the groups are
/// ignored. An object whose keys are all plural categories (one, few, many, other…) is a count's
/// translation, for languages with more forms than English. "_meta" holds the language's name.
/// </remarks>
public sealed class Locales
{
    public const string English = "en";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> PluralCategories = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>Steam's names for the languages LMU comes in, by language code.</summary>
    private static readonly Dictionary<string, string> SteamLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "en",
        ["french"] = "fr",
        ["german"] = "de",
        ["italian"] = "it",
        ["spanish"] = "es",
        ["latam"] = "es",
        ["brazilian"] = "pt-BR",
        ["portuguese"] = "pt-BR",
        ["polish"] = "pl",
        ["japanese"] = "ja",
        ["koreana"] = "ko",
        ["schinese"] = "zh-CN",
        ["tchinese"] = "zh-TW",
    };

    private readonly Dictionary<string, Locale> _shipped;
    private readonly Dictionary<string, Locale> _own;

    private Locales(Dictionary<string, Locale> shipped, Dictionary<string, Locale> own, List<string> problems)
    {
        _shipped = shipped;
        _own = own;
        Problems = problems;
    }

    public static string OwnFolder => Path.Combine(CareerStore.DefaultRoot, "locales");

    /// <summary>Files in the locales folder that couldn't be read, to show in Settings.</summary>
    public IReadOnlyList<string> Problems { get; }

    public static Locales Load()
    {
        var problems = new List<string>();
        var shipped = new Dictionary<string, Locale>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(Locales).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("locales/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var locale = Parse(Code(resource), stream);
            shipped[locale.Code] = locale;
        }

        var own = new Dictionary<string, Locale>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(OwnFolder))
        {
            foreach (var file in Directory.EnumerateFiles(OwnFolder, "*.json").Where(f => !Path.GetFileName(f).StartsWith('_')))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var locale = Parse(Code(file), stream);
                    own[locale.Code] = locale;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        return new Locales(shipped, own, problems);
    }

    /// <summary>Every language there's a file for, English first, by code and name.</summary>
    public IReadOnlyList<(string Code, string Name)> Available() =>
        _shipped.Keys.Concat(_own.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => !code.Equals(English, StringComparison.OrdinalIgnoreCase))
            .Select(code => (code, Name: (_own.GetValueOrDefault(code) ?? _shipped[code]).Name))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture)
            .Prepend((English, "English"))
            .ToList();

    /// <summary>
    /// The language to show: the player's choice, or on "auto" the one LMU runs in (Steam's
    /// language for the game), then Windows' display language, then English.
    /// </summary>
    public string Resolve(string? choice, string? lmuRoot)
    {
        if (choice is { Length: > 0 } && choice != "auto" && Match(choice) is { } chosen) return chosen;
        return Match(SteamCode(lmuRoot)) ?? Match(CultureInfo.CurrentUICulture.Name) ?? English;
    }

    /// <summary>The language code for LMU's Steam language, when it's one LMU comes in.</summary>
    public static string? SteamCode(string? lmuRoot) =>
        lmuRoot is not null && LmuInstall.SteamLanguage(lmuRoot) is { } steam ? SteamLanguages.GetValueOrDefault(steam) : null;

    /// <summary>A language there's a file for: the exact code, or the same language ("pt-PT" → "pt-BR").</summary>
    private string? Match(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var codes = Available().Select(l => l.Code).ToList();
        return codes.FirstOrDefault(c => c.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? codes.FirstOrDefault(c => Base(c).Equals(Base(code), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A language's translations: the shipped file's, with the player's own file on top.</summary>
    public JsonObject Strings(string code)
    {
        var strings = new JsonObject();
        foreach (var locale in new[] { _shipped.GetValueOrDefault(code), _own.GetValueOrDefault(code) }.OfType<Locale>())
        {
            foreach (var (text, translation) in locale.Strings)
                strings[text] = translation.DeepClone();
        }
        return strings;
    }

    /// <summary>A plain string in the given language, for text the app shows itself (file dialogs).</summary>
    public string Text(string code, string text) =>
        (_own.GetValueOrDefault(code)?.Strings.GetValueOrDefault(text) ?? _shipped.GetValueOrDefault(code)?.Strings.GetValueOrDefault(text))
            is JsonValue value && value.TryGetValue(out string? translated) && translated.Length > 0
            ? translated
            : text;

    private static string Code(string path) => Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar));

    private static string Base(string code) => code.Split('-')[0];

    private static Locale Parse(string code, Stream stream)
    {
        using var doc = JsonDocument.Parse(stream, Lenient);
        var strings = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var name = code;

        void Read(JsonElement group)
        {
            foreach (var property in group.EnumerateObject())
            {
                var value = property.Value;
                if (property.Name == "_meta")
                {
                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } named)
                        name = named;
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    if (value.GetString() is { Length: > 0 } text) strings[property.Name] = JsonValue.Create(text);
                }
                else if (value.ValueKind == JsonValueKind.Object)
                {
                    var keys = value.EnumerateObject().Select(p => p.Name).ToList();
                    if (keys.Count > 0 && keys.All(PluralCategories.Contains)) strings[property.Name] = JsonNode.Parse(value.GetRawText())!;
                    else Read(value);
                }
            }
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("The file should be one JSON object.");
        Read(doc.RootElement);
        return new Locale(code, name, strings);
    }

    private sealed record Locale(string Code, string Name, Dictionary<string, JsonNode> Strings);
}
