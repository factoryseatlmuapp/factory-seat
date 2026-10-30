namespace LmuCareer.Core.Careers;

/// <summary>What a New Game+ round must be raced at: set in LMU, then checked against the results.</summary>
/// <param name="AiStrength">LMU's AI Strength, in percent.</param>
/// <param name="Wet">A wet round: LMU's weather set to rain.</param>
/// <param name="MinDamage">The lowest damage LMU may be set to, in percent (never invulnerable).</param>
public sealed record RoundFloor(int AiStrength, bool Wet, int MinDamage);

/// <summary>
/// New Game+'s raised floor, round by round (design doc, Oct 16 and Oct 28): AI Strength drawn
/// within a band that rises by class, a third of each championship wet, and damage by race length.
/// </summary>
public static class Floor
{
    /// <summary>AI aggression in every class: LMU's High (Settings.JSON "AI Aggression Mode" 2).</summary>
    public const int Aggression = 2;

    /// <summary>The AI Strength band for a class, in percent; IMSA's classes use the LMU class they race as.</summary>
    public static (int Low, int High) Band(string carClass) => carClass switch
    {
        "Hyper" or "GTP" => (99, 104),
        "LMP2" => (95, 98),
        "LMP3" => (92, 95),
        "GTE" => (93, 96),
        _ => (90, 95),
    };

    /// <summary>The damage floor by the race's set-up length: full in sprints, a buffer against AI chaos in long races.</summary>
    public static int MinDamage(int raceMinutes) => raceMinutes switch
    {
        <= 30 => 100,
        <= 90 => 75,
        <= 360 => 60,
        _ => 50,
    };

    /// <summary>
    /// How often it rains at each track in the real race's season, as a weight for drawing the wet
    /// rounds (Oct 30, from climate norms: Spa and Fuji often, the Gulf and Long Beach almost never).
    /// </summary>
    public static double RainWeight(string trackFolder)
    {
        var name = trackFolder.Split('_')[0].ToLowerInvariant();
        return name switch
        {
            "spa" => 0.45,
            "fujiwec" => 0.40,
            "silverstone" => 0.40,
            "interlagos" => 0.35,
            "lemans" => 0.30,
            "imolawec" => 0.30,
            "monza" => 0.25,
            "sebring" => 0.25,
            "roadatlanta" => 0.25,
            "cotawec" => 0.20,
            "daytona" => 0.15,
            "paulricard" => 0.15,
            "barcelona" => 0.15,
            "portimaowec" => 0.12,
            "lagunaseca" => 0.08,
            "longbeach" => 0.05,
            "bahrainwec" => 0.02,
            "qatar" => 0.02,
            _ => 0.2,
        };
    }

    /// <summary>Wet rounds in a championship of <paramref name="rounds"/>: a third, rounded up.</summary>
    public static int WetRounds(int rounds) => (rounds + 2) / 3;

    /// <summary>
    /// Gives every round of an NG+ season that hasn't one its floor, once (it's saved with the
    /// round). Rounds added later (a guest drive, a test day) get strength and damage, and stay dry.
    /// </summary>
    /// <returns>True if any round changed.</returns>
    public static bool Fill(Career career, Random? random = null)
    {
        if (!career.IsNewGamePlus) return false;
        var season = career.CurrentSeason;
        var open = season.Rounds.Where(r => r.Floor is null && r.State is RoundState.Upcoming or RoundState.Armed).ToList();
        if (open.Count == 0) return false;

        random ??= Random.Shared;

        // Wet rounds are drawn once per championship, when none of its rounds has a floor yet.
        var wet = new HashSet<Round>();
        foreach (var series in season.Rounds.Where(r => !r.Guest && r.Test is null).GroupBy(r => r.SeriesId))
        {
            if (series.Any(r => r.Floor is not null)) continue;
            var pool = series.Where(r => r.State is RoundState.Upcoming or RoundState.Armed).ToList();
            for (var n = Math.Min(WetRounds(series.Count()), pool.Count); n > 0; n--)
            {
                var pick = Weighted(pool, r => RainWeight(r.TrackFolder), random);
                wet.Add(pick);
                pool.Remove(pick);
            }
        }

        foreach (var round in open)
        {
            var (low, high) = Band(season.CarFor(round).CarClass);
            round.Floor = new RoundFloor(random.Next(low, high + 1), wet.Contains(round), MinDamage(round.RaceMinutes));
        }
        return true;
    }

    private static T Weighted<T>(IReadOnlyList<T> items, Func<T, double> weight, Random random)
    {
        var total = items.Sum(weight);
        var roll = random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }
        return items[^1];
    }
}
