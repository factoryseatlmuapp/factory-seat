using LmuCareer.Core.Careers;

namespace LmuCareer.Core.Content;

/// <summary>The settings a briefing prescribes for one race, in values LMU's menus actually offer.</summary>
public sealed record RaceFormat(
    int RaceMinutes,
    int TimeScale,
    int FuelMultiplier,
    int TireMultiplier,
    // True for 12 and 24 hour races run at 2 hours or more: several driver swaps on a stint plan.
    // Otherwise the race is split down the middle with one swap.
    bool FullStintPlan,
    int Stints,
    double TankMinutes);

/// <summary>
/// Works out race length, Time Scale, Fuel Usage and Tyre Wear for an event. Only values LMU's
/// menus offer come out: race lengths from <see cref="RaceLengths"/>, Time Scale X1 to X60, and
/// Fuel Usage and Tyre Wear Real, x2 or x3.
/// </summary>
public static class RaceFormats
{
    /// <summary>LMU's race length steps, in minutes.</summary>
    public static readonly IReadOnlyList<int> RaceLengths = [5, 10, 15, 20, 30, 45, 60, 72, 90, 120, 144, 180, 240, 360, 480, 600, 720, 1440];

    public static readonly IReadOnlyList<int> Multipliers = [1, 2, 3];

    public const int MaxTimeScale = 60;

    /// <summary>
    /// How long a tank lasts at Fuel Usage Real. Real Hypercars run 45 to 55 minutes; LMU's own
    /// figure per class still needs a test race.
    /// </summary>
    public const double DefaultTankMinutes = 50;

    /// <summary>The length a round runs at unless the player changes it: 6 h for 24 h races, 3 h for 12 h, else 30 min.</summary>
    public static int DefaultMinutes(double realHours) => realHours switch
    {
        >= 24 => 360,
        >= 12 => 180,
        _ => 30,
    };

    /// <summary>Points multiplier for an event: 24-hour races pay double, 10 and 12 hour races half again.</summary>
    public static decimal DefaultWeight(double realHours) => realHours switch
    {
        >= 24 => 2,
        >= 10 => 1.5m,
        _ => 1,
    };

    public static int SnapLength(int minutes) => RaceLengths.MinBy(l => Math.Abs(l - minutes));

    public static RaceFormat For(double realHours, int? minutes = null, double tankMinutes = DefaultTankMinutes)
    {
        var length = SnapLength(minutes ?? DefaultMinutes(realHours));
        var timeScale = (int)Math.Clamp(Math.Round(realHours * 60 / length), 1, MaxTimeScale);
        var fullPlan = realHours >= 12 && length >= 120;

        int multiplier;
        if (fullPlan)
        {
            // Stints of about 22 minutes keep roughly half the real race's stops.
            multiplier = Multipliers.MinBy(m => Math.Abs(tankMinutes / m - 22));
        }
        else
        {
            // One swap at halfway: use the heaviest fuel usage whose tank still reaches halfway,
            // so the fuel stop lands near the middle and doubles as the swap.
            multiplier = Multipliers.Where(m => tankMinutes / m >= length / 2.0).DefaultIfEmpty(1).Max();
        }

        var tank = tankMinutes / multiplier;
        var stints = fullPlan ? (int)Math.Ceiling(length / tank) : 2;
        return new RaceFormat(length, timeScale, multiplier, multiplier, fullPlan, stints, tank);
    }

    /// <summary>
    /// Share of the race the player must drive to score driver points, from WEC's rules: 45 minutes
    /// of 6 hours for Hypercar drivers, 1 h 45 of 6 hours for Silver and Bronze drivers elsewhere
    /// (6 of 24 hours at Le Mans). Gold and Platinum drivers outside Hypercar get the Hypercar floor.
    /// </summary>
    public static double MinimumDriveShare(string carClass, DriverRating rating, double realHours)
    {
        const double floor = 0.75 / 6;
        if (carClass.Equals("Hyper", StringComparison.OrdinalIgnoreCase)) return floor;
        if (rating is DriverRating.Gold or DriverRating.Platinum) return floor;
        return realHours >= 24 ? 0.25 : 1.75 / 6;
    }
}
