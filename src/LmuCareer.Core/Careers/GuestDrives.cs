using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// One-off seats at the classic American endurance races (Daytona 24, Sebring 12, Petit Le Mans),
/// offered at the start of a season once the player has made a name. A guest drive is a round in
/// the season's calendar in another team's car; it doesn't count for the championship, but a good
/// result builds reputation. A driver close to the next class's bar may get a guest seat in that
/// class: a team trying them out.
/// </summary>
public static class GuestDrives
{
    /// <summary>No guest seats for a driver below this reputation.</summary>
    public const decimal MinReputation = 5;

    /// <summary>How close to the next class's bar a driver must be for a team there to try them out.</summary>
    public const decimal AuditionMargin = 5;

    /// <summary>Makes the season's guest offers, once, while the season hasn't started.</summary>
    /// <returns>True when offers were made (the career needs saving).</returns>
    public static bool EnsureOffers(Career career, ContentCatalog catalog, LmuInstall? install, int? seed = null)
    {
        var season = career.CurrentSeason;
        if (season.GuestOffers is not null || !Sponsorship.CanSign(season)) return false;
        season.GuestOffers = MakeOffers(career, catalog, install, new Random(seed ?? Progression.Seed(career) ^ 0x6E57)).ToList();
        return true;
    }

    public static IReadOnlyList<GuestOffer> MakeOffers(Career career, ContentCatalog catalog, LmuInstall? install, Random random)
    {
        var season = career.CurrentSeason;
        var reputation = career.Reputation;
        var count = reputation switch
        {
            >= 45 => 3,
            >= 25 => 2,
            >= MinReputation => 1,
            _ => 0,
        };

        // Events the player can drive that aren't already championship rounds.
        var events = catalog.Events
            .Where(e => e.Series == "guest"
                && catalog.Track(e.Folder) is { } track && ContentCatalog.IsOwned(track.Pack, career.OwnedContent)
                && (install is null || install.HasLayout(e.Folder, e.Layout))
                && !season.Rounds.Any(r => r.EventId == e.Id))
            .OrderBy(_ => random.Next())
            .Take(count)
            .OrderBy(e => e.Month)
            .ToList();

        // IMSA has no LMP3 class at its big races: an LMP3 driver guests in LMP2.
        var ownClass = season.Car.CarClass == "LMP3" ? "LMP2" : season.Car.CarClass;
        var next = Progression.NextClass(season.Car.CarClass, c => c != "LMP3" && catalog.Cars.Any(car => car.Class == c && Progression.CanOffer(car, career, install)));
        var auditionOpen = next is not null && next != ownClass && reputation >= Progression.Threshold(next) - AuditionMargin;

        var offers = new List<GuestOffer>();
        foreach (var e in events)
        {
            // One audition at most, at the biggest race on offer.
            var audition = auditionOpen && !offers.Any(o => o.Audition) && e == events.MaxBy(x => x.Hours);
            var carClass = audition ? next! : ownClass;
            var tier = Progression.BestTier(reputation, carClass);

            var pool = catalog.Teams
                .Where(t => t.Class == carClass && t.Tier >= tier
                    && !t.Name.Equals(season.Contract?.TeamName ?? season.Car.TeamName, StringComparison.OrdinalIgnoreCase))
                .Select(t => (Team: t, Car: CarFor(t)))
                .Where(x => x.Car is not null)
                .OrderBy(x => x.Team.Tier)
                .ToList();
            if (pool.Count == 0) continue;
            var best = pool.Where(x => x.Team.Tier == pool[0].Team.Tier).ToList();
            var (team, car) = best[random.Next(best.Count)];

            offers.Add(new GuestOffer
            {
                Id = $"g{season.Number}-{offers.Count + 1}",
                EventId = e.Id,
                EventName = e.Name,
                TeamName = team.Name,
                CarClass = carClass,
                CarFolder = car!.Folder,
                Tier = team.Tier,
                Numbers = team.Numbers,
                Audition = audition,
            });
        }
        return offers;

        CarInfo? CarFor(TeamInfo team)
        {
            var cars = team.Cars.Count > 0
                ? team.Cars.Select(catalog.Car).OfType<CarInfo>()
                : catalog.Cars.Where(c => c.Class == team.Class);
            return cars.FirstOrDefault(c => Progression.CanOffer(c, career, install));
        }
    }

