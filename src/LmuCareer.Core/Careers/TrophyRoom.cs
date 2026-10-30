namespace LmuCareer.Core.Careers;

/// <summary>A class win: a plate on that track's trophy.</summary>
public sealed record TrophyWin(string TrackFolder, string EventName, string CarClass, string CarType, int Season);

/// <summary>A class championship won: a cup on the title shelf.</summary>
/// <param name="SeriesId">Empty for the season's own championship; "imsa" for IMSA beside it.</param>
public sealed record TrophyTitle(string CarClass, string CarType, int Season, string SeriesId);

public sealed record CareerTrophies(Guid CareerId, string CareerName, IReadOnlyList<TrophyWin> Wins, IReadOnlyList<TrophyTitle> Titles);

/// <summary>
/// The trophy room: every class win (by track) and every class title a career has won, from its
/// counted results. Guest drives count as wins; test days don't.
/// </summary>
public static class TrophyRoom
{
    public static CareerTrophies For(Career career)
    {
        var seasons = career.PastSeasons.Append(career.CurrentSeason).ToList();

        var wins = seasons.SelectMany(s => s.Rounds
                .Where(r => r.State == RoundState.Completed && r.Test is null
                    && r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer) is { Classified: true, ClassRank: 1 })
                .OrderBy(r => r.Result!.AcceptedAt)
                .Select(r => new TrophyWin(r.TrackFolder, r.EventName, s.CarFor(r).CarClass, s.CarFor(r).CarType, s.Number)))
            .ToList();

        // A title is the top of the player's class table once the season is over, in each championship raced.
        var titles = new List<TrophyTitle>();
        foreach (var season in seasons.Where(s => s.IsFinished))
        {
            foreach (var (seriesId, car) in season.Series.Select(x => (x.Id, x.Car)).Prepend(("", season.Car)))
            {
                var table = season.Standings(seriesId).FirstOrDefault(t => t.CarClass.Equals(car.CarClass, StringComparison.OrdinalIgnoreCase));
                if (table?.Drivers.FirstOrDefault() is { IsPlayer: true })
                    titles.Add(new TrophyTitle(car.CarClass, car.CarType, season.Number, seriesId));
            }
        }

        return new CareerTrophies(career.Id, career.Name, wins, titles);
    }
}
