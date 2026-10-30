using System.Text.Json;
using System.Text.Json.Serialization;

namespace LmuCareer.Core.Careers;

/// <summary>
/// What makes a career New Game+: the head starts it began with, the career it carries on from,
/// and the rounds flagged below the floor. Null on a normal career.
/// </summary>
public sealed class NewGamePlusState
{
    /// <summary>The head starts picked at the start (ids from <see cref="HeadStarts"/>).</summary>
    public List<string> HeadStarts { get; set; } = [];

    /// <summary>The retired career whose Hall of Fame entry the number, team and old rival come from.</summary>
    public Guid? Legacy { get; set; }

    /// <summary>The old rival who comes back as a sponsor's target in season 1.</summary>
    public string? OldRival { get; set; }

    /// <summary>When too many rounds fell below the floor and the career became a normal one, for good.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>A retired career, kept after its save is gone: what it won and what its achievements unlock.</summary>
public sealed class HallOfFameEntry
{
    public Guid CareerId { get; set; }
    public string Name { get; set; } = "";
    public string DriverName { get; set; } = "";
    public DateTimeOffset RetiredAt { get; set; }
    public int Seasons { get; set; }
    public bool NewGamePlus { get; set; }
    public List<TrophyTitle> Titles { get; set; } = [];

    /// <summary>The wins it's remembered for: the biggest races first.</summary>
    public List<TrophyWin> SignatureWins { get; set; } = [];

    public int Wins { get; set; }
    public List<string> Achievements { get; set; } = [];

    /// <summary>The last car number and team, for the "carry the number" head start.</summary>
    public string CarNumber { get; set; } = "";
    public string TeamName { get; set; } = "";

    /// <summary>The career's latest rival, for the "old rival returns" head start.</summary>
    public string? Rival { get; set; }
}

/// <summary>The Hall of Fame file: every retired career, app-wide, beside the career saves.</summary>
public sealed class HallOfFame
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _file;

    public HallOfFame(string root) => _file = Path.Combine(root, "hall-of-fame.json");

    public IReadOnlyList<HallOfFameEntry> Entries()
    {
        if (!File.Exists(_file)) return [];
        return JsonSerializer.Deserialize<List<HallOfFameEntry>>(File.ReadAllText(_file), Json) ?? [];
    }

    public void Add(HallOfFameEntry entry)
    {
        var entries = Entries().Where(e => e.CareerId != entry.CareerId).Append(entry).ToList();
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, _file, overwrite: true);
    }
}

/// <summary>Head starts: one per achievement a retired career earned, for New Game+ careers only.</summary>
public static class HeadStarts
{
    public const string Reputation5 = "reputation-5";
    public const string Reputation10 = "reputation-10";
    public const string FrontRunner = "front-runner";
    public const string ExtraSponsor = "extra-sponsor";
    public const string OldRival = "old-rival";
    public const string CarryNumber = "carry-number";

    /// <summary>Which achievement unlocks which head start (decided Oct 30).</summary>
    public static readonly IReadOnlyDictionary<string, string> UnlockedBy = new Dictionary<string, string>
    {
        ["on-their-radar"] = Reputation5,
        ["factory-driver"] = Reputation10,
        ["called-up"] = FrontRunner,
        ["sponsors-favourite"] = ExtraSponsor,
        ["nemesis"] = OldRival,
        ["one-make"] = CarryNumber,
    };

    /// <summary>Head starts unlocked across the Hall of Fame, in a fixed order.</summary>
    public static IReadOnlyList<string> Unlocked(IEnumerable<HallOfFameEntry> hall)
    {
        var earned = hall.SelectMany(e => e.Achievements).ToHashSet();
        return UnlockedBy.Where(x => earned.Contains(x.Key)).Select(x => x.Value).Distinct().ToList();
    }

    /// <summary>Starting reputation from head starts: the bigger one wins, always under LMP3's bar.</summary>
    public static decimal StartingReputation(IReadOnlyCollection<string> picked) =>
        Math.Min(Progression.Threshold("LMP3") - 1, picked.Contains(Reputation10) ? 10 : picked.Contains(Reputation5) ? 5 : 0);

    public static bool Has(Career career, string headStart) =>
        career.IsNewGamePlus && career.NewGamePlus!.HeadStarts.Contains(headStart);
}

/// <summary>New Game+'s consequences: reputation a race costs, and losing the seat (design doc, Oct 16 and Oct 28).</summary>
public static class Consequences
{
    public const decimal PenaltyCost = 1, DisqualifiedCost = 3, BarrierDnfCost = 2;

    /// <summary>From LMP3 up, a team lets the player go once reputation falls this far below the class's bar.</summary>
    public const decimal LetGoBelowBar = 10;

    public static decimal Cost(FloorCheck check) =>
        check.Penalties * PenaltyCost + (check.Disqualified ? DisqualifiedCost : 0) + (check.BarrierDnf ? BarrierDnfCost : 0);

    /// <summary>The reputation a class's teams look for; IMSA's classes use their LMU counterpart's.</summary>
    public static decimal Bar(string carClass) => Progression.Threshold(carClass switch
    {
        "GTP" => "Hyper",
        "GTD" => "GT3",
        _ => carClass,
    });

