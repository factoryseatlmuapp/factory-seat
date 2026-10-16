using LmuCareer.Core.Content;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

public enum ObjectiveStatus { InProgress, Met, Failed }

public enum ApproachStatus { Open, Signed, Declined, Lapsed }

/// <summary>A sponsor's offer mid-season, in an inbox message: signed or declined there, gone when the next race counts.</summary>
public sealed class SponsorApproach
{
    public SponsorDeal Deal { get; set; } = new();
    public ApproachStatus Status { get; set; }
}

/// <summary>Where a deal's objective stands: <see cref="Done"/> of <see cref="Needed"/>, where counting makes sense.</summary>
public sealed record ObjectiveProgress(ObjectiveStatus Status, int Done, int Needed);

/// <summary>
/// Personal sponsors: at the start of each season a few offer a deal with one objective, and the
/// player signs up to two before the first round counts. A met objective pays reputation at the
/// season's review. Brands from the car's home region (livery ideas) call more often, and much
/// more often once the player has built their livery.
/// </summary>
public static class Sponsorship
{
    public const int MaxDeals = 2;

    /// <summary>The most offers a season starts with (at 60 reputation).</summary>
    public const int OffersMade = 3;

    /// <summary>A season that starts on 1.2's sponsor rules gets this in <see cref="Season.SponsorRules"/>.</summary>
    public const int CurrentRules = 2;

    /// <summary>Below this reputation a driver is a rookie: one small local deal, nothing bigger.</summary>
    public const decimal RookieReputation = 25;

    /// <summary>What a local brand's deal pays.</summary>
    public const decimal LocalReward = 2;

    /// <summary>A country needs this many local brands of its own; with fewer, its region's brands call too.</summary>
    public const int EnoughLocals = 3;

    /// <summary>Running the sponsor's livery is optional; doing it is worth this much on top.</summary>
    public const decimal LiveryBonus = 1;

    /// <summary>Deals are signed (and dropped) before the season's first championship round counts; a guest drive doesn't close the window.</summary>
    public static bool CanSign(Season season) =>
        season.Rounds.Count > 0 && !season.ChampionshipRounds.Any(r => r.State == RoundState.Completed);

    /// <summary>Makes the season's offers, once, while deals can still be signed.</summary>
    /// <param name="ownSponsors">Brands the player added themselves (their own liveries), alongside the catalog's.</param>
    /// <returns>True when offers were made (the career needs saving).</returns>
    /// <param name="language">The app's language: where local brands come from when the driver has no nationality set.</param>
    public static bool EnsureOffers(Career career, ContentCatalog catalog, IReadOnlyCollection<string> builtLiveries,
        IReadOnlyList<SponsorInfo>? ownSponsors = null, int? seed = null, string language = "en")
    {
        var season = career.CurrentSeason;
        if (career.ChampionshipOnly || season.SponsorOffers is not null || !CanSign(season)) return false;
        season.SponsorOffers = MakeOffers(career, catalog, builtLiveries, ownSponsors ?? [],
            new Random(seed ?? Progression.Seed(career) ^ 0x5B0B), language).ToList();
        season.SponsorRules = CurrentRules;
        return true;
    }

    /// <summary>
    /// How many sponsors call before a season: one for a rookie (a small local deal) and up to 25
    /// reputation, two from 40, three from 60. A team at the back of the grid draws one fewer, but
    /// never none.
    /// </summary>
    public static int OffersFor(Career career)
    {
        var count = career.Reputation >= 60 ? 3 : career.Reputation >= 40 ? 2 : 1;
        if (career.CurrentSeason.Contract?.Tier == 3) count--;
        return Math.Max(1, count);
    }

    public static IReadOnlyList<SponsorDeal> MakeOffers(Career career, ContentCatalog catalog, IReadOnlyCollection<string> builtLiveries,
        IReadOnlyList<SponsorInfo> ownSponsors, Random random, string language = "en")
    {
        var pool = Pool(career, catalog, builtLiveries, ownSponsors, language);
        var objectives = Objectives(career).OrderBy(_ => random.Next()).ToList();
        var local = LocalObjectives(career.CurrentSeason, after: 0).OrderBy(_ => random.Next()).ToList();
        var offers = new List<SponsorDeal>();
        while (offers.Count < OffersFor(career) && pool.Count > 0)
        {
            var sponsor = Pick(pool, random);
            var from = sponsor.Kind == "local" ? local : objectives;
            if (from.Count == 0) continue;
            // Two deals with the same objective would pay twice for one result.
            var deal = from.FirstOrDefault(d => offers.All(o => o.Objective != d.Objective)) ?? from[0];
            from.Remove(deal);
            offers.Add(Fill(deal, sponsor));
        }
        return offers;
    }

