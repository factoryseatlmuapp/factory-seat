using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

/// <summary>1.2's sponsor rules: offers that grow with reputation, local brands by nationality, approaches mid-season.</summary>
public class SponsorRulesTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private static Career Upcoming(decimal reputation, string? nationality = null, int tier = 2)
    {
        var career = CareerWith(reputation, "GT3");
        career.Nationality = nationality;
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = tier };
        career.CurrentSeason.Rounds = Enumerable.Range(1, 8).Select(n => new Round { Number = n, EventName = $"Round {n}" }).ToList();
        return career;
    }

    private static HashSet<string> CountryOf(params string[] countries) =>
        Catalog.Sponsors.Where(s => s.Kind == "local" && countries.Contains(s.Country)).Select(s => s.Id).ToHashSet();

    [Fact]
    public void The_catalog_has_the_local_tier()
    {
        var locals = Catalog.Sponsors.Where(s => s.Kind == "local").ToList();
        Assert.Equal(32, locals.Count);
        Assert.Equal(["BR", "DE", "ES", "FR", "GB", "IE", "KR", "US"], locals.Select(s => s.Country).Distinct().Order());
        Assert.Equal("성심당", locals.Single(s => s.Id == "sungsimdang").LocalName);
    }

    [Fact]
    public void A_rookie_gets_one_small_local_deal_from_home()
    {
        var korean = CountryOf("KR");
        for (var seed = 0; seed < 30; seed++)
        {
            var career = Upcoming(0, "KR");
            Assert.True(Sponsorship.EnsureOffers(career, Catalog, [], seed: seed));
            var offer = Assert.Single(career.CurrentSeason.SponsorOffers!);
            Assert.Contains(offer.SponsorId, korean);
            Assert.True(offer.Local);
            Assert.NotNull(offer.LocalName);
            Assert.Equal(Sponsorship.LocalReward, offer.Reward);
            Assert.Contains(offer.Objective, new[] { ObjectiveKind.FinishEveryRound, ObjectiveKind.ChampionshipTop, ObjectiveKind.CleanSeason });
            Assert.Equal(Sponsorship.CurrentRules, career.CurrentSeason.SponsorRules);
        }
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(25, 2, 1)]
    [InlineData(40, 2, 2)]
    [InlineData(60, 2, 3)]
    [InlineData(60, 3, 2)]
    [InlineData(40, 3, 1)]
    [InlineData(0, 3, 1)]
    public void Offers_grow_with_reputation_and_a_back_of_the_grid_team_draws_fewer(int reputation, int tier, int offers)
    {
        Assert.Equal(offers, Sponsorship.OffersFor(Upcoming(reputation, tier: tier)));
    }

    [Theory]
    [InlineData("IE", "en", "GB,IE")]
    [InlineData("AT", "en", "DE")]
    [InlineData("CH", "fr", "DE")]
    [InlineData("PT", "en", "BR")]
    [InlineData(null, "ko", "KR")]
    [InlineData(null, "pt-BR", "BR")]
    [InlineData("JP", "fr", "FR")]
    [InlineData("JP", "en", "US,GB,IE")]
    [InlineData("US", "de", "US")]
    public void Local_brands_come_from_the_country_then_its_region_then_the_language(string? nationality, string language, string countries)
    {
        var pool = Sponsorship.LocalPool(Catalog, nationality, language);
        Assert.Equal(CountryOf(countries.Split(',')), pool.Select(s => s.Id).ToHashSet());
    }

    // ---------- Mid-season approaches ----------

    /// <summary>Two rounds run (P5, then the given finish), three to go with Le Mans among them.</summary>
    private static Career MidSeason(int secondFinish, decimal reputation = 30, int rules = Sponsorship.CurrentRules)
    {
        var career = CareerWith(reputation, "GT3", Counted(1, 5), Counted(2, secondFinish),
            new Round { Number = 3, EventName = "Round 3" },
            new Round { Number = 4, EventName = "Le Mans 24 Hours", PointsWeight = 2 },
            new Round { Number = 5, EventName = "Round 5" });
        career.Nationality = "US";
        career.CurrentSeason.SponsorRules = rules;
        return career;
    }

    private static InboxMessage? Approach(Career career, int seed = 1) =>
        Sponsorship.Approach(career, career.CurrentSeason.Rounds[1], Catalog, [], [], "en", Now, seed);

    [Fact]
    public void A_result_above_expectations_brings_one_offer_for_the_rounds_left()
    {
        var career = MidSeason(secondFinish: 2);
        var message = Approach(career)!;

        Assert.Equal(MessageKind.SponsorApproach, message.Kind);
        Assert.Same(message, career.Inbox[^1]);
        Assert.Equal(ApproachStatus.Open, message.Approach!.Status);
        Assert.Equal(2, message.Approach.Deal.SignedAfterRound);
        Assert.Equal("Your manager", message.From.ToString());
        Assert.EndsWith(" wants to back you", message.Subject.ToString());
    }

    [Fact]
    public void Nobody_calls_without_a_reason_room_for_a_deal_or_rounds_left()
    {
        // P6 after a P5: not a podium, not well above the target of 6, not a best.
        Assert.Null(Approach(MidSeason(secondFinish: 6)));
        // A first P4 is a career best.
        Assert.NotNull(Approach(MidSeason(secondFinish: 4)));
        // Seasons that started before 1.2 keep the old rules.
        Assert.Null(Approach(MidSeason(secondFinish: 1, rules: 0)));

        var full = MidSeason(secondFinish: 1);
        full.CurrentSeason.Sponsors.AddRange([new SponsorDeal { SponsorId = "a" }, new SponsorDeal { SponsorId = "b" }]);
        Assert.Null(Approach(full));

        var last = MidSeason(secondFinish: 1);
        last.CurrentSeason.Rounds.RemoveAll(r => r.Number > 2);
        Assert.Null(Approach(last));
    }

    [Fact]
    public void An_offer_is_signed_or_declined_once_and_lapses_when_the_next_race_counts()
    {
        var signed = MidSeason(secondFinish: 1);
        var offer = Approach(signed)!;
        Sponsorship.AnswerApproach(signed, offer.Id, sign: true);
        Assert.Equal(ApproachStatus.Signed, offer.Approach!.Status);
        Assert.Equal(offer.Approach.Deal.SponsorId, Assert.Single(signed.CurrentSeason.Sponsors).SponsorId);
        Assert.Throws<PlayerError>(() => Sponsorship.AnswerApproach(signed, offer.Id, sign: false));

        var declined = MidSeason(secondFinish: 1);
        var turnedDown = Approach(declined)!;
        Sponsorship.AnswerApproach(declined, turnedDown.Id, sign: false);
        Assert.Equal(ApproachStatus.Declined, turnedDown.Approach!.Status);
        Assert.Empty(declined.CurrentSeason.Sponsors);

        var lapsed = MidSeason(secondFinish: 1);
        var waiting = Approach(lapsed)!;
        lapsed.CurrentSeason.Rounds[2] = Counted(3, 4);
        Sponsorship.LapseOpenApproaches(lapsed, lapsed.CurrentSeason.Rounds[2]);
        Assert.Equal(ApproachStatus.Lapsed, waiting.Approach!.Status);
        Assert.Throws<PlayerError>(() => Sponsorship.AnswerApproach(lapsed, waiting.Id, sign: true));
    }

    [Fact]
    public void A_sponsor_who_called_this_season_doesnt_call_again()
    {
        var career = MidSeason(secondFinish: 1);
        var first = Approach(career)!;
        Sponsorship.AnswerApproach(career, first.Id, sign: false);
        career.CurrentSeason.Rounds[2] = Counted(3, 1);
        var second = Sponsorship.Approach(career, career.CurrentSeason.Rounds[2], Catalog, [], [], "en", Now, seed: 1)!;
        Assert.NotEqual(first.Approach!.Deal.SponsorId, second.Approach!.Deal.SponsorId);
    }

    [Fact]
    public void A_mid_season_deal_counts_only_the_rounds_after_it_was_signed()
    {
        var career = MidSeason(secondFinish: 1);
        var season = career.CurrentSeason;
        var deal = new SponsorDeal { Objective = ObjectiveKind.Podiums, Count = 1, SignedAfterRound = 2, OverRounds = 3 };

        // The win that brought the offer doesn't count toward it.
        Assert.Equal(new ObjectiveProgress(ObjectiveStatus.InProgress, 0, 1), Sponsorship.Progress(season, deal));
        season.Rounds[2] = Counted(3, 3);
        Assert.Equal(ObjectiveStatus.Met, Sponsorship.Progress(season, deal).Status);

        Assert.Equal("a podium in the last 3 rounds", Sponsorship.Describe(deal).ToString());
        Assert.Equal("2 podiums in the last 4 rounds", Sponsorship.Describe(new SponsorDeal { Objective = ObjectiveKind.Podiums, Count = 2, OverRounds = 4 }).ToString());
        Assert.Equal("finish each of the last 3 rounds", Sponsorship.Describe(new SponsorDeal { Objective = ObjectiveKind.FinishEveryRound, OverRounds = 3 }).ToString());

        var finish = new SponsorDeal { Objective = ObjectiveKind.FinishEveryRound, SignedAfterRound = 2 };
        Assert.Equal(3, Sponsorship.Progress(season, finish).Needed);
    }
}