    /// <summary>Whether reputation is low enough for a team in the class to let the player go. GT3 and GTE have no bar to fall under.</summary>
    public static bool BelowBar(decimal reputation, string carClass) =>
        Bar(carClass) > 0 && reputation < Bar(carClass) - LetGoBelowBar;

    /// <summary>
    /// Takes what the counted race cost; from LMP3 up, a reputation that falls far enough below the
    /// class's bar loses the seat mid-season. An IMSA seat goes alone: the season's own stays.
    /// </summary>
    public static void AfterRace(Career career, Round round, FloorCheck check, DateTimeOffset now)
    {
        var before = career.Reputation;
        career.Reputation = Math.Max(0, before - Cost(check));
        check.ReputationCost = before - career.Reputation;

        var season = career.CurrentSeason;
        if (check.ReputationCost == 0 || !BelowBar(career.Reputation, season.CarFor(round).CarClass)) return;
        var contract = season.ContractFor(round);
        if (contract is null)
        {
            contract = new Contract { TeamName = season.TeamFor(round) };
            if (round.SeriesId.Length == 0) season.Contract = contract;
            else if (season.SeriesOf(round) is { } series) series.Contract = contract;
        }
        if (contract.LetGoAt is not null) return;
        contract.LetGoAt = now;
        check.LetGo = true;
        foreach (var rest in season.SeriesRounds(round.SeriesId).Where(r => r.State == RoundState.Upcoming))
            rest.State = RoundState.Skipped;
    }

    /// <summary>Let go: one loyalty stage down with the make or team, not burned (the bottom of the stage below).</summary>
    public static decimal StageDown(decimal points) => Loyalties.Stage(points) switch
    {
        LoyaltyStage.FactoryDriver => Loyalties.DevelopmentAt,
        LoyaltyStage.DevelopmentDriver => Loyalties.RadarAt,
        LoyaltyStage.OnTheirRadar => 0,
        _ => points,
    };

    /// <summary>
    /// The class a let-go player's offers come from: the highest on their road whose bar their
    /// reputation still clears, never above the class they were let go from; the road's first class
    /// when none does.
    /// </summary>
    public static string ClassAfterLetGo(Career career, string carClass)
    {
        var ladder = Progression.LadderFor(career.Path);
        var top = ladder.ToList().IndexOf(carClass);
        if (top < 0) return carClass;
        for (var i = top; i >= 0; i--)
            if (career.Reputation >= Bar(ladder[i])) return ladder[i];
        return ladder[0];
    }
}

public static class Retirement
{
    /// <summary>Counted races a season needs in the starting class before the career can retire.</summary>
    public const int RacesToRetire = 5;

    /// <summary>A career can retire once it has finished a season of 5 or more counted races in the class it started in.</summary>
    public static bool CanRetire(Career career)
    {
        if (career.RetiredAt is not null) return false;
        var seasons = career.PastSeasons.Append(career.CurrentSeason).ToList();
        var startClass = seasons[0].Car.CarClass;
        return seasons.Any(s => s.IsFinished && s.Car.CarClass == startClass
            && s.ChampionshipRounds.Count(r => r.State == RoundState.Completed && r.Test is null) >= RacesToRetire);
    }

    /// <summary>Retires the career: it stops racing, and its Hall of Fame entry is written.</summary>
    public static HallOfFameEntry Retire(Career career, Content.ContentCatalog catalog, DateTimeOffset now)
    {
        if (!CanRetire(career)) throw new PlayerError("A career can retire once it has finished a season of 5 or more races.");
        var trophies = TrophyRoom.For(career, catalog);
        var season = career.CurrentSeason;
        career.RetiredAt = now;
        // A weekend left armed is called off: a retired career counts no more races.
        foreach (var round in season.Rounds.Where(r => r.State == RoundState.Armed))
        {
            round.State = RoundState.Upcoming;
            round.ArmedAt = null;
        }
        return new HallOfFameEntry
        {
            CareerId = career.Id,
            Name = career.Name,
            DriverName = career.DriverName,
            RetiredAt = now,
            Seasons = career.PastSeasons.Count + (season.Rounds.Any(r => r.State == RoundState.Completed) ? 1 : 0),
            NewGamePlus = career.IsNewGamePlus,
            Titles = trophies.Titles.ToList(),
            SignatureWins = trophies.Wins.OrderByDescending(w => Signature(w.TrackFolder)).ThenByDescending(w => w.Season).Take(3).ToList(),
            Wins = trophies.Wins.Count,
            Achievements = career.Achievements.Select(a => a.Id).ToList(),
            CarNumber = season.Car.CarNumber,
            TeamName = season.Contract?.TeamName is { Length: > 0 } team ? team : season.Car.TeamName,
            Rival = career.Rivals.OrderByDescending(r => r.Since).FirstOrDefault()?.Name,
        };
    }

    // The 24-hour races first, then the 12 hours, then the rest.
    private static int Signature(string trackFolder) =>
        trackFolder.StartsWith("LeMans", StringComparison.OrdinalIgnoreCase) ? 3
        : trackFolder.StartsWith("Daytona", StringComparison.OrdinalIgnoreCase) || trackFolder.StartsWith("Spa", StringComparison.OrdinalIgnoreCase) ? 2
        : trackFolder.StartsWith("Sebring", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
}
