using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

public enum RoundStatus
{
    /// <summary>No session for the round has been written yet.</summary>
    Waiting,

    /// <summary>A finished race matches the briefing and can be scored.</summary>
    Ready,

    /// <summary>A finished race is right apart from a setting or two; the player decides whether it counts.</summary>
    NeedsConfirmation,

    /// <summary>The race was quit before the flag; the player reruns it or takes the DNF.</summary>
    QuitEarly,

    /// <summary>
    /// The race was left part-way after saving it in LMU to finish later. Nothing to decide until
    /// the finished race turns up, though the player can still take the DNF if they abandon it.
    /// </summary>
    SavedToResume,
}

/// <param name="SavedAs">For <see cref="RoundStatus.SavedToResume"/>: the name LMU saved the race weekend under.</param>
public sealed record RoundEvaluation(
    RoundStatus Status,
    SessionCheck? Race,
    SessionCheck? Qualifying,
    IReadOnlyList<SessionCheck> Ignored,
    string? SavedAs = null)
{
    /// <summary>The race stopped before the flag, quit or saved: counting it means taking the DNF.</summary>
    public bool EndedEarly => Status is RoundStatus.QuitEarly or RoundStatus.SavedToResume;
}

/// <summary>Arming, evaluating and accepting a round.</summary>
public static class RoundFlow
{
    /// <summary>Starts the race weekend for the season's next round, so results written from now on can count.</summary>
    public static Round Arm(Season season, DateTimeOffset now)
    {
        var round = season.NextRound ?? throw new InvalidOperationException("The season has no rounds left.");
        if (round.State == RoundState.Armed) return round;

        round.State = RoundState.Armed;
        round.ArmedAt = now;
        return round;
    }

    public static void Disarm(Round round)
    {
        if (round.State != RoundState.Armed) return;
        round.State = RoundState.Upcoming;
        round.ArmedAt = null;
    }

    public static void Skip(Round round)
    {
        if (round.State == RoundState.Completed) throw new InvalidOperationException("The round has already been run.");
        round.State = RoundState.Skipped;
        round.ArmedAt = null;
    }

    /// <summary>
    /// Picks the session that decides the round. A finished, matching race wins; restarts leave
    /// several race files, and the last finished one counts.
    /// </summary>
    public static RoundEvaluation Evaluate(IEnumerable<SessionCheck> checks)
    {
        var all = checks.ToList();
        var relevant = all.Where(c => c.Verdict != MatchVerdict.Ignored).ToList();
        var races = relevant.Where(c => c.Session.Kind == SessionKind.Race).OrderByDescending(c => c.WrittenAt).ToList();

        var (status, race) =
            races.FirstOrDefault(c => c.Verdict == MatchVerdict.Match && c.Session.IsComplete) is { } ready ? (RoundStatus.Ready, ready)
            : races.FirstOrDefault(c => c.Session.IsComplete) is { } near ? (RoundStatus.NeedsConfirmation, near)
            : races.FirstOrDefault() is { } quit ? (RoundStatus.QuitEarly, quit)
            : (RoundStatus.Waiting, (SessionCheck?)null);

        var qualifying = relevant
            .Where(c => c.Session.Kind == SessionKind.Qualifying && (race is null || c.WrittenAt <= race.WrittenAt))
            .MaxBy(c => c.WrittenAt);

        return new RoundEvaluation(status, race, qualifying,
            all.Where(c => c.Verdict == MatchVerdict.Ignored).ToList());
    }

    /// <summary>Scores the round from the evaluation and stores the result in it.</summary>
    /// <param name="takeDnf">Required for a race that was quit before the flag.</param>
    /// <param name="acceptDifferences">Required for a race whose settings didn't fully match the briefing.</param>
    public static ScoredRace Accept(
        Round round, RoundEvaluation evaluation, DateTimeOffset now, bool takeDnf = false, bool acceptDifferences = false)
    {
        if (round.State != RoundState.Armed) throw new InvalidOperationException("Only the armed round can be accepted.");
        var check = evaluation.Race ?? throw new InvalidOperationException("There's no race to accept yet.");

        if (evaluation.EndedEarly && !takeDnf)
            throw new InvalidOperationException("The race was quit early: rerun it or take the DNF.");
        if (check.Verdict == MatchVerdict.NearMiss && !acceptDifferences)
            throw new InvalidOperationException("The race doesn't match the briefing: " + string.Join("; ", check.Reasons));

        var race = evaluation.EndedEarly ? AsQuitByPlayer(check.Session) : check.Session;
        var scored = RaceScorer.Score(race, evaluation.Qualifying?.Session,
            weight: round.PointsWeight,
            playerMinimumDriveShare: Features.AiDriverSwaps ? round.MinimumDriveShare : 0,
            roundName: round.EventName);

        round.State = RoundState.Completed;
        round.Result = new RoundResult
        {
            RaceFile = check.FileName,
            QualifyingFile = evaluation.Qualifying?.FileName,
            AcceptedAt = now,
            QuitEarly = evaluation.EndedEarly,
            AcceptedDespite = check.Verdict == MatchVerdict.NearMiss ? check.Reasons : [],
            PlayerPenalties = race.Player is { } player
                // A disqualification is logged as a penalty too, but the DQ result already counts it.
                ? race.Events.Count(e => e.Kind == RaceEventKind.Penalty && e.Text != "Disqualify" && e.Driver is { } driver
                    && Standings.NameKey(driver) == Standings.NameKey(player.Name))
                : 0,
            Entries = scored.Entries
                .Select(e => e.Entry.IsPlayer ? e : e with { Entry = e.Entry with { LapRecords = [] } })
                .ToList(),
        };
        return scored;
    }

    /// <summary>
    /// When the player quits, LMU leaves every other car with no finish status. Taking the DNF
    /// freezes the running order at that moment: the others count as finished where they were.
    /// A player who was disqualified before quitting stays disqualified.
    /// </summary>
    private static SessionResult AsQuitByPlayer(SessionResult race) => race with
    {
        Entries = race.Entries
            .Select(e => e.Status != FinishStatus.None ? e
                : e with { Status = e.IsPlayer ? FinishStatus.Dnf : FinishStatus.Finished })
            .ToList(),
    };
}
