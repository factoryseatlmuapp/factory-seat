using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// How teams are doing. A team's standing on the grid sets where it starts: front-runners have the
/// budget, the midfield is stable, the back of the grid struggles. Last season's team championship
/// moves it: a title brings cash in, a bad year takes it out. What a team can afford decides
/// whether it runs IMSA as well.
/// </summary>
public static class Teams
{
    /// <summary>A team's status from its tier and its place in last season's team standings in its class.</summary>
    public static TeamStatus Status(int tier, int? teamPosition, int teamsInClass)
    {
        var level = tier switch { 1 => 3, 2 => 1, _ => 0 };
        level += teamPosition switch
        {
            1 => 2,
            <= 3 => 1,
            { } p when teamsInClass > 0 && p > teamsInClass / 2.0 => -1,
            _ => 0,
        };
        return (TeamStatus)Math.Clamp(level, 0, 3);
    }

    /// <summary>A team's status after the season just finished, from the class's team standings.</summary>
    public static TeamStatus Status(Season season, string teamName, string carClass, int tier)
    {
        var table = season.Standings().FirstOrDefault(s => s.CarClass.Equals(carClass, StringComparison.OrdinalIgnoreCase))?.Teams.ToList();
        var index = table?.FindIndex(t => t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase)) ?? -1;
        return Status(tier, index >= 0 ? index + 1 : null, table?.Count ?? 0);
    }

    /// <summary>How likely a team with this status is to run IMSA beside its main programme.</summary>
    public static double ImsaChance(TeamStatus status) => status switch
    {
        TeamStatus.FrontRunnerBudget => 0.6,
        TeamStatus.Growing => 0.35,
        TeamStatus.Stable => 0.1,
        _ => 0,
    };

    /// <summary>A make places a driver it rates in its IMSA programme with a partner team this often.</summary>
    public const double PartnerChance = 0.5;

    /// <summary>
    /// Sets each offer's team status, and which offers come with an IMSA seat: the team's own
    /// programme, if it can afford one, or else a partner team of the same make for a driver the
    /// make rates (development driver or better). Draws from its own random sequence, so the
    /// offers themselves come out as they would without it.
    /// </summary>
    public static void AddImsa(Career career, IReadOnlyList<TeamOffer> offers, ContentCatalog catalog, LmuInstall? install, Random random)
    {
        var season = career.CurrentSeason;
        var imsaOwned = Imsa.Calendar(catalog, "GT3", career.Rating, career.OwnedContent, install).Count >= Imsa.MinRounds;
        foreach (var offer in offers)
        {
            offer.Status = Status(season, offer.TeamName, offer.CarClass, offer.Tier);
            if (!imsaOwned || !Imsa.RacesIn(offer.CarClass)) continue;

            if (random.NextDouble() < ImsaChance(offer.Status))
            {
                offer.ImsaTeam = offer.TeamName;
                continue;
            }

            if (catalog.Car(offer.CarFolder)?.Brand is not { } brand
                || Loyalties.Find(career, LoyaltyKind.Brand, brand) is not { Burned: false } loyalty
                || Loyalties.Stage(loyalty.Points) < LoyaltyStage.DevelopmentDriver) continue;
            var partners = catalog.Teams
                .Where(t => t.Class == offer.CarClass && !t.Name.Equals(offer.TeamName, StringComparison.OrdinalIgnoreCase)
                    && t.Cars.Any(f => catalog.Car(f)?.Brand == brand))
                .ToList();
            if (partners.Count > 0 && random.NextDouble() < PartnerChance)
                offer.ImsaTeam = partners[random.Next(partners.Count)].Name;
        }
    }
}
