using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

public class PreparationTests
{
    private static readonly DateTimeOffset ArmedAt = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
    private static readonly CareerCar Car = new("Lexus RCF LMGT3", "GT3", "69", "Sample Racing");

    private static Round At(Round round, string course)
    {
        round.TrackCourse = course;
        return round;
    }

    /// <summary>A season with the given rounds counted, then an upcoming round at Le Mans.</summary>
    private static Career Season(decimal nextWeight, params Round[] counted)
    {
        var next = new Round { Number = counted.Length + 1, EventName = "Le Mans 24 Hours", TrackCourse = "Circuit de la Sarthe", PointsWeight = nextWeight };
        return CareerWith(0, "GT3", [.. counted, next]);
    }

    private static Round Next(Career career) => career.CurrentSeason.NextRound!;

    private static List<string> Lines(Career career) => Preparation.Stakes(career, Next(career)).Lines.Select(l => l.ToString()).ToList();

    // ---------- Stakes ----------

    [Fact]
    public void Stakes_spell_out_the_points_to_pass_the_driver_ahead()
    {
        // After a P2: AI 1 has 25, the player 18. Winning, they pass AI 1 if AI 1 is third or lower.
        var lines = Lines(Season(1, Counted(1, 2)));
        Assert.Equal("You're P2 in the championship with 18 points.", lines[0]);
        Assert.Equal("AI 1 is 7 ahead in P1: win with them P3 or lower and you're past.", lines[1]);
        // AI 3 is 3 behind: a win for them beats anything but beating them.
        Assert.Equal("AI 3 is 3 behind in P3: beat them and you stay ahead.", lines[2]);
    }

    [Fact]
    public void Stakes_say_what_finish_keeps_the_driver_behind_behind()
    {
        // Two wins: 50 to AI 2's 36. Even if AI 2 wins, P4 (12) keeps the player ahead: 62 to 61.
        Assert.Contains("AI 2 is 14 behind in P2: finish P4 or better and you stay ahead, whatever they do.", Lines(Season(1, Counted(1, 1), Counted(2, 1))));

        // Three wins: 75 to 54, 21 apart. Even if AI 2 wins, P7 (6) or better keeps the player ahead.
        Assert.Contains("AI 2 is 21 behind in P2: finish P7 or better and you stay ahead, whatever they do.",
            Lines(Season(1, Counted(1, 1), Counted(2, 1), Counted(3, 1))));

        // Five wins: 125 to 90, more than one race can close.
        Assert.Contains("AI 2 is 35 behind in P2: you stay ahead of them whatever happens this round.",
            Lines(Season(1, Enumerable.Range(1, 5).Select(n => Counted(n, 1)).ToArray())));
    }

    [Fact]
    public void A_driver_too_far_ahead_is_out_of_reach_and_big_rounds_say_so()
    {
        // Nine P3s against AI 2's nine P2s: 27 points, more than a win's 25.
        var lines = Lines(Season(1, Enumerable.Range(1, 9).Select(n => Counted(n, 3)).ToArray()));
        Assert.Contains("AI 2 is 27 ahead in P2: out of reach this round, with 25 points for a win.", lines);

        Assert.Contains("Double points: 50 for a class win.", Lines(Season(2, Counted(1, 2))));
        Assert.Equal("No points on the board yet: this is where your championship starts.", Lines(Season(1))[0]);
    }

    [Fact]
    public void Stakes_name_an_open_bet_and_a_sponsor_who_wants_a_podium_here()
    {
        var career = Season(2, Counted(1, 1));
        career.CurrentSeason.Interviews.Add(new InterviewAnswer { Round = 1, EventName = "Round 1", Moment = Moment.FirstWin, Tone = InterviewTone.Confident });
        career.CurrentSeason.Sponsors.Add(new SponsorDeal { Name = "Motul", Objective = ObjectiveKind.PodiumAt, RoundNumber = 2, Reward = 4 });

        var lines = Lines(career);
        Assert.Contains("You talked big after the Round 1: miss the podium here and it costs 2 reputation.", lines);
        Assert.Contains("Motul wants a podium here: 4 reputation riding on it.", lines);

        // A bet from two rounds back was settled by the round in between.
        var older = Season(1, Counted(1, 1), Counted(2, 1));
        older.CurrentSeason.Interviews.Add(new InterviewAnswer { Round = 1, EventName = "Round 1", Moment = Moment.Win, Tone = InterviewTone.Confident });
        Assert.DoesNotContain(Lines(older), l => l.StartsWith("You talked big"));
    }

    // ---------- History and pace ----------

    [Fact]
    public void History_lists_races_at_this_layout_in_this_class_newest_first()
    {
        var career = Season(1,
            At(Counted(1, 4), "Circuit de la Sarthe"),
            At(Counted(2, 1), "Sebring International Raceway"),
            At(Counted(3, 2), "Circuit de la Sarthe"));
        var history = Preparation.History(career, Next(career));

        Assert.Equal([3, 1], history.Select(v => v.Round));
        Assert.Equal([2, 4], history.Select(v => v.Finish));
    }

