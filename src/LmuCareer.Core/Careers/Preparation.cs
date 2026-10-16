using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>An earlier race at this layout in this class.</summary>
/// <param name="Gap">Best lap against the fastest AI in class, in seconds; negative when the player was quicker.</param>
public sealed record TrackVisit(int Season, int Round, string EventName, int? Finish, FinishStatus Status, double? BestLap, double? Gap);

/// <summary>
/// A practice session run after the round's weekend was started, read for pace: the player's best
/// lap and the AI's. Practice earns nothing; it's information for the race.
/// </summary>
/// <param name="TargetLap">What the engineer asks for in the race: the AI's best practice lap, when the player was slower than it; null when they were already quicker.</param>
public sealed record PracticeRead(int Sessions, int Laps, double? PlayerBest, double? AiBest, string AiBestDriver, double? TargetLap)
{
    public double? Gap => PlayerBest is { } mine && AiBest is { } ai ? Math.Round(mine - ai, 3) : null;
}

public enum PaceSource
{
    /// <summary>From practice this weekend.</summary>
    Practice,

    /// <summary>From earlier races at this layout, the latest weighing most.</summary>
    Track,

    /// <summary>From the season's recent rounds; a first visit here, so the advice is kept small.</summary>
    Season,
}

/// <summary>What to do about AI Strength for this race, and what it's based on.</summary>
/// <param name="Gap">Seconds a lap against the fastest AI; negative when the player is quicker.</param>
public sealed record TrackPace(PaceAdvice Advice, PaceSource Source, double Gap, int Visits);

/// <summary>A driver near the player in the standings, and what this round needs to pass them or stay ahead.</summary>
/// <param name="Gap">Points between them, always positive.</param>
/// <param name="Finish">
/// For the driver ahead: the place they'd have to finish or lower, with the player winning, for the
/// player to pass them. For the driver behind: the place the player needs to finish or better to stay
/// ahead even if they win. Null when no result this round settles it.
/// </param>
public sealed record StakesRival(string Name, int Position, decimal Gap, int? Finish, bool Settled);

/// <summary>What's riding on the round: the championship maths, the team's target, the points weight, bets and sponsors.</summary>
public sealed record RaceStakes(
    int? Position,
    decimal Points,
    StakesRival? Ahead,
    StakesRival? Behind,
    int Target,
    decimal Weight,
    IReadOnlyList<Phrase> Lines);

/// <summary>The briefing's view of the race ahead: the stakes, the player's history here, practice, and AI Strength advice.</summary>
public sealed record RacePreparation(RaceStakes? Stakes, IReadOnlyList<TrackVisit> History, PracticeRead? Practice, TrackPace? Pace);

public static class Preparation
{
    public static RacePreparation For(Career career, Round round) => new(
        round.Guest ? null : Stakes(career, round),
        History(career, round),
        round.Practice,
        Pace(career, round));

    // ---------- History ----------

