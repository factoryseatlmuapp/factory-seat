using System.Text.Json;

namespace LmuCareer.Core.Content;

public sealed record PackInfo(string Id, string Name);

public sealed record TrackInfo(
    string Folder,
    string Name,
    string Location,
    string Country,
    double LengthKm,
    string Pack,
    // Layout file name (without .mas) to a display name for the layout.
    IReadOnlyDictionary<string, string> Layouts);

public sealed record CarInfo(
    string Folder,
    string Name,
    string Class,
    // Null when it isn't known which pack, if any, the car comes from.
    string? Pack,
    // How LMU writes the car's model in results files. A car can have several (the Toyota is
    // "GR010" on older season grids and "TR010" on 2026's), or none yet if no race has shown it.
    IReadOnlyList<string> CarTypes);

public sealed record EventInfo(
    string Id,
    string Name,
    // world, world-base, european, european-base, guest or library: see CalendarBuilder.
    string Series,
    int Month,
    string StartTime,
    double Hours,
    string Folder,
    string Layout);

public sealed record TeamInfo(
    string Name,
    string Class,
    // Car folders the team runs, best first; empty for a team that runs whichever car in its class the player owns.
    IReadOnlyList<string> Cars,
    // 1 = front-running or factory, 2 = midfield, 3 = back of the grid.
    int Tier,
    // The team's numbers on LMU's grid, so the briefing can point at the right livery; empty when unknown.
    IReadOnlyList<string> Numbers);

public sealed record SponsorInfo(
    string Id,
    string Name,
    // "backer": a real sports car sponsor, open to any car. "mashup": a brand with little racing
    // history, matched to the cars from its home region (a livery idea).
    string Kind,
    // Reputation the sponsor looks for before calling; prestige brands want an established name.
    int MinReputation,
    // Mashups only: the car folders it suits (none: any car). Spec prototype classes (LMP2, LMP3) suit any sponsor.
    IReadOnlyList<string> Cars,
    string Region);

/// <summary>What the app knows about LMU's tracks, cars, DLC packs, teams, sponsors and real-world events.</summary>
public sealed record ContentCatalog(
    IReadOnlyList<PackInfo> Packs,
    IReadOnlyList<TrackInfo> Tracks,
    IReadOnlyList<CarInfo> Cars,
    IReadOnlyList<EventInfo> Events)
{
    public const string BasePack = "base";

    /// <summary>Installed track and car folders that aren't career content (showrooms, one-make cup cars).</summary>
    public IReadOnlyList<string> NotRacing { get; init; } = [];

    public bool IsNotRacing(string folder) => NotRacing.Contains(folder, StringComparer.OrdinalIgnoreCase);

    /// <summary>Teams on LMU's grids, who make the player offers between seasons.</summary>
    public IReadOnlyList<TeamInfo> Teams { get; init; } = [];

    /// <summary>Personal sponsors, who offer deals at the start of each season.</summary>
    public IReadOnlyList<SponsorInfo> Sponsors { get; init; } = [];

    public CarInfo? Car(string folder) =>
        Cars.FirstOrDefault(c => c.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));

    private static readonly Lazy<ContentCatalog> Embedded = new(LoadEmbedded);

    /// <summary>The catalog shipped inside the app.</summary>
    public static ContentCatalog Default => Embedded.Value;

    public TrackInfo? Track(string folder) =>
        Tracks.FirstOrDefault(t => t.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));

    public EventInfo? Event(string id) => Events.FirstOrDefault(e => e.Id == id);

    public CarInfo? CarByType(string carType) =>
        Cars.FirstOrDefault(c => c.CarTypes.Contains(carType, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether a pack's content is available to a player who owns <paramref name="ownedPacks"/>.</summary>
    public static bool IsOwned(string? pack, IReadOnlyCollection<string> ownedPacks) =>
        pack is null or BasePack || ownedPacks.Contains(pack, StringComparer.OrdinalIgnoreCase);

    public static ContentCatalog Parse(string json) =>
        JsonSerializer.Deserialize<ContentCatalog>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty catalog.");

    private static ContentCatalog LoadEmbedded()
    {
        using var stream = typeof(ContentCatalog).Assembly.GetManifestResourceStream("LmuCareer.Core.Content.catalog.json")
            ?? throw new InvalidOperationException("The content catalog isn't embedded in the app.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
