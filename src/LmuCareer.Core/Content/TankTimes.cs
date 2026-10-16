namespace LmuCareer.Core.Content;

/// <summary>
/// How long a tank lasts at Fuel Usage Real, by class and track, so the pit plan picks a Fuel
/// Usage that makes the stops fall where they should. Measured Oct 15, 2026 from 288 results
/// files: LMU logs fuel (and virtual energy) for every car every lap, AI included; the figures are
/// the median AI car at AI Strength 90, dry, by day (tank_minutes.py in the project's notes).
/// </summary>
/// <remarks>
/// Pace moves these: slower drivers stretch a tank (about 30% longer at AI Strength 75), quicker
/// ones shorten it. The plan errs long (see <see cref="Margin"/>): one stop too many is just a
/// stop; a planned stop the race never needs is what broke an LMP3 race in 1.1 (Promo_714, Oct 15).
/// </remarks>
public static class TankTimes
{
    /// <summary>Planned as if a tank lasted this much longer than measured, so a planned stop is always needed.</summary>
    public const double Margin = 1.10;

    /// <summary>For a class nothing's known about.</summary>
    public const double Fallback = 50;

    // Minutes at Real by "folder/layout" (a layout of its own) or "folder" (the track's main layout).
    private static readonly Dictionary<string, Dictionary<string, double>> Measured = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GT3"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PortimaoWEC_2023"] = 52.3, ["ImolaWEC_2024"] = 51.1, ["Monza_2023"] = 48.5, ["Monza_2023/layoutGrande"] = 45.8,
            ["BahrainWEC_2023"] = 54.4, ["BahrainWEC_2023/layoutOuter"] = 50.4, ["Spa_2023"] = 51.5,
            ["LeMans_2023"] = 44.2, ["LeMans_2023/layoutMulsanne"] = 42.8, ["Daytona_2026"] = 54.0, ["FujiWEC_2023"] = 58.4,
            ["Qatar_2024"] = 54.3, ["RoadAtlanta_2026"] = 59.0, ["Sebring_2023"] = 50.3, ["Sebring_2023/layoutSchool"] = 47.2,
            ["Silverstone_2025/layoutNational"] = 71.3, ["LagunaSeca_2026"] = 64.1,
        },
        ["Hyper"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ImolaWEC_2024"] = 49.7, ["Monza_2023"] = 49.5, ["Spa_2023"] = 51.9, ["LeMans_2023"] = 42.7,
            ["Daytona_2026"] = 47.6, ["Qatar_2024"] = 57.1, ["Sebring_2023"] = 51.0,
        },
        ["LMP2"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ImolaWEC_2024"] = 43.9, ["Monza_2023"] = 42.2, ["Spa_2023"] = 44.6, ["LeMans_2023"] = 47.2,
            ["Daytona_2026"] = 46.4, ["Qatar_2024"] = 46.9, ["Sebring_2023"] = 42.7,
        },
        ["GTE"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Monza_2023"] = 60.9, ["Sebring_2023"] = 79.2,
        },
    };

    // A class's figure at tracks not measured: the median of its measured tracks.
    private static readonly Dictionary<string, double> ClassMedian = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GT3"] = 51.5,
        ["Hyper"] = 49.7,
        ["LMP2"] = 44.6,
        ["GTE"] = 70,
        // Not measured yet. LMP3 ran past 60 minutes at Imola on Real without a stop (Promo_714,
        // Oct 15), so an estimate above that; replace with measured runs.
        ["LMP3"] = 70,
    };

    /// <summary>Minutes a tank lasts at Real for the plan, margin included.</summary>
    public static double For(string carClass, string trackFolder, string layoutFile)
    {
        double measured;
        if (Measured.TryGetValue(carClass, out var tracks)
            && (tracks.TryGetValue($"{trackFolder}/{layoutFile}", out measured) || tracks.TryGetValue(trackFolder, out measured)))
        {
            return measured * Margin;
        }
        return ClassMedian.TryGetValue(carClass, out var median) ? median * Margin : Fallback;
    }
}
