using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

/// <summary>1.5 mid-season offers: who calls and when, deadlines, signing early, and the release clause.</summary>
public class MidSeasonTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);

    /// <summary>A random whose NextDouble always gives one value: 0 = every chance hits, 0.999 = none does.</summary>
    private sealed class Roll(double value) : Random
    {
        public override double NextDouble() => value;
    }

    private static readonly Random Always = new Roll(0);
    private static readonly Random Never = new Roll(0.999);

    private static Round Upcoming(int number) => new() { Number = number, EventName = $"Round {number}" };

    /// <summary>A 1.5 season of <paramref name="total"/> rounds with the first <paramref name="counted"/> counted at these ranks.</summary>
    private static Career Running(int counted, int total, Func<int, int> rank, int tier = 2, int seasonsLeft = 1, params Perk[] perks)
    {
        var rounds = Enumerable.Range(1, total).Select(n => n <= counted ? Counted(n, rank(n)) : Upcoming(n)).ToArray();
        var career = CareerWith(0, "GT3", rounds);
        career.CurrentSeason.ReputationRules = Progression.CurrentReputationRules;
        career.CurrentSeason.Contract = new Contract
        {
            TeamName = "Sample Racing",
            Tier = tier,
            TargetPosition = Progression.TargetFor(tier),
            SeasonsLeft = seasonsLeft,
            Perks = [.. perks],
        };
        return career;
    }

    private static Round Last(Career career) => career.CurrentSeason.Rounds.Last(r => r.State == RoundState.Completed);

    /// <summary>Counts the next upcoming round at <paramref name="rank"/>, as accepting it would (inbox included).</summary>
    private static Round CountNext(Career career, int rank, decimal weight = 1)
    {
        var rounds = career.CurrentSeason.Rounds;
        var index = rounds.FindIndex(r => r.State == RoundState.Upcoming);
        var round = Counted(rounds[index].Number, rank, weight: weight);
        rounds[index] = round;
        Inbox.AfterRace(career, round, Now);
        return round;
    }

    [Fact]
    public void Strong_form_after_round_two_brings_an_offer_from_a_tier_up_with_a_deadline()
    {
        var career = Running(2, 6, _ => 1);

        var message = MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);

        Assert.NotNull(message);
        var offer = Assert.Single(career.EarlyOffers);
        Assert.Equal(EarlyOfferStatus.Open, offer.Early);
        Assert.Equal(1, offer.Tier);
        Assert.Equal("GT3", offer.CarClass);
        Assert.NotEqual("Sample Racing", offer.TeamName);
        Assert.Equal(4, offer.AnswerBy);          // the round after next
        Assert.Equal("Round 4", offer.AnswerByEvent);
        Assert.NotNull(offer.Budget);             // haggling is open
        Assert.Equal(offer.Id, message!.OfferId);
        Assert.Contains(career.Inbox, m => m.Kind == MessageKind.TeamOffer && m.OfferId == offer.Id);
    }

    [Fact]
    public void No_offer_before_round_two_on_the_last_round_or_off_form()
    {
        var early = Running(1, 6, _ => 1);
        Assert.Null(MidSeason.AfterRace(early, Last(early), Catalog, null, Now, Always));

        var last = Running(6, 6, _ => 1);
        Assert.Null(MidSeason.AfterRace(last, Last(last), Catalog, null, Now, Always));

        var slow = Running(3, 6, _ => 8);   // a front-runner wants top 3
        Assert.Null(MidSeason.AfterRace(slow, Last(slow), Catalog, null, Now, Always));
    }

    [Fact]
    public void Form_only_sometimes_calls_but_a_double_points_win_always_does()
    {
        var form = Running(2, 6, _ => 1);
        Assert.Null(MidSeason.AfterRace(form, Last(form), Catalog, null, Now, Never));

        var big = Running(2, 6, _ => 1);
        var rounds = big.CurrentSeason.Rounds;
        rounds[1] = Counted(2, 1, weight: 2);
        Assert.NotNull(MidSeason.AfterRace(big, rounds[1], Catalog, null, Now, Never));
        Assert.Contains("Le Mans", big.EarlyOffers[0].Reason);
    }

    [Fact]
    public void Seasons_before_the_new_rules_and_championship_only_careers_get_none()
    {
        var old = Running(2, 6, _ => 1);
        old.CurrentSeason.ReputationRules = 0;
        Assert.Null(MidSeason.AfterRace(old, Last(old), Catalog, null, Now, Always));

        var champ = Running(2, 6, _ => 1);
        champ.ChampionshipOnly = true;
        Assert.Null(MidSeason.AfterRace(champ, Last(champ), Catalog, null, Now, Always));
    }

    [Fact]
    public void At_most_two_a_season()
    {
        var career = Running(2, 8, _ => 1);
        MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);
        for (var i = 0; i < 4; i++)
            MidSeason.AfterRace(career, CountNext(career, 1), Catalog, null, Now, Always);

        Assert.Equal(MidSeason.MaxOffers, career.EarlyOffers.Count);
        Assert.Equal(2, career.EarlyOffers.Select(o => o.TeamName).Distinct().Count());
    }

    [Fact]
    public void An_unanswered_offer_lapses_when_its_round_counts_with_a_line_in_the_inbox()
    {
        var career = Running(2, 6, _ => 1);
        MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);
        var offer = career.EarlyOffers[0];

        CountNext(career, 1);
        Assert.Equal(EarlyOfferStatus.Open, offer.Early);

        CountNext(career, 1);
        Assert.Equal(EarlyOfferStatus.Lapsed, offer.Early);
        var note = Assert.Single(career.Inbox, m => m.Kind == MessageKind.OfferLapsed);
        Assert.Contains(note.Body, line => line.Text == MidSeason.LapsedLine);
        Assert.Throws<PlayerError>(() => MidSeason.Sign(career, offer.Id, Now));
    }

    [Fact]
    public void Signing_early_finishes_the_season_and_makes_that_deal_the_only_offer()
    {
        var career = Running(2, 6, _ => 1);
        MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);
        MidSeason.AfterRace(career, CountNext(career, 1), Catalog, null, Now, Always);
        Assert.Equal(2, career.EarlyOffers.Count);
        var signed = career.EarlyOffers[0];

        MidSeason.Sign(career, signed.Id, Now);

        Assert.Equal(EarlyOfferStatus.Signed, signed.Early);
        Assert.Equal(EarlyOfferStatus.Declined, career.EarlyOffers[1].Early);
        Assert.Null(signed.Budget);
        Assert.Contains(career.Inbox, m => m.Kind == MessageKind.SignedEarly);
        // No more calls once signed.
        Assert.Null(MidSeason.AfterRace(career, CountNext(career, 1), Catalog, null, Now, Always));

        while (career.CurrentSeason.NextRound is not null) CountNext(career, 1);
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var offer = Assert.Single(career.Offers);
        Assert.Same(signed, offer);
        Assert.False(Negotiation.CanCounter(offer));
        Assert.DoesNotContain(career.CurrentSeason.Review!.Items, i => i.Label.Contains(signed.TeamName));

        var next = Progression.StartNextSeason(career, offer.Id, [Upcoming(1)], Catalog);
        Assert.Equal(signed.TeamName, next.Contract!.TeamName);
        Assert.Empty(career.EarlyOffers);
    }

    [Fact]
    public void Turning_an_offer_down_costs_nothing_and_leaves_the_off_season_as_usual()
    {
        var career = Running(2, 6, _ => 1);
        MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);
        var offer = career.EarlyOffers[0];

        MidSeason.Decline(career, offer.Id);
        while (career.CurrentSeason.NextRound is not null) CountNext(career, 1);
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.Equal(EarlyOfferStatus.Declined, offer.Early);
        Assert.True(career.Offers.Count > 1);
        Assert.DoesNotContain(career.Offers, o => o.Early is not null);
    }

    [Fact]
    public void Under_a_longer_contract_only_a_release_clause_lets_teams_call()
    {
        var held = Running(2, 6, _ => 1, seasonsLeft: 2);
        Assert.Null(MidSeason.AfterRace(held, Last(held), Catalog, null, Now, Always));

        var free = Running(2, 6, _ => 1, seasonsLeft: 2, perks: Perk.ReleaseClause);
        Assert.NotNull(MidSeason.AfterRace(free, Last(free), Catalog, null, Now, Always));
    }

    [Fact]
    public void A_release_clause_opens_the_off_season_to_the_class_s_other_teams()
    {
        int Moves(params Perk[] perks)
        {
            var career = Running(6, 6, _ => 2, seasonsLeft: 2, perks: perks);
            Progression.EndSeason(career, Catalog, install: null, seed: 1);
            return career.Offers.Count(o => o.Kind == OfferKind.Move);
        }

        Assert.Equal(0, Moves());
        Assert.True(Moves(Perk.ReleaseClause) > 0);
    }

    [Fact]
    public void A_mid_season_offer_can_be_talked_over_on_current_form()
    {
        var career = Running(2, 6, _ => 1);
        MidSeason.AfterRace(career, Last(career), Catalog, null, Now, Always);
        var offer = career.EarlyOffers[0];

        var outcome = Negotiation.Counter(career, offer.Id, offer.GainModifier!.Value, offer.LossModifier!.Value, offer.Perks, Catalog, null, new Roll(0.999));

        Assert.Equal(CounterOutcome.Accepted, outcome);
        Assert.True(offer.Agreed);
        Assert.Empty(career.Offers);   // no fallback seat mid-season

        MidSeason.Sign(career, offer.Id, Now);
        Assert.Equal(EarlyOfferStatus.Signed, offer.Early);
    }
}
