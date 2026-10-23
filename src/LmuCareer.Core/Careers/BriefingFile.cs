using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// The open career's next race, written as briefing.json next to the saves for companion apps
/// (an overlay that checks LMU's settings against it, say) to read. It's a file, not a server:
/// the app stays off the network. Values are LMU's own (layout files, car types, multipliers),
/// not the words on screen, so a companion can compare them with what the game reports.
/// </summary>
/// <remarks>
/// <see cref="Version"/> goes up only when something a companion relies on changes meaning or
/// goes away; new fields can appear at any time, so readers should ignore ones they don't know.
/// </remarks>
/// <param name="Open">True while the career is open in Factory Seat; false once the player leaves it or closes the app.</param>
public sealed record BriefingFile(
    string Format,
    int Version,
    string? App,
    DateTimeOffset WrittenAt,
    bool Open,
    BriefingCareer Career,
    // Null between seasons, when there's no race to set up.
    BriefingRound? Round,
    IReadOnlyList<BriefingSponsor> Sponsors)
{
    public const string FormatName = "factory-seat-briefing";
    public const int CurrentVersion = 1;
    public const string FileName = "briefing.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static BriefingFile For(Career career, ContentCatalog catalog, string? app, DateTimeOffset now)
    {
        var season = career.CurrentSeason;
        return new BriefingFile(FormatName, CurrentVersion, app, now, true,
            new BriefingCareer(career.Id, career.Name, season.Number, season.Car.CarClass, Math.Round(career.Reputation, 1),
                season.Contract?.TeamName is { Length: > 0 } team ? team : season.Car.TeamName,
                season.Contract?.TargetPosition ?? Progression.DefaultTarget),
            season.NextRound is { } round ? RoundOf(round, career, catalog) : null,
            season.Sponsors.Select(deal =>
            {
                var progress = Sponsorship.Progress(season, deal);
                return new BriefingSponsor(deal.Name, deal.Objective, Sponsorship.Describe(deal).ToString(),
                    progress.Status, progress.Done, progress.Needed, deal.Reward)
                {
                    Local = deal.Local,
                    SignedAfterRound = deal.SignedAfterRound,
                };
            }).ToList());
    }

    private static BriefingRound RoundOf(Round round, Career career, ContentCatalog catalog)
    {
        var season = career.CurrentSeason;
        var prep = Preparation.For(career, round);
        var track = catalog.Track(round.TrackFolder);
        var car = season.CarFor(round);
        var numbers = round.Guest ? round.GuestNumbers : (season.SeriesOf(round)?.Contract ?? season.Contract)?.Numbers ?? [];
        var stintMinutes = (int)Math.Round(round.RaceMinutes / (double)Math.Max(1, round.Stints));

        return new BriefingRound(
            round.Number,
            season.Rounds.Count,
            round.State,
            round.ArmedAt,
            round.Guest,
            new BriefingEvent(round.EventId.Length > 0 ? round.EventId : null, round.EventName, round.RealDurationHours, round.PointsWeight),
            new BriefingTrack(round.TrackFolder, track?.Name ?? round.TrackCourse, round.LayoutFile,
                track?.Layouts.GetValueOrDefault(round.LayoutFile) ?? round.LayoutFile, round.TrackCourse),
            new BriefingCar(car.CarClass,
                car.Folder.Length > 0 ? car.Folder : car.CarType.Length > 0 ? catalog.CarByType(car.CarType)?.Folder : null,
                new[] { car.CarType }.Concat(car.OtherCarTypes).Where(t => t.Length > 0).Distinct().ToList(),
                catalog.CarByType(car.CarType)?.Name ?? car.CarType,
                car.CarNumber.Length > 0 ? car.CarNumber : null,
                car.CustomTeam,
                car.TeamName.Length > 0 ? car.TeamName : null,
                numbers),
            new BriefingSettings(round.RaceMinutes, (int)Math.Round(round.FuelMultiplier), (int)Math.Round(round.TireMultiplier),
                (int)Math.Round(round.TimeScale), round.StartTime.Length > 0 ? round.StartTime : null),
            new BriefingPitPlan(round.Stints, stintMinutes, Math.Max(0, round.Stints - 1)))
        {
            Stakes = prep.Stakes is { } stakes ? StakesOf(stakes) : null,
            History = prep.History.Select(v => new BriefingVisit(v.Season, v.Round, v.EventName, v.Finish, v.Status, v.BestLap)).ToList(),
            Practice = prep.Practice is { } p
                ? new BriefingPractice(p.Sessions, p.Laps, p.PlayerBest, p.AiBest, p.AiBestDriver.Length > 0 ? p.AiBestDriver : null, p.Gap, p.TargetLap,
                    prep.Pace is { Source: PaceSource.Practice } fromPractice ? fromPractice.Advice : null,
                    prep.Pace is { Source: PaceSource.Practice } practiceStep ? PaceCheck.StepFor(practiceStep.Advice) : null)
                : null,
            Pace = prep.Pace is { } pace ? new BriefingPace(pace.Advice, PaceCheck.StepFor(pace.Advice), pace.Source, pace.Gap, pace.Visits) : null,
            Series = round.SeriesId.Length > 0 ? round.SeriesId : null,
            Test = round.Test is { } test ? new BriefingTest(test.TeamName, test.MinLaps, test.PassShare) : null,
        };
    }

    private static BriefingStakes StakesOf(RaceStakes stakes) => new(
        stakes.Position,
        stakes.Points,
        stakes.Ahead is { } a ? new BriefingRival(a.Name, a.Position, a.Gap, a.Finish, a.Settled) : null,
        stakes.Behind is { } b ? new BriefingRival(b.Name, b.Position, b.Gap, b.Finish, b.Settled) : null,
        stakes.OpenBet is { } bet ? new BriefingBet(bet.EventName, Paddock.IsGood(bet.Moment) ? "podium" : "target", 2) : null,
        stakes.Lines.Select(l => l.ToString()).ToList());

    /// <summary>Writes the file whole, so a companion reading it never sees half of it.</summary>
    public void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Marks the briefing no longer open: the player left the career or closed the app. The file
    /// stays, so a companion can still show the last briefing.
    /// </summary>
    public static void MarkClosed(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json || json["open"]?.GetValue<bool>() == false) return;
            json["open"] = false;
            json["writtenAt"] = DateTimeOffset.Now;
            var temp = path + ".tmp";
            File.WriteAllText(temp, json.ToJsonString(Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not ours to fix: the next briefing rewrites it.
        }
    }

    /// <summary>Removes the file when it describes the given career (it was deleted).</summary>
    public static void RemoveFor(string folder, Guid careerId)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<BriefingFile>(File.ReadAllText(path), Json)?.Career.Id == careerId)
                File.Delete(path);
        }
        catch (JsonException)
        {
            File.Delete(path);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed record BriefingCareer(Guid Id, string Name, int Season, string CarClass, decimal Reputation, string Team, int TargetPosition);

/// <param name="Of">Rounds on the season's calendar.</param>
/// <param name="State">Upcoming, or Armed once the race weekend is started in the app (only races after <see cref="ArmedAt"/> count).</param>
/// <summary>A test day: the team trying the player out, the laps it wants, and how close to the fastest AI in class impresses (0.01 = 1%).</summary>
public sealed record BriefingTest(string Team, int MinLaps, double PassShare);

public sealed record BriefingRound(
    int Number,
    int Of,
    RoundState State,
    DateTimeOffset? ArmedAt,
    bool Guest,
    BriefingEvent Event,
    BriefingTrack Track,
    BriefingCar Car,
    BriefingSettings Settings,
    BriefingPitPlan PitPlan)
{
    /// <summary>Since 1.2: what's riding on the race. Null for a guest drive.</summary>
    public BriefingStakes? Stakes { get; init; }

    /// <summary>Since 1.2: earlier counted races at this layout in this class, newest first; empty on a first visit.</summary>
    public IReadOnlyList<BriefingVisit> History { get; init; } = [];

    /// <summary>Since 1.2: practice run since the race weekend started, read for pace; null until there's been some.</summary>
    public BriefingPractice? Practice { get; init; }

    /// <summary>Since 1.2: AI Strength advice for this race (from practice, earlier races here, or the season's form).</summary>
    public BriefingPace? Pace { get; init; }

    /// <summary>Since 1.3: the championship raced beside the season's own that this round counts for ("imsa"); null for the season's own.</summary>
    public string? Series { get; init; }

    /// <summary>Since 1.3: a test day rather than a race (practice laps in the team's car); null for a race.</summary>
    public BriefingTest? Test { get; init; }
}

/// <param name="Id">The catalog event; null for a custom event.</param>
/// <param name="RealHours">Length of the real-world race the round stands in for.</param>
public sealed record BriefingEvent(string? Id, string Name, double RealHours, decimal PointsWeight);

/// <param name="Folder">The track's folder under LMU's Installed\Locations.</param>
/// <param name="LayoutFile">The layout's file without .mas: what results files and the round match on.</param>
/// <param name="TrackCourse">The layout as LMU names it in results files.</param>
public sealed record BriefingTrack(string Folder, string Name, string LayoutFile, string LayoutName, string TrackCourse);

/// <param name="Folder">The car's folder under LMU's Installed\Vehicles (e.g. BMW_M4_LMGT3_2023), as the .VEH path names it; null if not known.</param>
/// <param name="CarTypes">How LMU names the car in results files (a car can have more than one); the first is the one last raced. Empty until a race shows it.</param>
/// <param name="Number">The number to race, or null when the first race sets it (any livery).</param>
/// <param name="CustomTeam">A Race Control custom team car: any number on it counts.</param>
/// <param name="LiveryNumbers">The team's numbers on LMU's grid, to find its livery; empty when any will do.</param>
public sealed record BriefingCar(string Class, string? Folder, IReadOnlyList<string> CarTypes, string Name, string? Number, bool CustomTeam, string? Team, IReadOnlyList<string> LiveryNumbers);

/// <param name="RaceMinutes">One of LMU's race length steps.</param>
/// <param name="FuelUsage">1 = Real, 2 = x2, 3 = x3.</param>
/// <param name="TyreWear">1 = Real, 2 = x2, 3 = x3.</param>
/// <param name="TimeScale">1 = Normal, else Xn.</param>
/// <param name="StartTime">"HH:mm" of the real race, or null for LMU's default.</param>
public sealed record BriefingSettings(int RaceMinutes, int FuelUsage, int TyreWear, int TimeScale, string? StartTime);

/// <param name="StintMinutes">About how long a tank lasts at the briefing's Fuel Usage.</param>
public sealed record BriefingPitPlan(int Stints, int StintMinutes, int Stops);

/// <param name="Objective">The deal's objective in English, e.g. "3 podiums".</param>
/// <param name="Done">Progress so far, where counting makes sense (podiums so far, championship position…).</param>
public sealed record BriefingSponsor(string Name, ObjectiveKind Kind, string Objective, ObjectiveStatus Status, int Done, int Needed, decimal Reward)
{
    /// <summary>Since 1.2: a small local brand.</summary>
    public bool Local { get; init; }

    /// <summary>Since 1.2: for a deal signed mid-season, the round it was signed after (it counts only later rounds); 0 for one signed before the season.</summary>
    public int SignedAfterRound { get; init; }
}

/// <param name="Position">The player's place in the class drivers' standings; null before any points.</param>
/// <param name="OpenBet">A confident interview answer this race settles.</param>
/// <param name="Lines">The briefing's stakes, in English, most important first: the championship position is always first.</param>
public sealed record BriefingStakes(int? Position, decimal Points, BriefingRival? Ahead, BriefingRival? Behind, BriefingBet? OpenBet, IReadOnlyList<string> Lines);

/// <param name="Gap">Points between the driver and the player, always positive.</param>
/// <param name="Finish">
/// Ahead: with the player winning, the place this driver has to finish or lower for the player to pass them.
/// Behind: the place the player needs, or better, to stay ahead even if this driver wins. Null when no result settles it.
/// </param>
/// <param name="Settled">Behind only: the player stays ahead whatever happens.</param>
public sealed record BriefingRival(string Name, int Position, decimal Gap, int? Finish, bool Settled);

/// <param name="Needs">"podium" after a good moment, "target" (the team's target) after a bad one.</param>
/// <param name="Cost">Reputation lost if this race doesn't deliver.</param>
public sealed record BriefingBet(string Event, string Needs, int Cost);

public sealed record BriefingVisit(int Season, int Round, string Event, int? Finish, Results.FinishStatus Status, double? BestLap);

/// <param name="Gap">Best lap against the fastest AI in class, in seconds; negative when the player was quicker.</param>
/// <param name="TargetLap">The engineer's target for the race when the player was slower than the AI; null otherwise.</param>
/// <param name="AiStrength">The AI Strength advice practice gives: TooEasy, BitEasy, Matched, BitHard or TooHard.</param>
/// <param name="AiStrengthStep">The same advice as points on LMU's AI Strength slider: +3, +1, 0, -1 or -3.</param>
public sealed record BriefingPractice(int Sessions, int Laps, double? BestLap, double? AiBestLap, string? AiBestDriver, double? Gap, double? TargetLap, PaceAdvice? AiStrength, int? AiStrengthStep);

/// <param name="Source">Practice (this weekend), Track (earlier races here, the latest counting most) or Season (recent rounds, kept small on a first visit).</param>
/// <param name="Step">The advice as points on LMU's AI Strength slider: +3, +1, 0, -1 or -3.</param>
/// <param name="Visits">Earlier races here it's based on (Track only).</param>
public sealed record BriefingPace(PaceAdvice Advice, int Step, PaceSource Source, double Gap, int Visits);
