using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// 1.5 contract perks. Each changes only something Factory Seat controls; none claims to make the car faster.
/// </summary>
public enum Perk
{
    /// <summary>The team treats your car best: the app's premium look while it's held.</summary>
    GarageSpace,

    /// <summary>Richer debriefs and a pre-race target from the best car in class.</summary>
    ExpertEngineer,

    /// <summary>One extra personal sponsor offer a season, and up to 3 signed instead of 2.</summary>
    PreferredSponsors,

    /// <summary>The season's first DNF costs no reputation.</summary>
    TeamBacking,

    /// <summary>One guaranteed guest-drive offer when eligible.</summary>
    GuestDrivePriority,

    /// <summary>One guaranteed test day a season in the team's next-class car.</summary>
    DevelopmentDriver,

    /// <summary>Leave mid-contract if a higher-class team makes an offer.</summary>
    ReleaseClause,
}

/// <summary>The terms the player asked for in a counter.</summary>
public sealed record AskedTerms(decimal Gain, decimal Loss, List<Perk> Perks);

/// <summary>What became of a counter.</summary>
public enum CounterOutcome { Accepted, Countered, Final, WalkedAway }

/// <summary>
/// 1.5 "real negotiations" over an off-season offer: the player counters the gain and loss modifiers
/// and the perks, at most <see cref="MaxCounters"/> times. Each counter risks the team walking away,
/// by current form against the team's target ("leverage"). Haggling never costs reputation, and a
/// seat is always left: when every team has walked, a back-marker takes the player, no haggling.
/// </summary>
public static class Negotiation
{
    public const int MaxCounters = 2;
    public const int MaxPerks = 3;
    public const decimal MinModifier = 0.8m;
    public const decimal MaxModifier = 1.5m;

    /// <summary>A team never lets the gain modifier sit more than this above the loss one.</summary>
    public const decimal MaxGap = 0.3m;

    /// <summary>What a perk costs the team in a counter, as modifier.</summary>
    public static decimal Cost(Perk perk) => perk is Perk.TeamBacking or Perk.ReleaseClause ? 0.2m : 0.1m;

    /// <summary>The highest gain modifier a team of this tier goes to: front-runner ×1.5, midfield ×1.3, back-marker ×1.2.</summary>
    public static decimal GainCeiling(int tier) => tier switch { 1 => 1.5m, 2 => 1.3m, _ => 1.2m };

    /// <summary>The lowest loss modifier a team goes to: two steps under its opening.</summary>
    public static decimal LossFloor(int tier) => Math.Max(MinModifier, Progression.Prestige(tier) - 0.2m);

    /// <summary>Team's target position minus the player's championship position: positive is strong form.</summary>
    public static int Leverage(int targetPosition, int? championshipPosition) =>
        targetPosition - (championshipPosition ?? targetPosition + 6);

    /// <summary>Chance the team walks away at a counter: 15% − 5% × leverage, between 0 and 60%.</summary>
    public static double WalkChance(int leverage) => Math.Clamp(0.15 - 0.05 * leverage, 0, 0.6);

    /// <summary>How far past its opening a team will go, by the player's form.</summary>
    public static decimal Room(int leverage) => leverage switch
    {
        >= 3 => 0.3m,
        >= 0 => 0.2m,
        >= -3 => 0.1m,
        _ => 0m,
    };

    /// <summary>What a deal is worth to the player against the team's opening: better modifiers plus the perks.</summary>
    public static decimal Value(int tier, decimal gain, decimal loss, IEnumerable<Perk> perks)
    {
        var opening = Progression.Prestige(tier);
        return gain - opening + (opening - loss) + perks.Sum(Cost);
    }

