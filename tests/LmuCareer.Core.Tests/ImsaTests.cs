using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class ImsaTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly string[] AllPacks = Catalog.Packs.Select(p => p.Id).ToArray();

    /// <summary>A random sequence that always draws the same value: every chance below it comes up.</summary>
    private sealed class Always(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public void Owning_everything_gives_six_imsa_rounds_in_calendar_order()
    {
        var rounds = Imsa.Calendar(Catalog, "GT3", DriverRating.Silver, AllPacks);
        Assert.Equal(["daytona-24", "sebring-12", "long-beach", "laguna-seca", "cota-imsa", "petit-le-mans"], rounds.Select(r => r.EventId));
        Assert.All(rounds, r => Assert.Equal(Imsa.Id, r.SeriesId));
    }

    [Fact]
    public void The_base_game_alone_has_only_sebring()
    {
        Assert.Equal(["sebring-12"], Imsa.Calendar(Catalog, "GT3", DriverRating.Silver, []).Select(r => r.EventId));
    }

    [Theory]
    [InlineData("Hyper", true)]
    [InlineData("GT3", true)]
    [InlineData("LMP2", true)]
    [InlineData("LMP3", false)]
    [InlineData("GTE", false)]
    public void Imsa_races_gtp_gtd_and_lmp2(string carClass, bool races) => Assert.Equal(races, Imsa.RacesIn(carClass));

    [Fact]
    public void Imsa_rounds_slot_in_beside_the_world_season_by_month()
    {
        var season = new Season { Number = 2, Rounds = CalendarBuilder.DefaultSeason(Catalog, "GT3", DriverRating.Silver, AllPacks) };
        var wec = season.Rounds.Count;
        Imsa.Add(season, new CareerCar("Lexus RCF LMGT3", "GT3", "", "Sample Racing USA"), new Contract { TeamName = "Sample Racing USA" },
            Imsa.Calendar(Catalog, "GT3", DriverRating.Silver, AllPacks), Catalog);

        Assert.Equal(wec, season.ChampionshipRounds.Count());
        Assert.Equal(6, season.SeriesRounds(Imsa.Id).Count());
        Assert.Equal(Enumerable.Range(1, wec + 6), season.Rounds.Select(r => r.Number));
        var months = season.Rounds.Select(r => Catalog.Event(r.EventId)!.Month).ToList();
        Assert.Equal(months.OrderBy(m => m), months);
        Assert.Equal("daytona-24", season.Rounds[0].EventId);
        // April has two weekends: the WEC's Imola, then IMSA's Long Beach.
        var april = season.Rounds.Where(r => Catalog.Event(r.EventId)!.Month == 4).Select(r => r.EventId).ToList();
        Assert.Equal(["imola-6", "long-beach"], april);
    }

    [Theory]
    [InlineData(1, null, 10, TeamStatus.FrontRunnerBudget)]
    [InlineData(2, null, 10, TeamStatus.Stable)]
    [InlineData(3, null, 10, TeamStatus.Struggling)]
    [InlineData(3, 1, 10, TeamStatus.Growing)] // a small team that won the title: cash coming in
    [InlineData(2, 1, 10, TeamStatus.FrontRunnerBudget)]
    [InlineData(2, 3, 10, TeamStatus.Growing)]
    [InlineData(2, 8, 10, TeamStatus.Struggling)] // a bad year
    [InlineData(1, 9, 10, TeamStatus.Growing)]
    public void Team_status_comes_from_tier_and_last_season(int tier, int? position, int teams, TeamStatus status) =>
        Assert.Equal(status, Teams.Status(tier, position, teams));

    private static TeamOffer Offer(string team, string folder, int tier) =>
        new() { Id = "o", Kind = OfferKind.Move, TeamName = team, CarClass = "GT3", CarFolder = folder, Tier = tier, TargetPosition = 6 };

    [Fact]
    public void A_team_with_the_budget_can_run_imsa_too()
    {
        var career = ProgressionTests.CareerWith(30, "GT3", ProgressionTests.Season(6, _ => 5));
        var offers = new[] { Offer("Team WRT", "BMW_M4_LMGT3_2023", 1), Offer("Kessel Racing", "Ferrari_296GT3_2023", 3) };

        Teams.AddImsa(career, offers, Catalog, null, new Always(0.5));

        Assert.Equal(TeamStatus.FrontRunnerBudget, offers[0].Status);
        Assert.Equal("Team WRT", offers[0].ImsaTeam);
        Assert.Equal(TeamStatus.Struggling, offers[1].Status);
        Assert.Equal("", offers[1].ImsaTeam);
    }

    [Fact]
    public void A_make_that_rates_you_places_you_with_a_partner_team_in_imsa()
    {
        var career = ProgressionTests.CareerWith(30, "GT3", ProgressionTests.Season(6, _ => 5));
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "porsche", Points = 55 });
        var offers = new[] { Offer("Iron Dames", "911GT3R_2024", 2) };

        // Too much for the team's own budget (stable: 10%), but Porsche has partners.
        Teams.AddImsa(career, offers, Catalog, null, new Always(0.3));

        Assert.Contains(offers[0].ImsaTeam, new[] { "The Bend Manthey", "Manthey DK Engineering" });
    }

    [Fact]
    public void No_imsa_seats_without_the_tracks_for_a_season()
    {
        // The base game has only Sebring: one round isn't a season.
        var career = ProgressionTests.CareerWith(30, "GT3", ProgressionTests.Season(6, _ => 5));
        career.OwnedContent = [];
        var offers = new[] { Offer("Team WRT", "BMW_M4_LMGT3_2023", 1) };
        Teams.AddImsa(career, offers, Catalog, null, new Always(0.5));
        Assert.Equal("", offers[0].ImsaTeam);
        // US Track Pack 1 adds Daytona and Laguna Seca: three rounds.
        career.OwnedContent = ["us-1"];
        Teams.AddImsa(career, offers, Catalog, null, new Always(0.5));
        Assert.Equal("Team WRT", offers[0].ImsaTeam);
    }

    [Fact]
    public void Signing_a_deal_with_imsa_adds_the_imsa_season_beside_it()
    {
        var career = ProgressionTests.CareerWith(30, "GT3", ProgressionTests.Season(6, _ => 5));
        career.CurrentSeason.Review = new SeasonReview { ChampionshipPosition = 5, TargetPosition = 6, TargetMet = true };
        var offer = Offer("Team WRT", "BMW_M4_LMGT3_2023", 1);
        offer.ImsaTeam = "Team WRT";
        career.Offers = [offer];

        var next = Progression.StartNextSeason(career, "o", CalendarBuilder.DefaultSeason(Catalog, "GT3", DriverRating.Silver, AllPacks), Catalog);

        var imsa = Assert.Single(next.Series);
        Assert.Equal("Team WRT", imsa.Contract!.TeamName);
        Assert.Equal("BMW_M4_LMGT3_2023", imsa.Car.Folder);
        Assert.Equal(6, next.SeriesRounds(Imsa.Id).Count());
        Assert.False(next.IsFinished);
    }

    [Fact]
    public void The_imsa_championship_counts_in_the_review_at_half_weight()
    {
        var wec = ProgressionTests.Season(6, _ => 5);
        var imsa = Enumerable.Range(7, 6).Select(n =>
        {
            var r = ProgressionTests.Counted(n, 1);
            r.SeriesId = Imsa.Id;
            return r;
        });
        var career = ProgressionTests.CareerWith(0, "GT3", [.. wec, .. imsa]);
        career.CurrentSeason.Series.Add(new SeriesEntry { Id = Imsa.Id, Name = Imsa.Name, Car = career.CurrentSeason.Car });

        var review = Progression.Review(career.CurrentSeason, 0);

        Assert.Equal(5, review.ChampionshipPosition);
        Assert.Contains(review.Items, i => i.Label == "IMSA championship P1" && i.Points == 6);
        Assert.Contains(review.Items, i => i.Label == "6 class wins");
    }
}
