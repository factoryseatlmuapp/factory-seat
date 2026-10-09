using System.Text.Json.Serialization;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

public enum DriverRating { Bronze, Silver, Gold, Platinum }

public enum RoundState { Upcoming, Armed, Completed, Skipped }

/// <summary>The car the player races this season, as LMU writes it in the results files.</summary>
public sealed record CareerCar(string CarType, string CarClass, string CarNumber, string TeamName)
{
    /// <summary>Other names LMU uses for the same car on different season grids (Toyota GR010 and TR010).</summary>
    public IReadOnlyList<string> OtherCarTypes { get; init; } = [];

    /// <summary>The player races their Race Control custom team car: any number on it counts.</summary>
    public bool CustomTeam { get; init; }

    public bool IsCarType(string carType) =>
        carType.Equals(CarType, StringComparison.OrdinalIgnoreCase)
        || OtherCarTypes.Contains(carType, StringComparer.OrdinalIgnoreCase);
}

public sealed class Round
{
    public int Number { get; set; }

    /// <summary>The catalog event this round stands in for; empty for a custom event.</summary>
    public string EventId { get; set; } = "";

    public string EventName { get; set; } = "";

    /// <summary>The track's folder under Installed\Locations, e.g. Spa_2023.</summary>
    public string TrackFolder { get; set; } = "";

    /// <summary>The layout's file name without .mas, e.g. layoutSpaELMS. Sessions are matched on this.</summary>
    public string LayoutFile { get; set; } = "";

    /// <summary>
    /// The layout name as LMU writes it as TrackCourse in results files. Only used to match rounds
    /// with no layout file; several layouts can share one name.
    /// </summary>
    public string TrackCourse { get; set; } = "";

    /// <summary>Time of day the real race starts, "HH:mm", for the briefing.</summary>
    public string StartTime { get; set; } = "";

    /// <summary>Length of the real-world race this round stands in for.</summary>
    public double RealDurationHours { get; set; }

    public int RaceMinutes { get; set; }
    public int Stints { get; set; } = 2;
    public double TimeScale { get; set; } = 1;
    public double FuelMultiplier { get; set; } = 1;
    public double TireMultiplier { get; set; } = 1;
    public decimal PointsWeight { get; set; } = 1;

    /// <summary>Share of the laps the player must drive to score driver points; 0 = no minimum.</summary>
    public double MinimumDriveShare { get; set; }

    public RoundState State { get; set; } = RoundState.Upcoming;
    public DateTimeOffset? ArmedAt { get; set; }
    public RoundResult? Result { get; set; }

    /// <summary>A one-off guest drive for another team: run like any round, but outside the championship.</summary>
    public bool Guest { get; set; }

    /// <summary>For a guest drive: the car raced, which isn't the season's.</summary>
    public CareerCar? GuestCar { get; set; }

    /// <summary>For a guest drive: the team's numbers on LMU's grid, to find its livery.</summary>
    public IReadOnlyList<string> GuestNumbers { get; set; } = [];
}

public sealed class RoundResult
{
    public string RaceFile { get; set; } = "";
    public string? QualifyingFile { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }

    /// <summary>The race was quit before the flag and the player chose to take the DNF.</summary>
    public bool QuitEarly { get; set; }

    /// <summary>Accepted even though a setting didn't match the briefing.</summary>
    public IReadOnlyList<string> AcceptedDespite { get; set; } = [];

    /// <summary>Penalties race control gave the player (drive-throughs, stop-and-gos), not counting a disqualification.</summary>
    public int PlayerPenalties { get; set; }

    /// <summary>Every car's scored result. Lap-by-lap records are kept for the player's car only.</summary>
    public IReadOnlyList<ScoredEntry> Entries { get; set; } = [];
}

/// <summary>The deal the player races a season under: who for, and what the team expects.</summary>
public sealed class Contract
{
    public string TeamName { get; set; } = "";

    /// <summary>1 = front-running or factory team, 2 = midfield, 3 = back of the grid.</summary>
    public int Tier { get; set; } = 2;

    /// <summary>The team's target: finish the season this high or better in the class drivers' standings.</summary>
    public int TargetPosition { get; set; } = Progression.DefaultTarget;

