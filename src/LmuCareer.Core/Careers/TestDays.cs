using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// Test days: a team tries the player out in its car, in a practice session against a lap time.
/// Pre-season, teams a class up the player's road (or from the other road) offer one; mid-season,
/// a team a class up that noticed a big win invites them to one straight after it. A test day is a
/// round outside every championship, run in LMU as a practice session. Impress, and the team makes
/// an offer at the season's end (or a better one, if it was already going to); fall short and
/// nothing is lost.
/// </summary>
public static class TestDays
{
    /// <summary>Practice laps a team wants to see before it judges.</summary>
    public const int MinLaps = 8;

    /// <summary>No pre-season test for a driver below this reputation; two from <see cref="TwoOffersAt"/>.</summary>
    public const decimal MinReputation = 15, TwoOffersAt = 35;

    /// <summary>How close to the fastest AI lap in class impresses: front-runners ask the most.</summary>
    public static double PassShare(int tier) => tier switch
    {
        1 => 0.005,
        2 => 0.010,
        _ => 0.015,
    };

    /// <summary>Makes the season's pre-season test offers, once, while the season hasn't started.</summary>
    /// <returns>True when offers were made (the career needs saving).</returns>
    public static bool EnsureOffers(Career career, ContentCatalog catalog, LmuInstall? install, int? seed = null)
    {
        var season = career.CurrentSeason;
        if (career.ChampionshipOnly || career.GteClassic || season.TestOffers is not null || !Sponsorship.CanSign(season)) return false;
        season.TestOffers = MakeOffers(career, catalog, install, new Random(seed ?? Progression.Seed(career) ^ 0x7E57)).ToList();
        return true;
    }

    /// <summary>
    /// Up to two test days: one with a team a class up the player's road, one with a team from the
    /// other road (a crossover tryout). Teams test drivers they'd consider, so the class has to be
    /// within reach of the player's reputation.
    /// </summary>
    public static IReadOnlyList<TestOffer> MakeOffers(Career career, ContentCatalog catalog, LmuInstall? install, Random random)
    {
        var season = career.CurrentSeason;
        var reputation = career.Reputation;
        var count = reputation >= TwoOffersAt ? 2 : reputation >= MinReputation ? 1 : 0;
        if (count == 0) return [];

        bool Available(string carClass) => catalog.Cars.Any(c => c.Class == carClass && Progression.CanOffer(c, career, install));
        var current = season.Car.CarClass;
        var up = Progression.NextClass(current, Available, career.Path);
        var across = career.Path switch
        {
            CareerPath.Gt => reputation >= Progression.Threshold("LMP2") ? "LMP2" : "LMP3",
            CareerPath.Prototype => "GT3",
            _ => null,
        };
        var classes = new[] { up, across }
            .OfType<string>()
            .Where(c => c != current && Available(c) && reputation >= Progression.Threshold(c) - InReach)
            .Distinct()
            .Take(count)
            .ToList();

        var offers = new List<TestOffer>();
        foreach (var carClass in classes)
        {
            var seat = Seat(career, catalog, install, carClass, random);
            var venue = Venue(career, catalog, install, carClass, random);
            if (seat is not { } s || venue is null) continue;
            offers.Add(new TestOffer
            {
                Id = $"t{season.Number}-{offers.Count + 1}",
                TeamName = s.Team.Name,
                CarClass = carClass,
                CarFolder = s.Car.Folder,
                Tier = s.Team.Tier,
                Numbers = s.Team.Numbers,
                VenueEventId = venue.Id,
            });
        }
        return offers;
    }

    /// <summary>How far below a class's bar a team there will still try a driver out.</summary>
    public const decimal InReach = 15;

    /// <summary>Takes a pre-season test: it goes in the calendar ahead of the season's first round.</summary>
    public static Round Accept(Career career, string offerId, ContentCatalog catalog)
    {
        var season = career.CurrentSeason;
        if (!Sponsorship.CanSign(season)) throw new PlayerError("Test days are agreed before the season's first round counts.");
        var offer = season.TestOffers?.FirstOrDefault(o => o.Id == offerId) ?? throw new KeyNotFoundException("That test day isn't on offer.");
        if (offer.Accepted) throw new PlayerError("You've already agreed to that one.");
        var venue = catalog.Event(offer.VenueEventId) ?? throw new KeyNotFoundException($"No event \"{offer.VenueEventId}\".");
        var car = catalog.Car(offer.CarFolder) ?? throw new KeyNotFoundException($"No car \"{offer.CarFolder}\".");

        var round = NewRound(catalog, venue, car, offer.TeamName, offer.CarClass, offer.Tier, offer.Numbers, career.Rating, null);
        var first = season.Rounds.FindIndex(r => r.State != RoundState.Completed);
        Insert(season, round, first < 0 ? season.Rounds.Count : first);
        offer.Accepted = true;
        return round;
    }

