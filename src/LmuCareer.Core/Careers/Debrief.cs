using System.Xml;
using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>The AI's best in a class, which the player's laps are measured against.</summary>
/// <param name="LapSeconds">The best lap as LMU scored it; null when no AI car set a time.</param>
/// <param name="Sector1">The best of each sector on any timed lap, which can come from different cars.</param>
public sealed record ClassBest(
    string CarClass,
    double? LapSeconds,
    string LapDriver,
    double? Sector1,
    double? Sector2,
    double? Sector3,
    double? TopSpeedKph);

/// <summary>
/// What the engineer debrief and the lap analysis are built from, beside the player's laps:
/// read once when the race counts, so it survives the results file being deleted.
/// </summary>
public sealed class RaceDebrief
{
    public IReadOnlyList<ClassBest> ClassBests { get; set; } = [];

    /// <summary>The player's penalties, track limits, contacts (either car) and damage, in session order.</summary>
    public IReadOnlyList<RaceEvent> PlayerEvents { get; set; } = [];

    /// <summary>Read from the results file after the round counted: a race counted before 1.2.</summary>
    public bool Backfilled { get; set; }
}

public static class Debriefs
{
    public static RaceDebrief From(SessionResult race)
    {
        var playerKey = race.Player is { } player ? Standings.NameKey(player.Name) : null;
        return new RaceDebrief
        {
            ClassBests = race.Entries
                .Where(e => !e.IsPlayer)
                .GroupBy(e => e.CarClass)
                .Select(ClassBestOf)
                .ToList(),
            PlayerEvents = playerKey is null ? [] : race.Events.Where(e => e.Involves(playerKey)).ToList(),
        };
    }

    private static ClassBest ClassBestOf(IGrouping<string, EntryResult> cars)
    {
        var fastest = cars.Where(c => c.BestLapSeconds is > 0).MinBy(c => c.BestLapSeconds);
        var timed = cars.SelectMany(c => c.LapRecords).Where(l => l.LapTimeSeconds is > 0).ToList();
        double? Best(Func<LapRecord, double?> pick) => timed.Select(pick).Where(v => v is > 0).Min();

        return new ClassBest(
            cars.Key,
            fastest?.BestLapSeconds,
            fastest?.Name ?? "",
            Best(l => l.Sector1),
            Best(l => l.Sector2),
            Best(l => l.Sector3),
            timed.Select(l => l.TopSpeedKph).Where(v => v is > 0).Max());
    }

    /// <summary>
    /// Rounds counted before 1.2 kept only the player's lap times: where the race's results file is
    /// still in LMU's folder, its debrief data is read from it now. A file that's gone, or that
    /// doesn't match the stored result, leaves the round as it was.
    /// </summary>
    /// <returns>True when a round gained its debrief (the career needs saving).</returns>
    public static bool Backfill(Career career, string? resultsFolder)
    {
        if (string.IsNullOrEmpty(resultsFolder) || !Directory.Exists(resultsFolder)) return false;

        var changed = false;
        foreach (var round in career.PastSeasons.Append(career.CurrentSeason).SelectMany(s => s.Rounds))
        {
            if (round.Result is not { Debrief: null } result || result.RaceFile.Length == 0) continue;
            var path = Path.Combine(resultsFolder, result.RaceFile);
            if (!File.Exists(path)) continue;

            SessionResult race;
            try
            {
                race = ResultsParser.Load(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or XmlException or UnauthorizedAccessException)
            {
                continue;
            }

            var entries = result.Entries.ToList();
            var index = entries.FindIndex(e => e.Entry.IsPlayer);
            if (index < 0 || race.Player is not { } player) continue;

            // File names are timestamps, so this is the race; still, nothing goes into the save unless it agrees.
            var stored = entries[index].Entry;
            if (player.Name != stored.Name || player.CarNumber != stored.CarNumber || player.Laps != stored.Laps) continue;

            entries[index] = entries[index] with { Entry = stored with { LapRecords = player.LapRecords } };
            result.Entries = entries;
            var debrief = From(race);
            debrief.Backfilled = true;
            result.Debrief = debrief;
            changed = true;
        }
        return changed;
    }
}
