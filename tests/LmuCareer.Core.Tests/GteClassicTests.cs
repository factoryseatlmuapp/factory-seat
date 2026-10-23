using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class GteClassicTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    private static Career Gte(decimal reputation, int rank, int tier = 2)
    {
        var rounds = Enumerable.Range(1, 7).Select(n => ProgressionTests.Counted(n, rank, carClass: "GTE")).ToArray();
        var career = ProgressionTests.CareerWith(reputation, "GTE", rounds);
        career.GteClassic = true;
        career.CurrentSeason.Car = new CareerCar("Porsche 911 RSR-19", "GTE", "69", "Sample Racing") { Folder = "Porsche_911RSR-19_2023" };
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = tier, TargetPosition = 6 };
        return career;
    }

    [Fact]
    public void The_calendar_is_the_2023_wec_on_base_game_tracks()
    {
        var rounds = CalendarBuilder.DefaultSeason(Catalog, "GTE", DriverRating.Silver, []);
        Assert.Equal(["sebring-1000", "portimao-6", "spa-6", "le-mans-24", "monza-6", "fuji-6", "bahrain-8"], rounds.Select(r => r.EventId));
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(10, 2)]
    [InlineData(34, 2)]
    [InlineData(35, 1)]
    public void Factory_pro_seats_want_an_established_name(decimal reputation, int tier) =>
        Assert.Equal(tier, Progression.BestTier(reputation, "GTE"));

    [Fact]
    public void A_gte_career_stays_in_gte()
    {
        // Reputation that would open Hypercar doors anywhere else.
        var career = Gte(80, rank: 1, tier: 1);
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.NotEmpty(career.Offers);
        Assert.All(career.Offers, o => Assert.Equal("GTE", o.CarClass));
    }

    [Fact]
    public void An_am_title_earns_a_pro_call()
    {
        // Reputation 0 + a capped season = 25: short of Pro's 35, but the title speaks.
        var career = Gte(0, rank: 1, tier: 3);
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var pro = Assert.Single(career.Offers, o => o.Tier == 1);
        Assert.Equal("your GTE Am title", pro.Reason);
        Assert.Equal(OfferKind.Promotion, pro.Kind);
    }

    [Fact]
    public void A_middling_am_season_brings_only_am_seats()
    {
        var career = Gte(0, rank: 5, tier: 3);
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.DoesNotContain(career.Offers, o => o.Tier == 1);
    }

    [Fact]
    public void Pro_races_get_tougher_opponent_advice()
    {
        var am = Gte(10, rank: 5, tier: 2);
        var pro = Gte(10, rank: 5, tier: 1);
        foreach (var career in new[] { am, pro })
        {
            var next = new Round { Number = 8, EventName = "Round 8", TrackFolder = "Spa_2023", LayoutFile = "layoutSpa" };
            next.Practice = new PracticeRead(1, 10, 140.0, 140.0, "AI 1", null);
            career.CurrentSeason.Rounds.Add(next);
        }

        var amPace = Preparation.Pace(am, am.CurrentSeason.Rounds[^1])!;
        var proPace = Preparation.Pace(pro, pro.CurrentSeason.Rounds[^1])!;
        Assert.Equal(PaceAdvice.Matched, amPace.Advice);
        Assert.Equal(PaceAdvice.BitEasy, proPace.Advice);
    }

    [Fact]
    public void No_test_days_out_of_gte()
    {
        var career = Gte(60, rank: 1);
        career.CurrentSeason.Rounds = [new Round { Number = 1, EventName = "Round 1" }];
        Assert.False(TestDays.EnsureOffers(career, Catalog, null, seed: 1));
    }
}
