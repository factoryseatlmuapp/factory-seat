using LmuCareer.Core.Content;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

/// <summary>
/// The off-season: a finished season is reviewed, reputation moves, and teams make offers for the
/// next one. Moving up the ladder (LMGT3, LMP3, LMP2, Hypercar) only ever happens through an offer.
/// </summary>
public static class Progression
{
    /// <summary>The target for a season raced without a contract: top six in the class standings.</summary>
    public const int DefaultTarget = 6;

    /// <summary>A season of this many rounds counts in full; shorter ones count pro rata.</summary>
    public const int FullSeasonRounds = 6;

    /// <summary>Reputation drifts: one season moves it at most this far up or down.</summary>
    public const decimal MaxGain = 25;
    public const decimal MaxLoss = 12;

    /// <summary>Strong LMP3 or LMP2 seasons a Hypercar team wants to see first.</summary>
    public const int PrototypeSeasonsForHypercar = 2;

    public static readonly IReadOnlyList<string> Ladder = ["GT3", "LMP3", "LMP2", "Hyper"];

    /// <summary>Reputation a team in the class looks for before offering a seat.</summary>
    public static decimal Threshold(string carClass) => carClass switch
    {
        "LMP3" => 25,
        "LMP2" => 40,
        "Hyper" => 60,
        _ => 0,
    };

    /// <summary>Front-running teams expect a top-three season, midfield teams top six, the rest top ten.</summary>
    public static int TargetFor(int tier) => tier switch
    {
        1 => 3,
        2 => DefaultTarget,
        _ => 10,
    };

    /// <summary>The best tier of team in a class that will talk to a driver with this reputation.</summary>
    public static int BestTier(decimal reputation, string carClass) => (reputation - Threshold(carClass)) switch
    {
        >= 25 => 1,
        >= 10 => 2,
        _ => 3,
    };

    public static DriverRating RatingFor(decimal reputation) => reputation switch
    {
        >= 70 => DriverRating.Platinum,
        >= 40 => DriverRating.Gold,
        _ => DriverRating.Silver,
    };

    /// <summary>
    /// Reviews the finished current season and makes the offers for the next, once: a season
    /// already reviewed is left as it is, so reopening the off-season never moves reputation twice.
    /// </summary>
    /// <returns>True when this call did the review (the career needs saving).</returns>
    public static bool EndSeason(Career career, ContentCatalog catalog, LmuInstall? install, int? seed = null)
    {
        var season = career.CurrentSeason;
        if (!season.IsFinished) throw new InvalidOperationException("The season isn't over yet.");
        if (season.Review is not null) return false;

        season.Review = Review(season, career.Reputation);
        career.Reputation = season.Review.ReputationAfter;
        career.Rating = RatingFor(career.Reputation);
        career.Offers = MakeOffers(career, catalog, install, new Random(seed ?? Seed(career))).ToList();
        return true;
    }

