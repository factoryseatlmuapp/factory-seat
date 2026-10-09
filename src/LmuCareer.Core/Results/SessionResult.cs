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

public sealed record LapRecord(int Number, int Position, double? LapTimeSeconds, double ElapsedSeconds);

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

public enum RaceEventKind { Incident, Penalty, TrackLimits }

/// <summary>A logged event from the session stream. <see cref="Driver"/> is null when LMU didn't attribute it.</summary>
public sealed record RaceEvent(RaceEventKind Kind, double ElapsedSeconds, string? Driver, string Text);

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
