using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>A class win: a plate on that track's trophy.</summary>
/// <param name="NewGamePlus">Won in a New Game+ race that passed its floor: the plate goes on the New Game+ wing.</param>
public sealed record TrophyWin(string TrackFolder, string EventName, string CarClass, string CarType, int Season, bool NewGamePlus = false);

/// <summary>A class championship won: a cup on the title shelf.</summary>
/// <param name="SeriesId">Empty for the season's own championship; "imsa" for IMSA beside it.</param>
/// <param name="NewGamePlus">Every round of the championship passed its New Game+ floor.</param>
public sealed record TrophyTitle(string CarClass, string CarType, int Season, string SeriesId, bool NewGamePlus = false);

/// <summary>One career's best at a record: the page takes the best across the careers it shows.</summary>
/// <param name="Season">The season it was set in, when it belongs to one.</param>
public sealed record RecordValue(string Id, decimal Value, int? Season = null);

/// <param name="NewGamePlus">A New Game+ career (now or once): the room has a New Game+ wing.</param>
public sealed record CareerTrophies(
    Guid CareerId, string CareerName, IReadOnlyList<TrophyWin> Wins, IReadOnlyList<TrophyTitle> Titles, IReadOnlyList<RecordValue> Records,
    bool NewGamePlus = false);

/// <summary>
/// The trophy room: every class win (by track) and every class title a career has won, from its
/// counted results. Guest drives count as wins; test days don't.
/// </summary>
public static class TrophyRoom
{
    /// <summary>Records where the lowest value is the best (the rest: the highest).</summary>
    public static readonly IReadOnlyList<string> LowestWins = ["fastest-to-hypercar"];

    /// <summary>Records summed across careers rather than the best one taken.</summary>
    public static readonly IReadOnlyList<string> Summed = ["hours-raced"];

    public static CareerTrophies For(Career career, ContentCatalog catalog)
    {
        var seasons = career.PastSeasons.Append(career.CurrentSeason).ToList();

        var wins = seasons.SelectMany(s => s.Rounds
                .Where(r => r.State == RoundState.Completed && r.Test is null
                    && r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer) is { Classified: true, ClassRank: 1 })
                .OrderBy(r => r.Result!.AcceptedAt)
                .Select(r => new TrophyWin(r.TrackFolder, r.EventName, s.CarFor(r).CarClass, s.CarFor(r).CarType, s.Number, FloorChecks.Credited(r))))
            .ToList();

        // A title is the top of the player's class table once the season is over, in each championship raced.
        var titles = new List<TrophyTitle>();
        foreach (var season in seasons.Where(s => s.IsFinished))
        {
            foreach (var (seriesId, car) in season.Series.Select(x => (x.Id, x.Car)).Prepend(("", season.Car)))
            {
                var table = season.Standings(seriesId).FirstOrDefault(t => t.CarClass.Equals(car.CarClass, StringComparison.OrdinalIgnoreCase));
                if (table?.Drivers.FirstOrDefault() is { IsPlayer: true })
                    titles.Add(new TrophyTitle(car.CarClass, car.CarType, season.Number, seriesId, FloorChecks.Credited(season, seriesId)));
            }
        }

        return new CareerTrophies(career.Id, career.Name, wins, titles, Records(career, seasons, catalog), career.NewGamePlus is not null);
    }

    private static IReadOnlyList<RecordValue> Records(Career career, IReadOnlyList<Season> seasons, ContentCatalog catalog)
    {
        var records = new List<RecordValue>();
        void Best(string id, IEnumerable<(decimal Value, int? Season)> values, bool lowest = false)
        {
            var list = values.Where(v => v.Value > 0).ToList();
            if (list.Count == 0) return;
            var best = lowest ? list.MinBy(v => v.Value) : list.MaxBy(v => v.Value);
            records.Add(new RecordValue(id, best.Value, best.Season));
        }

        var raced = seasons.SelectMany(s => s.Rounds
                .Where(r => r.State == RoundState.Completed && r.Test is null && r.Result is not null)
                .OrderBy(r => r.Result!.AcceptedAt)
                .Select(r => (Season: s, Round: r, Me: r.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer))))
            .ToList();
        static bool Won(Scoring.ScoredEntry? me) => me is { Classified: true, ClassRank: 1 };

        Best("wins-in-a-season", raced.GroupBy(x => x.Season.Number).Select(g => ((decimal)g.Count(x => Won(x.Me)), (int?)g.Key)));
        Best("poles-in-a-season", raced.GroupBy(x => x.Season.Number).Select(g => ((decimal)g.Count(x => x.Me?.ClassPole == true), (int?)g.Key)));

        // Class wins in a row, counted races in order (a streak can run across seasons).
        var streak = 0;
        var streaks = new List<(decimal, int?)>();
        foreach (var x in raced)
        {
            streak = Won(x.Me) ? streak + 1 : 0;
            if (streak > 0) streaks.Add((streak, x.Season.Number));
        }
        Best("win-streak", streaks);

        // Points between the player and second place, in a title season.
        Best("title-margin", seasons.Where(s => s.IsFinished).Select(s =>
        {
            var table = s.Standings().FirstOrDefault(t => t.CarClass.Equals(s.Car.CarClass, StringComparison.OrdinalIgnoreCase));
            return table is { Drivers: [{ IsPlayer: true } first, var second, ..] } ? (first.Points - second.Points, (int?)s.Number) : (0m, null);
        }));

        // Seasons in a row with the same make.
        var run = 0;
        string? make = null;
        var runs = new List<(decimal, int?)>();
        foreach (var s in seasons.Where(s => s.Rounds.Any(r => r.State == RoundState.Completed)))
        {
            var brand = catalog.Car(s.Car.Folder)?.Brand ?? catalog.CarByType(s.Car.CarType)?.Brand;
            run = brand is not null && brand == make ? run + 1 : brand is null ? 0 : 1;
            make = brand;
            if (run > 0) runs.Add((run, s.Number));
        }
        Best("loyalty-streak", runs);

        // The season the player first raced a Hypercar, for a career that didn't start in one.
        if (!career.ChampionshipOnly && seasons.Count > 0 && seasons[0].Car.CarClass != "Hyper"
            && seasons.FirstOrDefault(s => s.Car.CarClass == "Hyper") is { } hyper)
        {
            Best("fastest-to-hypercar", [(hyper.Number, hyper.Number)], lowest: true);
        }

        // The length of the longest race won, as briefed (not one counted despite a different setting).
        Best("longest-win", raced.Where(x => Won(x.Me) && x.Round.Result!.AcceptedDespite.Count == 0)
            .Select(x => ((decimal)x.Round.RaceMinutes, (int?)x.Season.Number)));

        var minutes = raced.Where(x => !x.Round.Result!.QuitEarly).Sum(x => x.Round.RaceMinutes);
        if (minutes > 0) records.Add(new RecordValue("hours-raced", Math.Round(minutes / 60m, 1)));
        return records;
    }
}
