using System.IO;
using System.Text.Json;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.App;

/// <summary>App-wide preferences, kept next to the career saves.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string? LmuRoot { get; set; }

    /// <summary>"system", "dark" or "light".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Click and hover sound volume, 0 to 1.</summary>
    public double SoundVolume { get; set; } = 0.6;

    public bool Muted { get; set; }

    /// <summary>Ask GitHub for a newer release when the app starts. The app's only network call.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Sponsors whose livery the player has built, by sponsor id: those sponsors call more often.</summary>
    public List<string> BuiltLiveries { get; set; } = [];

    /// <summary>Brands the player added themselves, for liveries of their own design.</summary>
    public List<OwnSponsor> CustomSponsors { get; set; } = [];

    public static string FilePath => Path.Combine(CareerStore.DefaultRoot, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

/// <summary>A sponsor the player made up: a brand they built (or mean to build) a livery for.</summary>
public sealed class OwnSponsor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";

    /// <summary>Car folders the livery suits; empty for any car.</summary>
    public List<string> Cars { get; set; } = [];

    public SponsorInfo ToSponsor() => new(Id, Name, "mashup", 0, Cars, Region);
}
