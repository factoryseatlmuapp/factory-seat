using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

/// <summary>1.5 "real negotiations": counters, walk-aways, perks, and a seat always left.</summary>
public class NegotiationTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    /// <summary>A random whose rolls all come out at one value: 0 walks whenever there's any chance, 1 never.</summary>
    private sealed class Roll(double value) : Random
    {
        public override double NextDouble() => value;
    }

    private static readonly Random NeverWalk = new Roll(0.999);

    /// <summary>A reviewed season finished at <paramref name="position"/>, with one fresh offer from a team of <paramref name="tier"/>.</summary>
    private static (Career Career, TeamOffer Offer) Talks(int position, int tier)
    {
        var career = CareerWith(0, "GT3", Season(6, _ => position));
        career.CurrentSeason.ReputationRules = Progression.CurrentReputationRules;
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var offer = new TeamOffer
        {
            Id = "talks",
            Kind = OfferKind.Move,
            TeamName = "Test Racing",
            CarClass = "GT3",
            CarFolder = career.Offers[0].CarFolder,
            Tier = tier,
            TargetPosition = Progression.TargetFor(tier),
        };
        Negotiation.Open(offer, Negotiation.Leverage(offer.TargetPosition, career.CurrentSeason.Review!.ChampionshipPosition), new Random(1));
        career.Offers = [offer];
        return (career, offer);
    }

    [Fact]
    public void Fresh_offers_open_at_prestige_with_their_tier_of_perks()
    {
        var career = CareerWith(0, "GT3", Season(6, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.All(career.Offers.Where(o => !o.UnderContract), o =>
        {
            Assert.Equal(Progression.Prestige(o.Tier), o.GainModifier);
            Assert.NotNull(o.Budget);
            Assert.InRange(o.Perks.Count, o.Tier == 1 ? 0 : o.Tier == 2 ? 1 : 2, o.Tier == 1 ? 1 : o.Tier == 2 ? 2 : 3);
            if (o.Tier == 1) Assert.All(o.Perks, p => Assert.Contains(p, new[] { Perk.ExpertEngineer, Perk.GarageSpace }));
        });
    }

    [Theory]
    [InlineData(5, 0.0)]   // well ahead of the target
    [InlineData(0, 0.15)]  // on it
    [InlineData(-3, 0.30)]
    [InlineData(-9, 0.60)] // capped
    public void Walk_chance_follows_form(int leverage, double chance) =>
        Assert.Equal(chance, Negotiation.WalkChance(leverage), 3);

    [Fact]
    public void Asking_past_what_the_team_can_ever_do_is_refused_without_a_counter()
    {
        var (career, offer) = Talks(1, 3);

        Assert.Throws<PlayerError>(() => Negotiation.Counter(career, offer.Id, 1.3m, 1.0m, [], Catalog, null, NeverWalk));   // back-marker tops out at ×1.2
        Assert.Throws<PlayerError>(() => Negotiation.Counter(career, offer.Id, 1.2m, 0.8m, [], Catalog, null, NeverWalk));   // gap 0.4
        Assert.Throws<PlayerError>(() => Negotiation.Counter(career, offer.Id, 1.0m, 1.0m,
            [Perk.GarageSpace, Perk.TeamBacking, Perk.ReleaseClause, Perk.ExpertEngineer], Catalog, null, NeverWalk));

        Assert.Equal(0, offer.Counters);
    }

    [Fact]
    public void An_ask_inside_the_budget_is_agreed()
    {
        var (career, offer) = Talks(1, 2);
        var perks = offer.Perks.ToList();

        var outcome = Negotiation.Counter(career, offer.Id, 1.3m, 1.1m, perks, Catalog, null, NeverWalk);

        Assert.Equal(CounterOutcome.Accepted, outcome);
        Assert.True(offer.Agreed);
        Assert.Equal((1.3m, 1.1m), (offer.GainModifier, offer.LossModifier));
        Assert.False(Negotiation.CanCounter(offer));
    }

    [Fact]
    public void A_greedy_ask_is_met_part_way_and_the_second_reply_is_final()
    {
        var (career, offer) = Talks(1, 2);
        Perk[] greedy = [Perk.TeamBacking, Perk.ReleaseClause, Perk.ExpertEngineer];

        var first = Negotiation.Counter(career, offer.Id, 1.2m, 0.9m, greedy, Catalog, null, NeverWalk);

        Assert.Equal(CounterOutcome.Countered, first);
        Assert.True(Negotiation.Value(offer.Tier, offer.GainModifier!.Value, offer.LossModifier!.Value, offer.Perks) <= offer.Budget);
        Assert.True(Negotiation.CanCounter(offer));

        var second = Negotiation.Counter(career, offer.Id, 1.2m, 0.9m, greedy, Catalog, null, NeverWalk);

        Assert.Equal(CounterOutcome.Final, second);
        Assert.True(offer.FinalOffer);
        Assert.False(Negotiation.CanCounter(offer));
        Assert.Throws<PlayerError>(() => Negotiation.Counter(career, offer.Id, 1.1m, 1.1m, [], Catalog, null, NeverWalk));
    }

    [Fact]
    public void A_team_that_walks_leaves_a_last_resort_seat_and_cant_be_signed()
    {
        var (career, offer) = Talks(10, 1);

        var outcome = Negotiation.Counter(career, offer.Id, 1.4m, 1.3m, [], Catalog, null, new Roll(0));

        Assert.Equal(CounterOutcome.WalkedAway, outcome);
        Assert.True(offer.WalkedAway);
        var fallback = Assert.Single(career.Offers, Negotiation.Signable);
        Assert.True(fallback.LastResort);
        Assert.Empty(fallback.Perks);
        Assert.False(Negotiation.CanCounter(fallback));
        Assert.True(Retirement.AtLastResort(career));
        Assert.Throws<PlayerError>(() => Progression.StartNextSeason(career, offer.Id, [new Round { EventName = "R1", RaceMinutes = 60 }], Catalog));
    }

    [Fact]
    public void Strong_form_never_walks()
    {
        var (career, offer) = Talks(1, 3);

        var outcome = Negotiation.Counter(career, offer.Id, 1.1m, 1.0m, offer.Perks, Catalog, null, new Roll(0));

        Assert.NotEqual(CounterOutcome.WalkedAway, outcome);
    }

    [Fact]
    public void Only_back_markers_after_a_bad_season_is_take_it_or_retire()
    {
        var career = CareerWith(0, "GT3", Season(6, _ => 10));
        career.CurrentSeason.ReputationRules = Progression.CurrentReputationRules;

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.NotEmpty(career.Offers);
        Assert.All(career.Offers, o => Assert.True(o.LastResort));
        Assert.All(career.Offers, o => Assert.Empty(o.Perks));
        Assert.True(Retirement.AtLastResort(career));

        Retirement.RetireNoSeat(career, DateTimeOffset.UnixEpoch);

        Assert.True(career.RetiredNoSeat);
        Assert.NotNull(career.RetiredAt);
    }

    [Fact]
    public void Signing_carries_the_agreed_perks_into_the_contract()
    {
        var (career, offer) = Talks(1, 2);
        Negotiation.Counter(career, offer.Id, 1.2m, 1.1m, [Perk.ExpertEngineer], Catalog, null, NeverWalk);

        var next = Progression.StartNextSeason(career, offer.Id, [new Round { EventName = "R1", RaceMinutes = 60 }], Catalog);

        Assert.Equal([Perk.ExpertEngineer], next.Contract!.Perks);
        Assert.Equal((1.2m, 1.1m), Progression.Modifiers(next.Contract));
    }
}