    /// <summary>The perks a team opens with: front-runners 0–1 (engineer or garage), midfield 1–2, back-markers 2–3.</summary>
    public static List<Perk> OpeningPerks(int tier, Random random)
    {
        var (pool, min, max) = tier switch
        {
            1 => (new[] { Perk.ExpertEngineer, Perk.GarageSpace }, 0, 1),
            2 => (Enum.GetValues<Perk>(), 1, 2),
            _ => (Enum.GetValues<Perk>(), 2, 3),
        };
        var count = random.Next(min, max + 1);
        return pool.OrderBy(_ => random.Next()).Take(count).OrderBy(p => p).ToList();
    }

    /// <summary>Sets a fresh offer's opening terms: the team's prestige both ways, its perks and how far it will move.</summary>
    public static void Open(TeamOffer offer, int leverage, Random random)
    {
        offer.GainModifier = offer.LossModifier = Progression.Prestige(offer.Tier);
        offer.Perks = OpeningPerks(offer.Tier, random);
        offer.Budget = offer.Perks.Sum(Cost) + Room(leverage);
    }

    /// <summary>
    /// Only back-marker seats left after a season that fell short: no haggling, no perks. The player
    /// takes one of them or retires.
    /// </summary>
    public static void MakeLastResort(TeamOffer offer)
    {
        offer.LastResort = true;
        offer.Perks = [];
        offer.Budget = null;
        offer.GainModifier = offer.LossModifier = Progression.Prestige(offer.Tier);
    }

    /// <summary>Whether the player can still counter this offer.</summary>
    public static bool CanCounter(TeamOffer offer) =>
        offer.Budget is not null && !offer.UnderContract && !offer.LastResort && !offer.WalkedAway && !offer.Agreed
        && !offer.FinalOffer && offer.Counters < MaxCounters;

    public const string AcceptedReply = "\"We can do that. The deal's yours when you sign.\""; // phrase
    public const string CounterReply = "\"We can't go that far. Here's where we can meet you.\""; // phrase
    public const string FinalReply = "\"This is as far as we go. Final offer.\""; // phrase
    public const string WalkedReply = "\"We're going another way. Good luck next season.\""; // phrase

    /// <summary>
    /// The player's counter: the terms they ask for. Asking past what the team can ever go to is
    /// refused outright (no counter used, no risk). Otherwise the counter is used, the team may walk
    /// away, and it either takes the ask or meets the player part way.
    /// </summary>
    public static CounterOutcome Counter(Career career, string offerId, decimal gain, decimal loss, IReadOnlyCollection<Perk> perks,
        ContentCatalog catalog, LmuInstall? install, Random? random = null)
    {
        var review = career.CurrentSeason.Review ?? throw new PlayerError("Offers are talked over at the end of a season.");
        var offer = career.Offers.FirstOrDefault(o => o.Id == offerId) ?? throw new PlayerError("That offer isn't on the table any more.");
        if (!CanCounter(offer)) throw new PlayerError("{team} isn't talking terms any more.", ("team", offer.TeamName));

        gain = Math.Round(gain, 1);
        loss = Math.Round(loss, 1);
        var asked = perks.Distinct().OrderBy(p => p).ToList();
        var team = ("team", (object?)offer.TeamName);
        if (gain > GainCeiling(offer.Tier))
            throw new PlayerError("{team} won't go past gains ×{x}.", team, ("x", Times(GainCeiling(offer.Tier))));
        if (loss < LossFloor(offer.Tier))
            throw new PlayerError("{team} won't go under losses ×{x}.", team, ("x", Times(LossFloor(offer.Tier))));
        if (gain < MinModifier || loss > MaxModifier)
            throw new PlayerError("Modifiers run from ×{min} to ×{max}.", ("min", Times(MinModifier)), ("max", Times(MaxModifier)));
        if (gain - loss > MaxGap)
            throw new PlayerError("No team lets gains sit more than {gap} above losses.", ("gap", Times(MaxGap)));
        if (asked.Count > MaxPerks)
            throw new PlayerError("A deal carries at most {n} perks.", ("n", MaxPerks));

        offer.Counters++;
        offer.Asked = new AskedTerms(gain, loss, asked.ToList());
        random ??= new Random(Progression.Seed(career) ^ Hash(offer.Id) ^ offer.Counters * 104729);
        var leverage = Leverage(offer.TargetPosition, review.ChampionshipPosition);
        if (random.NextDouble() < WalkChance(leverage))
        {
            offer.WalkedAway = true;
            offer.Reply = Phrase.Of(WalkedReply);
            if (!career.Offers.Any(Signable)) AddFallbackSeat(career, catalog, install);
            return CounterOutcome.WalkedAway;
        }

        var budget = offer.Budget!.Value;
        if (Value(offer.Tier, gain, loss, asked) <= budget)
        {
            (offer.GainModifier, offer.LossModifier, offer.Perks) = (gain, loss, asked);
            offer.Agreed = true;
            offer.Reply = Phrase.Of(AcceptedReply);
            return CounterOutcome.Accepted;
        }

        // Meet the player part way: modifiers back toward the opening first, then the perks they
        // added, dearest first ("can't do ×1.4, but you get the test car").
        var opening = Progression.Prestige(offer.Tier);
        var had = offer.Perks.ToHashSet();
        while (Value(offer.Tier, gain, loss, asked) > budget)
        {
            if (gain > opening) gain -= 0.1m;
            else if (loss < opening) loss += 0.1m;
            else
            {
                var drop = asked.Where(p => !had.Contains(p)).OrderByDescending(Cost).Cast<Perk?>().FirstOrDefault()
                    ?? asked.OrderByDescending(Cost).Cast<Perk?>().FirstOrDefault();
                if (drop is null) break;
                asked.Remove(drop.Value);
            }
        }
        (offer.GainModifier, offer.LossModifier, offer.Perks) = (gain, loss, asked);
        offer.FinalOffer = offer.Counters >= MaxCounters;
        offer.Reply = Phrase.Of(offer.FinalOffer ? FinalReply : CounterReply);
        return offer.FinalOffer ? CounterOutcome.Final : CounterOutcome.Countered;
    }