    /// <summary>The team's numbers on LMU's grid, to find its livery; empty when any livery will do.</summary>
    public IReadOnlyList<string> Numbers { get; set; } = [];

    /// <summary>Seasons the contract runs, this one included: 2 on a two-season deal's first season.</summary>
    public int SeasonsLeft { get; set; } = 1;
}

/// <summary>A team that noticed a big result mid-season: it makes an offer when the season ends.</summary>
public sealed class TeamInterest
{
    public string TeamName { get; set; } = "";
    public string CarClass { get; set; } = "";
    public string CarFolder { get; set; } = "";
    public int Tier { get; set; }
    public IReadOnlyList<string> Numbers { get; set; } = [];

    /// <summary>What caught their eye, e.g. "your win at the Le Mans 24 Hours".</summary>
    public string Reason { get; set; } = "";

    /// <summary><see cref="Reason"/> to translate; null on saves from before translations.</summary>
    public Phrase? ReasonText { get; set; }
}

/// <param name="Label">The item in English, as saves from before translations have it.</param>
public sealed record ReputationItem(string Label, decimal Points)
{
    /// <summary><see cref="Label"/> to translate; null on saves from before translations.</summary>
    public Phrase? Text { get; init; }

    // A factory, not a second constructor: saves are read through the one constructor.
    public static ReputationItem For(Phrase text, decimal points) => new(text.ToString(), points) { Text = text };

    // The phrase is the label in translatable form: items with the same label and points are the same.
    public bool Equals(ReputationItem? other) => other is not null && Label == other.Label && Points == other.Points;

    public override int GetHashCode() => HashCode.Combine(Label, Points);
}

/// <summary>How a finished season went, and what it did to the player's reputation.</summary>
public sealed class SeasonReview
{
    public int? ChampionshipPosition { get; set; }
    public int TargetPosition { get; set; }
    public bool TargetMet { get; set; }
    public decimal ReputationBefore { get; set; }
    public decimal ReputationAfter { get; set; }
    public IReadOnlyList<ReputationItem> Items { get; set; } = [];

    /// <summary>A short season counts for less: this share of the season-long items applied.</summary>
    public double SeasonWeight { get; set; } = 1;
}

public enum OfferKind
{
    /// <summary>The player's current team, for another season.</summary>
    ReSign,

    /// <summary>Another team in the class the player raced.</summary>
    Move,

    /// <summary>A seat one step up the ladder.</summary>
    Promotion,
}

/// <summary>A one-off seat at a classic endurance race, outside the championship.</summary>
public sealed class GuestOffer
{
    public string Id { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string CarClass { get; set; } = "";
    public string CarFolder { get; set; } = "";
    public int Tier { get; set; }
    public IReadOnlyList<string> Numbers { get; set; } = [];

    /// <summary>A seat a class above the player's: a team trying them out.</summary>
    public bool Audition { get; set; }

    public bool Accepted { get; set; }
}

/// <summary>A seat for next season, offered at the end of the last.</summary>
public sealed class TeamOffer
{
    public string Id { get; set; } = "";
    public OfferKind Kind { get; set; }
    public string TeamName { get; set; } = "";
    public string CarClass { get; set; } = "";
    public string CarFolder { get; set; } = "";
    public int Tier { get; set; }
    public int TargetPosition { get; set; }
    public IReadOnlyList<string> Numbers { get; set; } = [];

    /// <summary>Only for a re-sign: the number the player raced last season, kept.</summary>
    public string CarNumber { get; set; } = "";

    /// <summary>How many seasons the deal runs: 1, or 2 for a longer commitment.</summary>
    public int Seasons { get; set; } = 1;

    /// <summary>For a re-sign: the player is already contracted for next season, on these terms.</summary>
    public bool UnderContract { get; set; }

    /// <summary>For a team that noticed the player mid-season: what caught its eye.</summary>
    public string Reason { get; set; } = "";

    /// <summary><see cref="Reason"/> to translate; null on saves from before translations.</summary>
    public Phrase? ReasonText { get; set; }
}

public enum ObjectiveKind
{
    /// <summary>Be classified in every round of the season.</summary>
    FinishEveryRound,

    /// <summary>At least <see cref="SponsorDeal.Count"/> class podiums.</summary>
    Podiums,

