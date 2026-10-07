using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Tests;

public class ProgressionTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly string[] AllPacks = Catalog.Packs.Select(p => p.Id).ToArray();

    /// <summary>A counted round where the player finished in <paramref name="playerRank"/> against nine AI cars.</summary>
    internal static Round Counted(int number, int playerRank, string carClass = "GT3", decimal weight = 1,
        string status = "Finished Normally", int penalties = 0, bool customCar = false)
    {
        var xml = new ResultsXml();
        var carType = carClass == "GT3" ? "Lexus RCF LMGT3" : "Oreca 07";
        for (var pos = 1; pos <= 10; pos++)
        {
            if (pos == playerRank) xml.Car("Me", carClass: carClass, carType: carType, classPos: pos, player: true, number: "69", team: "Sample Racing", status: status,
                vehicle: customCar ? "Lexus Custom Team 2026 #397" : "Akkodis ASP Team 2026 #87:LM");
            else xml.Car($"AI {pos}", carClass: carClass, carType: carType, classPos: pos, number: $"{pos}");
        }

        return new Round
        {
            Number = number,
            EventName = weight >= 2 ? "Le Mans 24 Hours" : $"Round {number}",
            PointsWeight = weight,
            State = RoundState.Completed,
            Result = new RoundResult
            {
                RaceFile = $"R{number}.xml",
                PlayerPenalties = penalties,
                Entries = RaceScorer.Score(xml.Parse(), weight: weight).Entries,
            },
        };
    }

    internal static Career CareerWith(decimal reputation, string carClass, params Round[] rounds) => new()
    {
        Reputation = reputation,
        OwnedContent = [.. AllPacks],
        CurrentSeason = new Season
        {
            Number = 1,
            Car = carClass == "GT3"
                ? new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing")
                : new CareerCar("Oreca 07", carClass, "69", "Sample Racing"),
            Rounds = rounds.ToList(),
        },
    };

    internal static Round[] Season(int rounds, Func<int, int> rank) =>
        Enumerable.Range(1, rounds).Select(n => Counted(n, rank(n))).ToArray();

    [Fact]
    public void A_dominant_rookie_season_is_capped_and_earns_a_promotion()
    {
        var career = CareerWith(0, "GT3", Season(8, _ => 1));

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var review = career.CurrentSeason.Review!;
        Assert.Equal(1, review.ChampionshipPosition);
        Assert.True(review.TargetMet);
        Assert.Equal(Progression.MaxGain, career.Reputation);
        Assert.Contains(review.Items, i => i.Label.StartsWith("Reputation grows at most"));

        Assert.Contains(career.Offers, o => o.Kind == OfferKind.ReSign && o.TeamName == "Sample Racing" && o.CarNumber == "69");
        Assert.Equal(2, career.Offers.Count(o => o.Kind == OfferKind.Promotion && o.CarClass == "LMP3"));
        Assert.All(career.Offers.Where(o => o.Kind == OfferKind.Move), o => Assert.Equal("GT3", o.CarClass));
        Assert.InRange(career.Offers.Count, 2, 4);
    }

    [Fact]
    public void A_short_season_counts_pro_rata()
    {
        var career = CareerWith(0, "GT3", Counted(1, 1));

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var review = career.CurrentSeason.Review!;
        Assert.Equal(1.0 / 6, review.SeasonWeight, 3);
        Assert.Equal(new ReputationItem("Championship P1", 2), review.Items[0]);
        Assert.InRange(career.Reputation, 1, 10);
        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.Promotion);
    }

    [Fact]
    public void A_bad_season_costs_reputation_and_the_seat()
    {
        var career = CareerWith(30, "GT3",
            Counted(1, 9, status: "DNF"), Counted(2, 10), Counted(3, 9, penalties: 2), Counted(4, 10), Counted(5, 8), Counted(6, 10));

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var review = career.CurrentSeason.Review!;
        Assert.False(review.TargetMet);
        Assert.Contains(new ReputationItem("1 DNF", -2), review.Items);
        Assert.Contains(new ReputationItem("2 penalties", -1), review.Items);
        Assert.Equal(22, career.Reputation);
        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.ReSign);
        Assert.Equal(3, career.Offers.Count(o => o.Kind == OfferKind.Move));
    }

    [Fact]
    public void Big_event_wins_count_extra()
    {
        var career = CareerWith(0, "GT3", Counted(1, 1, weight: 2), Counted(2, 1), Counted(3, 2), Counted(4, 3));

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Contains(new ReputationItem("Won the Le Mans 24 Hours", 5), review.Items);
        Assert.Contains(new ReputationItem("1 class win", 2), review.Items);
        Assert.Contains(new ReputationItem("2 podiums", 2), review.Items);
        Assert.Contains(new ReputationItem("Clean season", 3), review.Items);
    }

    [Fact]
    public void The_review_happens_once()
    {
        var career = CareerWith(0, "GT3", Season(6, _ => 1));

        Assert.True(Progression.EndSeason(career, Catalog, install: null, seed: 1));
        var reputation = career.Reputation;
        var offers = career.Offers.Select(o => o.Id).ToList();

        Assert.False(Progression.EndSeason(career, Catalog, install: null, seed: 2));
        Assert.Equal(reputation, career.Reputation);
        Assert.Equal(offers, career.Offers.Select(o => o.Id));
    }

    [Fact]
    public void Offers_only_use_cars_the_player_owns()
    {
        var career = CareerWith(0, "GT3", Season(8, _ => 1));
        career.OwnedContent = [];

        Progression.EndSeason(career, Catalog, install: null, seed: 3);

        Assert.All(career.Offers.Where(o => o.Kind != OfferKind.ReSign),
            o => Assert.True(ContentCatalog.IsOwned(Catalog.Car(o.CarFolder)!.Pack, [])));
        // No LMP3 car without the European packs: the ladder skips to LMP2, whose bar a rookie hasn't cleared.
        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.Promotion);
    }

    [Fact]
    public void Hypercar_teams_want_two_strong_prototype_seasons()
    {
        var career = CareerWith(70, "LMP2", Enumerable.Range(1, 6).Select(n => Counted(n, 1, carClass: "LMP2")).ToArray());

        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.DoesNotContain(career.Offers, o => o.CarClass == "Hyper");

        var veteran = CareerWith(70, "LMP2", Enumerable.Range(1, 6).Select(n => Counted(n, 1, carClass: "LMP2")).ToArray());
        veteran.PastSeasons.Add(new Season
        {
            Car = new CareerCar("Ligier JS P325", "LMP3", "69", ""),
            Review = new SeasonReview { ChampionshipPosition = 2, TargetMet = true },
        });

        Progression.EndSeason(veteran, Catalog, install: null, seed: 1);
        Assert.Contains(veteran.Offers, o => o.Kind == OfferKind.Promotion && o.CarClass == "Hyper");
    }

    [Fact]
    public void Better_teams_call_as_reputation_grows()
    {
        Assert.Equal(3, Progression.BestTier(0, "GT3"));
        Assert.Equal(2, Progression.BestTier(12, "GT3"));
        Assert.Equal(1, Progression.BestTier(25, "GT3"));
        Assert.Equal(3, Progression.BestTier(25, "LMP3"));
    }

    [Fact]
    public void Signing_an_offer_starts_the_next_season()
    {
        var career = CareerWith(0, "GT3", Season(8, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var offer = career.Offers.First(o => o.Kind == OfferKind.Promotion);
        var rounds = CalendarBuilder.DefaultSeason(Catalog, offer.CarClass, DriverRating.Silver, AllPacks);

        var next = Progression.StartNextSeason(career, offer.Id, rounds, Catalog);

        Assert.Same(next, career.CurrentSeason);
        Assert.Equal(2, next.Number);
        Assert.Equal("LMP3", next.Car.CarClass);
        Assert.Equal("", next.Car.CarNumber);
        Assert.Equal(offer.TeamName, next.Contract!.TeamName);
        Assert.Equal(offer.TargetPosition, next.Contract.TargetPosition);
        Assert.Single(career.PastSeasons);
        Assert.Empty(career.Offers);
        Assert.Equal(Enumerable.Range(1, rounds.Count), next.Rounds.Select(r => r.Number));
    }

    [Fact]
    public void Re_signing_keeps_the_number_and_raises_the_target_after_a_title()
    {
        var career = CareerWith(0, "GT3", Season(8, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var offer = career.Offers.Single(o => o.Kind == OfferKind.ReSign);
        Assert.Equal(3, offer.TargetPosition);

        var next = Progression.StartNextSeason(career, offer.Id, [new Round()], Catalog);

        Assert.Equal(("69", "Sample Racing"), (next.Car.CarNumber, next.Car.TeamName));
    }

    [Fact]
    public void Under_contract_the_team_keeps_you_and_only_a_step_up_can_buy_you_out()
    {
        var career = CareerWith(30, "GT3", Season(6, _ => 3));
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = 2, TargetPosition = 6, SeasonsLeft = 2 };

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var stay = career.Offers.Single(o => o.Kind == OfferKind.ReSign);
        Assert.True(stay.UnderContract);
        Assert.Equal((6, 1), (stay.TargetPosition, stay.Seasons));
        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.Move);
        Assert.Contains(career.Offers, o => o.Kind == OfferKind.Promotion);

        var next = Progression.StartNextSeason(career, stay.Id, [new Round()], Catalog);
        Assert.Equal(1, next.Contract!.SeasonsLeft);
    }

    [Fact]
    public void A_bad_season_ends_a_contract_early()
    {
        var career = CareerWith(30, "GT3", Season(6, _ => 10));
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = 2, TargetPosition = 3, SeasonsLeft = 2 };

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.ReSign);
        Assert.Contains(career.Offers, o => o.Kind == OfferKind.Move);
    }

    [Fact]
    public void A_big_win_gets_you_noticed_and_that_team_calls_at_the_end_of_the_season()
    {
        var career = CareerWith(20, "GT3", Counted(1, 1, weight: 2), Counted(2, 4), Counted(3, 1), Counted(4, 2), Counted(5, 2), Counted(6, 1));

        Assert.Null(Progression.NoticeResult(career, career.CurrentSeason.Rounds[2], Catalog, install: null));
        var interest = Progression.NoticeResult(career, career.CurrentSeason.Rounds[0], Catalog, install: null);

        Assert.NotNull(interest);
        Assert.Equal("your win at the Le Mans 24 Hours", interest.Reason);
        // Reputation 20 is within 15 of LMP3's bar: an LMP3 team takes a look.
        Assert.Equal("LMP3", interest.CarClass);

        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var offer = career.Offers.Single(o => o.Reason.Length > 0);
        Assert.Equal((interest.TeamName, OfferKind.Promotion), (offer.TeamName, offer.Kind));
    }

    [Fact]
    public void The_pace_check_compares_best_laps_with_the_fastest_ai_in_class()
    {
        // In the test races best laps are 100.5 + position: a winner is 1 s quicker than P2.
        var winning = CareerWith(0, "GT3", Season(4, _ => 1)).CurrentSeason;
        var summary = PaceCheck.Summarize(winning);
        Assert.Equal(-1.0, summary.Last!.Gap, 3);
        Assert.Equal((3, PaceAdvice.TooEasy), (summary.RecentRounds, summary.Advice));

        var midfield = PaceCheck.Summarize(CareerWith(0, "GT3", Season(3, _ => 3)).CurrentSeason);
        Assert.Equal(2.0, midfield.RecentGap!.Value, 3);
        Assert.Equal(PaceAdvice.TooHard, midfield.Advice);

        Assert.Null(PaceCheck.Summarize(CareerWith(0, "GT3").CurrentSeason).Advice);
    }

    [Fact]
    public void Every_catalog_team_runs_a_catalog_car_in_its_class()
    {
        foreach (var team in Catalog.Teams)
        {
            Assert.Contains(team.Class, Progression.Ladder);
            Assert.All(team.Cars, folder => Assert.Equal(team.Class, Catalog.Car(folder)?.Class));
        }
    }
}
