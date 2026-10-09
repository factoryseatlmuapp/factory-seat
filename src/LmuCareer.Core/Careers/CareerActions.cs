using LmuCareer.Core.Content;
using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>The steps of running a round that touch the results folder and the save.</summary>
public static class CareerActions
{
    /// <summary>
    /// Checks every results file written since the armed round was started. Older files are never
    /// read: they can't count.
    /// </summary>
    /// <param name="savesFolder">
    /// LMU's race weekend saves (UserData\Saves\Race Weekend Saves). LMU logs a race saved to
    /// finish later exactly like one that was quit, so a save written since the round started is
    /// what tells them apart, however long after it the player left (sat in the menus, or loaded
    /// the save another day and backed out without saving again).
    /// </param>
    public static RoundEvaluation CheckArmedRound(CareerStore store, Career career, string resultsFolder, string? savesFolder = null)
    {
        var season = career.CurrentSeason;
        var round = season.ArmedRound ?? throw new InvalidOperationException("No round is armed.");
        var armedAt = round.ArmedAt!.Value;

        // Files this career has already counted, by the round that counted them.
        var counted = career.PastSeasons.Append(season)
            .SelectMany(s => s.Rounds)
            .Where(r => r.Result is not null)
            .SelectMany(r => new[] { (File: r.Result!.RaceFile, Round: r), (File: r.Result.QualifyingFile ?? "", Round: r) })
            .Where(x => x.File.Length > 0)
            .GroupBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Round, StringComparer.OrdinalIgnoreCase);

        var checks = new List<SessionCheck>();
        if (Directory.Exists(resultsFolder))
        {
            foreach (var file in new DirectoryInfo(resultsFolder).EnumerateFiles("*.xml")
                .Where(f => f.LastWriteTimeUtc >= armedAt.UtcDateTime)
                .OrderBy(f => f.LastWriteTimeUtc))
            {
                SessionResult session;
                try
                {
                    session = ResultsParser.Load(file.FullName);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
                {
                    // LMU may still be writing it; it'll be read on the next check.
                    continue;
                }

                var writtenAt = new DateTimeOffset(file.LastWriteTimeUtc);
                checks.Add(counted.TryGetValue(file.Name, out var countedFor)
                    ? new SessionCheck(session, writtenAt, MatchVerdict.Ignored,
                        [Phrase.Of("already counted for round {number} ({event})", ("number", countedFor.Number), ("event", countedFor.EventName))])
                    : RoundMatcher.Check(session, writtenAt, round, round.GuestCar ?? season.Car, store.ClaimedByOther(file.Name, career.Id)));
            }
        }

        var evaluation = RoundFlow.Evaluate(checks);
        if (evaluation.Status == RoundStatus.QuitEarly && evaluation.Race is { } race
            && SaveBefore(savesFolder, armedAt, race.WrittenAt) is { } save)
        {
            return evaluation with { Status = RoundStatus.SavedToResume, SavedAs = save };
        }
        return evaluation;
    }

    /// <summary>The newest race weekend save written since the round started and before the race's results, by name.</summary>
    private static string? SaveBefore(string? savesFolder, DateTimeOffset armedAt, DateTimeOffset raceWrittenAt)
    {
        if (savesFolder is null || !Directory.Exists(savesFolder)) return null;
        var save = new DirectoryInfo(savesFolder).EnumerateFiles("*.json")
            .Where(f => f.LastWriteTimeUtc >= armedAt.UtcDateTime
                && f.LastWriteTimeUtc <= raceWrittenAt.UtcDateTime.AddMinutes(1))
            .MaxBy(f => f.LastWriteTimeUtc);
        if (save is null) return null;

        // LMU names saves "<id>-<the name the player typed>.json".
        var name = Path.GetFileNameWithoutExtension(save.Name);
        var dash = name.IndexOf('-');
        return dash > 0 && name[..dash].All(char.IsDigit) ? name[(dash + 1)..] : name;
    }

    /// <summary>
    /// Swaps the first season's car for another in the same class, until its first round has
    /// counted. The number and team stay: a Race Control custom team keeps its number across cars,
    /// and a livery player's next race sets them anyway. From the second season on, the car comes
    /// with the seat the player signed for.
    /// </summary>
    public static void ChangeCar(Career career, CarInfo car)
    {
        var season = career.CurrentSeason;
        if (career.PastSeasons.Count > 0)
            throw new PlayerError("The car comes with your seat; a different car means a different offer next season.");
        if (season.Rounds.Any(r => r.State == RoundState.Completed))
            throw new PlayerError("The season has started; the car is set until next season.");
        if (!car.Class.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase))
            throw new PlayerError("The season is built for {class}; pick a car in that class.", ("class", season.Car.CarClass));

