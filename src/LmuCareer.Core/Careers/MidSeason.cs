using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// 1.5 mid-season offers: after round 2, a team a tier or a class above the player's can offer next
/// season's seat when the player's championship position is at or better than that team's target,
/// or after a double-points win. Each offer has an answer-by round; unanswered, it lapses with an
/// Inbox line. Signing early is announced at once, but the player finishes the season with their
/// team and costs nothing in reputation. Under a contract that runs past this season, only the
/// Release clause perk lets teams call.
/// </summary>
public static class MidSeason
{
    /// <summary>At most this many mid-season offers a season.</summary>
    public const int MaxOffers = 2;

    /// <summary>Offers start once this many of the season's own rounds have counted (form to judge).</summary>
    public const int FromRound = 2;

    /// <summary>On form alone, the chance a round brings a call; a double-points win always does.</summary>
    public const double FormChance = 0.5;

    /// <summary>A team a class up looks once reputation is this close to the class's bar.</summary>
    public const decimal ClassUpWithin = 15;

    /// <summary>Whether this season runs mid-season offers: 1.5's rules, not championship-only.</summary>
    public static bool Runs(Career career) =>
        !career.ChampionshipOnly && career.RetiredAt is null && career.CurrentSeason.ReputationRules >= Progression.CurrentReputationRules;

    /// <summary>The deal the player signed early this season, if any.</summary>
    public static TeamOffer? SignedEarly(Career career) => career.EarlyOffers.FirstOrDefault(o => o.Early == EarlyOfferStatus.Signed);

    /// <summary>An offer the player can still sign or haggle over.</summary>
    public static bool IsOpen(TeamOffer offer) => offer.Early == EarlyOfferStatus.Open && !offer.WalkedAway;

