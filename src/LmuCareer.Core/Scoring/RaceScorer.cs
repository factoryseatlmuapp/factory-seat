using System.Text.Json.Serialization;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Scoring;

public sealed record ScoringRules
{
    /// <summary>WEC-style points for the top ten in each class.</summary>
    public IReadOnlyList<decimal> Points { get; init; } = [25, 18, 15, 12, 10, 8, 6, 4, 2, 1];

    /// <summary>For class pole in qualifying. Not scaled by the event weight.</summary>
    public decimal PoleBonus { get; init; } = 1;

    /// <summary>A finisher must cover this share of the class winner's laps to be classified (WEC uses 70%).</summary>
    public double ClassificationShare { get; init; } = 0.70;

    public static ScoringRules Wec { get; } = new();
}

public sealed record ScoredEntry(
    EntryResult Entry,
    bool Classified,
    int? ClassRank,
    decimal RacePoints,
    bool ClassPole,
    decimal PolePoints,
    double DriveShare,
    bool DriverEligible)
{
    /// <summary>What the car and the team score.</summary>
    [JsonIgnore]
    public decimal TotalPoints => RacePoints + PolePoints;

    /// <summary>What the driver scores: zero when they missed their minimum drive time.</summary>
    [JsonIgnore]
    public decimal DriverPoints => DriverEligible ? TotalPoints : 0;
}

public sealed record ScoredRace(
    string RoundName,
    SessionResult Race,
    decimal Weight,
    IReadOnlyList<ScoredEntry> Entries)
{
    public IEnumerable<ScoredEntry> InClass(string carClass) =>
        Entries.Where(e => e.Entry.CarClass == carClass).OrderBy(e => e.Entry.ClassPosition);
}

public static class RaceScorer
{
    /// <param name="qualifying">Supplies class poles. Null when there was no qualifying file.</param>
    /// <param name="weight">Points multiplier for the event, e.g. 2 for Le Mans.</param>
    /// <param name="playerMinimumDriveShare">
    /// Share of the car's laps the player must drive themselves (the rest go to the AI co-driver)
    /// to score driver points. 0 disables the check.
    /// </param>
    public static ScoredRace Score(
        SessionResult race,
        SessionResult? qualifying = null,
        ScoringRules? rules = null,
        decimal weight = 1,
        double playerMinimumDriveShare = 0,
        string? roundName = null)
    {
        rules ??= ScoringRules.Wec;

        var poleSitters = qualifying is null
            ? new HashSet<string>()
            : qualifying.Entries
                .Where(e => e.ClassPosition == 1 && e.Status != FinishStatus.Dq)
                .Select(e => Standings.NameKey(e.Name))
                .ToHashSet();

        var scored = new List<ScoredEntry>();
        foreach (var classEntries in race.Entries.GroupBy(e => e.CarClass))
        {
            var winnerLaps = classEntries.Max(e => e.Laps);
            var minimumLaps = (int)Math.Ceiling(winnerLaps * rules.ClassificationShare);

            var rank = 0;
            foreach (var entry in classEntries.OrderBy(e => e.ClassPosition))
            {
                var classified = entry.Status == FinishStatus.Finished && entry.Laps >= minimumLaps;
                int? classRank = classified ? ++rank : null;
                var racePoints = classRank is int r && r <= rules.Points.Count ? rules.Points[r - 1] * weight : 0;

                var pole = poleSitters.Contains(Standings.NameKey(entry.Name));
                var polePoints = pole && entry.Status != FinishStatus.Dq ? rules.PoleBonus : 0;

                var driveShare = DriveShare(entry);
                var eligible = !entry.IsPlayer || driveShare >= playerMinimumDriveShare;

                scored.Add(new ScoredEntry(entry, classified, classRank, racePoints, pole, polePoints, driveShare, eligible));
            }
        }

        return new ScoredRace(roundName ?? race.TrackCourse, race, weight, scored);
    }

    /// <summary>Share of the car's laps under player control. Cars with no control log count as fully driven.</summary>
    public static double DriveShare(EntryResult entry)
    {
        var player = entry.LapsUnder(Controller.Player);
        var total = player + entry.LapsUnder(Controller.AI);
        return total == 0 ? 1 : (double)player / total;
    }
}
