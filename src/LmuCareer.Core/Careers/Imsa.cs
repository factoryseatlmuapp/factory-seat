using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// IMSA, raced beside the season's own championship by a team with the means to run both. Its
/// classes map onto LMU's: GTP is Hypercar, GTD is LMGT3, and LMP2 is LMP2; there's no LMP3. The
/// calendar is the IMSA events the player owns, with COTA standing in for the rounds LMU doesn't
/// have. Rounds from a new track pack join the next season built, never a season already running.
/// </summary>
public static class Imsa
{
    public const string Id = "imsa";
    public const string Name = "IMSA";

    /// <summary>Fewer IMSA rounds than this owned (the base game has only Sebring) and no team offers an IMSA season.</summary>
    public const int MinRounds = 3;

    public static bool RacesIn(string carClass) => carClass is "Hyper" or "GT3" or "LMP2";

    /// <summary>The IMSA season for a class: every owned IMSA event, in calendar order, each round marked as IMSA's.</summary>
    public static List<Round> Calendar(ContentCatalog catalog, string carClass, DriverRating rating,
        IReadOnlyCollection<string> ownedPacks, LmuInstall? install = null) =>
        catalog.Events
            .Where(e => e.Imsa
                && catalog.Track(e.Folder) is { } track && ContentCatalog.IsOwned(track.Pack, ownedPacks)
                && (install is null || install.HasLayout(e.Folder, e.Layout)))
            .OrderBy(e => e.Month)
            .Select(e =>
            {
                var round = CalendarBuilder.RoundFor(catalog, e, carClass, rating);
                round.SeriesId = Id;
                return round;
            })
            .ToList();

    /// <summary>
    /// Adds IMSA to a season that hasn't started: its car and deal, and its rounds slotted in by
    /// month. Two rounds in the same month are two weekends, never the same one: the season's own
    /// comes first. Rounds the season already has (Daytona as a guest drive) aren't added twice.
    /// </summary>
    public static SeriesEntry Add(Season season, CareerCar car, Contract contract, IEnumerable<Round> rounds, ContentCatalog catalog)
    {
        if (season.Series.Any(s => s.Id == Id)) throw new InvalidOperationException("The season already runs IMSA.");
        var entry = new SeriesEntry { Id = Id, Name = Name, Car = car, Contract = contract };
        season.Series.Add(entry);

        foreach (var round in rounds.Where(r => !season.Rounds.Any(s => s.EventId.Length > 0 && s.EventId == r.EventId)))
        {
            var month = catalog.Event(round.EventId)?.Month ?? 12;
            // Rounds with no catalog month (custom events, test days) keep their place behind the round before them.
            var at = season.Rounds.Count;
            var current = 0;
            for (var i = 0; i < season.Rounds.Count; i++)
            {
                current = catalog.Event(season.Rounds[i].EventId)?.Month ?? current;
                if (current > month) { at = i; break; }
            }
            TestDays.Insert(season, round, at);
        }
        return entry;
    }
}
