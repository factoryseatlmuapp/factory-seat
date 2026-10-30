using LmuCareer.Core.Content;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

/// <summary>An achievement a career has earned, and where.</summary>
public sealed class EarnedAchievement
{
    public string Id { get; set; } = "";
    public DateTimeOffset EarnedAt { get; set; }
    public int Season { get; set; }

    /// <summary>The event it was earned at, for one earned at a race; empty otherwise.</summary>
    public string EventName { get; set; } = "";

    /// <summary>The car it was earned in, as LMU names it, for one earned at a race.</summary>
    public string CarType { get; set; } = "";

    /// <summary>Earned before achievements existed: found on the career's first open in 1.4, not as it happened.</summary>
    public bool Backfilled { get; set; }

    /// <summary>The player has been shown it (the unlock pop-up, or the note about backfilled ones).</summary>
    public bool Seen { get; set; }
}

/// <param name="Secret">Hidden (no rule, no progress) until earned.</param>
/// <param name="NewGamePlus">Only earned in a New Game+ career.</param>
/// <param name="Goal">For one with something to count: the count that earns it.</param>
public sealed record AchievementInfo(string Id, bool Secret = false, bool NewGamePlus = false, int? Goal = null);

/// <summary>Where a career stands on an achievement it hasn't earned.</summary>
public sealed record AchievementProgress(string Id, int Done, int Goal);

/// <summary>
/// Achievements: each checks what the career's saved results prove, never what a round was meant
/// to be. Normal ones measure progression, loyalty and longevity, not skill; the hard ones wait
/// behind New Game+. Ids are sim-agnostic so they can carry into other sims.
/// </summary>
public static class Achievements
{
    public const int CenturyRaces = 100;
    public const int OneMakeSeasons = 3;

    /// <summary>A full 24-hour race, as the results file has it.</summary>
    public const int TwentyFourHours = 1440;

    public static readonly IReadOnlyList<AchievementInfo> All =
    [
        new("rook"),
        new("on-their-radar"),
        new("factory-driver", Goal: 3),
        new("one-make", Goal: OneMakeSeasons),
        new("long-way-round"),
        new("am-to-pro"),
        new("called-up"),
        new("burned-bridge", Secret: true),
        new("iron-man"),
        new("sponsors-favourite"),
        new("raced-the-24"),
        new("century", Goal: CenturyRaces),
        new("sonny-hayes", Secret: true),
        new("hard-door"),
        new("nemesis"),
        new("pole", NewGamePlus: true),
        new("win-from-pole", NewGamePlus: true),
        new("double-points", NewGamePlus: true),
        new("crown-jewels", NewGamePlus: true, Goal: 3),
        new("both-sides", NewGamePlus: true, Goal: 2),
    ];

    /// <summary>
    /// Checks every achievement the career hasn't earned and records the ones it now has. The
    /// first check of a career from before achievements marks what it had already done as backfilled.
    /// </summary>
    /// <returns>The ones earned by this check.</returns>
    public static IReadOnlyList<EarnedAchievement> Update(Career career, ContentCatalog catalog, DateTimeOffset now)
    {
        var backfill = !career.AchievementsStarted;
        career.AchievementsStarted = true;
        var earned = new List<EarnedAchievement>();
        foreach (var info in All.Where(a => (!a.NewGamePlus || career.NewGamePlus is not null) && !career.Achievements.Any(e => e.Id == a.Id)))
        {
            if (Check(career, info.Id, catalog) is not { } where) continue;
            var achievement = new EarnedAchievement
            {
                Id = info.Id,
                EarnedAt = now,
                // Judged on how the career stands now: backfilled, the season it really happened in isn't known.
                Season = backfill && where.SeasonOf is null ? 0 : where.Season,
                EventName = where.Round?.EventName ?? "",
                CarType = where.Round is { } round ? where.SeasonOf!.CarFor(round).CarType : "",
                Backfilled = backfill,
            };
            career.Achievements.Add(achievement);
            earned.Add(achievement);
        }
        return earned;
    }

    /// <summary>Marks every earned achievement as shown to the player.</summary>
    public static bool MarkSeen(Career career)
    {
        var unseen = career.Achievements.Where(a => !a.Seen).ToList();
        foreach (var achievement in unseen) achievement.Seen = true;
        return unseen.Count > 0;
    }

