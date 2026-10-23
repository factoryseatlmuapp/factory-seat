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
}
