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
    IReadOnlyList<Phrase> Reasons)
{
    public string FileName => Path.GetFileName(Session.SourceFile);

    /// <summary>Ignored only because it was raced in another car.</summary>
    public bool WrongCar => Reasons.Any(r => r.Text == RoundMatcher.WrongCar);
}

/// <summary>
/// Decides whether a session in the results folder belongs to the armed round. LMU writes every
/// session there (online races, one-off races, a quick test drive), so only one that matches the
/// round's briefing counts.
/// </summary>
public static class RoundMatcher
{
    private const double MultiplierTolerance = 0.001;

    /// <summary>The reason a race in another car was ignored; the app tells the player about those.</summary>
    public const string WrongCar = "wrong car ({car})"; // phrase

    /// <param name="writtenAt">When the results file was written (its last-modified time).</param>
    /// <param name="claimedBy">Name of another career that already counted this file, if any.</param>
    public static SessionCheck Check(
        SessionResult session, DateTimeOffset writtenAt, Round round, CareerCar car, string? claimedBy = null)
    {
        SessionCheck Ignored(Phrase reason) => new(session, writtenAt, MatchVerdict.Ignored, [reason]);

        if (claimedBy is not null) return Ignored(Phrase.Of("already counted in the career \"{career}\"", ("career", claimedBy)));
        if (round.ArmedAt is not DateTimeOffset armedAt) return Ignored(Phrase.Of("the round hasn't been started"));
        if (writtenAt < armedAt) return Ignored(Phrase.Of("written before the round was started"));
        if (session.Kind is not (SessionKind.Race or SessionKind.Qualifying))
            return Ignored(session.Kind switch
            {
                SessionKind.Practice => Phrase.Of("practice session"),
                SessionKind.Warmup => Phrase.Of("warmup session"),
                _ => Phrase.Of("not a race or qualifying session"),
            });
        if (!session.IsSinglePlayer) return Ignored(Phrase.Of("multiplayer session"));
        if (!SameLayout(session, round))
            return Ignored(round.LayoutFile.Length > 0
                ? Phrase.Of("wrong track ({track}, {layout})", ("track", session.TrackCourse), ("layout", session.LayoutFile))
                : Phrase.Of("wrong track ({track})", ("track", session.TrackCourse)));
        if (session.Player is not { } player) return Ignored(Phrase.Of("no player car in the session"));

        var differences = new List<Phrase>();
        if (car.CarType.Length == 0)
        {
            // A car no race has shown yet (the catalog doesn't know how LMU names it): any car in the
            // right class is a near miss, and confirming it teaches the career the name.
            if (!player.CarClass.Equals(car.CarClass, StringComparison.OrdinalIgnoreCase))
                return Ignored(Phrase.Of(WrongCar, ("car", player.CarType)));
            differences.Add(Phrase.Of("first race in this car: LMU calls it \"{car}\"", ("car", player.CarType)));
        }
        else if (!car.IsCarType(player.CarType))
        {
            return Ignored(Phrase.Of(WrongCar, ("car", player.CarType)));
        }

        // Players without a Race Control custom team race under whichever real livery they pick, so
        // the number (and team) is learned from the first race, and changes when they switch livery.
        // A custom team car counts whatever number is on it this week.
        var number = ("number", (object?)player.CarNumber);
        var team = ("team", (object?)player.TeamName);
        var hasTeam = player.TeamName.Length > 0;
        if (car.CarNumber.Length == 0)
            differences.Add(hasTeam
                ? Phrase.Of("first race of the season: you raced as #{number} for {team}", number, team)
                : Phrase.Of("first race of the season: you raced as #{number}", number));
        else if (car.CustomTeam && !player.IsCustomTeam)
            differences.Add(hasTeam
                ? Phrase.Of("raced #{number} for {team}, not your custom team car", number, team)
                : Phrase.Of("raced #{number}, not your custom team car", number));
        else if (!car.CustomTeam && player.CarNumber != car.CarNumber)
            differences.Add(hasTeam
                ? Phrase.Of("car number #{number} for {team}, career car is #{career}", number, team, ("career", car.CarNumber))
                : Phrase.Of("car number #{number}, career car is #{career}", number, ("career", car.CarNumber)));
        if (session.RaceMinutes != round.RaceMinutes)
            differences.Add(Phrase.Of("race length {raced} min, briefing says {briefing}", ("raced", session.RaceMinutes), ("briefing", round.RaceMinutes)));
        if (Math.Abs(session.FuelMultiplier - round.FuelMultiplier) > MultiplierTolerance)
            differences.Add(Phrase.Of("fuel x{raced}, briefing says x{briefing}", ("raced", session.FuelMultiplier), ("briefing", round.FuelMultiplier)));
        if (Math.Abs(session.TireMultiplier - round.TireMultiplier) > MultiplierTolerance)
            differences.Add(Phrase.Of("tires x{raced}, briefing says x{briefing}", ("raced", session.TireMultiplier), ("briefing", round.TireMultiplier)));

        return new SessionCheck(session, writtenAt,
            differences.Count == 0 ? MatchVerdict.Match : MatchVerdict.NearMiss, differences);
    }

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
