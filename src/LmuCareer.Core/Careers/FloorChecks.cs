using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>Why a New Game+ race fell below its floor.</summary>
public enum FloorFlag
{
    /// <summary>Damage set below the round's floor.</summary>
    DamageLow,

    /// <summary>The Invulnerable aid was on.</summary>
    Invulnerable,

    /// <summary>Mechanical failures off.</summary>
    FailuresOff,

    /// <summary>Fuel or tyre use below real (1×).</summary>
    RatesLow,

    /// <summary>A wet round with no car on wet tyres.</summary>
    StayedDry,

    /// <summary>The AI Strength the weekend was raced at isn't the round's.</summary>
    AiStrength,

    /// <summary>AI aggression below High.</summary>
    Aggression,

    /// <summary>No proof of the AI Strength, and the player beat the class's fastest AI by a lot in qualifying and the race.</summary>
    Pace,
}

/// <summary>How a New Game+ race measured up to its floor, and what it did in the race that costs reputation.</summary>
public sealed class FloorCheck
{
    public List<FloorFlag> Flags { get; set; } = [];

    /// <summary>A car, AI included, ran a lap on wet tyres.</summary>
    public bool RanWet { get; set; }

    /// <summary>The AI Strength the weekend proved it was raced at (from LMU's save or settings), when known.</summary>
    public int? AiStrengthSeen { get; set; }

    /// <summary>How much faster than the class's fastest AI the player was, in percent, in qualifying and the race.</summary>
    public double? QualifyingGap { get; set; }
    public double? RaceGap { get; set; }

    /// <summary>Drive-throughs, stop-and-gos and time penalties the player got (track-limit ones included).</summary>
    public int Penalties { get; set; }

    public bool Disqualified { get; set; }

    /// <summary>Hit the barrier and didn't finish.</summary>
    public bool BarrierDnf { get; set; }

    /// <summary>Reputation the race cost, taken when it was counted (penalties, a DQ, a barrier DNF).</summary>
    public decimal ReputationCost { get; set; }

    /// <summary>The cost took reputation far enough below the class's bar that the team let the player go.</summary>
    public bool LetGo { get; set; }

    /// <summary>Below the floor: the points count, but nothing New Game+ comes of it.</summary>
    public bool Reduced => Flags.Count > 0;
}

/// <summary>New Game+'s checks on a counted race (design doc, Oct 16 and Oct 28).</summary>
public static class FloorChecks
{
    /// <summary>A race that earns New Game+ credit: checked against its floor, and not below it.</summary>
    public static bool Credited(Round round) => round.Result?.Floor is { Reduced: false };

    /// <summary>A championship whose every counted round earned New Game+ credit: its title goes on the New Game+ wing.</summary>
    public static bool Credited(Season season, string seriesId)
    {
        var counted = season.SeriesRounds(seriesId).Where(r => r.State == RoundState.Completed && r.Test is null).ToList();
        return counted.Count > 0 && counted.All(Credited);
    }

    /// <summary>Flagged rounds in one season that turn the career normal, for good.</summary>
    public const int FlagsToEnd = 3;

    /// <summary>Beating the class's fastest AI by more than this, in both sessions, suggests the AI was turned down.</summary>
    public const double PaceGapPercent = 2.5;

    /// <summary>
    /// Checks the race just counted against the round's floor and stores the result; a third
    /// flagged round in the season ends New Game+ for the career.
    /// </summary>
    public static void AfterRace(Career career, Round round, SessionResult race, SessionResult? qualifying, DateTimeOffset now,
        Content.LmuInstall? install = null)
    {
        if (!career.IsNewGamePlus || round.Floor is not { } floor || round.Result is not { } result) return;
        var (strength, aggression) = Proof(round, install);
        result.Floor = Check(floor, race, qualifying, strength);
        if (aggression is { } a && a < Floor.Aggression) result.Floor.Flags.Add(FloorFlag.Aggression);
        Consequences.AfterRace(career, round, result.Floor, now);

        var flagged = career.CurrentSeason.Rounds.Count(r => r.Result?.Floor?.Reduced == true);
        if (flagged >= FlagsToEnd) career.NewGamePlus!.EndedAt = now;
    }