    /// <summary>An offer the player can still sign.</summary>
    public static bool Signable(TeamOffer offer) => !offer.WalkedAway;

    /// <summary>
    /// A seat is always left: every team walked away, so a back-marker in the class takes the player
    /// on its own terms. Last resort: no haggling, sign it or retire.
    /// </summary>
    public static TeamOffer? AddFallbackSeat(Career career, ContentCatalog catalog, LmuInstall? install)
    {
        var season = career.CurrentSeason;
        var carClass = season.Car.CarClass;
        var gone = career.Offers.Select(o => (o.TeamName, o.CarClass)).ToHashSet();
        var pick = catalog.Teams
            .Where(t => t.Class == carClass && !gone.Contains((t.Name, t.Class)))
            .OrderByDescending(t => t.Tier)
            .Select(t => (Team: t, Car: (t.Cars.Count > 0 ? t.Cars.Select(catalog.Car) : catalog.Cars.Where(c => c.Class == t.Class))
                .OfType<CarInfo>().FirstOrDefault(c => Progression.CanOffer(c, career, install))))
            .FirstOrDefault(x => x.Car is not null);
        var car = pick.Car ?? catalog.CarByType(season.Car.CarType);
        if (car is null) return null;

        var offer = new TeamOffer
        {
            Id = $"s{season.Number}-{career.Offers.Count + 1}",
            Kind = OfferKind.Move,
            TeamName = pick.Team?.Name ?? (season.Car.TeamName is { Length: > 0 } name ? name : "Independent entry"),
            CarClass = carClass,
            CarFolder = car.Folder,
            Tier = 3,
            TargetPosition = Progression.TargetFor(3),
            Numbers = pick.Team?.Numbers ?? [],
        };
        MakeLastResort(offer);
        career.Offers.Add(offer);
        return offer;
    }

    private static string Times(decimal x) => x.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A hash that stays the same between runs (string.GetHashCode doesn't).</summary>
    internal static int Hash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