    /// <summary>Withdraws from a pre-season test before the season starts; its round leaves the calendar.</summary>
    public static void Withdraw(Career career, string offerId)
    {
        var season = career.CurrentSeason;
        if (!Sponsorship.CanSign(season)) throw new PlayerError("The season has started; skip the round instead.");
        var offer = season.TestOffers?.FirstOrDefault(o => o.Id == offerId && o.Accepted) ?? throw new KeyNotFoundException("You haven't agreed to that test day.");
        var index = season.Rounds.FindIndex(r => r.Test is { } t && t.TeamName == offer.TeamName && t.CarClass == offer.CarClass && r.State != RoundState.Completed);
        if (index >= 0)
        {
            if (season.Rounds[index].State == RoundState.Armed) throw new PlayerError("Cancel the race weekend first.");
            Remove(season, index);
        }
        offer.Accepted = false;
    }

    /// <summary>
    /// A team a class up that noticed a big win wants a look straight away: a test day goes in the
    /// calendar right after the round. Null when there's nowhere the player can test.
    /// </summary>
    public static Round? Invite(Career career, TeamInterest interest, Round after, ContentCatalog catalog, LmuInstall? install)
    {
        var season = career.CurrentSeason;
        var car = catalog.Car(interest.CarFolder);
        var venue = Venue(career, catalog, install, interest.CarClass, new Random(Progression.Seed(career) ^ after.Number * 7907));
        if (car is null || venue is null) return null;

        var round = NewRound(catalog, venue, car, interest.TeamName, interest.CarClass, interest.Tier, interest.Numbers, career.Rating, interest.ReasonText);
        Insert(season, round, season.Rounds.IndexOf(after) + 1);
        return round;
    }

    /// <summary>Whether the armed test day has the laps, and an AI lap in class, for the team to judge.</summary>
    public static bool IsReady(Round round) =>
        round.Test is { } test && round.Practice is { PlayerBest: not null, AiBest: not null } practice && practice.Laps >= test.MinLaps;

