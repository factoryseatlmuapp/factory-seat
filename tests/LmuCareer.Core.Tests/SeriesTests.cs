using LmuCareer.Core.Careers;

namespace LmuCareer.Core.Tests;

/// <summary>A season raced in two championships at once: the season's own (WEC) and IMSA beside it.</summary>
public class SeriesTests
{
    private static Season TwoSeries()
    {
        var wec1 = ProgressionTests.Counted(1, playerRank: 1);
        var imsa = ProgressionTests.Counted(2, playerRank: 5);
        imsa.SeriesId = "imsa";
        var wec2 = new Round { Number = 3, EventName = "Round 3" };
        return new Season
        {
            Number = 2,
            Car = new CareerCar("Lexus RCF LMGT3", "GT3", "14", "Akkodis ASP Team") { Folder = "LexusRCF_GT3_2024" },
            Contract = new Contract { TeamName = "Akkodis ASP Team", TargetPosition = 6 },
            Series =
            [
                new SeriesEntry
                {
                    Id = "imsa",
                    Name = "IMSA",
                    Car = new CareerCar("Lexus RCF LMGT3", "GT3", "12", "Sample Racing USA") { Folder = "LexusRCF_GT3_2024" },
                    Contract = new Contract { TeamName = "Sample Racing USA", TargetPosition = 3 },
                },
            ],
            Rounds = [wec1, imsa, wec2],
        };
    }

    [Fact]
    public void Each_round_races_its_own_championships_car_and_team()
    {
        var season = TwoSeries();
        Assert.Equal("14", season.CarFor(season.Rounds[0]).CarNumber);
        Assert.Equal("12", season.CarFor(season.Rounds[1]).CarNumber);
        Assert.Equal("Akkodis ASP Team", season.TeamFor(season.Rounds[0]));
        Assert.Equal("Sample Racing USA", season.TeamFor(season.Rounds[1]));
        Assert.Equal(3, season.ContractFor(season.Rounds[1])!.TargetPosition);
    }

    [Fact]
    public void Each_championship_keeps_its_own_standings()
    {
        var season = TwoSeries();
        Assert.Equal([1, 3], season.ChampionshipRounds.Select(r => r.Number));
        Assert.Equal([2], season.SeriesRounds("imsa").Select(r => r.Number));

        // P1 in the WEC round, P5 in the IMSA one: each table only has its own race in it.
        var wec = season.Standings().Single().Drivers.First(d => d.IsPlayer);
        var imsa = season.Standings("imsa").Single().Drivers.First(d => d.IsPlayer);
        Assert.Equal([1], wec.RoundRanks);
        Assert.Equal([5], imsa.RoundRanks);
        Assert.Equal(5, season.StandingsFor(season.Rounds[1])!.Drivers.First(d => d.IsPlayer).RoundRanks.Single());
    }

    [Fact]
    public void A_guest_drive_counts_for_neither_championship()
    {
        var season = TwoSeries();
        season.Rounds.Add(new Round { Number = 4, Guest = true, SeriesId = "" });
        Assert.DoesNotContain(season.Rounds[3], season.ChampionshipRounds);
        Assert.Null(season.ContractFor(season.Rounds[3]));
    }

    [Fact]
    public void Stakes_for_an_imsa_round_read_the_imsa_table_and_contract()
    {
        var season = TwoSeries();
        var career = new Career { Name = "Two series", CurrentSeason = season };
        var imsaNext = new Round { Number = 4, EventName = "Petit Le Mans", SeriesId = "imsa", PointsWeight = 1 };
        season.Rounds.Add(imsaNext);

        var stakes = Preparation.Stakes(career, imsaNext);
        Assert.Equal(5, stakes.Position);
    }

    [Fact]
    public void A_season_from_before_series_loads_with_every_round_in_its_own_championship()
    {
        var json = """{ "number": 1, "rounds": [ { "number": 1, "eventName": "Spa" } ] }""";
        var season = System.Text.Json.JsonSerializer.Deserialize<Season>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Empty(season.Series);
        Assert.Equal("", season.Rounds[0].SeriesId);
        Assert.Single(season.ChampionshipRounds);
    }
}