    /// <summary>
    /// The sponsors who might call, weighted. A rookie hears only from local brands. Past that, real
    /// backers call anyone with the reputation they look for; a mashup calls when it suits the car
    /// (any car in a spec class), and keenly once its livery exists; local brands keep calling.
    /// </summary>
    private static List<(SponsorInfo Sponsor, int Weight)> Pool(Career career, ContentCatalog catalog,
        IReadOnlyCollection<string> builtLiveries, IReadOnlyList<SponsorInfo> ownSponsors, string language)
    {
        var locals = LocalPool(catalog, career.Nationality, language);
        if (career.Reputation < RookieReputation) return locals.Select(s => (s, 2)).ToList();

        var season = career.CurrentSeason;
        var carFolder = catalog.CarByType(season.Car.CarType)?.Folder;
        var spec = season.Car.CarClass is "LMP2" or "LMP3";
        int Weight(SponsorInfo s)
        {
            // Past a rookie, local brands are the small tier: they still call, less often than the big names.
            if (s.Kind == "local") return 1;
            if (s.Kind != "mashup") return 2;
            // A brand of the player's own with no cars picked suits any car.
            var suits = s.Cars.Count == 0 || s.Cars.Contains(carFolder ?? "", StringComparer.OrdinalIgnoreCase);
            var built = builtLiveries.Contains(s.Id, StringComparer.OrdinalIgnoreCase);
            if (!suits && !spec) return 0;
            return (built ? 12 : 0) + (suits ? 4 : 1);
        }

        return catalog.Sponsors.Where(s => s.Kind != "local").Concat(ownSponsors).Concat(locals)
            .Where(s => career.Reputation >= s.MinReputation)
            .Select(s => (Sponsor: s, Weight: Weight(s)))
            .Where(x => x.Weight > 0)
            .ToList();
    }

    private static SponsorInfo Pick(List<(SponsorInfo Sponsor, int Weight)> pool, Random random)
    {
        var pick = random.Next(pool.Sum(x => x.Weight));
        var index = 0;
        while (pick >= pool[index].Weight) pick -= pool[index++].Weight;
        var sponsor = pool[index].Sponsor;
        pool.RemoveAt(index);
        return sponsor;
    }

    private static SponsorDeal Fill(SponsorDeal deal, SponsorInfo sponsor)
    {
        deal.SponsorId = sponsor.Id;
        deal.Name = sponsor.Name;
        deal.Local = sponsor.Kind == "local";
        deal.LocalName = sponsor.LocalName;
        return deal;
    }