    /// <summary>
    /// What LMU proves the AI was set to (design doc, Oct 28): the weekend's save first; without
    /// one, Settings.JSON at the weekend's start and now, which must agree.
    /// </summary>
    public static (int? Strength, int? Aggression) Proof(Round round, Content.LmuInstall? install)
    {
        if (install is null) return (null, null);
        var since = round.ArmedAt ?? DateTimeOffset.MinValue;
        var save = Content.AiProof.NewestSave([install.RaceSavesFolder, install.BackupSavesFolder], since);
        var now = Content.AiProof.Settings(install.SettingsFile);
        var start = round.AiAtStart;
        var aggression = new[] { start?.Aggression, now?.Aggression }.Where(a => a is not null).Min();

        if (save?.Strength is { } saved) return (saved, aggression);
        // LMU writes Settings.JSON on exit: one not written since the weekend started says nothing about it.
        if (!File.Exists(install.SettingsFile) || File.GetLastWriteTimeUtc(install.SettingsFile) < since.UtcDateTime) return (null, null);
        // Both reads must say the same: a slider turned down and back between them shows as the odd one out.
        int? strength = (start?.Strength, now?.Strength) switch
        {
            ({ } a, { } b) when a != b => round.Floor is { } f && a == f.AiStrength ? b : a,
            ({ } a, _) => a,
            (_, { } b) => b,
            _ => null,
        };
        return (strength, aggression);
    }

    public static FloorCheck Check(RoundFloor floor, SessionResult race, SessionResult? qualifying, int? aiStrengthSeen)
    {
        var check = new FloorCheck { AiStrengthSeen = aiStrengthSeen };
        var player = race.Player;

        if (race.DamageMultiplier is { } damage && damage < floor.MinDamage) check.Flags.Add(FloorFlag.DamageLow);
        if (player?.Control.Any(c => c.Controller == Controller.Player && c.Invulnerable) == true) check.Flags.Add(FloorFlag.Invulnerable);
        if (race.MechanicalFailures is 0) check.Flags.Add(FloorFlag.FailuresOff);
        if (race.FuelMultiplier < 1 || race.TireMultiplier < 1) check.Flags.Add(FloorFlag.RatesLow);

        check.RanWet = race.Entries.Any(e => e.LapRecords.Any(l => IsWet(l.FrontCompound) || IsWet(l.RearCompound)));
        if (floor.Wet && !check.RanWet) check.Flags.Add(FloorFlag.StayedDry);

        // The AI Strength: proof from LMU first; without it, the pace check (dry rounds only).
        if (aiStrengthSeen is { } seen)
        {
            if (seen != floor.AiStrength) check.Flags.Add(FloorFlag.AiStrength);
        }
        else if (player is not null)
        {
            check.QualifyingGap = qualifying is null ? null : Gap(qualifying, player.CarClass);
            check.RaceGap = Gap(race, player.CarClass);
            if (!floor.Wet && !check.RanWet && check.QualifyingGap > PaceGapPercent && check.RaceGap > PaceGapPercent)
                check.Flags.Add(FloorFlag.Pace);
        }

        if (player is not null)
        {
            var me = Standings.NameKey(player.Name);
            var mine = race.Events.Where(e => e.Driver is { } d && Standings.NameKey(d) == me).ToList();
            check.Penalties = mine.Count(e => e.Kind == RaceEventKind.Penalty && e.Text != "Disqualify");
            check.Disqualified = player.Status == FinishStatus.Dq;
            // The wall ended the race: the last contact, on the last lap or two, was with the barrier
            // (a tap on lap 3 and an engine failure on lap 40 isn't a barrier DNF).
            var lastLap = player.LapRecords.Count > 0 ? player.LapRecords.Max(l => l.ElapsedSeconds) : 0;
            var lastContact = mine.Where(e => e.Kind == RaceEventKind.Incident && e.ElapsedSeconds >= lastLap).MaxBy(e => e.ElapsedSeconds);
            check.BarrierDnf = player.Status == FinishStatus.Dnf && lastContact?.Object == "Immovable";
        }
        return check;
    }

    /// <summary>How much faster than the class's fastest AI the player's best lap was, in percent; negative when slower.</summary>
    private static double? Gap(SessionResult session, string carClass)
    {
        var player = session.Player;
        var ai = session.Entries.Where(e => !e.IsPlayer && e.CarClass == carClass && e.BestLapSeconds > 0).Min(e => e.BestLapSeconds);
        if (player?.BestLapSeconds is not > 0 || ai is null) return null;
        return Math.Round((ai.Value - player.BestLapSeconds.Value) / ai.Value * 100, 2);
    }

    private static bool IsWet(string? compound) => compound?.Contains("Wet", StringComparison.OrdinalIgnoreCase) == true;
}
