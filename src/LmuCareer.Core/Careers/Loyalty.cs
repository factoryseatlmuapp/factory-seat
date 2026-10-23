using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// What loyalty is to: a make in the classes where makes race each other (GT3, GTE, Hypercar), or a
/// team's outfit in the spec prototype classes (LMP3, LMP2), where every car is much the same.
/// </summary>
public enum LoyaltyKind { Brand, Team }

/// <summary>How a make or team sees the player, from the points in <see cref="Loyalty.Points"/>.</summary>
public enum LoyaltyStage { Unknown, OnTheirRadar, DevelopmentDriver, FactoryDriver }

/// <summary>How the player left a make or team, which decides what happens to the loyalty built there.</summary>
public enum Departure
{
    /// <summary>The team didn't offer a new deal: loyalty stays where it was.</summary>
    LetGo,

    /// <summary>The team offered a new deal and the player signed elsewhere.</summary>
    WalkedAway,

    /// <summary>The player left while still under contract.</summary>
    BrokeContract,

    /// <summary>The player signed with a rival: a rival make, or a rival driver's team.</summary>
    JoinedRival,
}

/// <summary>The player's standing with one make or team.</summary>
public sealed class Loyalty
{
    public LoyaltyKind Kind { get; set; }

    /// <summary>A brand id from the catalog (porsche, gm), or a team's outfit id (dkr, af-corse).</summary>
    public string Id { get; set; } = "";

    /// <summary>0 to 100; <see cref="Loyalties.Stage"/> turns it into a stage.</summary>
    public decimal Points { get; set; }

    /// <summary>The last season the player raced for them.</summary>
    public int LastSeason { get; set; }

    /// <summary>How the player left, once they have; null while they're still racing for them.</summary>
    public Departure? Left { get; set; }

    /// <summary>A bridge burned: loyalty here builds back only slowly.</summary>
    public bool Burned { get; set; }
}

public static class Loyalties
{
    /// <summary>The points where each stage starts: On their radar, Development driver, Factory driver.</summary>
    public const decimal RadarAt = 25, DevelopmentAt = 50, FactoryAt = 75;

    public const decimal Max = 100;

    public static LoyaltyStage Stage(decimal points) => points switch
    {
        >= FactoryAt => LoyaltyStage.FactoryDriver,
        >= DevelopmentAt => LoyaltyStage.DevelopmentDriver,
        >= RadarAt => LoyaltyStage.OnTheirRadar,
        _ => LoyaltyStage.Unknown,
    };

    /// <summary>Classes where loyalty goes to the make; in the rest it goes to the team.</summary>
    public static bool IsBrandClass(string carClass) => carClass is "GT3" or "GTE" or "Hyper";

    /// <summary>
    /// What a season in this car, for this team, builds loyalty with: the car's make in a brand
    /// class, the team's outfit in a spec prototype class. Null when neither is known (a custom
    /// team before its first race, or a car the catalog has no make for).
    /// </summary>
    public static (LoyaltyKind Kind, string Id)? Key(string carClass, string carFolder, string teamName, ContentCatalog catalog)
    {
        if (IsBrandClass(carClass))
            return catalog.Car(carFolder)?.Brand is { } brand ? (LoyaltyKind.Brand, brand) : null;
        if (teamName.Length == 0) return null;
        return (LoyaltyKind.Team, catalog.Team(teamName, carClass)?.OutfitId ?? ContentCatalog.Slug(teamName));
    }

    /// <summary>The key for a season, from its contract (or the team its first race showed) and its car.</summary>
    public static (LoyaltyKind Kind, string Id)? Key(Season season, ContentCatalog catalog)
    {
        var folder = season.Car.Folder.Length > 0 ? season.Car.Folder : catalog.CarByType(season.Car.CarType)?.Folder ?? "";
        var team = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;
        return Key(season.Car.CarClass, folder, team, catalog);
    }

    /// <summary>The player's standing with a make or team; null when they've never raced for them.</summary>
    public static Loyalty? Find(Career career, LoyaltyKind kind, string id) =>
        career.Loyalty.FirstOrDefault(l => l.Kind == kind && l.Id == id);

    // ---------- Building it ----------

    /// <summary>What a season with them is worth: being there, then results on top.</summary>
    public const decimal SeasonPoints = 15, TargetPoints = 10, PodiumPoints = 3, MaxPodiumPoints = 15, TitlePoints = 10;