    /// <summary>Progress on each unearned, non-secret achievement that has something to count.</summary>
    public static IReadOnlyList<AchievementProgress> Progress(Career career, ContentCatalog catalog)
    {
        var progress = new List<AchievementProgress>();
        foreach (var info in All.Where(a => a.Goal is not null && !a.Secret && (!a.NewGamePlus || career.NewGamePlus is not null)
            && !career.Achievements.Any(e => e.Id == a.Id)))
        {
            var done = info.Id switch
            {
                "century" => Raced(career).Count(),
                "one-make" => SeasonsByBrand(career, catalog).Select(g => g.Count()).DefaultIfEmpty().Max(),
                "factory-driver" => career.Loyalty.Select(l => (int)Loyalties.Stage(l.Points)).DefaultIfEmpty().Max(),
                "crown-jewels" => Credited(career).Where(x => Won(x.Round) && FullLength(x.Round, x.Round.RealDurationHours * 60))
                    .Select(x => Jewel(x.Round)).OfType<string>().Distinct().Count(),
                "both-sides" => Finished(career).Select(Titles).DefaultIfEmpty().Max(),
                _ => 0,
            };
            progress.Add(new AchievementProgress(info.Id, Math.Min(done, info.Goal!.Value), info.Goal!.Value));
        }
        return progress;
    }

    private sealed record Where(int Season, Season? SeasonOf = null, Round? Round = null);

    /// <summary>Where the career earned the achievement, or null if it hasn't.</summary>
    private static Where? Check(Career career, string id, ContentCatalog catalog)
    {
        var now = new Where(career.CurrentSeason.Number);
        Where At((Season Season, Round Round) x) => new(x.Season.Number, x.Season, x.Round);
        Where? AtFirst(IEnumerable<(Season Season, Round Round)> rounds) => rounds.Select(x => (x.Season, x.Round)).FirstOrDefault() is { Round: not null } x ? At(x) : null;
        Where? InSeason(Season? season) => season is null ? null : new Where(season.Number, season);

        switch (id)
        {
            case "rook":
                return AtFirst(Raced(career));
            case "on-their-radar":
                return career.Loyalty.Any(l => Loyalties.Stage(l.Points) >= LoyaltyStage.OnTheirRadar) ? now : null;
            case "factory-driver":
                return career.Loyalty.Any(l => Loyalties.Stage(l.Points) == LoyaltyStage.FactoryDriver) ? now : null;
            case "one-make":
                return SeasonsByBrand(career, catalog).FirstOrDefault(g => g.Count() >= OneMakeSeasons) is { } make
                    ? InSeason(make.ElementAt(OneMakeSeasons - 1))
                    : null;
            case "long-way-round":
                return career.Path == CareerPath.Prototype ? InSeason(Seasons(career).FirstOrDefault(s => s.Car.CarClass == "Hyper")) : null;
            case "am-to-pro":
                return career.GteClassic ? InSeason(Seasons(career).FirstOrDefault(s => s.Contract?.Tier == 1)) : null;
            case "called-up":
                return career.Interest.Any(i => i.Tested) ? now : null;
            case "burned-bridge":
                return career.Loyalty.Any(l => l.Burned) ? now : null;
            case "iron-man":
                return InSeason(Finished(career).FirstOrDefault(IronMan));
            case "sponsors-favourite":
                return InSeason(Finished(career).FirstOrDefault(s =>
                    s.Sponsors.Count > 0 && s.Sponsors.All(d => Sponsorship.Progress(s, d).Status == ObjectiveStatus.Met)));
            case "raced-the-24":
                return AtFirst(Raced(career).Where(x => x.Round.TrackCourse.StartsWith("Circuit de la Sarthe", StringComparison.OrdinalIgnoreCase)));
            case "century":
                return Raced(career).Skip(CenturyRaces - 1).Select(At).FirstOrDefault();
            case "sonny-hayes":
                return AtFirst(Raced(career).Where(x =>
                    x.Round.TrackCourse.Contains("Daytona", StringComparison.OrdinalIgnoreCase)
                    && Me(x.Round) is { Classified: true, ClassRank: 1 }
                    && x.Season.CarFor(x.Round).CarType.Contains("911 GT3 R", StringComparison.OrdinalIgnoreCase)));
            case "hard-door":
                return InSeason(Seasons(career).FirstOrDefault(s => s.Car.CarClass == "Hyper" && s.Contract?.ReputationDoor == true));
            case "nemesis":
                return InSeason(Finished(career).FirstOrDefault(s => BeatRival(career, s)));

            // New Game+: only races that passed their floor count.
            case "pole":
                return AtFirst(Credited(career).Where(x => Me(x.Round)?.ClassPole == true));
            case "win-from-pole":
                return AtFirst(Credited(career).Where(x => Won(x.Round) && Me(x.Round)!.ClassPole));
            case "double-points":
                return AtFirst(Credited(career).Where(x => Won(x.Round) && FullLength(x.Round, TwentyFourHours)));
            case "crown-jewels":
            {
                var jewels = new HashSet<string>();
                foreach (var x in Credited(career).Where(x => Won(x.Round) && FullLength(x.Round, x.Round.RealDurationHours * 60)))
                    if (Jewel(x.Round) is { } jewel && jewels.Add(jewel) && jewels.Count == 3) return At(x);
                return null;
            }
            case "both-sides":
                return InSeason(Finished(career).FirstOrDefault(s => Titles(s) >= 2));
            default:
                return null;
        }
    }