    /// <summary>Judges the armed test day from its practice: a team that's impressed makes an offer at the season's end.</summary>
    public static TestResult Complete(Career career, DateTimeOffset now)
    {
        var season = career.CurrentSeason;
        var round = season.ArmedRound ?? throw new InvalidOperationException("No round is armed.");
        var test = round.Test ?? throw new InvalidOperationException("The armed round isn't a test day.");
        if (!IsReady(round)) throw new PlayerError("The team wants at least {n} laps in their car first.", ("n", test.MinLaps));

        var practice = round.Practice!;
        var result = new TestResult(practice.Laps, practice.PlayerBest!.Value, practice.AiBest!.Value,
            practice.PlayerBest.Value <= practice.AiBest.Value * (1 + test.PassShare));
        test.Result = result;
        round.State = RoundState.Completed;

        if (result.Passed)
        {
            var interest = career.Interest.FirstOrDefault(i => i.TeamName == test.TeamName && i.CarClass == test.CarClass);
            if (interest is null)
            {
                var reason = Phrase.Of("your test day at {track}", ("track", round.TrackCourse));
                career.Interest.Add(interest = new TeamInterest
                {
                    TeamName = test.TeamName,
                    CarClass = test.CarClass,
                    CarFolder = round.GuestCar?.Folder ?? "",
                    Tier = test.Tier,
                    Numbers = round.GuestNumbers,
                    Reason = reason.ToString(),
                    ReasonText = reason,
                });
            }
            interest.Tested = true;
        }

        var gap = ("gap", (object?)Math.Abs(result.Gap).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        Inbox.Deliver(career, new InboxMessage
        {
            Id = InboxMessage.IdFor(season.Number, round.Number, MessageKind.TeamPrincipal),
            Kind = MessageKind.TeamPrincipal,
            Season = season.Number,
            Round = round.Number,
            At = now,
            From = Phrase.Of("Team principal, {team}", ("team", test.TeamName)),
            Subject = Phrase.Of("After the test at {track}", ("track", round.TrackCourse)),
            Body =
            [
                result.Gap <= 0
                    ? Phrase.Of("{gap} seconds quicker than anyone in class. We noticed.", gap)
                    : Phrase.Of("{gap} seconds off the fastest lap in class.", gap),
                result.Passed
                    ? Phrase.Of("We liked what we saw. Expect a call from us when the season's over.")
                    : Phrase.Of("Not quite what we were after this time. Keep at it."),
            ],
        });
        career.LastPlayedAt = now;
        return result;
    }

    // ---------- Building the round ----------

    private static Round NewRound(ContentCatalog catalog, EventInfo venue, CarInfo car, string teamName, string carClass, int tier,
        IReadOnlyList<string> numbers, DriverRating rating, Phrase? reason)
    {
        var round = CalendarBuilder.RoundFor(catalog, venue, carClass, rating);
        var track = catalog.Track(venue.Folder);
        round.EventId = "";
        round.EventName = $"{track?.Name ?? venue.Folder} test";
        round.PointsWeight = 0;
        round.MinimumDriveShare = 0;
        round.Guest = true;
        round.GuestCar = new CareerCar(car.CarTypes.FirstOrDefault() ?? "", carClass, "", teamName)
        {
            Folder = car.Folder,
            OtherCarTypes = car.CarTypes.Skip(1).ToList(),
        };
        round.GuestNumbers = numbers;
        round.Test = new TestDay
        {
            TeamName = teamName,
            CarClass = carClass,
            Tier = tier,
            PassShare = PassShare(tier),
            Reason = reason?.ToString() ?? "",
            ReasonText = reason,
        };
        return round;
    }

    /// <summary>A team in the class with a car the player can drive: the best tier that would look at them.</summary>
    private static (TeamInfo Team, CarInfo Car)? Seat(Career career, ContentCatalog catalog, LmuInstall? install, string carClass, Random random)
    {
        var tier = Progression.BestTier(career.Reputation, carClass);
        var pool = catalog.Teams
            .Where(t => t.Class == carClass && t.Tier >= tier)
            .Select(t => (Team: t, Car: (t.Cars.Count > 0 ? t.Cars.Select(catalog.Car).OfType<CarInfo>() : catalog.Cars.Where(c => c.Class == t.Class))
                .FirstOrDefault(c => Progression.CanOffer(c, career, install))))
            .Where(x => x.Car is not null)
            .OrderBy(x => x.Team.Tier)
            .ToList();
        if (pool.Count == 0) return null;
        var best = pool.Where(x => x.Team.Tier == pool[0].Team.Tier).ToList();
        var (team, car) = best[random.Next(best.Count)];
        return (team, car!);
    }

    /// <summary>A track the class races at that the player owns: where the team would test.</summary>
    private static EventInfo? Venue(Career career, ContentCatalog catalog, LmuInstall? install, string carClass, Random random)
    {
        var european = carClass is "LMP2" or "LMP3";
        var series = european ? new[] { "european", "european-base" } : ["world", "world-base"];
        var venues = catalog.Events
            .Where(e => series.Contains(e.Series)
                && catalog.Track(e.Folder) is { } track && ContentCatalog.IsOwned(track.Pack, career.OwnedContent)
                && (install is null || install.HasLayout(e.Folder, e.Layout)))
            .ToList();
        return venues.Count == 0 ? null : venues[random.Next(venues.Count)];
    }

    /// <summary>Puts a round in the calendar at an index; sponsor deals pinned to later rounds follow them.</summary>
    internal static void Insert(Season season, Round round, int at)
    {
        season.Rounds.Insert(at, round);
        var moved = season.Rounds.Skip(at + 1).ToDictionary(r => r.Number, r => r.Number + 1);
        foreach (var deal in season.Sponsors.Concat(season.SponsorOffers ?? []).Where(d => moved.ContainsKey(d.RoundNumber)))
            deal.RoundNumber = moved[deal.RoundNumber];
        CalendarBuilder.Number(season.Rounds);
    }

    private static void Remove(Season season, int index)
    {
        var moved = season.Rounds.Skip(index + 1).ToDictionary(r => r.Number, r => r.Number - 1);
        season.Rounds.RemoveAt(index);
        foreach (var deal in season.Sponsors.Concat(season.SponsorOffers ?? []).Where(d => moved.ContainsKey(d.RoundNumber)))
            deal.RoundNumber = moved[deal.RoundNumber];
        CalendarBuilder.Number(season.Rounds);
    }
}