        season.Car = season.Car with
        {
            CarType = car.CarTypes.FirstOrDefault() ?? "",
            OtherCarTypes = car.CarTypes.Skip(1).ToList(),
            Folder = car.Folder,
        };
    }

    /// <summary>
    /// A race at the round's track that was ignored only because it was in another car: worth
    /// telling the player about, since it looks like their race.
    /// </summary>
    public static SessionCheck? WrongCarRace(RoundEvaluation evaluation) =>
        evaluation.Ignored
            .Where(c => c.Session.Kind == SessionKind.Race && c.WrongCar)
            .MaxBy(c => c.WrittenAt);

    /// <summary>
    /// Scores the armed round and saves the career. What the career didn't know about the car
    /// (LMU's name for the model, the number and team it raced as) is learned from the race, and a
    /// race the player counted under a different number makes that number the career car's.
    /// </summary>
    public static ScoredRace AcceptArmedRound(
        CareerStore store, Career career, RoundEvaluation evaluation, bool takeDnf, bool acceptDifferences, DateTimeOffset now)
    {
        var season = career.CurrentSeason;
        var round = season.ArmedRound ?? throw new InvalidOperationException("No round is armed.");
        var scored = RoundFlow.Accept(round, evaluation, now, takeDnf, acceptDifferences);

        if (scored.Race.Player is { } player)
        {
            // A guest drive learns about its own car; the season's stays as it was.
            if (round.GuestCar is { } guest) round.GuestCar = Learn(guest, player);
            else season.Car = Learn(season.Car, player);
        }

        career.LastPlayedAt = now;
        store.Save(career);
        return scored;
    }

    private static CareerCar Learn(CareerCar car, Results.EntryResult player)
    {
        if (car.CarType.Length == 0) car = car with { CarType = player.CarType };
        if (player.CarNumber != car.CarNumber || player.IsCustomTeam != car.CustomTeam)
            return car with { CarNumber = player.CarNumber, TeamName = player.TeamName, CustomTeam = player.IsCustomTeam };
        return car.TeamName.Length == 0 ? car with { TeamName = player.TeamName } : car;
    }

    /// <summary>
    /// Careers saved before translations hold their season reviews and offer reasons as English
    /// text only. A review is worked out from the season's results alone, so working it out again
    /// gives each item its phrase back; an item that doesn't come out the same is left as it was.
    /// </summary>
    /// <returns>True when phrases were added (the career needs saving).</returns>
    public static bool AddPhrases(Career career)
    {
        var changed = false;
        foreach (var season in career.PastSeasons.Append(career.CurrentSeason))
        {
            if (season.Review is not { } review || review.Items.All(i => i.Text is not null)) continue;
            var fresh = Progression.Review(season, review.ReputationBefore).Items;
            var items = review.Items
                .Select(i => i.Text is null && fresh.FirstOrDefault(f => f.Label == i.Label)?.Text is { } text ? i with { Text = text } : i)
                .ToList();
            if (items.Zip(review.Items).Any(pair => pair.First.Text != pair.Second.Text))
            {
                review.Items = items;
                changed = true;
            }
        }

        const string winAt = "your win at the ";
        Phrase? Reason(string reason) => reason.StartsWith(winAt, StringComparison.Ordinal)
            ? Phrase.Of("your win at the {event}", ("event", reason[winAt.Length..]))
            : null;
        foreach (var offer in career.Offers.Where(o => o.ReasonText is null && Reason(o.Reason) is not null))
        {
            offer.ReasonText = Reason(offer.Reason);
            changed = true;
        }
        foreach (var interest in career.Interest.Where(i => i.ReasonText is null && Reason(i.Reason) is not null))
        {
            interest.ReasonText = Reason(interest.Reason);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Careers saved before custom team cars were recognised: if the season's last counted race was
    /// in the custom team car under the career's number, the car is that custom team car.
    /// </summary>
    /// <returns>True when the car changed (the career needs saving).</returns>
    public static bool RecognizeCustomTeam(Career career)
    {
        var season = career.CurrentSeason;
        if (season.Car.CustomTeam) return false;
        var last = season.ChampionshipRounds.LastOrDefault(r => r.State == RoundState.Completed)
            ?? career.PastSeasons.LastOrDefault()?.ChampionshipRounds.LastOrDefault(r => r.State == RoundState.Completed);
        var player = last?.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.Entry;
        if (player is null || !player.IsCustomTeam || player.CarNumber != season.Car.CarNumber) return false;
        season.Car = season.Car with { CustomTeam = true };
        return true;
    }
}
