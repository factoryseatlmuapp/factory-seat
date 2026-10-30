using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LmuCareer.Core.Content;

/// <summary>What LMU's own files say the AI was set to, and where that came from.</summary>
public sealed record AiSettingsSeen(int? Strength, int? Aggression, string Source);

/// <summary>
/// Reads the AI settings New Game+ checks from LMU's files. Read only, always: a weekend save is
/// signed, and Factory Seat never writes to LMU.
/// </summary>
public static partial class AiProof
{
    /// <summary>Settings.JSON: AI Strength (90 = 90%) and aggression (Low 0, Normal 1, High 2), as of LMU's last exit.</summary>
    public static AiSettingsSeen? Settings(string settingsFile)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsFile));
            return new AiSettingsSeen(FindInt(doc.RootElement, "AI Driver Strength"), FindInt(doc.RootElement, "AI Aggression Mode"), "settings");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A weekend save (LMU's backup when a weekend is left part-way, or a manual save): the AI
    /// Strength the weekend was set up with. The file is <c>{"compressedData": base64 zlib, ...}</c>
    /// then a signature; the player's entry holds <c>selectedAIStrength</c>, the AI's hold -1.
    /// </summary>
    public static AiSettingsSeen? Save(string saveFile)
    {
        try
        {
            var raw = File.ReadAllText(saveFile);
            var end = raw.LastIndexOf('}');
            if (end < 0) return null;
            using var doc = JsonDocument.Parse(raw[..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("compressedData", out var data) || data.GetString() is not { } base64) return null;

            using var zlib = new ZLibStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
            using var reader = new StreamReader(zlib);
            var text = reader.ReadToEnd();
            var strength = StrengthPattern().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Where(v => v >= 0).ToList();
            return strength.Count == 0 ? null : new AiSettingsSeen(strength.Max(), null, "save");
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The newest weekend save in the folders written since <paramref name="since"/>, read; null when there's none.</summary>
    public static AiSettingsSeen? NewestSave(IEnumerable<string> folders, DateTimeOffset since) =>
        folders.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.json"))
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc >= since.UtcDateTime)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => Save(f.FullName))
            .FirstOrDefault(s => s is not null);

    private static int? FindInt(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == name && property.Value.ValueKind == JsonValueKind.Number) return (int)Math.Round(property.Value.GetDouble());
                    if (FindInt(property.Value, name) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindInt(item, name) is { } found) return found;
                break;
        }
        return null;
    }

    [GeneratedRegex("\"selectedAIStrength\"\\s*:\\s*(-?\\d+)")]
    private static partial Regex StrengthPattern();
}