    /// <summary>After a burned bridge, loyalty there builds at this share of the usual rate.</summary>
    public const decimal BurnedRate = 0.25m;

    /// <summary>Walked away and gone: loyalty there fades this much each season after the first away.</summary>
    public const decimal FadePerSeason = 5;

    /// <summary>What a reviewed season adds with the make or team it was raced for; short seasons count pro rata.</summary>
    public static decimal SeasonGain(Season season)
    {
        if (season.Review is not { } review) return 0;
        var podiums = season.ChampionshipRounds
            .Count(r => r.State == RoundState.Completed && r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank <= 3);
        var gain = SeasonPoints
            + (review.TargetMet ? TargetPoints : 0)
            + Math.Min(MaxPodiumPoints, PodiumPoints * podiums)
            + (review.ChampionshipPosition == 1 ? TitlePoints : 0);
        return Math.Round(gain * (decimal)review.SeasonWeight, 1);
    }

    /// <summary>
    /// At a season's review: the make or team raced for gains its season's worth, and makes and
    /// teams the player walked away from fade a little. Run once per season, with the review.
    /// </summary>
    public static void AfterSeason(Career career, ContentCatalog catalog)
    {
        var season = career.CurrentSeason;
        var key = Key(season, catalog);
        foreach (var other in career.Loyalty.Where(l => (l.Kind, l.Id) != key))
        {
            // The season they left in doesn't count: the fade starts the season after.
            if (other.Left == Departure.WalkedAway && season.Number - other.LastSeason >= 2)
                other.Points = Math.Max(0, other.Points - FadePerSeason);
        }

        if (key is not { } k) return;
        var loyalty = Find(career, k.Kind, k.Id);
        if (loyalty is null) career.Loyalty.Add(loyalty = new Loyalty { Kind = k.Kind, Id = k.Id });
        var gain = SeasonGain(season) * (loyalty.Burned ? BurnedRate : 1);
        loyalty.Points = Math.Min(Max, loyalty.Points + gain);
        loyalty.LastSeason = season.Number;
        loyalty.Left = null;
    }

    // ---------- Leaving ----------

    /// <summary>
    /// How much walking away from a team costs: more when the team was a front-runner, less when the
    /// move was a step up (a better tier, or a class up). Never nothing: they did want you back.
    /// </summary>
    public static decimal WalkAwayCost(int fromTier, int toTier, bool classUp)
    {
        var prestige = fromTier switch { 1 => 30, 2 => 20, _ => 10 };
        var stepUp = fromTier - toTier + (classUp ? 1 : 0);
        return Math.Max(5, prestige - 10 * Math.Max(0, stepUp));
    }

    /// <summary>
    /// Signing with another make or team: records how the player left the one they raced for. A
    /// contract broken or a rival make joined burns the bridge; walking away from a team that
    /// offered a new deal costs some loyalty; being let go costs none. Staying (the same make, or
    /// the same outfit in a new class) isn't leaving.
    /// </summary>
    public static Departure? Leave(Career career, Season finished, TeamOffer signed, ContentCatalog catalog, bool classUp)
    {
        if (Key(finished, catalog) is not { } from) return null;
        var to = Key(signed.CarClass, signed.CarFolder, signed.TeamName, catalog);
        if (to == from) return null;
        if (Find(career, from.Kind, from.Id) is not { } loyalty) return null;

        var departure =
            finished.Contract?.SeasonsLeft > 1 ? Departure.BrokeContract
            : to is { Kind: LoyaltyKind.Brand } t && from.Kind == LoyaltyKind.Brand && catalog.AreRivalBrands(from.Id, t.Id) ? Departure.JoinedRival
            : career.Offers.Any(o => o.Kind == OfferKind.ReSign) ? Departure.WalkedAway
            : Departure.LetGo;

        switch (departure)
        {
            case Departure.BrokeContract or Departure.JoinedRival:
                loyalty.Points = 0;
                loyalty.Burned = true;
                break;
            case Departure.WalkedAway:
                loyalty.Points = Math.Max(0, loyalty.Points - WalkAwayCost(finished.Contract?.Tier ?? 2, signed.Tier, classUp));
                break;
        }
        loyalty.Left = departure;
        return departure;
    }
}