    /// <summary>The player's place in the season's own class standings now; null before anything counted.</summary>
    public static int? Position(Season season)
    {
        var table = season.Standings().FirstOrDefault(s => s.CarClass.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase));
        var index = table?.Drivers.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        return index >= 0 ? index + 1 : null;
    }

    /// <summary>The round's place among the season's own rounds (1st, 2nd...): stays put when test days or guest drives are added.</summary>
    private static int Ordinal(Season season, Round round) => season.ChampionshipRounds.ToList().IndexOf(round) + 1;

    /// <summary>
    /// After a championship round counted: maybe a team makes an offer, through the inbox.
    /// </summary>
    /// <returns>The offer's message, already in the inbox; null when no one called.</returns>
    public static InboxMessage? AfterRace(Career career, Round round, ContentCatalog catalog, LmuInstall? install, DateTimeOffset now, Random? random = null)
    {
        var season = career.CurrentSeason;
        if (!Runs(career) || round.Guest || round.Test is not null || round.SeriesId.Length > 0 || season.Review is not null) return null;
        if (SignedEarly(career) is not null || career.EarlyOffers.Count >= MaxOffers) return null;
        if (season.Contract?.LetGoAt is not null) return null;
        // Under a deal that runs past this season, the team holds the player unless they negotiated a way out.
        if (season.Contract?.SeasonsLeft > 1 && !Negotiation.Holds(season, Perk.ReleaseClause)) return null;

        var own = season.ChampionshipRounds.ToList();
        var counted = own.Count(r => r.State == RoundState.Completed && r.Result is not null);
        var left = own.Where(r => Ordinal(season, r) > Ordinal(season, round) && r.State is RoundState.Upcoming or RoundState.Armed).ToList();
        if (counted < FromRound || left.Count == 0) return null;

        var me = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
        if (me is null) return null;
        var position = Position(season);
        var bigResult = me.ClassRank == 1 && round.PointsWeight >= 2;
        random ??= new Random(Progression.Seed(career) ^ Negotiation.Hash($"mid-{Ordinal(season, round)}"));

        // Who might call: a tier up in the class, or a team a class up once the bar is close.
        var currentClass = career.GteClassic ? "GTE" : Progression.Ladder.Contains(season.Car.CarClass) ? season.Car.CarClass : "GT3";
        var currentTier = season.Contract?.Tier ?? 2;
        var candidates = new List<(string CarClass, int Tier)>();
        if (currentTier > 1) candidates.Add((season.Car.CarClass, currentTier - 1));
        var next = career.GteClassic ? null
            : Progression.NextClass(currentClass, c => catalog.Cars.Any(car => car.Class == c && Progression.CanOffer(car, career, install)), career.Path);
        if (next is not null && career.Reputation >= Progression.Threshold(next) - ClassUpWithin)
            candidates.Add((next, Progression.BestTier(career.Reputation, next)));
        candidates = candidates.Where(c => bigResult || position <= Progression.TargetFor(c.Tier)).ToList();
        if (candidates.Count == 0) return null;
        if (!bigResult && random.NextDouble() >= FormChance) return null;

        var currentTeam = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;
        foreach (var (carClass, tier) in candidates.OrderBy(_ => random.Next()))
        {
            var pool = catalog.Teams
                .Where(t => t.Class == carClass && t.Tier == tier
                    && !(t.Name.Equals(currentTeam, StringComparison.OrdinalIgnoreCase) && t.Class == season.Car.CarClass)
                    && !career.EarlyOffers.Any(o => o.TeamName == t.Name && o.CarClass == t.Class))
                .Select(t => (Team: t, Car: (t.Cars.Count > 0 ? t.Cars.Select(catalog.Car) : catalog.Cars.Where(c => c.Class == t.Class))
                    .OfType<CarInfo>().FirstOrDefault(c => Progression.CanOffer(c, career, install))))
                .Where(x => x.Car is not null)
                .ToList();
            if (pool.Count == 0) continue;

            var (team, car) = pool[random.Next(pool.Count)];
            var deadline = left[Math.Min(1, left.Count - 1)];
            var promotion = carClass != season.Car.CarClass;
            var offer = new TeamOffer
            {
                Id = $"m{season.Number}-{Ordinal(season, round)}",
                Kind = promotion ? OfferKind.Promotion : OfferKind.Move,
                TeamName = team.Name,
                CarClass = team.Class,
                CarFolder = car!.Folder,
                Tier = team.Tier,
                TargetPosition = Progression.TargetFor(team.Tier),
                Numbers = team.Numbers,
                Seasons = (team.Tier == 1 || promotion) && random.Next(2) == 0 ? 2 : 1,
                Early = EarlyOfferStatus.Open,
                AnswerBy = Ordinal(season, deadline),
                AnswerByEvent = deadline.EventName,
            };
            var reason = bigResult
                ? Phrase.Of("your win at the {event}", ("event", round.EventName))
                : Phrase.Of("your form: P{n} in the championship", ("n", position));
            offer.Reason = reason.ToString();
            offer.ReasonText = reason;
            Negotiation.Open(offer, Negotiation.Leverage(offer.TargetPosition, position), random);
            Teams.AddImsa(career, [offer], catalog, install, random);
            career.EarlyOffers.Add(offer);

            var name = ("team", (object?)team.Name);
            var message = new InboxMessage
            {
                Id = InboxMessage.IdFor(season.Number, round.Number, MessageKind.TeamOffer),
                Kind = MessageKind.TeamOffer,
                Season = season.Number,
                Round = round.Number,
                At = now,
                From = Phrase.Of("Your manager"),
                Subject = Phrase.Of("{team} wants you next season", name),
                Body =
                [
                    Phrase.Of("After the {event}, {team} is offering you a seat for next season. What caught their eye: {reason}.",
                        ("event", round.EventName), name).With("reason", reason),
                    Phrase.Of("They want an answer before the {event} counts.", ("event", deadline.EventName)),
                    currentTeam is { Length: > 0 }
                        ? Phrase.Of("Sign early and it's announced now: you finish this season with {current}, and the new deal starts next season. Turning it down costs nothing.",
                            ("current", currentTeam))
                        : Phrase.Of("Sign early and it's announced now: you finish this season with your team, and the new deal starts next season. Turning it down costs nothing."),
                ],
                OfferId = offer.Id,
            };
            Inbox.Deliver(career, message);
            return message;
        }
        return null;
    }

    private static TeamOffer Open(Career career, string offerId)
    {
        var offer = career.EarlyOffers.FirstOrDefault(o => o.Id == offerId) ?? throw new PlayerError("That offer isn't on the table any more.");
        if (career.CurrentSeason.Review is not null || offer.Early != EarlyOfferStatus.Open) throw new PlayerError("That offer isn't open any more.");
        if (offer.WalkedAway) throw new PlayerError("{team} walked away: that seat is gone.", ("team", offer.TeamName));
        return offer;
    }

    /// <summary>
    /// Signs a mid-season offer: announced now, the new deal starts next season. The other open
    /// offers are turned down. Costs no reputation; the team principal says goodbye in the inbox.
    /// </summary>
    public static void Sign(Career career, string offerId, DateTimeOffset now)
    {
        var offer = Open(career, offerId);
        offer.Early = EarlyOfferStatus.Signed;
        offer.Budget = null;
        foreach (var other in career.EarlyOffers.Where(o => o != offer && o.Early == EarlyOfferStatus.Open)) other.Early = EarlyOfferStatus.Declined;
        foreach (var m in career.Inbox.Where(m => m.OfferId == offerId)) m.Read = true;

        var season = career.CurrentSeason;
        var last = season.Rounds.LastOrDefault(r => r.State == RoundState.Completed);
        var current = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;
        var team = ("team", (object?)offer.TeamName);
        Inbox.Deliver(career, new InboxMessage
        {
            Id = InboxMessage.IdFor(season.Number, last?.Number ?? 0, MessageKind.SignedEarly),
            Kind = MessageKind.SignedEarly,
            Season = season.Number,
            Round = last?.Number ?? 0,
            At = now,
            Read = false,
            From = current is { Length: > 0 } ? Phrase.Of("Team principal, {team}", ("team", current)) : Phrase.Of("Your team principal"),
            Subject = Phrase.Of("You're off to {team} next season", team),
            Body =
            [
                Phrase.Of("We saw the announcement. We'll miss you, but that's racing."),
                Phrase.Of("You're ours until the last round. Let's finish strong."),
            ],
        });
    }

    /// <summary>Turns a mid-season offer down. Costs nothing.</summary>
    public static void Decline(Career career, string offerId)
    {
        var offer = Open(career, offerId);
        offer.Early = EarlyOfferStatus.Declined;
        foreach (var m in career.Inbox.Where(m => m.OfferId == offerId)) m.Read = true;
    }

    public const string LapsedLine = "{team} moved on and signed someone else."; // phrase

    /// <summary>A championship round counted: offers whose answer-by round it was lapse, with a line from the manager.</summary>
    public static void LapseOffers(Career career, Round round, DateTimeOffset now)
    {
        var season = career.CurrentSeason;
        if (round.Guest || round.Test is not null || round.SeriesId.Length > 0) return;
        var ordinal = Ordinal(season, round);
        var lapsed = career.EarlyOffers.Where(o => o.Early == EarlyOfferStatus.Open && o.AnswerBy <= ordinal).ToList();
        if (lapsed.Count == 0) return;
        foreach (var offer in lapsed) offer.Early = EarlyOfferStatus.Lapsed;
        // Only offers still worth a word: a team that walked away already said goodbye.
        var told = lapsed.Where(o => !o.WalkedAway).ToList();
        if (told.Count == 0) return;
        Inbox.Deliver(career, new InboxMessage
        {
            Id = InboxMessage.IdFor(season.Number, round.Number, MessageKind.OfferLapsed),
            Kind = MessageKind.OfferLapsed,
            Season = season.Number,
            Round = round.Number,
            At = now,
            From = Phrase.Of("Your manager"),
            Subject = told.Count == 1 ? Phrase.Of("{team} moved on", ("team", told[0].TeamName)) : Phrase.Of("Two offers lapsed"),
            Body = [.. told.Select(o => Phrase.Of(LapsedLine, ("team", o.TeamName)))],
        });
    }

    /// <summary>The season's over: anything still open lapses quietly (the last round already said so).</summary>
    public static void CloseSeason(Career career)
    {
        foreach (var offer in career.EarlyOffers.Where(o => o.Early == EarlyOfferStatus.Open)) offer.Early = EarlyOfferStatus.Lapsed;
    }
}
