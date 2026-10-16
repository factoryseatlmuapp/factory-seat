using System.Globalization;
using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

public enum RanLow { No, Fuel, Energy }

/// <param name="Average">Mean of the stint's clean laps; null when it had none.</param>
/// <param name="DropOff">How much slower its last clean laps were than its first, in seconds a lap; null on a short stint.</param>
/// <param name="RanLow">The stint ended with the tank or the virtual energy nearly empty: a slow end that isn't the tyres.</param>
public sealed record StintSummary(int Number, int FirstLap, int LastLap, int CleanLaps, double? Average, double? Best, double? DropOff, string? Compound,
    RanLow RanLow = RanLow.No);

/// <param name="Gap">Seconds: negative when the player was quicker.</param>
public sealed record SectorCompare(int Sector, double? Mine, double? AiBest, double? Gap);

/// <summary>A car-to-car contact: the other driver, the lap, and how hard LMU says it was.</summary>
public sealed record ContactSummary(int? Lap, string OtherDriver, double? Impact)
{
    /// <summary>
    /// LMU logs every touch. Below this it's a brush in traffic (half of all reports at Daytona were
    /// under 240); from it, a contact worth a mention.
    /// </summary>
    public const double Counts = 500;

    /// <summary>A hit hard enough to talk about on its own.</summary>
    public const double Hard = 2000;
}

/// <summary>Damage LMU reported, e.g. "suspension".</summary>
public sealed record DamageSummary(int? Lap, string Part);

