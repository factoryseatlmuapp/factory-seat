using System.Globalization;
using System.Text.RegularExpressions;

namespace LmuCareer.Core;

/// <summary>
/// Text for the player: English with {name} placeholders, and the values that fill them. The page
/// translates it; the English text is the key every language file translates. Text kept in a
/// career (a season review, what caught a team's eye) keeps its phrase, so it shows in whatever
/// language the app is in when it's read, not the one it was in when it was written.
/// </summary>
/// <param name="Text">The English text, or for a count its plural form ("{n} podiums").</param>
public sealed partial record Phrase(string Text)
{
    private static readonly IReadOnlyDictionary<string, string> NoArgs = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, Phrase> NoParts = new Dictionary<string, Phrase>();

    /// <summary>Values for the placeholders, already written out (numbers in invariant form).</summary>
    public IReadOnlyDictionary<string, string> Args { get; init; } = NoArgs;

    /// <summary>For a count: the singular English form ("{n} podium"). The count is the "n" arg.</summary>
    public string? One { get; init; }

    /// <summary>Placeholders filled by phrases of their own, translated before they go in.</summary>
    public IReadOnlyDictionary<string, Phrase> Parts { get; init; } = NoParts;

    public static Phrase Of(string text, params (string Name, object? Value)[] args) =>
        new(text) { Args = args.ToDictionary(a => a.Name, a => Write(a.Value)) };

    /// <summary>A count: "{n} podium" for one, "{n} podiums" otherwise (each language has its own rules).</summary>
    public static Phrase Count(int n, string one, string other, params (string Name, object? Value)[] args) =>
        Of(other, [("n", n), .. args]) with { One = one };

    /// <summary>The same phrase with a placeholder filled by another phrase.</summary>
    public Phrase With(string name, Phrase part) =>
        this with { Parts = new Dictionary<string, Phrase>(Parts) { [name] = part } };

    /// <summary>The phrase in English.</summary>
    public override string ToString()
    {
        var text = One is not null && Args.TryGetValue("n", out var n) && n == "1" ? One : Text;
        return Placeholder().Replace(text, m =>
            Parts.TryGetValue(m.Groups[1].Value, out var part) ? part.ToString()
            : Args.TryGetValue(m.Groups[1].Value, out var value) ? value
            : m.Value);
    }

    private static string Write(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}

/// <summary>A problem the player can run into and fix, with its message as a <see cref="Phrase"/> so it can be translated.</summary>
public sealed class PlayerError(Phrase message) : InvalidOperationException(message.ToString())
{
    public Phrase Phrase { get; } = message;

    public PlayerError(string text, params (string Name, object? Value)[] args) : this(Phrase.Of(text, args)) { }
}
