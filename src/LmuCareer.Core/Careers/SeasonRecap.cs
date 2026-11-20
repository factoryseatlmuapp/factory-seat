using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>One round on the recap card's results strip.</summary>
/// <param name="ClassRank">The class finish; null when not classified (see <paramref name="Status"/>).</param>
public sealed record RecapRound(string EventName, string TrackFolder, int? ClassRank, FinishStatus Status, bool Pole, bool Guest, string SeriesId);

/// <summary>
/// A finished season on one card, to save as an image and share: the championship, the numbers,
/// every round's finish, the titles, achievements earned and any lifetime records set.
/// </summary>
/// <param name="Gap">For the champion, points clear of second; for anyone else, points behind the leader.</param>
public sealed record SeasonRecapData(
    string CareerName, int Season, CareerCar Car, string TeamName, int? Position, decimal Points, decimal? Gap,
    int Races, int Wins, int Podiums, int Poles, int NotClassified,
    IReadOnlyList<RecapRound> Rounds, IReadOnlyList<TrophyTitle> Titles, IReadOnlyList<string> Achievements,
    IReadOnlyList<RecordValue> NewRecords, SeasonReview? Review, bool ChampionshipOnly);

public static class SeasonRecap
{
    /// <param name="rooms">Every career's trophies (this one included), to tell which records are lifetime bests.</param>
    public static SeasonRecapData For(Career career, int seasonNumber, IReadOnlyList<CareerTrophies> rooms)
    {
        var season = career.PastSeasons.Append(career.CurrentSeason).FirstOrDefault(s => s.Number == seasonNumber)
            ?? throw new KeyNotFoundException("No such season.");

        var table = season.Standings().FirstOrDefault(t => t.CarClass.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase));
        var index = table?.Drivers.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        var me = index >= 0 ? table!.Drivers[index] : null;
        decimal? gap = me is null ? null
            : index == 0 ? (table!.Drivers.Count > 1 ? me.Points - table.Drivers[1].Points : null)
            : table!.Drivers[0].Points - me.Points;

        var rounds = season.Rounds
            .Where(r => r.State == RoundState.Completed && r.Test is null && r.Result is not null)
            .Select(r => (Round: r, Me: r.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer)))
            .Select(x => new RecapRound(x.Round.EventName, x.Round.TrackFolder,
                x.Me is { Classified: true } ? x.Me.ClassRank : null, x.Me?.Entry.Status ?? FinishStatus.Dnf,
                x.Me?.ClassPole == true, x.Round.Guest, x.Round.SeriesId))
            .ToList();

        var mine = rooms.FirstOrDefault(r => r.CareerId == career.Id);
        var newRecords = mine?.Records
            .Where(r => r.Season == season.Number && !TrophyRoom.Summed.Contains(r.Id))
            .Where(r => rooms.Where(o => o.CareerId != career.Id).SelectMany(o => o.Records).Where(o => o.Id == r.Id)
                .All(o => TrophyRoom.LowestWins.Contains(r.Id) ? r.Value < o.Value : r.Value > o.Value))
            .ToList() ?? [];

        return new SeasonRecapData(
            career.Name, season.Number, season.Car, season.Contract?.TeamName is { Length: > 0 } team ? team : season.Car.TeamName,
            me is null ? null : index + 1, me?.Points ?? 0, gap,
            rounds.Count, rounds.Count(r => r.ClassRank == 1), rounds.Count(r => r.ClassRank is <= 3),
            rounds.Count(r => r.Pole), rounds.Count(r => r.ClassRank is null),
            rounds, mine?.Titles.Where(t => t.Season == season.Number).ToList() ?? [],
            career.Achievements.Where(a => a.Season == season.Number).Select(a => a.Id).ToList(),
            newRecords, season.Review, career.ChampionshipOnly);
    }
}
