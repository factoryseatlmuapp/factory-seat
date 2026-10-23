using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class LoyaltyTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    [Fact]
    public void Every_gt_and_hypercar_has_a_known_brand_and_no_prototype_has_one()
    {
        var brands = Catalog.Brands.Select(b => b.Id).ToHashSet();
        foreach (var car in Catalog.Cars)
        {
            if (Loyalties.IsBrandClass(car.Class)) Assert.True(car.Brand is not null && brands.Contains(car.Brand), $"{car.Folder}: brand {car.Brand}");
            else Assert.Null(car.Brand);
        }
    }

    [Fact]
    public void Rival_pairs_name_known_brands_and_work_both_ways()
    {
        var brands = Catalog.Brands.Select(b => b.Id).ToHashSet();
        Assert.All(Catalog.BrandRivals, pair =>
        {
            Assert.Equal(2, pair.Count);
            Assert.All(pair, id => Assert.Contains(id, brands));
        });
        Assert.True(Catalog.AreRivalBrands("porsche", "ferrari"));
        Assert.True(Catalog.AreRivalBrands("ferrari", "porsche"));
        Assert.False(Catalog.AreRivalBrands("porsche", "bmw"));
    }

    [Fact]
    public void Corvette_and_cadillac_share_a_brand_and_so_do_lexus_and_toyota()
    {
        Assert.Equal("gm", Catalog.Car("Corvette_Z06GT3R_2023")!.Brand);
        Assert.Equal("gm", Catalog.Car("Cadillac_V-lmdh_2023")!.Brand);
        Assert.Equal("toyota", Catalog.Car("LexusRCF_GT3_2024")!.Brand);
        Assert.Equal("toyota", Catalog.Car("Toyota_GR10_2023")!.Brand);
    }

    [Fact]
    public void One_outfit_links_its_entries_across_classes()
    {
        Assert.Equal("dkr", Catalog.Team("DKR Engineering", "LMP3")!.OutfitId);
        Assert.Equal("dkr", Catalog.Team("DKR Engineering", "LMP2")!.OutfitId);
        Assert.Equal("af-corse", Catalog.Team("AF Corse", "LMP2")!.OutfitId);
        Assert.Equal("af-corse", Catalog.Team("AF Corse", "Hyper")!.OutfitId);
        Assert.Equal("tf", Catalog.Team("AO by TF", "LMP2")!.OutfitId);
        Assert.Equal("tf", Catalog.Team("TF Sport", "GT3")!.OutfitId);
        // A team that only races under its own name is its own outfit.
        Assert.Equal("ultimate", Catalog.Team("Ultimate", "LMP3")!.OutfitId);
    }

    [Fact]
    public void Slugs_drop_accents_and_punctuation()
    {
        Assert.Equal("racing-spirit-of-leman", ContentCatalog.Slug("Racing Spirit of Léman"));
        Assert.Equal("13-autosport", ContentCatalog.Slug("13 Autosport"));
    }

    [Theory]
    [InlineData(0, LoyaltyStage.Unknown)]
    [InlineData(24.9, LoyaltyStage.Unknown)]
    [InlineData(25, LoyaltyStage.OnTheirRadar)]
    [InlineData(50, LoyaltyStage.DevelopmentDriver)]
    [InlineData(75, LoyaltyStage.FactoryDriver)]
    [InlineData(100, LoyaltyStage.FactoryDriver)]
    public void Points_make_stages(decimal points, LoyaltyStage stage) => Assert.Equal(stage, Loyalties.Stage(points));

    [Fact]
    public void A_gt3_season_builds_brand_loyalty_and_a_prototype_season_team_loyalty()
    {
        Assert.Equal((LoyaltyKind.Brand, "porsche"), Loyalties.Key("GT3", "911GT3R_2024", "Manthey DK Engineering", Catalog));
        Assert.Equal((LoyaltyKind.Brand, "ferrari"), Loyalties.Key("Hyper", "Ferrari_499P_2023", "AF Corse", Catalog));
        Assert.Equal((LoyaltyKind.Team, "dkr"), Loyalties.Key("LMP3", "Ginetta_G61Evo_2025", "DKR Engineering", Catalog));
        // A custom team in a prototype is loyal to itself.
        Assert.Equal((LoyaltyKind.Team, "sample-racing"), Loyalties.Key("LMP3", "Ginetta_G61Evo_2025", "Sample Racing", Catalog));
        // Before the first race names the team, there's nothing to be loyal to yet.
        Assert.Null(Loyalties.Key("LMP2", "Oreca_07_LM_2023", "", Catalog));
    }

    [Fact]
    public void A_season_key_falls_back_to_the_car_type_on_old_saves()
    {
        var season = new Season { Car = new CareerCar("Lexus RCF LMGT3", "GT3", "14", "Akkodis ASP Team") };
        Assert.Equal((LoyaltyKind.Brand, "toyota"), Loyalties.Key(season, Catalog));
    }

    // ---------- Building and leaving ----------

    private static TeamOffer OfferFor(string teamName, string carClass, string folder, int tier, OfferKind kind = OfferKind.Move) =>
        new() { Id = "x", Kind = kind, TeamName = teamName, CarClass = carClass, CarFolder = folder, Tier = tier, TargetPosition = 6 };

    [Fact]
    public void A_title_season_with_every_podium_builds_most_of_the_way_to_development_driver()
    {
        // Lexus GT3: loyalty goes to Toyota. 15 for the season, 10 target, 15 podiums (capped), 10 title.
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(8, _ => 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        var toyota = Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!;
        Assert.Equal(50, toyota.Points);
        Assert.Equal(LoyaltyStage.DevelopmentDriver, Loyalties.Stage(toyota.Points));
        Assert.Equal(1, toyota.LastSeason);
    }

    [Fact]
    public void A_poor_season_still_counts_for_being_there()
    {
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(6, _ => 9));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.Equal(Loyalties.SeasonPoints, Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Points);
    }

    [Fact]
    public void Championship_only_careers_build_no_loyalty()
    {
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(6, _ => 1));
        career.ChampionshipOnly = true;
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.Empty(career.Loyalty);
    }

    [Theory]
    [InlineData(1, 1, false, 30)] // front-runner to front-runner: a big hit
    [InlineData(2, 2, false, 20)]
    [InlineData(3, 1, false, 5)] // a small team to a front-runner: understood
    [InlineData(2, 2, true, 10)] // a step up a class softens it
    [InlineData(1, 3, false, 30)] // stepping down from a front-runner gets no credit
    public void Walking_away_costs_more_the_better_the_team_you_left(int from, int to, bool classUp, decimal cost) =>
        Assert.Equal(cost, Loyalties.WalkAwayCost(from, to, classUp));

    private static Career ReviewedToyotaCareer(int seasonsLeft = 1)
    {
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(8, _ => 1));
        career.CurrentSeason.Contract = new Contract { TeamName = "Akkodis ASP Team", Tier = 2, SeasonsLeft = seasonsLeft };
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        return career;
    }

    [Fact]
    public void Walking_away_from_a_team_that_wanted_you_back_costs_loyalty()
    {
        var career = ReviewedToyotaCareer();
        career.Offers = [OfferFor("Akkodis ASP Team", "GT3", "LexusRCF_GT3_2024", 2, OfferKind.ReSign)];

        var left = Loyalties.Leave(career, career.CurrentSeason, OfferFor("Team WRT", "GT3", "BMW_M4_LMGT3_2023", 1), Catalog, classUp: false);

        Assert.Equal(Departure.WalkedAway, left);
        var toyota = Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!;
        Assert.Equal(50 - Loyalties.WalkAwayCost(2, 1, false), toyota.Points);
        Assert.False(toyota.Burned);
    }

    [Fact]
    public void Being_let_go_keeps_loyalty_as_it_was()
    {
        var career = ReviewedToyotaCareer();
        career.Offers = [];
        Assert.Equal(Departure.LetGo, Loyalties.Leave(career, career.CurrentSeason, OfferFor("Team WRT", "GT3", "BMW_M4_LMGT3_2023", 1), Catalog, false));
        Assert.Equal(50, Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Points);
    }

    [Fact]
    public void Breaking_a_contract_burns_the_bridge()
    {
        var career = ReviewedToyotaCareer(seasonsLeft: 2);
        Assert.Equal(Departure.BrokeContract, Loyalties.Leave(career, career.CurrentSeason, OfferFor("Team WRT", "GT3", "BMW_M4_LMGT3_2023", 1), Catalog, false));
        var toyota = Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!;
        Assert.Equal(0, toyota.Points);
        Assert.True(toyota.Burned);
    }

    [Fact]
    public void Choosing_a_rival_make_burns_the_bridge()
    {
        // Toyota and Porsche are rivals; the team wanted the player back.
        var career = ReviewedToyotaCareer();
        career.Offers = [OfferFor("Akkodis ASP Team", "GT3", "LexusRCF_GT3_2024", 2, OfferKind.ReSign)];
        Assert.Equal(Departure.JoinedRival, Loyalties.Leave(career, career.CurrentSeason, OfferFor("Manthey DK Engineering", "GT3", "911GT3R_2024", 2), Catalog, false));
        Assert.True(Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Burned);
    }

    [Fact]
    public void Let_go_and_taking_a_rivals_seat_is_no_betrayal()
    {
        var career = ReviewedToyotaCareer();
        career.Offers = [];
        Assert.Equal(Departure.LetGo, Loyalties.Leave(career, career.CurrentSeason, OfferFor("Manthey DK Engineering", "GT3", "911GT3R_2024", 2), Catalog, false));
        Assert.False(Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Burned);
    }

    [Fact]
    public void Choosing_a_rival_drivers_team_burns_the_bridge()
    {
        var career = ReviewedToyotaCareer();
        career.Offers = [OfferFor("Akkodis ASP Team", "GT3", "LexusRCF_GT3_2024", 2, OfferKind.ReSign)];
        career.Rivals.Add(new Rival { Name = "AI 2", TeamName = "Team WRT", Kind = RivalryKind.Contact, Since = 1 });
        Assert.Equal(Departure.JoinedRival, Loyalties.Leave(career, career.CurrentSeason, OfferFor("Team WRT", "GT3", "BMW_M4_LMGT3_2023", 1), Catalog, false));
    }

    [Fact]
    public void Moving_to_another_team_with_the_same_make_isnt_leaving()
    {
        // Lexus GT3 to the Toyota Hypercar: still Toyota.
        var career = ReviewedToyotaCareer(seasonsLeft: 2);
        Assert.Null(Loyalties.Leave(career, career.CurrentSeason, OfferFor("Toyota Racing", "Hyper", "Toyota_GR10_2023", 1, OfferKind.Promotion), Catalog, true));
        Assert.Null(Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Left);
    }

    [Fact]
    public void A_burned_bridge_builds_back_slowly()
    {
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(6, _ => 9));
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "toyota", Burned = true });
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        Assert.Equal(Loyalties.SeasonPoints * Loyalties.BurnedRate, Loyalties.Find(career, LoyaltyKind.Brand, "toyota")!.Points);
    }

    [Fact]
    public void A_make_walked_away_from_fades_from_the_second_season_away()
    {
        var career = ProgressionTests.CareerWith(0, "GT3", ProgressionTests.Season(6, _ => 9));
        career.CurrentSeason.Number = 3;
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "porsche", Points = 40, LastSeason = 2, Left = Departure.WalkedAway });
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "bmw", Points = 40, LastSeason = 1, Left = Departure.WalkedAway });
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "ferrari", Points = 40, LastSeason = 1, Left = Departure.LetGo });

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.Equal(40, Loyalties.Find(career, LoyaltyKind.Brand, "porsche")!.Points); // first season away
        Assert.Equal(40 - Loyalties.FadePerSeason, Loyalties.Find(career, LoyaltyKind.Brand, "bmw")!.Points);
        Assert.Equal(40, Loyalties.Find(career, LoyaltyKind.Brand, "ferrari")!.Points); // let go: frozen
    }

    [Fact]
    public void A_career_from_before_loyalty_loads_with_none()
    {
        var json = """{ "schemaVersion": 1, "name": "Old", "currentSeason": { "number": 1, "rounds": [] } }""";
        var career = System.Text.Json.JsonSerializer.Deserialize<Career>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Empty(career.Loyalty);
    }
}
