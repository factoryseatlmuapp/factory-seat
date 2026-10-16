using LmuCareer.Core.Careers;

namespace LmuCareer.Core.Tests;

public class PaddockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
    private static readonly string[] Rivals = ["Dan Harper", "Lee Quick", "Ana Ruiz"];

    /// <summary>One race: the player's class finish, how it ended, where they started, and the rivals in the remaining places.</summary>
    private sealed record Race(int Pos, string Status = "Finished Normally", int Grid = 0, int Laps = 10);

    private static Career Career(decimal[] weights, int target = 3) => new()
    {
        Name = "Paddock run",
        DriverName = "Sam Sample",
        CreatedAt = Now,
        LastPlayedAt = Now,
        CurrentSeason = new Season
        {
            Number = 1,
            Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
            Contract = new Contract { TeamName = "Sample Racing", TargetPosition = target },
            Rounds = weights.Select((w, i) => new Round
            {
                Number = i + 1, EventName = $"Round {i + 1} Hours", TrackCourse = "Circuit de la Sarthe",
                RaceMinutes = 30, FuelMultiplier = 3.5, TireMultiplier = 3.5, PointsWeight = w,
            }).ToList(),
        },
    };

    private static void Count(Career career, Race race, int index)
    {
        var xml = new ResultsXml().Car("Sam Sample", player: true, number: "69", team: "Sample Racing",
            classPos: race.Pos, status: race.Status, grid: race.Grid, laps: race.Laps);
        var others = Enumerable.Range(1, Rivals.Length + 1).Where(p => p != race.Pos).ToList();
        for (var i = 0; i < Rivals.Length; i++) xml.Car(Rivals[i], classPos: others[i], laps: 10);

        var round = RoundFlow.Arm(career.CurrentSeason, Now.AddDays(index));
        var check = RoundMatcher.Check(xml.Parse($"race{index}.xml"), Now.AddDays(index).AddHours(1), round, career.CurrentSeason.Car);
        var evaluation = RoundFlow.Evaluate([check]);
        CareerActions.AcceptArmedRound(new CareerStore(Path.Combine(Path.GetTempPath(), "lmucareer-paddock-" + Guid.NewGuid().ToString("N"))),
            career, evaluation, takeDnf: evaluation.EndedEarly, acceptDifferences: false, Now.AddDays(index).AddHours(1));
    }

    private static Career Run(decimal[] weights, params Race[] races) => Run(weights, 3, races);

    private static Career Run(decimal[] weights, int target, params Race[] races)
    {
        var career = Career(weights, target);
        for (var i = 0; i < races.Length; i++) Count(career, races[i], i);
        return career;
    }

    private static IReadOnlyList<Moment> MomentsOfLast(Career career) =>
        Paddock.MomentsOf(career, career.CurrentSeason.Rounds.Last(r => r.State == RoundState.Completed));

    private static InboxMessage? Last(Career career, MessageKind kind) =>
        career.Inbox.LastOrDefault(m => m.Kind == kind && m.Round == career.CurrentSeason.Rounds.Last(r => r.State == RoundState.Completed).Number);

    [Fact]
    public void A_first_win_takes_the_lead_and_the_next_is_just_a_win()
    {
        var career = Run([1, 1, 1], new Race(1));
        Assert.Equal([Moment.FirstWin, Moment.TookLead], MomentsOfLast(career));
        Assert.Equal("Sam Sample takes a first career win at the Round 1 Hours", Last(career, MessageKind.Press)!.Subject.ToString());

        Count(career, new Race(1), 1);
        Assert.Equal([Moment.Win], MomentsOfLast(career));
        Assert.Equal("Sam Sample wins the Round 2 Hours for Sample Racing", Last(career, MessageKind.Press)!.Subject.ToString());
    }

    [Fact]
    public void Losing_the_lead_comebacks_and_big_podiums_are_news()
    {
        // Round 1: player 25, Dan 18. Round 2: Dan wins, player P4: Dan 43, player 37.
        Assert.Equal([Moment.LostLead], MomentsOfLast(Run([1, 1], new Race(1), new Race(4))));
        Assert.Contains(Moment.Comeback, MomentsOfLast(Run([1], new Race(3, Grid: 12))));
        Assert.Equal([Moment.BigPodium], MomentsOfLast(Run([2], new Race(2))));
    }

    [Fact]
    public void A_retirement_is_news_but_finishing_unclassified_isnt()
    {
        var retired = Run([1], new Race(4, Status: "DNF"));
        Assert.Equal([Moment.Retired], MomentsOfLast(retired));
        Assert.Equal("Heartbreak at the Round 1 Hours: Sam Sample retires", Last(retired, MessageKind.Press)!.Subject.ToString());

        var unclassified = Run([1], new Race(4, Laps: 3));
        Assert.Empty(MomentsOfLast(unclassified));
        Assert.Null(Last(unclassified, MessageKind.Press));
    }

    [Fact]
    public void Interviews_follow_big_moments_with_the_tones_that_fit()
    {
        var win = Last(Run([1], new Race(1)), MessageKind.Interview)!;
        Assert.Equal([InterviewTone.Humble, InterviewTone.Confident], win.Interview!.Options.Select(o => o.Tone));
        Assert.Equal("Your first win! What made the difference at the Round 1 Hours?", win.Interview.Question.ToString());
        // The interview tops the inbox: it's the message waiting on the player.
        Assert.Equal(MessageKind.Interview, Run([1], new Race(1)).Inbox[^1].Kind);

        var bigDnf = Last(Run([2], new Race(4, Status: "DNF")), MessageKind.Interview)!;
        Assert.Equal([InterviewTone.Humble, InterviewTone.Confident, InterviewTone.BlameTeam], bigDnf.Interview!.Options.Select(o => o.Tone));

        // A retirement at an ordinary round makes the news but isn't asked about; neither is a quiet P4.
        Assert.Null(Last(Run([1], new Race(4, Status: "DNF")), MessageKind.Interview));
        Assert.Null(Last(Run([1, 1], new Race(2), new Race(4)), MessageKind.Interview));
    }

    [Fact]
    public void An_answer_is_given_once_and_only_until_the_next_race_counts()
    {
        var career = Run([1, 1, 1], new Race(1));
        var interview = Last(career, MessageKind.Interview)!;

        Paddock.Answer(career, interview.Id, InterviewTone.Humble);
        Assert.Equal(InterviewTone.Humble, interview.Interview!.Answer);
        Assert.True(interview.Read);
        Assert.Single(career.CurrentSeason.Interviews);
        Assert.Throws<PlayerError>(() => Paddock.Answer(career, interview.Id, InterviewTone.Confident));

        Count(career, new Race(1), 1);
        var second = Last(career, MessageKind.Interview)!;
        Count(career, new Race(1), 2);
        Assert.True(second.Interview!.Lapsed);
        Assert.Throws<PlayerError>(() => Paddock.Answer(career, second.Id, InterviewTone.Humble));
        // A good moment never offers blame.
        Assert.Throws<PlayerError>(() => Paddock.Answer(career, Last(career, MessageKind.Interview)!.Id, InterviewTone.BlameTeam));
    }

    private static List<(string, decimal)> Items(Career career) =>
        Paddock.ReviewItems(career.CurrentSeason).Select(i => (i.Label, i.Points)).ToList();

    [Fact]
    public void Confidence_is_a_bet_the_next_round_settles()
    {
        var talkedBig = Run([1, 1], new Race(1));
        Paddock.Answer(talkedBig, Last(talkedBig, MessageKind.Interview)!.Id, InterviewTone.Confident);
        Assert.Equal([("Confident interview after the Round 1 Hours", 2m)], Items(talkedBig));
        Count(talkedBig, new Race(4), 1);
        Assert.Equal([("Confident interview after the Round 1 Hours", 2m), ("Talked big after the Round 1 Hours, then missed the podium", -2m)], Items(talkedBig));

        var backedUp = Run([1, 1], new Race(1));
        Paddock.Answer(backedUp, Last(backedUp, MessageKind.Interview)!.Id, InterviewTone.Confident);
        Count(backedUp, new Race(3), 1);
        Assert.Equal([("Confident interview after the Round 1 Hours", 2m)], Items(backedUp));

        var comeback = Run([2, 1], target: 3, new Race(4, Status: "DNF"));
        Paddock.Answer(comeback, Last(comeback, MessageKind.Interview)!.Id, InterviewTone.Confident);
        Count(comeback, new Race(4), 1);
        Assert.Equal([("Confident interview after the Round 1 Hours", 1m), ("Promised a comeback after the Round 1 Hours, then missed the target again", -2m)], Items(comeback));
    }

    [Fact]
    public void Blaming_the_team_works_once_a_season_and_shows_in_the_review()
    {
        var career = Run([2, 2, 1], new Race(4, Status: "DNF"));
        Paddock.Answer(career, Last(career, MessageKind.Interview)!.Id, InterviewTone.BlameTeam);
        Count(career, new Race(4, Status: "DNF"), 1);
        var second = Last(career, MessageKind.Interview)!;
        Assert.Equal("−3: you've blamed the team once already this season", second.Interview!.Options.Single(o => o.Tone == InterviewTone.BlameTeam).Effect.ToString());
        Paddock.Answer(career, second.Id, InterviewTone.BlameTeam);

        Assert.Equal([("Blamed the team again after the Round 2 Hours", -3m)], Items(career));
        Assert.Contains(Progression.Review(career.CurrentSeason, 20).Items, i => i.Label == "Blamed the team again after the Round 2 Hours" && i.Points == -3);
    }
}