/// <summary>
/// The engineer's view of one counted race, worked out from what the round stores. Rounds counted
/// before 1.2 whose results file had gone have only lap times: <see cref="HasDetail"/> is false and
/// the sectors, stints' compounds and stream events are empty.
/// </summary>
public sealed record DebriefReport(
    int RoundNumber,
    string EventName,
    bool HasDetail,
    int GridPosition,
    int? Lap1Position,
    int? FinishPosition,
    FinishStatus Status,
    int Laps,
    int PitStops,
    double? BestLap,
    double? AiBestLap,
    string AiBestDriver,
    IReadOnlyList<SectorCompare> Sectors,
    IReadOnlyList<StintSummary> Stints,
    int TrackLimitStrikes,
    int InvalidatedLaps,
    int Penalties,
    IReadOnlyList<ContactSummary> Contacts,
    IReadOnlyList<DamageSummary> Damage,
    int AiLaps)
{
    /// <summary>Best lap against the fastest AI in class, in seconds: negative when the player was quicker.</summary>
    public double? Gap => BestLap is { } mine && AiBestLap is { } ai ? Math.Round(mine - ai, 3) : null;

    /// <summary>A stint has to have this many clean laps for its drop-off to mean anything.</summary>
    public const int DropOffLaps = 6;

    /// <summary>Fuel or virtual energy below this share at a stint's end means the car was running on fumes.</summary>
    public const double NearlyEmpty = 0.12;

    public static DebriefReport? For(Round round)
    {
        if (round.State != RoundState.Completed || round.Result is not { } result) return null;
        var scored = result.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
        if (scored is null) return null;
        var me = scored.Entry;
        var laps = me.LapRecords;
        var debrief = result.Debrief;
        var playerKey = Standings.NameKey(me.Name);

        var aiBest = debrief?.ClassBests.FirstOrDefault(b => b.CarClass == me.CarClass);
        var aiBestLap = aiBest?.LapSeconds ?? result.Entries
            .Where(e => !e.Entry.IsPlayer && e.Entry.CarClass == me.CarClass && e.Entry.BestLapSeconds > 0)
            .MinBy(e => e.Entry.BestLapSeconds)?.Entry.BestLapSeconds;
        var aiBestDriver = aiBest?.LapDriver ?? result.Entries
            .Where(e => !e.Entry.IsPlayer && e.Entry.CarClass == me.CarClass && e.Entry.BestLapSeconds > 0)
            .MinBy(e => e.Entry.BestLapSeconds)?.Entry.Name ?? "";

        var timed = laps.Where(l => l.LapTimeSeconds > 0).ToList();
        double? MyBest(Func<LapRecord, double?> pick) => timed.Select(pick).Where(v => v > 0).Min();
        var sectors = debrief is null || timed.All(l => l.Sector1 is null) ? Array.Empty<SectorCompare>() : new[]
        {
            Compare(1, MyBest(l => l.Sector1), aiBest?.Sector1),
            Compare(2, MyBest(l => l.Sector2), aiBest?.Sector2),
            Compare(3, MyBest(l => l.Sector3), aiBest?.Sector3),
        };

        var events = debrief?.PlayerEvents ?? [];
        var limits = events.Where(e => e.Kind == RaceEventKind.TrackLimits && Standings.NameKey(e.Driver ?? "") == playerKey).ToList();

        return new DebriefReport(
            round.Number,
            round.EventName,
            HasDetail: debrief is not null,
            GridPosition: me.ClassGridPosition,
            Lap1Position: laps.FirstOrDefault(l => l.Number == 1)?.ClassPosition,
            FinishPosition: scored.ClassRank,
            Status: me.Status,
            Laps: me.Laps,
            PitStops: me.Pitstops,
            BestLap: me.BestLapSeconds > 0 ? me.BestLapSeconds : null,
            AiBestLap: aiBestLap,
            AiBestDriver: aiBestDriver,
            Sectors: sectors,
            Stints: SplitStints(me),
            TrackLimitStrikes: limits.Count(e => e.WarningPoints > 0),
            InvalidatedLaps: limits.Count(e => e.Text.StartsWith("Invalid Lap", StringComparison.OrdinalIgnoreCase)),
            Penalties: result.PlayerPenalties,
            Contacts: CarContacts(events, playerKey, laps),
            Damage: events.Where(e => e.Kind == RaceEventKind.Damage && Standings.NameKey(e.Driver ?? "") == playerKey)
                .Select(e => new DamageSummary(LapAt(laps, e.ElapsedSeconds), DamagedPart(e.Text)))
                .ToList(),
            AiLaps: me.LapsUnder(Controller.AI));
    }

    private static SectorCompare Compare(int sector, double? mine, double? ai) =>
        new(sector, mine, ai, mine is { } m && ai is { } a ? Math.Round(m - a, 3) : null);

    /// <summary>
    /// Laps that show the player's real pace: timed, driven by the player, not the first lap, not
    /// a pit lap or the lap out of the pits, and not far off their usual pace (a yellow, a spin).
    /// </summary>
    public static IReadOnlyList<LapRecord> CleanLaps(EntryResult me)
    {
        var laps = me.LapRecords;
        var candidates = laps.Where((lap, i) =>
            lap.LapTimeSeconds > 0
            && lap.Number > 1
            && !lap.Pit
            && !(i > 0 && laps[i - 1].Pit)
            && !me.Control.Any(c => c.Controller == Controller.AI && lap.Number >= c.StartLap && lap.Number <= c.EndLap))
            .ToList();
        if (candidates.Count == 0) return [];
        var times = candidates.Select(l => l.LapTimeSeconds!.Value).Order().ToList();
        var median = times[times.Count / 2];
        return candidates.Where(l => l.LapTimeSeconds <= median * 1.07).ToList();
    }

    /// <summary>The race split at each pit lap: a stint runs to the lap the car came in on.</summary>
    private static IReadOnlyList<StintSummary> SplitStints(EntryResult me)
    {
        var laps = me.LapRecords;
        if (laps.Count == 0) return [];
        var clean = CleanLaps(me).Select(l => l.Number).ToHashSet();

        var stints = new List<StintSummary>();
        var start = 0;
        for (var i = 0; i < laps.Count; i++)
        {
            if (!laps[i].Pit && i < laps.Count - 1) continue;
            var stint = laps.Skip(start).Take(i - start + 1).ToList();
            var times = stint.Where(l => clean.Contains(l.Number)).Select(l => l.LapTimeSeconds!.Value).ToList();
            double? drop = times.Count >= DropOffLaps
                ? Math.Round(times.TakeLast(3).Average() - times.Take(3).Average(), 3)
                : null;
            // A pit lap ends the stint refuelled, so the stint's end is the last lap before it.
            var end = stint.Where(l => !l.Pit).TakeLast(3).ToList();
            var ranLow = end.Any(l => l.Energy < NearlyEmpty) ? RanLow.Energy
                : end.Any(l => l.Fuel < NearlyEmpty) ? RanLow.Fuel
                : RanLow.No;
            stints.Add(new StintSummary(
                stints.Count + 1,
                stint[0].Number,
                stint[^1].Number,
                times.Count,
                times.Count > 0 ? Math.Round(times.Average(), 3) : null,
                times.Count > 0 ? times.Min() : null,
                drop,
                stint[0].FrontCompound,
                ranLow));
            start = i + 1;
        }
        return stints;
    }

    /// <summary>Car-to-car contacts involving the player. Both cars can report the same touch, so reports of one pair within 3 s count once.</summary>
    private static IReadOnlyList<ContactSummary> CarContacts(IReadOnlyList<RaceEvent> events, string playerKey, IReadOnlyList<LapRecord> laps)
    {
        var contacts = new List<(double At, string Other, double? Impact)>();
        foreach (var e in events.Where(e => e.Kind == RaceEventKind.Incident && e.OtherDriver is not null))
        {
            var other = Standings.NameKey(e.Driver ?? "") == playerKey ? e.OtherDriver! : e.Driver ?? "";
            if (Standings.NameKey(other) == playerKey) continue;
            var same = contacts.FindIndex(c => Standings.NameKey(c.Other) == Standings.NameKey(other) && Math.Abs(c.At - e.ElapsedSeconds) <= 3);
            if (same >= 0)
            {
                if (e.Impact > contacts[same].Impact) contacts[same] = contacts[same] with { Impact = e.Impact };
                continue;
            }
            contacts.Add((e.ElapsedSeconds, other, e.Impact));
        }
        return contacts.Select(c => new ContactSummary(LapAt(laps, c.At), c.Other, c.Impact)).ToList();
    }

    /// <summary>The lap the player was on at a moment in the session; null before the start.</summary>
    public static int? LapAt(IReadOnlyList<LapRecord> laps, double elapsed)
    {
        LapRecord? on = null;
        foreach (var lap in laps)
        {
            if (lap.ElapsedSeconds > elapsed) break;
            on = lap;
        }
        return on?.Number;
    }

    // "Bill Strube(0) reports new suspension damage" → "suspension"
    private static string DamagedPart(string text)
    {
        var at = text.IndexOf("reports new ", StringComparison.Ordinal);
        var end = text.LastIndexOf(" damage", StringComparison.Ordinal);
        return at >= 0 && end > at ? text[(at + "reports new ".Length)..end].Trim() : "";
    }

    /// <summary>A lap time as LMU shows it: 2:04.951, or 58.204 under a minute.</summary>
    public static string LapTime(double seconds)
    {
        var minutes = (int)(seconds / 60);
        var rest = seconds - minutes * 60;
        return minutes > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}:{rest:00.000}")
            : rest.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
