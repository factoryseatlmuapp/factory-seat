using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

public enum MatchVerdict
{
    /// <summary>Belongs to the round.</summary>
    Match,

    /// <summary>Right track and car, but a setting differs from the briefing; the player decides.</summary>
    NearMiss,

    /// <summary>Not part of the round at all.</summary>
    Ignored,
}

public sealed record SessionCheck(
    SessionResult Session,
    DateTimeOffset WrittenAt,
    MatchVerdict Verdict,
    IReadOnlyList<string> Reasons)
{
    public string FileName => Path.GetFileName(Session.SourceFile);
}

/// <summary>
/// Decides whether a session in the results folder belongs to the armed round. LMU writes every
/// session there (online races, one-off races, a quick test drive), so only one that matches the
/// round's briefing counts.
/// </summary>
public static class RoundMatcher
{
    private const double MultiplierTolerance = 0.001;

    /// <param name="writtenAt">When the results file was written (its last-modified time).</param>
    /// <param name="claimedBy">Name of another career that already counted this file, if any.</param>
    public static SessionCheck Check(
        SessionResult session, DateTimeOffset writtenAt, Round round, CareerCar car, string? claimedBy = null)
    {
        SessionCheck Ignored(string reason) => new(session, writtenAt, MatchVerdict.Ignored, [reason]);

        if (claimedBy is not null) return Ignored($"already counted in the career \"{claimedBy}\"");
        if (round.ArmedAt is not DateTimeOffset armedAt) return Ignored("the round hasn't been started");
        if (writtenAt < armedAt) return Ignored("written before the round was started");
        if (session.Kind is not (SessionKind.Race or SessionKind.Qualifying))
            return Ignored($"{session.Kind.ToString().ToLowerInvariant()} session");
        if (!session.IsSinglePlayer) return Ignored("multiplayer session");
        if (!SameLayout(session, round))
            return Ignored(round.LayoutFile.Length > 0
                ? $"wrong track ({session.TrackCourse}, {session.LayoutFile})"
                : $"wrong track ({session.TrackCourse})");
        if (session.Player is not { } player) return Ignored("no player car in the session");

        var differences = new List<string>();
        if (car.CarType.Length == 0)
        {
            // A car no race has shown yet (the catalog doesn't know how LMU names it): any car in the
            // right class is a near miss, and confirming it teaches the career the name.
            if (!player.CarClass.Equals(car.CarClass, StringComparison.OrdinalIgnoreCase))
                return Ignored($"wrong car ({player.CarType})");
            differences.Add($"first race in this car: LMU calls it \"{player.CarType}\"");
        }
        else if (!car.IsCarType(player.CarType))
        {
            return Ignored($"wrong car ({player.CarType})");
        }

        // Players without a Race Control custom team race under whichever real livery they pick, so
        // the number (and team) is learned from the first race, and changes when they switch livery.
        // A custom team car counts whatever number is on it this week.
        if (car.CarNumber.Length == 0)
            differences.Add($"first race of the season: you raced as #{player.CarNumber}{TeamSuffix(player)}");
        else if (car.CustomTeam && !player.IsCustomTeam)
            differences.Add($"raced #{player.CarNumber}{TeamSuffix(player)}, not your custom team car");
        else if (!car.CustomTeam && player.CarNumber != car.CarNumber)
            differences.Add($"car number #{player.CarNumber}{TeamSuffix(player)}, career car is #{car.CarNumber}");
        if (session.RaceMinutes != round.RaceMinutes)
            differences.Add($"race length {session.RaceMinutes} min, briefing says {round.RaceMinutes}");
        if (Math.Abs(session.FuelMultiplier - round.FuelMultiplier) > MultiplierTolerance)
            differences.Add($"fuel x{session.FuelMultiplier:0.##}, briefing says x{round.FuelMultiplier:0.##}");
        if (Math.Abs(session.TireMultiplier - round.TireMultiplier) > MultiplierTolerance)
            differences.Add($"tires x{session.TireMultiplier:0.##}, briefing says x{round.TireMultiplier:0.##}");

        return new SessionCheck(session, writtenAt,
            differences.Count == 0 ? MatchVerdict.Match : MatchVerdict.NearMiss, differences);
    }

    private static string TeamSuffix(EntryResult player) =>
        player.TeamName.Length > 0 ? $" for {player.TeamName}" : "";

    /// <summary>
    /// Rounds match on the layout file, since LMU writes the same TrackCourse for different layouts
    /// (Spa and Spa ELMS are both "Circuit de Spa-Francorchamps"). Rounds without one fall back to
    /// the name.
    /// </summary>
    private static bool SameLayout(SessionResult session, Round round) =>
        round.LayoutFile.Length > 0
            ? session.LayoutFile.Equals(round.LayoutFile, StringComparison.OrdinalIgnoreCase)
                && session.TrackFolder.Equals(round.TrackFolder, StringComparison.OrdinalIgnoreCase)
            : session.TrackCourse == round.TrackCourse;
}