    // Where local brands come from when the driver's own country has too few (design doc, Oct 13).
    private static readonly Dictionary<string, string[]> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = ["GB", "IE"],
        ["IE"] = ["GB", "IE"],
        ["AT"] = ["DE"],
        ["CH"] = ["DE"],
        ["PT"] = ["BR"],
    };

    private static readonly Dictionary<string, string[]> LanguageCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["DE"],
        ["fr"] = ["FR"],
        ["es"] = ["ES"],
        ["pt"] = ["BR"],
        ["ko"] = ["KR"],
        ["en"] = ["US", "GB", "IE"],
    };

    /// <summary>
    /// The local brands that back this driver: from their country, or if it has too few, its region,
    /// then the app's language, then English-speaking brands.
    /// </summary>
    public static IReadOnlyList<SponsorInfo> LocalPool(ContentCatalog catalog, string? nationality, string language)
    {
        var locals = catalog.Sponsors.Where(s => s.Kind == "local").ToList();
        List<SponsorInfo> From(string[] countries) => locals.Where(s => countries.Contains(s.Country, StringComparer.OrdinalIgnoreCase)).ToList();

        var steps = new List<string[]>();
        if (nationality is { Length: > 0 } country)
        {
            steps.Add([country]);
            if (Regions.TryGetValue(country, out var region)) steps.Add(region);
        }
        if (LanguageCountries.TryGetValue(language.Split('-')[0], out var spoken)) steps.Add(spoken);
        steps.Add(LanguageCountries["en"]);

        foreach (var step in steps)
        {
            var pool = From(step);
            if (pool.Count >= EnoughLocals) return pool;
        }
        return From(LanguageCountries["en"]);
    }

    /// <summary>A local brand's simple objectives, for the rounds after <paramref name="after"/>: finish them all, a top-10 season, keep it clean.</summary>
    private static IEnumerable<SponsorDeal> LocalObjectives(Season season, int after)
    {
        var rounds = season.ChampionshipRounds.Count(r => r.Number > after && r.State is RoundState.Upcoming or RoundState.Armed);
        if (rounds >= (after == 0 ? 2 : 1))
            yield return new() { Objective = ObjectiveKind.FinishEveryRound, Count = rounds, Reward = LocalReward, OverRounds = after == 0 ? 0 : rounds };
        if (after == 0) yield return new() { Objective = ObjectiveKind.ChampionshipTop, Count = 10, Reward = LocalReward };
        if (rounds >= (after == 0 ? 3 : 2)) yield return new() { Objective = ObjectiveKind.CleanSeason, Reward = LocalReward, OverRounds = after == 0 ? 0 : rounds };
    }

    /// <summary>One deal of each kind that fits the season, pitched at what the player's team expects.</summary>
    private static IEnumerable<SponsorDeal> Objectives(Career career)
    {
        var season = career.CurrentSeason;
        var rounds = season.ChampionshipRounds.Count();
        var target = season.Contract?.TargetPosition ?? Progression.DefaultTarget;
        int Share(double share) => Math.Clamp((int)Math.Round(rounds * share), 1, rounds);

        if (rounds >= 2) yield return new() { Objective = ObjectiveKind.FinishEveryRound, Count = rounds, Reward = rounds >= 6 ? 4 : 3 };

        var podiums = Share(target <= 3 ? 0.5 : target <= 6 ? 0.3 : 0.15);
        yield return new() { Objective = ObjectiveKind.Podiums, Count = podiums, Reward = podiums >= 3 ? 4 : 3 };

        if (target <= 6) yield return new() { Objective = ObjectiveKind.Wins, Count = Share(target <= 3 ? 0.3 : 0.15), Reward = 4 };

        yield return new() { Objective = ObjectiveKind.Poles, Count = Share(0.25), Reward = 2 };

        if (rounds >= 2 && season.ChampionshipRounds.MaxBy(r => r.PointsWeight) is { } big)
            yield return new() { Objective = ObjectiveKind.PodiumAt, Count = 1, RoundNumber = big.Number, EventName = big.EventName, Reward = big.PointsWeight >= 2 ? 4 : 3 };

        var top = Math.Max(1, target - 2);
        yield return new() { Objective = ObjectiveKind.ChampionshipTop, Count = top, Reward = top == 1 ? 5 : 4 };

        if (rounds >= 3) yield return new() { Objective = ObjectiveKind.CleanSeason, Reward = 2 };

        if (Rival(career) is { } rival) yield return new() { Objective = ObjectiveKind.BeatRival, Rival = rival, Reward = 3 };
    }

    /// <summary>
    /// The AI driver who finished just ahead of the player last season (just behind, for a champion),
    /// when the player stays in the same class.
    /// </summary>
    private static string? Rival(Career career)
    {
        var last = career.PastSeasons.LastOrDefault();
        if (last is null || last.Car.CarClass != career.CurrentSeason.Car.CarClass) return null;

        var drivers = last.Standings().FirstOrDefault(s => s.CarClass == last.Car.CarClass)?.Drivers;
        var me = drivers?.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        if (drivers is null || me < 0) return null;

        var rival = me > 0 ? drivers[me - 1] : drivers.ElementAtOrDefault(1);
        return rival?.Name;
    }

    public static void Sign(Season season, string sponsorId)
    {
        if (!CanSign(season)) throw new PlayerError("Sponsors sign before the season's first round counts.");
        var offer = season.SponsorOffers?.FirstOrDefault(o => o.SponsorId == sponsorId) ?? throw new KeyNotFoundException("That deal isn't on offer.");
        if (season.Sponsors.Any(s => s.SponsorId == sponsorId)) return;
        if (season.Sponsors.Count >= MaxDeals) throw new PlayerError("You can carry {n} personal sponsors. Drop one first.", ("n", MaxDeals));
        season.Sponsors.Add(offer);
    }

    public static void Drop(Season season, string sponsorId)
    {
        if (!CanSign(season)) throw new PlayerError("The season has started; the deal runs to the end of it.");
        season.Sponsors.RemoveAll(s => s.SponsorId == sponsorId);
    }

    public static void SetLivery(Season season, string sponsorId, bool running)
    {
        var deal = season.Sponsors.FirstOrDefault(s => s.SponsorId == sponsorId) ?? throw new KeyNotFoundException("No such deal this season.");
        deal.RunningLivery = running;
    }

    /// <summary>
    /// Championship rounds counted so far, and how many of them the player raced in their custom
    /// team car: the livery bonus needs the car the livery is painted on for at least half of them.
    /// The paint itself isn't in the results, so which livery is on it stays the player's word.
    /// </summary>
    public static (int Custom, int Counted) LiveryRounds(Season season)
    {
        var players = season.ChampionshipRounds
            .Where(r => r.State == RoundState.Completed)
            .Select(r => r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.Entry)
            .OfType<EntryResult>()
            .ToList();
        return (players.Count(p => p.IsCustomTeam), players.Count);
    }

    public static bool LiveryRun(Season season)
    {
        var (custom, counted) = LiveryRounds(season);
        return counted > 0 && custom * 2 >= counted;
    }

    /// <summary>The objective in words, e.g. "3 podiums" or "a podium at the Le Mans 24 Hours".</summary>
    public static Phrase Describe(SponsorDeal deal) => deal.Objective switch
    {
        ObjectiveKind.FinishEveryRound when deal.OverRounds > 0 => Phrase.Count(deal.OverRounds, "finish the last round", "finish each of the last {n} rounds"),
        ObjectiveKind.FinishEveryRound => Phrase.Of("finish every round"),
        ObjectiveKind.Podiums when deal.OverRounds > 0 => Phrase.Count(deal.Count, "a podium in the last {rounds} rounds", "{n} podiums in the last {rounds} rounds", ("rounds", deal.OverRounds)),
        ObjectiveKind.Podiums => Phrase.Count(deal.Count, "a podium", "{n} podiums"),
        ObjectiveKind.CleanSeason when deal.SignedAfterRound > 0 => Phrase.Of("a clean run to the end of the season (no DQ, at most one penalty)"),
        ObjectiveKind.Wins => Phrase.Count(deal.Count, "a class win", "{n} class wins"),
        ObjectiveKind.Poles => Phrase.Count(deal.Count, "a pole", "{n} poles"),
        ObjectiveKind.PodiumAt => Phrase.Of("a podium at the {event}", ("event", deal.EventName)),
        ObjectiveKind.ChampionshipTop => deal.Count == 1
            ? Phrase.Of("win the championship")
            : Phrase.Of("top {n} in the championship", ("n", deal.Count)),
        ObjectiveKind.CleanSeason => Phrase.Of("a clean season (no DQ, at most one penalty)"),
        ObjectiveKind.BeatRival => Phrase.Of("beat {rival} in the championship", ("rival", deal.Rival)),
        _ => Phrase.Of(deal.Objective.ToString()),
    };

    /// <summary>Where the objective stands now. At the end of the season nothing is left in progress.</summary>
    public static ObjectiveProgress Progress(Season season, SponsorDeal deal)
    {
        var finished = season.IsFinished;
        // Guest drives are outside the championship, and outside the sponsors' deals. A deal signed
        // mid-season counts only the rounds after it.
        var rounds = season.ChampionshipRounds.Where(r => r.Number > deal.SignedAfterRound).ToList();
        var completed = rounds.Where(r => r.State == RoundState.Completed && r.Result is not null).ToList();
        var remaining = rounds.Count(r => r.State is RoundState.Upcoming or RoundState.Armed);
        var mine = completed.Select(r => (Round: r, Me: r.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer))).ToList();

        ObjectiveProgress Count(int done, int needed) =>
            new(done >= needed ? ObjectiveStatus.Met : done + remaining < needed ? ObjectiveStatus.Failed : ObjectiveStatus.InProgress, done, needed);

        ObjectiveProgress AtEnd(bool met, int done, int needed) =>
            new(!finished ? ObjectiveStatus.InProgress : met ? ObjectiveStatus.Met : ObjectiveStatus.Failed, done, needed);

        switch (deal.Objective)
        {
            case ObjectiveKind.FinishEveryRound:
            {
                var classified = mine.Count(x => x.Me?.ClassRank is not null);
                var missed = mine.Count - classified + rounds.Count(r => r.State == RoundState.Skipped);
                return new(missed > 0 ? ObjectiveStatus.Failed : finished ? ObjectiveStatus.Met : ObjectiveStatus.InProgress,
                    classified, rounds.Count);
            }
            case ObjectiveKind.Podiums:
                return Count(mine.Count(x => x.Me?.ClassRank <= 3), deal.Count);
            case ObjectiveKind.Wins:
                return Count(mine.Count(x => x.Me?.ClassRank == 1), deal.Count);
            case ObjectiveKind.Poles:
                return Count(mine.Count(x => x.Me?.ClassPole == true), deal.Count);
            case ObjectiveKind.PodiumAt:
            {
                var round = rounds.FirstOrDefault(r => r.Number == deal.RoundNumber);
                if (round is null || round.State == RoundState.Skipped) return new(ObjectiveStatus.Failed, 0, 1);
                if (round.State != RoundState.Completed) return new(finished ? ObjectiveStatus.Failed : ObjectiveStatus.InProgress, 0, 1);
                var podium = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank <= 3;
                return new(podium ? ObjectiveStatus.Met : ObjectiveStatus.Failed, podium ? 1 : 0, 1);
            }
            case ObjectiveKind.ChampionshipTop:
            {
                var position = Position(season, d => d.IsPlayer);
                return AtEnd(position <= deal.Count, position ?? 0, deal.Count);
            }
            case ObjectiveKind.CleanSeason:
            {
                var penalties = completed.Sum(r => r.Result!.PlayerPenalties);
                if (penalties > 1 || mine.Any(x => x.Me?.Entry.Status == FinishStatus.Dq)) return new(ObjectiveStatus.Failed, penalties, 1);
                return AtEnd(true, penalties, 1);
            }
            case ObjectiveKind.BeatRival:
            {
                var key = Scoring.Standings.NameKey(deal.Rival);
                var me = Position(season, d => d.IsPlayer);
                var rival = Position(season, d => !d.IsPlayer && Scoring.Standings.NameKey(d.Name) == key);
                // A rival who never starts can't finish ahead.
                var ahead = me is int m && (rival is not int r || m < r);
                return AtEnd(ahead, me ?? 0, rival ?? 0);
            }
            default:
                return new(ObjectiveStatus.Failed, 0, 0);
        }
    }

    /// <summary>
    /// A result that beats expectations: a podium or a win, a finish well above the team's target,
    /// a career-best finish, or a charge from the back.
    /// </summary>
    public static bool BeatExpectations(Career career, Round round)
    {
        if (round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank is not { } rank) return false;
        if (rank <= 3) return true;
        if (rank <= (career.CurrentSeason.Contract?.TargetPosition ?? Progression.DefaultTarget) - 3) return true;

        var before = career.PastSeasons.Append(career.CurrentSeason)
            .SelectMany(s => s.ChampionshipRounds)
            .Where(r => r != round && r.State == RoundState.Completed)
            .Select(r => r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank)
            .OfType<int>()
            .ToList();
        if (before.Count > 0 && rank < before.Min()) return true;

        return Paddock.MomentsOf(career, round).Contains(Moment.Comeback);
    }

    /// <summary>
    /// After a result that beats expectations, one sponsor can approach the player through the
    /// inbox with a deal for the rest of the season: at most one a round, none once two deals are
    /// signed, and only in seasons on 1.2's rules. The offer lapses when the next round counts.
    /// </summary>
    /// <returns>The message, already in the inbox; null when no one called.</returns>
    public static InboxMessage? Approach(Career career, Round round, ContentCatalog catalog, IReadOnlyCollection<string> builtLiveries,
        IReadOnlyList<SponsorInfo> ownSponsors, string language, DateTimeOffset now, int? seed = null)
    {
        var season = career.CurrentSeason;
        if (career.ChampionshipOnly || season.SponsorRules < CurrentRules || round.Guest || season.Sponsors.Count >= MaxDeals) return null;
        var left = season.ChampionshipRounds.Where(r => r.Number > round.Number && r.State is RoundState.Upcoming or RoundState.Armed).ToList();
        if (left.Count == 0 || !BeatExpectations(career, round)) return null;

        // Not a sponsor already signed, or one that's already called this season.
        var called = career.Inbox.Where(m => m.Season == season.Number && m.Approach is not null).Select(m => m.Approach!.Deal.SponsorId)
            .Concat(season.Sponsors.Select(s => s.SponsorId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var random = new Random(seed ?? Progression.Seed(career) ^ (round.Number * 7919));
        var pool = Pool(career, catalog, builtLiveries, ownSponsors, language).Where(x => !called.Contains(x.Sponsor.Id)).ToList();
        if (pool.Count == 0) return null;
        var sponsor = Pick(pool, random);

        var objectives = (sponsor.Kind == "local" ? LocalObjectives(season, round.Number) : MidSeasonObjectives(season, round, left))
            .Where(d => season.Sponsors.All(s => s.Objective != d.Objective))
            .OrderBy(_ => random.Next()).ToList();
        if (objectives.Count == 0) return null;
        var deal = Fill(objectives[0], sponsor);
        deal.SignedAfterRound = round.Number;

        var name = ("sponsor", (object?)sponsor.Name);
        var message = new InboxMessage
        {
            Id = InboxMessage.IdFor(season.Number, round.Number, MessageKind.SponsorApproach),
            Kind = MessageKind.SponsorApproach,
            Season = season.Number,
            Round = round.Number,
            At = now,
            From = Phrase.Of("Your manager"),
            Subject = Phrase.Of("{sponsor} wants to back you", name),
            Body =
            [
                Phrase.Of("After the {event}, {sponsor} is offering a deal for the rest of the season.", ("event", round.EventName), name),
                Phrase.Of("The objective: {objective}. Meet it and it pays {n} reputation at the season review.", ("n", deal.Reward))
                    .With("objective", Describe(deal)),
                Phrase.Of("The offer stands until your next race counts. Turning it down costs nothing."),
            ],
            Approach = new SponsorApproach { Deal = deal },
        };
        Inbox.Deliver(career, message);
        return message;
    }

    /// <summary>A bigger brand's deal for the rounds left: podiums over them, a podium at a big round still to come, or beat the driver just ahead.</summary>
    private static IEnumerable<SponsorDeal> MidSeasonObjectives(Season season, Round after, IReadOnlyList<Round> left)
    {
        var podiums = Math.Clamp((int)Math.Round(left.Count * 0.5), 1, left.Count);
        yield return new() { Objective = ObjectiveKind.Podiums, Count = podiums, Reward = podiums >= 2 ? 4 : 3, OverRounds = left.Count };

        if (left.Where(r => r.PointsWeight >= Paddock.BigRound).MaxBy(r => r.PointsWeight) is { } big)
            yield return new() { Objective = ObjectiveKind.PodiumAt, Count = 1, RoundNumber = big.Number, EventName = big.EventName, Reward = big.PointsWeight >= 2 ? 4 : 3 };

        // The driver just ahead in the standings, or for the leader, the one chasing.
        var drivers = season.Standings().FirstOrDefault(s => s.CarClass == season.Car.CarClass)?.Drivers;
        var me = drivers?.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        if (drivers is not null && me >= 0 && (me > 0 ? drivers[me - 1] : drivers.ElementAtOrDefault(1)) is { } rival)
            yield return new() { Objective = ObjectiveKind.BeatRival, Rival = rival.Name, Reward = 3 };
    }

    /// <summary>Signs or turns down a mid-season offer still open.</summary>
    public static void AnswerApproach(Career career, string messageId, bool sign)
    {
        var message = career.Inbox.FirstOrDefault(m => m.Id == messageId)
            ?? throw new PlayerError("That message isn't in your inbox any more.");
        if (message.Approach is not { } approach) throw new PlayerError("That message isn't a sponsor's offer.");
        var season = career.CurrentSeason;
        if (approach.Status != ApproachStatus.Open || message.Season != season.Number || season.Review is not null)
            throw new PlayerError("That offer isn't open any more.");

        message.Read = true;
        if (!sign)
        {
            approach.Status = ApproachStatus.Declined;
            return;
        }
        if (season.Sponsors.Count >= MaxDeals) throw new PlayerError("You already carry {n} personal sponsors this season.", ("n", MaxDeals));
        season.Sponsors.Add(approach.Deal);
        approach.Status = ApproachStatus.Signed;
    }

    /// <summary>A championship round counted: offers from earlier rounds aren't open any more.</summary>
    public static void LapseOpenApproaches(Career career, Round round)
    {
        if (round.Guest) return;
        foreach (var m in career.Inbox.Where(m => m.Approach is { Status: ApproachStatus.Open }
            && (m.Season < career.CurrentSeason.Number || (m.Season == career.CurrentSeason.Number && m.Round < round.Number))))
        {
            m.Approach!.Status = ApproachStatus.Lapsed;
        }
    }

    private static int? Position(Season season, Func<Scoring.DriverStanding, bool> who)
    {
        var drivers = season.Standings().FirstOrDefault(s => s.CarClass == season.Car.CarClass)?.Drivers.ToList();
        var index = drivers?.FindIndex(d => who(d)) ?? -1;
        return index >= 0 ? index + 1 : null;
    }
}
