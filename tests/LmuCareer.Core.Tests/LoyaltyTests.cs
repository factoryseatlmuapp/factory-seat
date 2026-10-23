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

    [Fact]
    public void A_career_from_before_loyalty_loads_with_none()
    {
        var json = """{ "schemaVersion": 1, "name": "Old", "currentSeason": { "number": 1, "rounds": [] } }""";
        var career = System.Text.Json.JsonSerializer.Deserialize<Career>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Empty(career.Loyalty);
    }
}
