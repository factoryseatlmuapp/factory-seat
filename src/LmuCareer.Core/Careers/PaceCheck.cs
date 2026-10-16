namespace LmuCareer.Core.Careers;

/// <summary>The player's best race lap against the fastest AI car's in their class, at one round.</summary>
/// <param name="Gap">Seconds a lap: negative when the player was quicker.</param>
public sealed record RoundPace(int RoundNumber, string EventName, double PlayerBest, double AiBest, double Gap);

public enum PaceAdvice
{
    /// <summary>Well clear of the AI: raise AI Strength.</summary>
    TooEasy,

    /// <summary>A little quicker than the AI: maybe a notch up.</summary>
    BitEasy,

    /// <summary>Close to the AI's pace.</summary>
    Matched,

    /// <summary>Well off the AI's pace: lower AI Strength.</summary>
    TooHard,

    /// <summary>A little off the AI's pace on a first visit: maybe a notch down.</summary>
    BitHard,
}

public sealed record PaceSummary(RoundPace? Last, double? RecentGap, int RecentRounds, PaceAdvice? Advice);

/// <summary>
/// How the player's pace compares with the AI, to suggest an AI Strength. Best race laps are a fair
/// yardstick: everyone runs the same fuel and tyre rules, and one bad lap doesn't drag a best lap
/// down. The advice looks at the last few championship rounds, so one odd race doesn't swing it.
/// </summary>
public static class PaceCheck
{
    public const int RecentRounds = 3;

    public static RoundPace? For(Round round)
    {
        if (round.State != RoundState.Completed || round.Result is not { } result) return null;
        var me = result.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.Entry;
        if (me?.BestLapSeconds is not double mine || mine <= 0) return null;

        var aiBest = result.Entries
            .Where(e => !e.Entry.IsPlayer && e.Entry.CarClass == me.CarClass && e.Entry.BestLapSeconds > 0)
            .Select(e => e.Entry.BestLapSeconds!.Value)
            .DefaultIfEmpty()
            .Min();
        return aiBest <= 0 ? null : new RoundPace(round.Number, round.EventName, mine, aiBest, Math.Round(mine - aiBest, 3));
    }

    public static PaceSummary Summarize(Season season)
    {
        var paces = season.ChampionshipRounds.Select(For).OfType<RoundPace>().ToList();
        if (paces.Count == 0) return new PaceSummary(null, null, 0, null);

        var recent = paces.TakeLast(RecentRounds).ToList();
        var gap = Math.Round(recent.Average(p => p.Gap), 2);
        return new PaceSummary(paces[^1], gap, recent.Count, AdviceFor(gap));
    }

    /// <summary>
    /// The advice as a change to LMU's AI Strength slider, in points: "a few points" for too easy or
    /// too hard, "a notch" for a bit. Factory Seat can't see where the slider is now, so it's a step,
    /// not a setting.
    /// </summary>
    public static int StepFor(PaceAdvice advice) => advice switch
    {
        PaceAdvice.TooEasy => 3,
        PaceAdvice.BitEasy => 1,
        PaceAdvice.BitHard => -1,
        PaceAdvice.TooHard => -3,
        _ => 0,
    };

    /// <summary>What a gap to the fastest AI (seconds a lap, negative when quicker) says about AI Strength.</summary>
    public static PaceAdvice AdviceFor(double gap) => gap switch
    {
        <= -1.0 => PaceAdvice.TooEasy,
        <= -0.4 => PaceAdvice.BitEasy,
        >= 1.5 => PaceAdvice.TooHard,
        _ => PaceAdvice.Matched,
    };
}
