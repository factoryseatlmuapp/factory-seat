using LmuCareer.Core.Careers;

namespace LmuCareer.Core.Content;

/// <summary>Builds season calendars from the catalog's events.</summary>
public static class CalendarBuilder
{
    /// <summary>Rounds in a full world season; base-game substitutes fill the gaps up to this many.</summary>
    public const int WorldSeasonRounds = 8;

    /// <summary>Rounds in a full European season.</summary>
    public const int EuropeanSeasonRounds = 6;

    /// <summary>
    /// The default calendar for a class: the world season for GT3 and Hypercar, the European season
    /// for LMP3 and LMP2 (plus Le Mans for LMP2). Rounds at tracks the player doesn't own are left
    /// out, and rounds the real championship has run on base-game tracks fill the gaps.
    /// </summary>
    public static List<Round> DefaultSeason(
        ContentCatalog catalog, string carClass, DriverRating rating,
        IReadOnlyCollection<string> ownedPacks, LmuInstall? install = null)
    {
        var european = carClass.Equals("LMP2", StringComparison.OrdinalIgnoreCase)
            || carClass.Equals("LMP3", StringComparison.OrdinalIgnoreCase);
        var (series, fallback, size) = european
            ? ("european", "european-base", EuropeanSeasonRounds)
            : ("world", "world-base", WorldSeasonRounds);

        bool Available(EventInfo e) =>
            catalog.Track(e.Folder) is { } track
            && ContentCatalog.IsOwned(track.Pack, ownedPacks)
            && (install is null || install.HasLayout(e.Folder, e.Layout));

        var events = catalog.Events.Where(e => e.Series == series && Available(e)).ToList();
        events.AddRange(catalog.Events.Where(e => e.Series == fallback && Available(e)).Take(Math.Max(0, size - events.Count)));

        if (carClass.Equals("LMP2", StringComparison.OrdinalIgnoreCase) && catalog.Event("le-mans-24") is { } leMans && Available(leMans))
            events.Add(leMans);

        return Number(events.OrderBy(e => e.Month).Select(e => RoundFor(catalog, e, carClass, rating)));
    }

    /// <summary>A round for a catalog event, at its default length unless <paramref name="minutes"/> says otherwise.</summary>
    public static Round RoundFor(ContentCatalog catalog, EventInfo e, string carClass, DriverRating rating, int? minutes = null)
    {
        var round = new Round
        {
            EventId = e.Id,
            EventName = e.Name,
            TrackFolder = e.Folder,
            LayoutFile = e.Layout,
            TrackCourse = catalog.Track(e.Folder)?.Name ?? e.Folder,
            StartTime = e.StartTime,
            RealDurationHours = e.Hours,
            PointsWeight = RaceFormats.DefaultWeight(e.Hours),
            MinimumDriveShare = Features.AiDriverSwaps ? RaceFormats.MinimumDriveShare(carClass, rating, e.Hours) : 0,
        };
        ApplyLength(round, minutes);
        return round;
    }

    /// <summary>Sets the race length and recalculates Time Scale, Fuel Usage, Tyre Wear and stints to fit.</summary>
    public static void ApplyLength(Round round, int? minutes)
    {
        var format = RaceFormats.For(round.RealDurationHours, minutes);
        round.RaceMinutes = format.RaceMinutes;
        round.TimeScale = format.TimeScale;
        round.FuelMultiplier = format.FuelMultiplier;
        round.TireMultiplier = format.TireMultiplier;
        round.Stints = format.Stints;
    }

    /// <summary>Renumbers rounds 1..n in calendar order.</summary>
    public static List<Round> Number(IEnumerable<Round> rounds)
    {
        var list = rounds.ToList();
        for (var i = 0; i < list.Count; i++) list[i].Number = i + 1;
        return list;
    }
}
