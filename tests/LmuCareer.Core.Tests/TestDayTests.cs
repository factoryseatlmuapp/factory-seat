using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class TestDayTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A GT3 career before its season's first round, with three rounds to come.</summary>
    private static Career PreSeason(decimal reputation, CareerPath? path)
    {
        var career = ProgressionTests.CareerWith(reputation, "GT3",
            Enumerable.Range(1, 3).Select(n => new Round { Number = n, EventName = $"Round {n}" }).ToArray());
        career.Path = path;
        return career;
    }

    [Fact]
    public void No_tests_for_an_unknown_driver()
    {
        Assert.Empty(TestDays.MakeOffers(PreSeason(10, CareerPath.Gt), Catalog, null, new Random(1)));
    }

    [Fact]
    public void A_gt_driver_short_of_hypercar_tests_with_a_prototype_team()
    {
        // 40: LMP2's bar, but Hypercar (60) is more than 15 away.
        var offer = Assert.Single(TestDays.MakeOffers(PreSeason(40, CareerPath.Gt), Catalog, null, new Random(1)));
        Assert.Equal("LMP2", offer.CarClass);
    }

    [Fact]
    public void Closer_to_hypercar_a_gt_driver_tests_there_too()
    {
        var offers = TestDays.MakeOffers(PreSeason(50, CareerPath.Gt), Catalog, null, new Random(1));
        Assert.Equal(["Hyper", "LMP2"], offers.Select(o => o.CarClass));
        Assert.All(offers, o => Assert.NotNull(Catalog.Event(o.VenueEventId)));
    }

    [Fact]
    public void A_career_without_a_path_tests_one_class_up_its_old_ladder()
    {
        var offer = Assert.Single(TestDays.MakeOffers(PreSeason(20, null), Catalog, null, new Random(1)));
        Assert.Equal("LMP3", offer.CarClass);
    }

    private static (Career Career, Round Round) Accepted(decimal reputation = 50)
    {
        var career = PreSeason(reputation, CareerPath.Gt);
        career.CurrentSeason.Sponsors.Add(new SponsorDeal { Name = "Sample Oil", Objective = ObjectiveKind.PodiumAt, RoundNumber = 2 });
        TestDays.EnsureOffers(career, Catalog, null, seed: 1);
        var offer = career.CurrentSeason.TestOffers!.First(o => o.CarClass == "Hyper");
        var round = TestDays.Accept(career, offer.Id, Catalog);
        return (career, round);
    }

    [Fact]
    public void A_pre_season_test_goes_ahead_of_round_one_outside_the_championship()
    {
        var (career, round) = Accepted();
        var season = career.CurrentSeason;

        Assert.Same(round, season.Rounds[0]);
        Assert.Equal([1, 2, 3, 4], season.Rounds.Select(r => r.Number));
        Assert.DoesNotContain(round, season.ChampionshipRounds);
        Assert.Equal("Hyper", season.CarFor(round).CarClass);
        Assert.Equal("", round.EventId);
        // The sponsor's podium round moved with the calendar.
        Assert.Equal(3, season.Sponsors[0].RoundNumber);
        // Running it doesn't start the season: deals can still be signed.
        round.State = RoundState.Completed;
        Assert.True(Sponsorship.CanSign(season));
    }

    [Fact]
    public void Withdrawing_takes_the_test_back_out()
    {
        var (career, _) = Accepted();
        var offer = career.CurrentSeason.TestOffers!.First(o => o.Accepted);
        TestDays.Withdraw(career, offer.Id);
        Assert.Equal(3, career.CurrentSeason.Rounds.Count);
        Assert.Equal(2, career.CurrentSeason.Sponsors[0].RoundNumber);
    }

    private static void Run(Career career, Round round, int laps, double mine, double ai)
    {
        round.State = RoundState.Armed;
        round.ArmedAt = Now;
        round.Practice = new PracticeRead(1, laps, mine, ai, "AI 1", null);
    }

    [Fact]
    public void A_test_needs_enough_laps_to_judge()
    {
        var (career, round) = Accepted();
        Run(career, round, laps: 5, mine: 100, ai: 100);
        Assert.False(TestDays.IsReady(round));
        Assert.Throws<PlayerError>(() => TestDays.Complete(career, Now));
    }

    [Fact]
    public void Impressing_gets_a_call_at_the_seasons_end()
    {
        var (career, round) = Accepted();
        var share = round.Test!.PassShare;
        Run(career, round, laps: 10, mine: 100 * (1 + share) - 0.01, ai: 100);

        var result = TestDays.Complete(career, Now);

        Assert.True(result.Passed);
        Assert.Equal(RoundState.Completed, round.State);
        var interest = Assert.Single(career.Interest);
        Assert.True(interest.Tested);
        Assert.Equal(round.Test.TeamName, interest.TeamName);
        Assert.Contains(career.Inbox, m => m.Subject.ToString().StartsWith("After the test at"));
    }

    [Fact]
    public void Falling_short_costs_nothing()
    {
        var (career, round) = Accepted();
        var reputation = career.Reputation;
        Run(career, round, laps: 10, mine: 103, ai: 100);

        Assert.False(TestDays.Complete(career, Now).Passed);
        Assert.Empty(career.Interest);
        Assert.Equal(reputation, career.Reputation);
    }

    [Fact]
    public void A_team_that_was_impressed_offers_a_two_season_deal()
    {
        var career = ProgressionTests.CareerWith(30, "GT3", ProgressionTests.Season(6, _ => 5));
        career.Interest.Add(new TeamInterest
        {
            TeamName = "Toyota Racing", CarClass = "Hyper", CarFolder = "Toyota_GR10_2023", Tier = 1,
            Reason = "your test day at Fuji", Tested = true,
        });
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var offer = Assert.Single(career.Offers, o => o.TeamName == "Toyota Racing");
        Assert.Equal(2, offer.Seasons);
    }

    [Fact]
    public void A_big_win_that_catches_a_class_up_team_brings_a_test_day_straight_after()
    {
        // 45 is within 15 of LMP3's bar: a big win gets an LMP3 team interested (the old ladder).
        var won = ProgressionTests.Counted(1, playerRank: 1, weight: 2);
        var next = new Round { Number = 2, EventName = "Round 2" };
        var career = ProgressionTests.CareerWith(20, "GT3", won, next);

        var interest = Progression.NoticeResult(career, won, Catalog, install: null);

        Assert.NotNull(interest);
        Assert.Equal("LMP3", interest!.CarClass);
        var test = career.CurrentSeason.Rounds[1];
        Assert.NotNull(test.Test);
        Assert.Equal(interest.TeamName, test.Test!.TeamName);
        Assert.Equal("your win at the Le Mans 24 Hours", test.Test.Reason);
        Assert.Same(next, career.CurrentSeason.Rounds[2]);
        Assert.Equal(3, next.Number);
    }
}