    /// <summary>
    /// Takes the guest seat: the race goes into the calendar at its month, before the first round
    /// that comes later in the year. Sponsor deals pinned to a round follow it if it moves.
    /// </summary>
    public static Round Accept(Career career, string offerId, int? minutes, ContentCatalog catalog)
    {
        var season = career.CurrentSeason;
        if (!Sponsorship.CanSign(season)) throw new InvalidOperationException("Guest drives are agreed before the season's first round counts.");
        var offer = season.GuestOffers?.FirstOrDefault(o => o.Id == offerId) ?? throw new KeyNotFoundException("That guest drive isn't on offer.");
        if (offer.Accepted) throw new InvalidOperationException("You've already agreed to that one.");
        var e = catalog.Event(offer.EventId) ?? throw new KeyNotFoundException($"No event \"{offer.EventId}\".");
        var car = catalog.Car(offer.CarFolder) ?? throw new KeyNotFoundException($"No car \"{offer.CarFolder}\".");

        var round = CalendarBuilder.RoundFor(catalog, e, offer.CarClass, career.Rating, minutes);
        round.Guest = true;
        round.GuestCar = new CareerCar(car.CarTypes.FirstOrDefault() ?? "", offer.CarClass, "", offer.TeamName)
        {
            OtherCarTypes = car.CarTypes.Skip(1).ToList(),
        };
        round.GuestNumbers = offer.Numbers;

        // Rounds with no catalog month (custom events) keep their place behind the round before them.
        var month = 0;
        var at = season.Rounds.Count;
        for (var i = 0; i < season.Rounds.Count; i++)
        {
            month = catalog.Event(season.Rounds[i].EventId)?.Month ?? month;
            if (month > e.Month) { at = i; break; }
        }

        season.Rounds.Insert(at, round);
        var moved = season.Rounds.Skip(at + 1).ToDictionary(r => r.Number, r => r.Number + 1);
        foreach (var deal in season.Sponsors.Concat(season.SponsorOffers ?? []).Where(d => moved.ContainsKey(d.RoundNumber)))
            deal.RoundNumber = moved[deal.RoundNumber];
        CalendarBuilder.Number(season.Rounds);

        offer.Accepted = true;
        return round;
    }

    /// <summary>Withdraws from a guest drive before the season starts; its round leaves the calendar.</summary>
    public static void Withdraw(Career career, string offerId)
    {
        var season = career.CurrentSeason;
        if (!Sponsorship.CanSign(season)) throw new InvalidOperationException("The season has started; skip the round instead.");
        var offer = season.GuestOffers?.FirstOrDefault(o => o.Id == offerId && o.Accepted) ?? throw new KeyNotFoundException("You haven't agreed to that guest drive.");

        var index = season.Rounds.FindIndex(r => r.Guest && r.EventId == offer.EventId);
        if (index >= 0)
        {
            if (season.Rounds[index].State == RoundState.Armed) throw new InvalidOperationException("Cancel the race weekend first.");
            if (season.Rounds[index].State == RoundState.Completed) throw new InvalidOperationException("You've already raced that one.");
            var moved = season.Rounds.Skip(index + 1).ToDictionary(r => r.Number, r => r.Number - 1);
            season.Rounds.RemoveAt(index);
            foreach (var deal in season.Sponsors.Concat(season.SponsorOffers ?? []).Where(d => moved.ContainsKey(d.RoundNumber)))
                deal.RoundNumber = moved[deal.RoundNumber];
            CalendarBuilder.Number(season.Rounds);
        }
        offer.Accepted = false;
    }
}