    /// <summary>Every season so far, oldest first, the current one included.</summary>
    private static IEnumerable<Season> Seasons(Career career) => career.PastSeasons.Append(career.CurrentSeason);

    /// <summary>Seasons raced to the end, oldest first.</summary>
    private static IEnumerable<Season> Finished(Career career) => Seasons(career).Where(s => s.IsFinished);

    /// <summary>Every round the player raced and counted (not test days), in order.</summary>
    private static IEnumerable<(Season Season, Round Round)> Raced(Career career) =>
        Seasons(career).SelectMany(s => s.Rounds
            .Where(r => r.State == RoundState.Completed && r.Result is not null && r.Test is null)
            .OrderBy(r => r.Result!.AcceptedAt)
            .Select(r => (s, r)));

    private static Scoring.ScoredEntry? Me(Round round) => round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);

    private static bool Won(Round round) => Me(round) is { Classified: true, ClassRank: 1 };

    /// <summary>New Game+: counted races that passed their floor, in order (guest drives carry no floor).</summary>
    private static IEnumerable<(Season Season, Round Round)> Credited(Career career) =>
        Raced(career).Where(x => FloorChecks.Credited(x.Round));

    /// <summary>Raced as long as the real event (or at least <paramref name="minutes"/>), as briefed, to the end.</summary>
    private static bool FullLength(Round round, double minutes) =>
        minutes > 0 && round.RaceMinutes >= minutes && round.Result is { QuitEarly: false, AcceptedDespite.Count: 0 };

    /// <summary>Daytona, Sebring or Le Mans: the crown jewels of endurance racing.</summary>
    private static string? Jewel(Round round) =>
        round.TrackCourse.Contains("Daytona", StringComparison.OrdinalIgnoreCase) ? "daytona"
        : round.TrackCourse.Contains("Sebring", StringComparison.OrdinalIgnoreCase) ? "sebring"
        : round.TrackCourse.StartsWith("Circuit de la Sarthe", StringComparison.OrdinalIgnoreCase) ? "le-mans"
        : null;

    /// <summary>New Game+: class titles a finished season won in championships raced wholly above the floor (the season's own, IMSA).</summary>
    private static int Titles(Season season) =>
        season.Series.Select(x => (Id: x.Id, Car: x.Car)).Prepend((Id: "", Car: season.Car)).Count(c =>
            FloorChecks.Credited(season, c.Id)
            && season.Standings(c.Id).FirstOrDefault(t => t.CarClass.Equals(c.Car.CarClass, StringComparison.OrdinalIgnoreCase))?.Drivers.FirstOrDefault() is { IsPlayer: true });

    /// <summary>Finished seasons by the make of the season's car, in order.</summary>
    private static IEnumerable<IGrouping<string, Season>> SeasonsByBrand(Career career, ContentCatalog catalog) =>
        Finished(career)
            .Select(s => (Season: s, Brand: catalog.Car(s.Car.Folder)?.Brand ?? catalog.CarByType(s.Car.CarType)?.Brand))
            .Where(x => x.Brand is not null)
            .GroupBy(x => x.Brand!, x => x.Season);

    /// <summary>Every championship round raced (none skipped), and every one finished: no DNF, DQ or quitting early.</summary>
    private static bool IronMan(Season season)
    {
        var rounds = season.ChampionshipRounds.ToList();
        return rounds.Count > 0 && rounds.All(r =>
            r.State == RoundState.Completed && r.Result is { QuitEarly: false }
            && Me(r) is { } me && me.Entry.Status == FinishStatus.Finished);
    }

    /// <summary>A rival from that season or before finished the championship behind the player, in the player's class.</summary>
    private static bool BeatRival(Career career, Season season)
    {
        var rivals = career.Rivals.Where(r => r.Since <= season.Number).Select(r => Scoring.Standings.NameKey(r.Name)).ToHashSet();
        if (rivals.Count == 0) return false;
        var table = season.Standings().FirstOrDefault(s => s.CarClass.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase));
        if (table is null) return false;
        var drivers = table.Drivers.ToList();
        var me = drivers.FindIndex(d => d.IsPlayer);
        return me >= 0 && drivers.Skip(me + 1).Any(d => rivals.Contains(Scoring.Standings.NameKey(d.Name)));
    }
}