    /// <summary>What the season was worth: the championship, the team's target, each race's result and the sponsors' objectives.</summary>
    public static SeasonReview Review(Season season, decimal reputationBefore)
    {
        var carClass = season.Car.CarClass;
        var target = season.Contract?.TargetPosition ?? DefaultTarget;
        var completed = season.ChampionshipRounds.Where(r => r.State == RoundState.Completed && r.Result is not null).ToList();
        var weight = Math.Min(1.0, completed.Count / (double)FullSeasonRounds);
        var w = (decimal)weight;

        var table = season.Standings().FirstOrDefault(s => s.CarClass.Equals(carClass, StringComparison.OrdinalIgnoreCase));
        var index = table?.Drivers.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        int? position = index >= 0 ? index + 1 : null;

        var items = new List<ReputationItem>();
        void Add(Phrase label, decimal points)
        {
            points = Math.Round(points, 1);
            if (points != 0) items.Add(ReputationItem.For(label, points));
        }

        if (completed.Count > 0)
        {
            Add(position is int p ? Phrase.Of("Championship P{n}", ("n", p)) : Phrase.Of("Not classified in the championship"), w * (position switch
            {
                1 => 12,
                2 => 9,
                3 => 7,
                <= 5 => 4,
                <= 10 => 1,
                _ => -2,
            }));

            var met = position <= target;
            if (met)
            {
                Add(Phrase.Of("Team target met (top {n})", ("n", target)), 6 * w);
                if (position <= target - 3) Add(Phrase.Of("Beat the target by {n} places", ("n", target - position)), 3 * w);
            }
            else
            {
                var missedBy = position is int q ? q - target : int.MaxValue;
                Add(Phrase.Of("Team target missed (top {n})", ("n", target)), (missedBy > 5 ? -10 : -6) * w);
            }
        }

        // Each race's result. Big events (double points: the 24-hour races) make a name.
        var mine = completed.Select(r => (Round: r, Me: r.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer))).Where(x => x.Me is not null).ToList();
        foreach (var (round, me) in mine.Where(x => x.Me!.ClassRank == 1 && x.Round.PointsWeight >= 2))
            Add(Phrase.Of("Won the {event}", ("event", round.EventName)), 5);
        var wins = mine.Count(x => x.Me!.ClassRank == 1 && x.Round.PointsWeight < 2);
        Add(Phrase.Count(wins, "{n} class win", "{n} class wins"), 2 * wins);
        var podiums = mine.Count(x => x.Me!.ClassRank is 2 or 3);
        Add(Phrase.Count(podiums, "{n} podium", "{n} podiums"), podiums);
        var poles = mine.Count(x => x.Me!.ClassPole);
        Add(Phrase.Count(poles, "{n} pole", "{n} poles"), 0.5m * poles);
        var dnfs = mine.Count(x => x.Me!.Entry.Status == FinishStatus.Dnf);
        Add(Phrase.Count(dnfs, "{n} DNF", "{n} DNFs"), -2 * dnfs);
        var dqs = mine.Count(x => x.Me!.Entry.Status == FinishStatus.Dq);
        Add(Phrase.Count(dqs, "{n} disqualification", "{n} disqualifications"), -4 * dqs);
        var penalties = completed.Sum(r => r.Result!.PlayerPenalties);
        Add(Phrase.Count(penalties, "{n} penalty", "{n} penalties"), -0.5m * penalties);
        if (completed.Count >= 3 && dnfs == 0 && dqs == 0 && penalties <= 1) Add(Phrase.Of("Clean season"), 3);

        // Guest drives sit outside the championship, but a result there gets noticed.
        foreach (var round in season.Rounds.Where(r => r.Guest && r.State == RoundState.Completed && r.Result is not null))
        {
            var me = round.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
            if (me is null) continue;
            var guestEvent = ("event", (object?)round.EventName);
            var team = ("team", (object?)round.GuestCar?.TeamName);
            var hasTeam = round.GuestCar?.TeamName is { Length: > 0 };
            Add(me.ClassRank switch
            {
                1 => hasTeam ? Phrase.Of("Guest win: {event} for {team}", guestEvent, team) : Phrase.Of("Guest win: {event}", guestEvent),
                <= 3 => hasTeam ? Phrase.Of("Guest podium: {event} for {team}", guestEvent, team) : Phrase.Of("Guest podium: {event}", guestEvent),
                not null => Phrase.Of("Guest drive finished: {event}", guestEvent),
                _ => me.Entry.Status == FinishStatus.Dq
                    ? Phrase.Of("Guest drive disqualified: {event}", guestEvent)
                    : Phrase.Of("Guest drive retired: {event}", guestEvent),
            }, me.ClassRank switch
            {
                1 => round.PointsWeight >= 2 ? 6 : 4,
                <= 3 => 2,
                not null => 1,
                _ => me.Entry.Status == FinishStatus.Dq ? -2 : -1,
            });
        }

        // Personal sponsors pay out for a met objective; a missed one costs nothing but is listed.
        foreach (var deal in season.Sponsors)
        {
            var objective = Sponsorship.Describe(deal);
            var sponsor = ("sponsor", (object?)deal.Name);
            if (Sponsorship.Progress(season, deal).Status == ObjectiveStatus.Met)
                Add(Phrase.Of("{sponsor}: {objective}", sponsor).With("objective", objective), deal.Reward);
            else items.Add(ReputationItem.For(Phrase.Of("{sponsor}: missed ({objective})", sponsor).With("objective", objective), 0));
            if (deal.RunningLivery)
            {
                var (custom, counted) = Sponsorship.LiveryRounds(season);
                if (Sponsorship.LiveryRun(season))
                    Add(Phrase.Of("Ran the {sponsor} livery ({custom} of {counted} rounds)", sponsor, ("custom", custom), ("counted", counted)), Sponsorship.LiveryBonus);
                else items.Add(ReputationItem.For(Phrase.Of("{sponsor} livery not seen: your custom car ran {custom} of {counted} rounds",
                    sponsor, ("custom", custom), ("counted", counted)), 0));
            }
        }