    private static Round WithGap(Round round, double gap)
    {
        // Counted gives the player a best lap of 100.5 + rank and the AI 1 (fastest) 101.5: shift the player's.
        var me = round.Result!.Entries.First(e => e.Entry.IsPlayer);
        var fastestAi = round.Result.Entries.Where(e => !e.Entry.IsPlayer).Min(e => e.Entry.BestLapSeconds)!.Value;
        round.Result.Entries = round.Result.Entries.Select(e => e == me ? e with { Entry = e.Entry with { BestLapSeconds = fastestAi + gap } } : e).ToList();
        return round;
    }

    [Fact]
    public void Pace_comes_from_practice_then_this_track_then_the_season_kept_small()
    {
        // Two visits here: 1 s slower last time, 2 s quicker before that; the latest counts double.
        var track = Season(1,
            WithGap(At(Counted(1, 2), "Circuit de la Sarthe"), -2.0),
            WithGap(At(Counted(2, 2), "Circuit de la Sarthe"), 1.0));
        var fromTrack = Preparation.Pace(track, Next(track))!;
        Assert.Equal(PaceSource.Track, fromTrack.Source);
        Assert.Equal(0, fromTrack.Gap);
        Assert.Equal(PaceAdvice.Matched, fromTrack.Advice);

        // Practice this weekend wins over history.
        Next(track).Practice = new PracticeRead(1, 12, 99.0, 101.0, "AI 1", 101.0);
        var fromPractice = Preparation.Pace(track, Next(track))!;
        Assert.Equal((PaceSource.Practice, PaceAdvice.TooEasy), (fromPractice.Source, fromPractice.Advice));

        // A first visit: three seconds a lap quicker elsewhere is "a bit easy", not "too easy".
        var elsewhere = Season(1, WithGap(At(Counted(1, 2), "Sebring International Raceway"), -3.0));
        var fromSeason = Preparation.Pace(elsewhere, Next(elsewhere))!;
        Assert.Equal((PaceSource.Season, PaceAdvice.BitEasy), (fromSeason.Source, fromSeason.Advice));

        var slow = Season(1, WithGap(At(Counted(1, 2), "Sebring International Raceway"), 3.0));
        Assert.Equal(PaceAdvice.BitHard, Preparation.Pace(slow, Next(slow))!.Advice);
    }

    // ---------- Practice ----------

    private static Round Armed() => new()
    {
        Number = 1, EventName = "Le Mans 24 Hours", TrackCourse = "Circuit de la Sarthe", RaceMinutes = 30,
        FuelMultiplier = 3.5, TireMultiplier = 3.5, State = RoundState.Armed, ArmedAt = ArmedAt,
    };

    private static SessionCheck Practice(int minutesAfterArming, string carType = "Lexus RCF LMGT3", string course = "Circuit de la Sarthe", int laps = 10, int playerPos = 2)
    {
        var session = new ResultsXml { Session = "Practice1", Course = course }
            .Car("Sam Sample", player: true, number: "69", carType: carType, classPos: playerPos, laps: laps)
            .Car("Dan Harper", classPos: playerPos == 1 ? 2 : 1)
            .Car("Hyper Fast", carClass: "Hyper", carType: "Toyota GR010", classPos: 1)
            .Parse();
        return RoundMatcher.Check(session, ArmedAt.AddMinutes(minutesAfterArming), Armed(), Car);
    }

    [Fact]
    public void Practice_after_the_weekend_starts_is_read_for_pace()
    {
        // ResultsXml's best laps are 100.5 + class position: the player 102.5, Dan Harper 101.5.
        var read = Preparation.ReadPractice([Practice(20), Practice(40, laps: 8)], Armed(), Car)!;
        Assert.Equal(2, read.Sessions);
        Assert.Equal(18, read.Laps);
        Assert.Equal(102.5, read.PlayerBest);
        Assert.Equal(101.5, read.AiBest);
        Assert.Equal("Dan Harper", read.AiBestDriver);
        Assert.Equal(101.5, read.TargetLap);
        Assert.Equal(1.0, read.Gap);
    }

    [Fact]
    public void Practice_before_the_weekend_elsewhere_or_in_another_car_isnt_read()
    {
        Assert.Null(Preparation.ReadPractice([Practice(-10)], Armed(), Car));
        // Quicker than the AI already: no target to chase.
        Assert.Null(Preparation.ReadPractice([Practice(20, playerPos: 1)], Armed(), Car)!.TargetLap);
        Assert.Null(Preparation.ReadPractice([Practice(20, course: "Sebring International Raceway")], Armed(), Car));
        Assert.Null(Preparation.ReadPractice([Practice(20, carType: "Ford Mustang LMGT3")], Armed(), Car));
    }
}
