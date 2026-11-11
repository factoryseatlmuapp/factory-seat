using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <param name="Team">The team whose garage it is, for the badge in the top bar.</param>
/// <param name="Make">The make on the car's badge.</param>
/// <param name="Primary">The make's colour: the only accent while the perk is held.</param>
/// <param name="Secondary">A second livery line for two-colour identities; null for one colour.</param>
public sealed record GarageLook(string Team, string Make, string Primary, string? Secondary);

/// <summary>
/// 1.5's Preferred garage space perk: the team treats the player's car best, and the app shows it
/// with a premium look (forged carbon, brushed metal, frosted panels) in the colour of the make on
/// the car's badge. Colours are by make, not team or livery: official liveries are encrypted.
/// </summary>
public static class Garage
{
    /// <summary>
    /// Matched in order against the car's name, by the badge (Corvette and Cadillac are both GM, Lexus is
    /// Toyota's, but each has its own colour). Spec prototypes go by chassis maker. Decided 11/2.
    /// </summary>
    public static readonly IReadOnlyList<(string Badge, string Make, string Primary, string? Secondary)> Makes =
    [
        ("Corvette", "Corvette", "#FFCD00", null),
        ("Cadillac", "Cadillac", "#C9A227", null),
        ("Lexus", "Lexus", "#C0C6CC", "#CC0000"),
        ("Toyota", "Toyota", "#EB0A1E", null),
        ("Ferrari", "Ferrari", "#D40000", null),
        ("McLaren", "McLaren", "#FF8000", null),
        ("BMW", "BMW", "#1C69D4", null),
        ("Aston Martin", "Aston Martin", "#00665E", null),
        ("Alpine", "Alpine", "#0055B8", null),
        ("Ford", "Ford", "#1F4E9C", null),
        ("Glickenhaus", "Glickenhaus", "#C8102E", "#111111"),
        ("Genesis", "Genesis", "#FF5A1F", "#8B0A1A"),
        ("Vanwall", "Vanwall", "#0B5D3B", null),
        ("Porsche", "Porsche", "#D5001C", "#FFFFFF"),
        ("Lamborghini", "Lamborghini", "#D9A62E", null),
        ("Mercedes", "Mercedes", "#C0C6CC", null),
        ("Peugeot", "Peugeot", "#C8E600", null),
        ("Isotta", "Isotta Fraschini", "#0072CE", null),
        ("Oreca", "Oreca", "#1E5AA8", null),
        ("Ligier", "Ligier", "#0055A4", null),
        ("Ginetta", "Ginetta", "#F28C28", null),
        ("Duqueine", "Duqueine", "#F26B1D", "#111111"),
        ("ADESS", "ADESS", "#00A651", null),
    ];

    /// <summary>A car we have no colour for: brushed silver, close to the metal.</summary>
    public const string Neutral = "#C0C6CC";

    /// <summary>The make on a car's badge, from its name; null when it isn't in <see cref="Makes"/>.</summary>
    public static (string Make, string Primary, string? Secondary)? MakeOf(string carName)
    {
        foreach (var (badge, make, primary, secondary) in Makes)
            if (carName.Contains(badge, StringComparison.OrdinalIgnoreCase)) return (make, primary, secondary);
        return null;
    }

    /// <summary>The premium look while the season's contract carries Preferred garage space; null otherwise.</summary>
    public static GarageLook? For(Career career, ContentCatalog catalog)
    {
        var season = career.CurrentSeason;
        if (career.ChampionshipOnly || !Negotiation.Holds(season, Perk.GarageSpace)) return null;
        var car = (season.Car.Folder is { Length: > 0 } folder ? catalog.Car(folder) : null) ?? catalog.CarByType(season.Car.CarType);
        var name = car?.Name ?? season.Car.CarType;
        var team = season.Contract?.TeamName is { Length: > 0 } contracted ? contracted : season.Car.TeamName;
        return MakeOf(name) is var (make, primary, secondary)
            ? new GarageLook(team, make, primary, secondary)
            : new GarageLook(team, "", Neutral, null);
    }
}