        var total = items.Sum(i => i.Points);
        var change = Math.Clamp(total, -MaxLoss, MaxGain);
        if (change != total)
            items.Add(ReputationItem.For(change > 0
                ? Phrase.Of("Reputation grows at most {n} a season", ("n", MaxGain))
                : Phrase.Of("Reputation falls at most {n} a season", ("n", MaxLoss)), change - total));

        return new SeasonReview
        {
            ChampionshipPosition = position,
            TargetPosition = target,
            TargetMet = position <= target,
            ReputationBefore = reputationBefore,
            ReputationAfter = Math.Clamp(reputationBefore + change, 0, 100),
            Items = items,
            SeasonWeight = weight,
        };
    }

    /// <summary>
    /// A big win (an event paying 1.5x points or more: Le Mans, Daytona, Qatar, Sebring, Petit) gets
    /// the player noticed: a better team, or one a class up if they're close to its bar, registers
    /// interest and makes an offer at the season's end. At most two teams a season.
    /// </summary>
    /// <returns>The new interest, or null when the result didn't catch anyone's eye.</returns>
    public static TeamInterest? NoticeResult(Career career, Round round, ContentCatalog catalog, LmuInstall? install)
    {
        const int MaxInterest = 2;
        var me = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
        if (me?.ClassRank != 1 || round.PointsWeight < 1.5m || career.Interest.Count >= MaxInterest) return null;

        var season = career.CurrentSeason;
        var reputation = career.Reputation;
        var currentClass = Ladder.Contains(season.Car.CarClass) ? season.Car.CarClass : "GT3";
        var next = NextClass(currentClass, c => catalog.Cars.Any(car => car.Class == c && CanOffer(car, career, install)));
        // Close enough to the next class's bar, a team there takes a look; otherwise a better team in the class.
        var carClass = next is not null && reputation >= Threshold(next) - 15 ? next : currentClass;
        var tier = Math.Max(1, BestTier(reputation, carClass) - 1);
        var currentTeam = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;

        var random = new Random(Seed(career) ^ round.Number * 104729);
        var pool = catalog.Teams
            .Where(t => t.Class == carClass && t.Tier == tier
                && !(t.Name.Equals(currentTeam, StringComparison.OrdinalIgnoreCase) && t.Class == season.Car.CarClass)
                && !career.Interest.Any(i => i.TeamName == t.Name && i.CarClass == t.Class))
            .Select(t => (Team: t, Car: t.Cars.Count > 0
                ? t.Cars.Select(catalog.Car).OfType<CarInfo>().FirstOrDefault(c => CanOffer(c, career, install))
                : catalog.Cars.FirstOrDefault(c => c.Class == t.Class && CanOffer(c, career, install))))
            .Where(x => x.Car is not null)
            .ToList();
        if (pool.Count == 0) return null;

        var (team, chosen) = pool[random.Next(pool.Count)];
        var interest = new TeamInterest
        {
            TeamName = team.Name,
            CarClass = team.Class,
            CarFolder = chosen!.Folder,
            Tier = team.Tier,
            Numbers = team.Numbers,
        };
        var reason = Phrase.Of("your win at the {event}", ("event", round.EventName));
        interest.Reason = reason.ToString();
        interest.ReasonText = reason;
        career.Interest.Add(interest);
        return interest;
    }

    /// <summary>
    /// Two to five seats for next season, all in cars the player owns: their own team again if the
    /// season went well enough, other teams in the class (better ones as reputation grows), and
    /// seats a step up the ladder once reputation clears the next class's bar.
    /// </summary>
    public static IReadOnlyList<TeamOffer> MakeOffers(Career career, ContentCatalog catalog, LmuInstall? install, Random random)
    {
        var season = career.CurrentSeason;
        var review = season.Review ?? throw new InvalidOperationException("Review the season first.");
        var reputation = career.Reputation;
        var currentClass = season.Car.CarClass;
        var currentCar = catalog.CarByType(season.Car.CarType);
        var currentTeam = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;

        bool Drivable(CarInfo car) => CanOffer(car, career, install);

        CarInfo? CarFor(TeamInfo team)
        {
            if (team.Cars.Count > 0) return team.Cars.Select(catalog.Car).OfType<CarInfo>().FirstOrDefault(Drivable);
            var any = catalog.Cars.Where(c => c.Class == team.Class && Drivable(c)).ToList();
            return any.Count == 0 ? null : any[random.Next(any.Count)];
        }

        var offers = new List<TeamOffer>();
        TeamOffer Offer(OfferKind kind, TeamInfo team, CarInfo car, int target, string number = "") => new()
        {
            Id = $"s{season.Number}-{offers.Count + 1}",
            Kind = kind,
            TeamName = team.Name,
            CarClass = team.Class,
            CarFolder = car.Folder,
            Tier = team.Tier,
            TargetPosition = target,
            Numbers = team.Numbers,
            CarNumber = number,
        };

        bool IsCurrentTeam(TeamInfo team) =>
            team.Name.Equals(currentTeam, StringComparison.OrdinalIgnoreCase) && team.Class == currentClass;

        // Picks a team of the best tier on offer, else the next tier down, never one already offering.
        void AddFrom(OfferKind kind, string carClass, int bestTier, int count)
        {
            for (var n = 0; n < count; n++)
            {
                for (var tier = bestTier; tier <= 3; tier++)
                {
                    var pool = catalog.Teams
                        .Where(t => t.Class == carClass && t.Tier == tier && !IsCurrentTeam(t)
                            && !offers.Any(o => o.TeamName == t.Name && o.CarClass == t.Class))
                        .Select(t => (Team: t, Car: CarFor(t)))
                        .Where(x => x.Car is not null)
                        .ToList();
                    if (pool.Count == 0) continue;

                    var (team, car) = pool[random.Next(pool.Count)];
                    var offer = Offer(kind, team, car!, TargetFor(team.Tier));
                    // Front-running teams and step-up seats often want a longer commitment.
                    if ((team.Tier == 1 || kind == OfferKind.Promotion) && random.Next(2) == 0) offer.Seasons = 2;
                    offers.Add(offer);
                    break;
                }
            }
        }

        // The current team wants the player back unless the season fell well short of its target.
        // A player still under contract stays unless the season went badly enough for the team to
        // let them go; only a step up (or a team that noticed them) can buy them out.
        var underContract = season.Contract?.SeasonsLeft > 1;
        var keeps = underContract
            ? review.ChampionshipPosition <= review.TargetPosition + 4
            : review.TargetMet || review.ChampionshipPosition <= review.TargetPosition + 2;
        if (keeps && currentCar is not null)
        {
            var tier = season.Contract?.Tier ?? 2;
            var team = catalog.Teams.FirstOrDefault(IsCurrentTeam)
                ?? new TeamInfo(currentTeam is { Length: > 0 } ? currentTeam : "Your team", currentClass, [currentCar.Folder], tier, []);
            // After a season well above the target, the team raises its expectations; a contract keeps its terms.
            var target = underContract
                ? season.Contract!.TargetPosition
                : Math.Max(1, Math.Min(TargetFor(tier), (review.ChampionshipPosition ?? TargetFor(tier)) + 2));
            var offer = Offer(OfferKind.ReSign, team with { Tier = tier }, currentCar, target, season.Car.CarNumber);
            offer.UnderContract = underContract;
            offer.Seasons = underContract ? season.Contract!.SeasonsLeft - 1 : review.TargetMet && random.Next(5) < 2 ? 2 : 1;
            offers.Add(offer);
        }

        var next = NextClass(currentClass, c => catalog.Cars.Any(car => car.Class == c && CanOffer(car, career, install)));
        var promoted = next is not null && reputation >= Threshold(next)
            && (next != "Hyper" || StrongPrototypeSeasons(career) >= PrototypeSeasonsForHypercar);

        // Teams that noticed a big result this season make their offer first.
        var ladderIndex = Ladder.ToList().IndexOf(Ladder.Contains(currentClass) ? currentClass : "GT3");
        foreach (var interest in career.Interest)
        {
            if (catalog.Car(interest.CarFolder) is not { } car || !CanOffer(car, career, install)) continue;
            if (offers.Any(o => o.TeamName == interest.TeamName && o.CarClass == interest.CarClass)) continue;
            var team = new TeamInfo(interest.TeamName, interest.CarClass, [car.Folder], interest.Tier, interest.Numbers);
            var offer = Offer(Ladder.ToList().IndexOf(interest.CarClass) > ladderIndex ? OfferKind.Promotion : OfferKind.Move,
                team, car, TargetFor(interest.Tier));
            offer.Reason = interest.Reason;
            offer.ReasonText = interest.ReasonText;
            offers.Add(offer);
        }

        // Under contract, the only other calls are step-up seats.
        var ladderClass = Ladder.Contains(currentClass) ? currentClass : "GT3";
        var moves = underContract && keeps ? 0 : promoted ? 1 : keeps ? 2 : 3;
        AddFrom(OfferKind.Move, ladderClass, BestTier(reputation, ladderClass), Math.Max(0, moves - career.Interest.Count));
        if (promoted) AddFrom(OfferKind.Promotion, next!, BestTier(reputation, next!), 2);

        return offers.Take(5).ToList();
    }

    /// <summary>
    /// Whether a team can offer the player this car: it's installed and in a pack they own. A car
    /// whose pack isn't known yet may not be theirs to drive, so it's never offered.
    /// </summary>
    public static bool CanOffer(CarInfo car, Career career, LmuInstall? install) =>
        car.Pack is not null
        && ContentCatalog.IsOwned(car.Pack, career.OwnedContent)
        && (install is null || install.CarFolders.Contains(car.Folder, StringComparer.OrdinalIgnoreCase));

    /// <summary>The next class up the ladder the player can race, skipping one they own no car for (LMP3 without the European packs).</summary>
    public static string? NextClass(string carClass, Func<string, bool> available)
    {
        var index = Ladder.ToList().IndexOf(carClass);
        return Ladder.Skip(Math.Max(0, index) + 1).FirstOrDefault(available);
    }

    /// <summary>LMP3 and LMP2 seasons, this one included, that met the team's target or ended in the top five.</summary>
    public static int StrongPrototypeSeasons(Career career) =>
        career.PastSeasons.Append(career.CurrentSeason)
            .Count(s => s.Car.CarClass is "LMP3" or "LMP2" && s.Review is { } r && (r.TargetMet || r.ChampionshipPosition <= 5));

    /// <summary>
    /// Signs the offer: the finished season moves to the career's history and the new one starts
    /// with the given calendar. A new team's number and team name come from the first race, as in
    /// the first season; a re-sign keeps the number.
    /// </summary>
    public static Season StartNextSeason(Career career, string offerId, IReadOnlyList<Round> rounds, ContentCatalog catalog)
    {
        var finished = career.CurrentSeason;
        if (finished.Review is null) throw new InvalidOperationException("The season hasn't been reviewed yet.");
        var offer = career.Offers.FirstOrDefault(o => o.Id == offerId) ?? throw new PlayerError("That offer isn't on the table any more.");
        var car = catalog.Car(offer.CarFolder) ?? throw new KeyNotFoundException($"No car \"{offer.CarFolder}\".");
        if (rounds.Count == 0) throw new PlayerError("The season needs at least one round.");

        var reSign = offer.Kind == OfferKind.ReSign;
        var next = new Season
        {
            Number = finished.Number + 1,
            Car = new CareerCar(car.CarTypes.FirstOrDefault() ?? "", offer.CarClass, offer.CarNumber,
                reSign ? finished.Car.TeamName : offer.TeamName)
            {
                OtherCarTypes = car.CarTypes.Skip(1).ToList(),
            },
            Teammate = reSign ? finished.Teammate : "",
            Contract = new Contract
            {
                TeamName = offer.TeamName,
                Tier = offer.Tier,
                TargetPosition = offer.TargetPosition,
                Numbers = offer.Numbers,
                SeasonsLeft = Math.Max(1, offer.Seasons),
            },
            Rounds = CalendarBuilder.Number(rounds),
        };

        career.PastSeasons.Add(finished);
        career.CurrentSeason = next;
        career.Offers = [];
        career.Interest = [];
        return next;
    }

    /// <summary>The same career and season always get the same offers.</summary>
    internal static int Seed(Career career) =>
        BitConverter.ToInt32(career.Id.ToByteArray(), 0) ^ (career.CurrentSeason.Number * 7919);
}