    /// <summary>At least <see cref="SponsorDeal.Count"/> class wins.</summary>
    Wins,

    /// <summary>At least <see cref="SponsorDeal.Count"/> class poles.</summary>
    Poles,

    /// <summary>A class podium at one named round.</summary>
    PodiumAt,

    /// <summary>Finish the season in the top <see cref="SponsorDeal.Count"/> of the class standings.</summary>
    ChampionshipTop,

    /// <summary>No disqualification and at most one penalty all season.</summary>
    CleanSeason,

    /// <summary>Finish the season ahead of <see cref="SponsorDeal.Rival"/> in the class standings.</summary>
    BeatRival,
}

/// <summary>A personal sponsor's deal for one season: one objective, paid in reputation when met.</summary>
public sealed class SponsorDeal
{
    public string SponsorId { get; set; } = "";
    public string Name { get; set; } = "";
    public ObjectiveKind Objective { get; set; }
    public int Count { get; set; }

    /// <summary>For <see cref="ObjectiveKind.PodiumAt"/>: the round and its event name.</summary>
    public int RoundNumber { get; set; }
    public string EventName { get; set; } = "";

    /// <summary>For <see cref="ObjectiveKind.BeatRival"/>: the AI driver to beat.</summary>
    public string Rival { get; set; } = "";

    public decimal Reward { get; set; }

    /// <summary>The player says they run the sponsor's livery: worth a little extra, met or not.</summary>
    public bool RunningLivery { get; set; }
}

public sealed class Season
{
    public int Number { get; set; }
    public CareerCar Car { get; set; } = new("", "", "", "");
    public string Teammate { get; set; } = "";

    /// <summary>Null on saves from before contracts existed; the default target applies.</summary>
    public Contract? Contract { get; set; }

    /// <summary>Written once, when the season's review is first opened.</summary>
    public SeasonReview? Review { get; set; }

    /// <summary>One-off guest drives on offer before the season's first round counts; null until they're made.</summary>
    public List<GuestOffer>? GuestOffers { get; set; }

    /// <summary>Deals on offer before the season's first round counts; null until they're made.</summary>
    public List<SponsorDeal>? SponsorOffers { get; set; }

    /// <summary>Deals the player signed, at most <see cref="Sponsorship.MaxDeals"/>.</summary>
    public List<SponsorDeal> Sponsors { get; set; } = [];

    public List<Round> Rounds { get; set; } = [];

    /// <summary>The rounds that count for the championship: all but guest drives.</summary>
    [JsonIgnore]
    public IEnumerable<Round> ChampionshipRounds => Rounds.Where(r => !r.Guest);

    [JsonIgnore]
    public Round? ArmedRound => Rounds.FirstOrDefault(r => r.State == RoundState.Armed);

    /// <summary>The round to run next: the armed one, or else the first that hasn't been run.</summary>
    [JsonIgnore]
    public Round? NextRound => ArmedRound ?? Rounds.FirstOrDefault(r => r.State == RoundState.Upcoming);

    [JsonIgnore]
    public bool IsFinished => Rounds.Count > 0 && NextRound is null;

    public IReadOnlyList<ClassStandings> Standings() =>
        Scoring.Standings.Build(ChampionshipRounds
            .Where(r => r.State == RoundState.Completed && r.Result is not null)
            .Select(r => r.Result!.Entries)
            .ToList());
}

public sealed class Career
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }

    /// <summary>The player's driver name in LMU; informational, since matching goes by car.</summary>
    public string DriverName { get; set; } = "";

    public DriverRating Rating { get; set; } = DriverRating.Silver;
    public decimal Reputation { get; set; }

    /// <summary>DLC packs the player has ticked as owned.</summary>
    public List<string> OwnedContent { get; set; } = [];

    public Season CurrentSeason { get; set; } = new();
    public List<Season> PastSeasons { get; set; } = [];

    /// <summary>Seats on offer for next season, once the current one has been reviewed.</summary>
    public List<TeamOffer> Offers { get; set; } = [];

    /// <summary>Teams that noticed a big result this season; their offers come at the season's end.</summary>
    public List<TeamInterest> Interest { get; set; } = [];
}
