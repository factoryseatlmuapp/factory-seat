using System.Globalization;
using System.Text;

namespace LmuCareer.Core.Scoring;

public sealed record DriverStanding(
    string Name,
    string TeamName,
    string CarNumber,
    bool IsPlayer,
    decimal Points,
    int Starts,
    int Wins,
    int Podiums,
    int Poles,
    // Class finishing rank in each round, in calendar order; null = not classified or didn't start.
    IReadOnlyList<int?> RoundRanks);

public sealed record TeamStanding(string TeamName, decimal Points, int Wins, IReadOnlyList<int?> BestRoundRanks);

public sealed record ClassStandings(
    string CarClass,
    IReadOnlyList<DriverStanding> Drivers,
    IReadOnlyList<TeamStanding> Teams);

public static class Standings
{
    /// <summary>Championship tables per class, with ties broken on countback (most wins, then most seconds, and so on).</summary>
    public static IReadOnlyList<ClassStandings> Build(IReadOnlyList<ScoredRace> rounds) =>
        Build(rounds.Select(r => r.Entries).ToList());

    /// <param name="rounds">Each round's scored entries, in calendar order.</param>
    public static IReadOnlyList<ClassStandings> Build(IReadOnlyList<IReadOnlyList<ScoredEntry>> rounds)
    {
        var classes = rounds.SelectMany(r => r.Select(e => e.Entry.CarClass)).Distinct();
        return classes.Select(c => BuildClass(c, rounds)).ToList();
    }

    private static ClassStandings BuildClass(string carClass, IReadOnlyList<IReadOnlyList<ScoredEntry>> rounds)
    {
        var entries = rounds
            .SelectMany((round, index) => round
                .Where(e => e.Entry.CarClass == carClass)
                .Select(e => (Round: index, Scored: e)))
            .ToList();

        var drivers = entries
            .GroupBy(x => NameKey(x.Scored.Entry.Name))
            .Select(g =>
            {
                // Name, team and number come from the driver's most recent start.
                var latest = g.MaxBy(x => x.Round).Scored.Entry;
                var ranks = new int?[rounds.Count];
                foreach (var (round, scored) in g) ranks[round] = scored.ClassRank;

                return new DriverStanding(
                    Name: latest.Name,
                    TeamName: latest.TeamName,
                    CarNumber: latest.CarNumber,
                    IsPlayer: g.Any(x => x.Scored.Entry.IsPlayer),
                    Points: g.Sum(x => x.Scored.DriverPoints),
                    Starts: g.Count(),
                    Wins: g.Count(x => x.Scored.ClassRank == 1),
                    Podiums: g.Count(x => x.Scored.ClassRank <= 3),
                    Poles: g.Count(x => x.Scored.ClassPole),
                    RoundRanks: ranks);
            })
            .OrderByDescending(d => d.Points)
            .ThenBy(d => d.RoundRanks, Countback)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        var teams = entries
            .GroupBy(x => x.Scored.Entry.TeamName)
            .Select(g =>
            {
                var best = new int?[rounds.Count];
                foreach (var (round, scored) in g)
                {
                    if (scored.ClassRank is int r && (best[round] is null || r < best[round])) best[round] = r;
                }

                return new TeamStanding(
                    TeamName: g.Key,
                    Points: g.Sum(x => x.Scored.TotalPoints),
                    Wins: g.Count(x => x.Scored.ClassRank == 1),
                    BestRoundRanks: best);
            })
            .OrderByDescending(t => t.Points)
            .ThenBy(t => t.BestRoundRanks, Countback)
            .ThenBy(t => t.TeamName, StringComparer.Ordinal)
            .ToList();

        return new ClassStandings(carClass, drivers, teams);
    }

    /// <summary>
    /// Identity for a driver across rounds. LMU spells some AI names differently between game
    /// versions ("François Hériau" and "Francois Heriau"), so accents and case are ignored.
    /// </summary>
    public static string NameKey(string name)
    {
        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var bare = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return string.Concat(bare).Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>Orders the better record first: more wins, then more second places, and so on down.</summary>
    private static readonly Comparer<IReadOnlyList<int?>> Countback = Comparer<IReadOnlyList<int?>>.Create((a, b) =>
    {
        var deepest = a.Concat(b).Select(r => r ?? 0).DefaultIfEmpty(0).Max();
        for (var place = 1; place <= deepest; place++)
        {
            var diff = b.Count(r => r == place) - a.Count(r => r == place);
            if (diff != 0) return diff;
        }
        return 0;
    });
}
