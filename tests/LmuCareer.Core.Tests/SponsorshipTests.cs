using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

public class SponsorshipTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    private static Career Upcoming(int rounds, decimal reputation = 0, string carType = "Lexus RCF LMGT3")
    {
        var career = CareerWith(reputation, "GT3");
        career.CurrentSeason.Car = career.CurrentSeason.Car with { CarType = carType };
        career.CurrentSeason.Rounds = Enumerable.Range(1, rounds)
            .Select(n => new Round { Number = n, EventName = n == 4 ? "Le Mans 24 Hours" : $"Round {n}", PointsWeight = n == 4 ? 2 : 1 })
            .ToList();
        return career;
    }

    [Fact]
    public void Three_different_deals_are_offered_once_before_the_season()
    {
        var career = Upcoming(8);

        Assert.True(Sponsorship.EnsureOffers(career, Catalog, [], seed: 1));
        var offers = career.CurrentSeason.SponsorOffers!;
        Assert.Equal(Sponsorship.OffersMade, offers.Count);
        Assert.Equal(offers.Count, offers.Select(o => o.SponsorId).Distinct().Count());
        Assert.Equal(offers.Count, offers.Select(o => o.Objective).Distinct().Count());

        Assert.False(Sponsorship.EnsureOffers(career, Catalog, [], seed: 2));
    }

    [Fact]
    public void Mashups_only_call_cars_they_suit_and_a_built_livery_calls_first()
    {
        var calls = new Dictionary<string, int>();
        for (var seed = 0; seed < 200; seed++)
        {
            var career = Upcoming(8, carType: "Ford Mustang LMGT3");
            Sponsorship.EnsureOffers(career, Catalog, ["carhartt"], seed: seed);
            foreach (var offer in career.CurrentSeason.SponsorOffers!)
                calls[offer.SponsorId] = calls.GetValueOrDefault(offer.SponsorId) + 1;
        }

        Assert.False(calls.ContainsKey("makita"));
        Assert.False(calls.ContainsKey("rolex"));
        Assert.True(calls["carhartt"] > calls.GetValueOrDefault("faygo"));
        Assert.True(calls["carhartt"] > 150);
    }

    [Fact]
    public void A_brand_of_your_own_calls_like_a_built_livery()
    {
        var mine = new SponsorInfo("own-greggs-pasty", "Pasty Power", "mashup", 0, [], "Cornwall");
        var calls = Enumerable.Range(0, 100).Count(seed =>
        {
            var career = Upcoming(8, carType: "Ford Mustang LMGT3");
            Sponsorship.EnsureOffers(career, Catalog, ["own-greggs-pasty"], [mine], seed);
            return career.CurrentSeason.SponsorOffers!.Any(o => o.Name == "Pasty Power");
        });

        Assert.True(calls > 75);
    }

    [Fact]
    public void Prestige_sponsors_wait_for_a_reputation()
    {
        var seen = Enumerable.Range(0, 200).SelectMany(seed =>
        {
            var career = Upcoming(8, reputation: 60);
            Sponsorship.EnsureOffers(career, Catalog, [], seed: seed);
            return career.CurrentSeason.SponsorOffers!.Select(o => o.SponsorId);
        }).ToHashSet();

        Assert.Contains("rolex", seen);
    }

    [Fact]
    public void At_most_two_deals_signed_and_only_before_the_first_round_counts()
    {
        var career = Upcoming(8);
        Sponsorship.EnsureOffers(career, Catalog, [], seed: 1);
        var season = career.CurrentSeason;
        var ids = season.SponsorOffers!.Select(o => o.SponsorId).ToList();

        Sponsorship.Sign(season, ids[0]);
        Sponsorship.Sign(season, ids[1]);
        Assert.ThrowsAny<InvalidOperationException>(() => Sponsorship.Sign(season, ids[2]));

        Sponsorship.Drop(season, ids[1]);
        Sponsorship.Sign(season, ids[2]);
        Assert.Equal([ids[0], ids[2]], season.Sponsors.Select(s => s.SponsorId));

        season.Rounds[0] = Counted(1, 1);
        Assert.ThrowsAny<InvalidOperationException>(() => Sponsorship.Drop(season, ids[0]));
    }

    [Fact]
    public void Count_objectives_fail_once_they_are_out_of_reach()
    {
        var season = CareerWith(0, "GT3", Counted(1, 5), Counted(2, 6)).CurrentSeason;
        season.Rounds.Add(new Round { Number = 3 });
        var deal = new SponsorDeal { Objective = ObjectiveKind.Podiums, Count = 2 };

        Assert.Equal(new ObjectiveProgress(ObjectiveStatus.Failed, 0, 2), Sponsorship.Progress(season, deal));
        deal.Count = 1;
        Assert.Equal(ObjectiveStatus.InProgress, Sponsorship.Progress(season, deal).Status);
    }

    [Fact]
    public void A_met_deal_pays_at_the_review_and_a_missed_one_is_listed_for_nothing()
    {
        var career = CareerWith(0, "GT3", Enumerable.Range(1, 6).Select(n => Counted(n, n == 3 ? 4 : 1, customCar: n > 2)).ToArray());
        var season = career.CurrentSeason;
        season.Sponsors.Add(new SponsorDeal { Name = "Carhartt", Objective = ObjectiveKind.Podiums, Count = 3, Reward = 3, RunningLivery = true });
        season.Sponsors.Add(new SponsorDeal { Name = "Motul", Objective = ObjectiveKind.PodiumAt, RoundNumber = 3, EventName = "Round 3", Reward = 3 });

        var review = Progression.Review(season, 0);

        Assert.Contains(new ReputationItem("Carhartt: 3 podiums", 3), review.Items);
        Assert.Contains(new ReputationItem("Ran the Carhartt livery (4 of 6 rounds)", 1), review.Items);
        Assert.Contains(new ReputationItem("Motul: missed (a podium at the Round 3)", 0), review.Items);
    }

    [Fact]
    public void The_livery_bonus_needs_the_custom_car_in_half_the_rounds()
    {
        var career = CareerWith(0, "GT3", Enumerable.Range(1, 6).Select(n => Counted(n, 5, customCar: n <= 2)).ToArray());
        career.CurrentSeason.Sponsors.Add(new SponsorDeal { Name = "Makita", Objective = ObjectiveKind.Poles, Count = 1, Reward = 2, RunningLivery = true });

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Contains(new ReputationItem("Makita livery not seen: your custom car ran 2 of 6 rounds", 0), review.Items);
    }

    [Fact]
    public void The_rival_is_last_seasons_driver_just_ahead()
    {
        var career = CareerWith(0, "GT3", Season(6, _ => 2));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var stay = career.Offers.First(o => o.CarClass == "GT3");
        Progression.StartNextSeason(career, stay.Id, Enumerable.Range(1, 6).Select(n => new Round { Number = n }).ToList(), Catalog);

        var offers = Enumerable.Range(0, 50).SelectMany(seed =>
        {
            career.CurrentSeason.SponsorOffers = null;
            Sponsorship.EnsureOffers(career, Catalog, [], seed: seed);
            return career.CurrentSeason.SponsorOffers!;
        });

        Assert.Contains(offers, o => o.Objective == ObjectiveKind.BeatRival && o.Rival == "AI 1");
    }
}
