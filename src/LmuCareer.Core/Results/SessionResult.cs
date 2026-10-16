using System.Text.Json.Serialization;

namespace LmuCareer.Core.Results;

public enum SessionKind { Practice, Qualifying, Warmup, Race, Unknown }

public enum FinishStatus
{
    /// <summary>
    /// Still running when the session ended: the session was quit before the flag, or the car was
    /// still on its last lap when the player left after taking the flag.
    /// </summary>
    None,
    Finished,
    Dnf,
    Dq,
    Unknown,
}

public enum Controller { Player, AI }

/// <summary>A run of laps driven by one controller; a mid-race AI handover splits the car's laps into several of these.</summary>
public sealed record ControlSegment(int StartLap, int EndLap, Controller Controller)
{
    public int LapCount => Math.Max(0, EndLap - StartLap + 1);
}

/// <summary>Tread left on each tyre: 1 = new, 0 = worn through.</summary>
public sealed record TyreWear(double FrontLeft, double FrontRight, double RearLeft, double RearRight);

/// <param name="Position">Overall position at the end of the lap.</param>
/// <param name="ElapsedSeconds">Session time when the lap started: the first lap starts at the green flag.</param>
/// <remarks>
/// Everything past the four parameters is read since 1.2, for the debrief; laps saved before it
/// have none of it, and LMU leaves out what a car doesn't have (virtual energy on older cars).
/// </remarks>
public sealed record LapRecord(int Number, int Position, double? LapTimeSeconds, double ElapsedSeconds)
{
    /// <summary>Position in the car's class at the end of the lap, worked out from when each class car crossed the line.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ClassPosition { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Sector1 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Sector2 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Sector3 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? TopSpeedKph { get; init; }

    /// <summary>Share of the tank left at the end of the lap, 0 to 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Fuel { get; init; }

    /// <summary>Share of the tank the lap used.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FuelUsed { get; init; }

    /// <summary>Virtual energy left at the end of the lap, 0 to 1.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Energy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? EnergyUsed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TyreWear? Wear { get; init; }

    /// <summary>Tyre compounds on the lap, e.g. "Medium".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FrontCompound { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RearCompound { get; init; }

    /// <summary>The car was in the pit lane on this lap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Pit { get; init; }

    /// <summary>When the car crossed the line to end the lap; null when LMU gave the lap no time and it was the car's last.</summary>
    [JsonIgnore]
    public double? EndSeconds => LapTimeSeconds is { } time ? ElapsedSeconds + time : null;
}

public sealed record EntryResult(
    string Name,
    string TeamName,
    string CarNumber,
    string VehicleName,
    string CarType,
    string CarClass,
    bool IsPlayer,
    int GridPosition,
    int ClassGridPosition,
    int Position,
    int ClassPosition,
    int Laps,
    double? BestLapSeconds,
    double? FinishTimeSeconds,
    int Pitstops,
    FinishStatus Status,
    IReadOnlyList<ControlSegment> Control,
    IReadOnlyList<LapRecord> LapRecords)
{
    public int LapsUnder(Controller controller) =>
        Control.Where(s => s.Controller == controller).Sum(s => s.LapCount);

    /// <summary>
    /// A Race Control custom team car: LMU calls it "&lt;car&gt; Custom Team &lt;year&gt; #397" whatever
    /// number, team name and paint the player gave it.
    /// </summary>
    [JsonIgnore]
    public bool IsCustomTeam => VehicleName.Contains("Custom Team", StringComparison.OrdinalIgnoreCase);
}

public enum RaceEventKind { Incident, Penalty, TrackLimits, Damage }

/// <summary>A logged event from the session stream. <see cref="Driver"/> is null when LMU didn't attribute it.</summary>
/// <remarks>The details past the four parameters are read since 1.2; events saved before it have none.</remarks>
public sealed record RaceEvent(RaceEventKind Kind, double ElapsedSeconds, string? Driver, string Text)
{
    /// <summary>For a contact with another car: its driver.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OtherDriver { get; init; }

    /// <summary>For a contact with something else: what LMU calls it ("Immovable", "Post", "Sign").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Object { get; init; }

    /// <summary>For a contact: how hard, as LMU reports it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Impact { get; init; }

    /// <summary>For track limits: the lap LMU logged it on.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Lap { get; init; }

    /// <summary>For track limits: LMU's code for the decision; <see cref="Text"/> names it ("Warning", "Invalid Lap Cut Track").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Resolution { get; init; }

    /// <summary>For track limits: the points this event added, and the driver's total after it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WarningPoints { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? CurrentPoints { get; init; }

    /// <summary>Whether <paramref name="nameKey"/> (see <see cref="Scoring.Standings.NameKey"/>) is the driver, or the other car in a contact.</summary>
    public bool Involves(string nameKey) =>
        (Driver is { } driver && Scoring.Standings.NameKey(driver) == nameKey)
        || (OtherDriver is { } other && Scoring.Standings.NameKey(other) == nameKey);
}

public sealed record SessionResult(
    string SourceFile,
    string Setting,
    SessionKind Kind,
    DateTimeOffset StartTime,
    string TrackVenue,
    string TrackCourse,
    double TrackLengthMeters,
    string GameVersion,
    int RaceMinutes,
    double FuelMultiplier,
    double TireMultiplier,
    int MostLapsCompleted,
    IReadOnlyList<EntryResult> Entries,
    IReadOnlyList<RaceEvent> Events)
{
    /// <summary>The track's folder under Installed\Locations, from the TrackData path (e.g. Spa_2023).</summary>
    public string TrackFolder { get; init; } = "";

    /// <summary>The layout's file name without .mas, from the TrackData path (e.g. layoutSpaELMS).</summary>
    public string LayoutFile { get; init; } = "";

    /// <summary>Offline race weekend, as opposed to a multiplayer server.</summary>
    public bool IsSinglePlayer => Setting.Equals("Race Weekend", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// False when the session was quit before the checkered flag: LMU then writes every
    /// car that was still running with a status of "None". A player who took the flag finished
    /// the race even if they left before the cars still on their last lap got there.
    /// </summary>
    public bool IsComplete => Entries.Count > 0
        && (Player?.Status == FinishStatus.Finished || Entries.All(e => e.Status != FinishStatus.None));

    public EntryResult? Player => Entries.FirstOrDefault(e => e.IsPlayer);

    public IEnumerable<string> Classes => Entries.Select(e => e.CarClass).Distinct();
}