    /// <summary>Earlier counted races at the round's layout in the class the player races it in, newest first.</summary>
    public static IReadOnlyList<TrackVisit> History(Career career, Round round)
    {
        var carClass = (round.GuestCar ?? career.CurrentSeason.Car).CarClass;
        return career.PastSeasons.Append(career.CurrentSeason)
            .SelectMany(s => s.Rounds.Select(r => (Season: s, Round: r)))
            .Where(x => x.Round != round && x.Round.State == RoundState.Completed && x.Round.Result is not null && SameLayout(x.Round, round))
            .Select(x => (x.Season, x.Round, Me: x.Round.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer)))
            .Where(x => x.Me is not null && x.Me.Entry.CarClass.Equals(carClass, StringComparison.OrdinalIgnoreCase))
            .Select(x => new TrackVisit(x.Season.Number, x.Round.Number, x.Round.EventName, x.Me!.ClassRank, x.Me.Entry.Status,
                x.Me.Entry.BestLapSeconds > 0 ? x.Me.Entry.BestLapSeconds : null, PaceCheck.For(x.Round)?.Gap))
            .OrderByDescending(v => v.Season).ThenByDescending(v => v.Round)
            .ToList();
    }

    private static bool SameLayout(Round a, Round b) =>
        a.LayoutFile.Length > 0 && b.LayoutFile.Length > 0
            ? a.LayoutFile.Equals(b.LayoutFile, StringComparison.OrdinalIgnoreCase) && a.TrackFolder.Equals(b.TrackFolder, StringComparison.OrdinalIgnoreCase)
            : a.TrackCourse.Length > 0 && a.TrackCourse.Equals(b.TrackCourse, StringComparison.OrdinalIgnoreCase);

    // ---------- Practice ----------

    /// <summary>
    /// Reads the practice sessions run since the weekend started, at the round's layout in the
    /// round's car: the player's best lap and the best AI in class across them.
    /// </summary>
    public static PracticeRead? ReadPractice(IEnumerable<SessionCheck> checks, Round round, CareerCar car)
    {
        var sessions = checks
            .Where(c => c.Session.Kind == SessionKind.Practice && c.Session.IsSinglePlayer && RoundMatcher.SameLayout(c.Session, round)
                && round.ArmedAt is { } armed && c.WrittenAt >= armed)
            .Select(c => c.Session)
            .Where(s => s.Player is { } p && car.IsCarType(p.CarType))
            .ToList();
        if (sessions.Count == 0) return null;

        var mine = sessions.Select(s => s.Player!).ToList();
        var ai = sessions.SelectMany(s => s.Entries.Where(e => !e.IsPlayer && e.CarClass == s.Player!.CarClass && e.BestLapSeconds > 0)).ToList();
        var fastest = ai.MinBy(e => e.BestLapSeconds);
        var best = mine.Select(p => p.BestLapSeconds).Where(t => t > 0).Min();
        return new PracticeRead(
            sessions.Count,
            mine.Sum(p => p.Laps),
            best,
            fastest?.BestLapSeconds,
            fastest?.Name ?? "",
            best > fastest?.BestLapSeconds ? fastest.BestLapSeconds : null);
    }

    // ---------- Pace advice for this track ----------

    /// <summary>
    /// AI Strength advice for this race. Practice this weekend says the most; then earlier races
    /// here, the latest counting double the one before it; failing both, the season's recent form,
    /// kept to small changes on a first visit.
    /// </summary>
    public static TrackPace? Pace(Career career, Round round)
    {
        if (round.Practice?.Gap is { } practice) return new TrackPace(PaceCheck.AdviceFor(practice), PaceSource.Practice, practice, 0);

        var visits = History(career, round).Where(v => v.Gap is not null).ToList();
        if (visits.Count > 0)
        {
            double weighted = 0, total = 0, weight = 1;
            foreach (var visit in visits)
            {
                weighted += visit.Gap!.Value * weight;
                total += weight;
                weight /= 2;
            }
            var gap = Math.Round(weighted / total, 2);
            return new TrackPace(PaceCheck.AdviceFor(gap), PaceSource.Track, gap, visits.Count);
        }

        var season = PaceCheck.Summarize(career.CurrentSeason);
        if (season.RecentGap is not { } recent) return null;
        var softened = PaceCheck.AdviceFor(recent) switch
        {
            PaceAdvice.TooEasy => PaceAdvice.BitEasy,
            PaceAdvice.TooHard => PaceAdvice.BitHard,
            var advice => advice,
        };
        return new TrackPace(softened, PaceSource.Season, recent, 0);
    }

    // ---------- Stakes ----------

    public static RaceStakes Stakes(Career career, Round round)
    {
        var season = career.CurrentSeason;
        var target = season.Contract?.TargetPosition ?? Progression.DefaultTarget;
        var weight = round.PointsWeight;
        var points = ScoringRules.Wec.Points.Select(p => p * weight).ToList();
        var lines = new List<Phrase>();

        var drivers = season.Standings().FirstOrDefault(s => s.CarClass.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase))?.Drivers.ToList() ?? [];
        var me = drivers.FindIndex(d => d.IsPlayer);
        StakesRival? ahead = null, behind = null;
        decimal myPoints = 0;

        if (me < 0)
        {
            lines.Add(Phrase.Of("No points on the board yet: this is where your championship starts."));
        }
        else
        {
            myPoints = drivers[me].Points;
            lines.Add(Phrase.Of("You're P{n} in the championship with {points} points.", ("n", me + 1), ("points", myPoints)));

            if (me > 0)
            {
                var rival = drivers[me - 1];
                var gap = rival.Points - myPoints;
                // Winning, the player passes them when they finish low enough; the first place
                // (from 2nd down) where that's true, or none when even their scoring nothing won't do.
                int? finish = null;
                for (var place = 2; place <= points.Count + 1; place++)
                {
                    var theirs = place <= points.Count ? points[place - 1] : 0;
                    if (points[0] - theirs > gap) { finish = place; break; }
                }
                ahead = new StakesRival(rival.Name, me, gap, finish, false);
                var name = ("rival", (object?)rival.Name);
                lines.Add(finish switch
                {
                    null => Phrase.Of("{rival} is {gap} ahead in P{n}: out of reach this round, with {max} points for a win.",
                        name, ("gap", gap), ("n", me), ("max", points[0])),
                    2 => Phrase.Of("{rival} is {gap} ahead in P{n}: beat them and you're past.", name, ("gap", gap), ("n", me)),
                    _ when finish > points.Count => Phrase.Of("{rival} is {gap} ahead in P{n}: you need to win with them out of the points.", name, ("gap", gap), ("n", me)),
                    _ => Phrase.Of("{rival} is {gap} ahead in P{n}: win with them P{finish} or lower and you're past.", name, ("gap", gap), ("n", me), ("finish", finish)),
                });
            }

            if (me + 1 < drivers.Count)
            {
                var rival = drivers[me + 1];
                var gap = myPoints - rival.Points;
                // Even if they win: the lowest place that keeps the player ahead.
                int? finish = null;
                var safe = gap > points[0];
                if (!safe)
                {
                    for (var place = points.Count; place >= 2; place--)
                        if (gap + points[place - 1] > points[0]) { finish = place; break; }
                }
                behind = new StakesRival(rival.Name, me + 2, gap, finish, safe);
                var name = ("rival", (object?)rival.Name);
                lines.Add(safe
                    ? Phrase.Of("{rival} is {gap} behind in P{n}: you stay ahead of them whatever happens this round.", name, ("gap", gap), ("n", me + 2))
                    : finish is { } needed
                    ? Phrase.Of("{rival} is {gap} behind in P{n}: finish P{finish} or better and you stay ahead, whatever they do.", name, ("gap", gap), ("n", me + 2), ("finish", needed))
                    : Phrase.Of("{rival} is {gap} behind in P{n}: beat them and you stay ahead.", name, ("gap", gap), ("n", me + 2)));
            }
        }

        if (weight != 1)
            lines.Add(weight == 2
                ? Phrase.Of("Double points: {max} for a class win.", ("max", points[0]))
                : Phrase.Of("Points x{weight}: {max} for a class win.", ("weight", weight), ("max", points[0])));

        // A confident interview answer is settled by the next championship round: this one, if it's next.
        var previous = season.ChampionshipRounds.Where(r => r.Number < round.Number && r.State == RoundState.Completed).Select(r => r.Number).DefaultIfEmpty(0).Max();
        foreach (var bet in season.Interviews.Where(a => a.Tone == InterviewTone.Confident && a.Round == previous))
        {
            lines.Add(Paddock.IsGood(bet.Moment)
                ? Phrase.Of("You talked big after the {event}: miss the podium here and it costs 2 reputation.", ("event", bet.EventName))
                : Phrase.Of("You promised a comeback after the {event}: miss the team's target here and it costs 2 reputation.", ("event", bet.EventName)));
        }

        foreach (var deal in season.Sponsors.Where(d => d.Objective == ObjectiveKind.PodiumAt && d.RoundNumber == round.Number))
            lines.Add(Phrase.Of("{sponsor} wants a podium here: {reward} reputation riding on it.", ("sponsor", deal.Name), ("reward", deal.Reward)));

        return new RaceStakes(me >= 0 ? me + 1 : null, myPoints, ahead, behind, target, weight, lines);
    }
}
