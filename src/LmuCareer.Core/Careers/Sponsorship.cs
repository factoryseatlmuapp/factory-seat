using LmuCareer.Core.Content;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

public enum ObjectiveStatus { InProgress, Met, Failed }

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
    public const int OffersMade = 3;

    /// <summary>Running the sponsor's livery is optional; doing it is worth this much on top.</summary>
    public const decimal LiveryBonus = 1;

    /// <summary>Deals are signed (and dropped) before the season's first championship round counts; a guest drive doesn't close the window.</summary>
    public static bool CanSign(Season season) =>
        season.Rounds.Count > 0 && !season.ChampionshipRounds.Any(r => r.State == RoundState.Completed);

    /// <summary>Makes the season's offers, once, while deals can still be signed.</summary>
    /// <param name="ownSponsors">Brands the player added themselves (their own liveries), alongside the catalog's.</param>
    /// <returns>True when offers were made (the career needs saving).</returns>
    public static bool EnsureOffers(Career career, ContentCatalog catalog, IReadOnlyCollection<string> builtLiveries,
        IReadOnlyList<SponsorInfo>? ownSponsors = null, int? seed = null)
    {
        var season = career.CurrentSeason;
        if (season.SponsorOffers is not null || !CanSign(season)) return false;
        season.SponsorOffers = MakeOffers(career, catalog, builtLiveries, ownSponsors ?? [],
            new Random(seed ?? Progression.Seed(career) ^ 0x5B0B)).ToList();
        return true;
    }

    public static IReadOnlyList<SponsorDeal> MakeOffers(Career career, ContentCatalog catalog, IReadOnlyCollection<string> builtLiveries,
        IReadOnlyList<SponsorInfo> ownSponsors, Random random)
    {
        var season = career.CurrentSeason;
        var carFolder = catalog.CarByType(season.Car.CarType)?.Folder;
        var spec = season.Car.CarClass is "LMP2" or "LMP3";

        // Real backers call anyone with the reputation they look for; a mashup calls when it suits
        // the car (any car in a spec class), and keenly once its livery exists.
        var pool = catalog.Sponsors.Concat(ownSponsors)
            .Where(s => career.Reputation >= s.MinReputation)
            .Select(s => (Sponsor: s, Weight: Weight(s)))
            .Where(x => x.Weight > 0)
            .ToList();

        int Weight(SponsorInfo s)
        {
            if (s.Kind != "mashup") return 2;
            // A brand of the player's own with no cars picked suits any car.
            var suits = s.Cars.Count == 0 || s.Cars.Contains(carFolder ?? "", StringComparer.OrdinalIgnoreCase);
            var built = builtLiveries.Contains(s.Id, StringComparer.OrdinalIgnoreCase);
            if (!suits && !spec) return 0;
            return (built ? 12 : 0) + (suits ? 4 : 1);
        }

        var objectives = Objectives(career).OrderBy(_ => random.Next()).ToList();
        var offers = new List<SponsorDeal>();
        while (offers.Count < OffersMade && pool.Count > 0 && objectives.Count > 0)
        {
            var pick = random.Next(pool.Sum(x => x.Weight));
            var index = 0;
            while (pick >= pool[index].Weight) pick -= pool[index++].Weight;
            var sponsor = pool[index].Sponsor;
            pool.RemoveAt(index);

            var deal = objectives[0];
            objectives.RemoveAt(0);
            deal.SponsorId = sponsor.Id;
            deal.Name = sponsor.Name;
            offers.Add(deal);
        }
        return offers;
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
        ObjectiveKind.FinishEveryRound => Phrase.Of("finish every round"),
        ObjectiveKind.Podiums => Phrase.Count(deal.Count, "a podium", "{n} podiums"),
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
        // Guest drives are outside the championship, and outside the sponsors' deals.
        var rounds = season.ChampionshipRounds.ToList();
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

    private static int? Position(Season season, Func<Scoring.DriverStanding, bool> who)
    {
        var drivers = season.Standings().FirstOrDefault(s => s.CarClass == season.Car.CarClass)?.Drivers.ToList();
        var index = drivers?.FindIndex(d => who(d)) ?? -1;
        return index >= 0 ? index + 1 : null;
    }
}
