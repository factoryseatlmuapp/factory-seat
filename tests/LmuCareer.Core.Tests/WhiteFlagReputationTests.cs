using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

/// <summary>1.5 "White Flag" reputation: no gain cap, team prestige, loss limits, race length, giant-killer, Triple Crown.</summary>
public class WhiteFlagReputationTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    private static Career OnNewRules(decimal reputation, int tier, Round[] rounds, int minutes = 60)
    {
        foreach (var round in rounds) round.RaceMinutes = minutes;
        var career = CareerWith(reputation, "GT3", rounds);
        career.CurrentSeason.ReputationRules = Progression.CurrentReputationRules;
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = tier, TargetPosition = Progression.TargetFor(tier) };
        return career;
    }

    [Fact]
    public void A_dominant_season_is_no_longer_capped()
    {
        var career = OnNewRules(0, 2, Season(8, _ => 1));

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var review = career.CurrentSeason.Review!;
        Assert.True(career.Reputation > Progression.MaxGain);
        Assert.DoesNotContain(review.Items, i => i.Label.StartsWith("Reputation grows at most"));
        Assert.Contains(review.Items, i => i.Label == "Team prestige: gains ×1.1" && i.Points > 0);
    }

    [Fact]
    public void Prestige_opens_at_the_team_tier_and_sets_the_loss_limit()
    {
        Assert.Equal((1.3m, 1.3m), Progression.Modifiers(new Contract { Tier = 1 }));
        Assert.Equal((1.1m, 1.1m), Progression.Modifiers(new Contract { Tier = 2 }));
        Assert.Equal((1.0m, 1.0m), Progression.Modifiers(new Contract { Tier = 3 }));
        Assert.Equal((1.4m, 1.2m), Progression.Modifiers(new Contract { Tier = 1, GainModifier = 1.4m, LossModifier = 1.2m }));
        Assert.Equal([16m, 13m, 12m], new[] { 1, 2, 3 }.Select(Progression.LossLimit));
    }

    [Fact]
    public void A_front_runner_disaster_stops_at_its_loss_limit()
    {
        var career = OnNewRules(50, 1, Enumerable.Range(1, 6).Select(n => Counted(n, 10, status: "DNF", penalties: 2)).ToArray());

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.Equal(34, career.Reputation);
        Assert.Contains(career.CurrentSeason.Review!.Items, i => i.Label == "Reputation falls at most 16 a season");
    }

    [Fact]
    public void A_back_marker_giant_killer_season_is_graded_and_flat()
    {
        var career = OnNewRules(0, 3, Season(6, _ => 5));

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Equal(5, review.ChampionshipPosition);
        Assert.Contains(new ReputationItem("Giant-killer: P5 with a team expected in the top 10", 5), review.Items);
        Assert.DoesNotContain(review.Items, i => i.Label.StartsWith("Beat the target"));
        Assert.DoesNotContain(review.Items, i => i.Label.StartsWith("Team prestige"));
    }

    [Theory]
    [InlineData(3, 8)]
    [InlineData(4, 5)]
    [InlineData(7, 3)]
    [InlineData(8, 0)]
    public void Giant_killer_grades(int finish, int bonus)
    {
        var review = Progression.Review(OnNewRules(0, 3, Season(6, _ => finish)).CurrentSeason, 0);

        var item = review.Items.SingleOrDefault(i => i.Label.StartsWith("Giant-killer"));
        Assert.Equal(bonus, item?.Points ?? 0);
    }

    [Fact]
    public void Short_races_pay_pro_rata()
    {
        var review = Progression.Review(OnNewRules(0, 3, Season(6, _ => 1), minutes: 15).CurrentSeason, 0);

        Assert.Contains(new ReputationItem("6 class wins", 4), review.Items);
        Assert.Contains(new ReputationItem("Championship P1", 4), review.Items);
        Assert.Contains(new ReputationItem("Team target met (top 10)", 2), review.Items);
    }

    [Fact]
    public void A_full_distance_Triple_Crown_win_pays_ten()
    {
        var career = OnNewRules(0, 3, [Counted(1, 1, weight: 2), Counted(2, 3), Counted(3, 3)]);
        career.CurrentSeason.Rounds[0].EventId = "le-mans-24";
        career.CurrentSeason.Rounds[0].RaceMinutes = 360;

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Contains(new ReputationItem("Won the Le Mans 24 Hours at full distance", 10), review.Items);
        Assert.DoesNotContain(review.Items, i => i.Label == "Won the Le Mans 24 Hours");
    }

    [Fact]
    public void A_shorter_Triple_Crown_win_keeps_the_big_event_five()
    {
        var career = OnNewRules(0, 3, [Counted(1, 1, weight: 2), Counted(2, 3), Counted(3, 3)]);
        career.CurrentSeason.Rounds[0].EventId = "le-mans-24";
        career.CurrentSeason.Rounds[0].RaceMinutes = 120;

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Contains(new ReputationItem("Won the Le Mans 24 Hours", 5), review.Items);
    }

    [Fact]
    public void A_season_from_before_1_5_keeps_the_old_rules()
    {
        var career = OnNewRules(0, 1, Season(8, _ => 1), minutes: 10);
        career.CurrentSeason.ReputationRules = 0;

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.Equal(Progression.MaxGain, career.Reputation);
        Assert.DoesNotContain(career.CurrentSeason.Review!.Items, i => i.Label.StartsWith("Team prestige"));
    }

    [Fact]
    public void Offers_open_at_the_team_prestige_and_signing_carries_the_terms()
    {
        var career = CareerWith(0, "GT3", Season(8, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.All(career.Offers, o =>
        {
            Assert.Equal(Progression.Prestige(o.Tier), o.GainModifier);
            Assert.Equal(Progression.Prestige(o.Tier), o.LossModifier);
        });

        var offer = career.Offers[0];
        offer.GainModifier = 1.4m;
        offer.LossModifier = 1.2m;
        var next = Progression.StartNextSeason(career, offer.Id, [new Round { EventName = "Round 1", RaceMinutes = 60 }], Catalog);

        Assert.Equal((1.4m, 1.2m), Progression.Modifiers(next.Contract));
    }

    [Fact]
    public void A_running_contract_keeps_its_terms()
    {
        var career = OnNewRules(0, 2, Season(6, _ => 1));
        career.CurrentSeason.Contract!.SeasonsLeft = 2;
        career.CurrentSeason.Contract.GainModifier = 1.3m;
        career.CurrentSeason.Contract.LossModifier = 1.2m;

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var stay = career.Offers.Single(o => o.Kind == OfferKind.ReSign);
        Assert.True(stay.UnderContract);
        Assert.Equal((1.3m, 1.2m), (stay.GainModifier, stay.LossModifier));
    }

    [Fact]
    public void The_next_season_starts_on_the_new_rules()
    {
        var career = CareerWith(0, "GT3", Season(6, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var next = Progression.StartNextSeason(career, career.Offers[0].Id, [new Round { EventName = "Round 1", RaceMinutes = 60 }], Catalog);

        Assert.Equal(Progression.CurrentReputationRules, next.ReputationRules);
        Assert.Equal(0, career.PastSeasons[^1].ReputationRules);
    }
}
