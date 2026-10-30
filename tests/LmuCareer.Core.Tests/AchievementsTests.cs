using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class AchievementsTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly CareerCar Lexus = new("Lexus RCF LMGT3", "GT3", "69", "Sample Racing");

    private static Career Fresh(params Round[] rounds) =>
        new() { CurrentSeason = new Season { Number = 1, Car = Lexus, Rounds = [.. rounds] } };

    private static Round Upcoming(int number) => new() { Number = number, EventName = $"Round {number}" };

    private static IEnumerable<string> Ids(IEnumerable<EarnedAchievement> earned) => earned.Select(e => e.Id);

    [Fact]
    public void The_first_counted_race_earns_rook_where_it_was_raced()
    {
        var career = Fresh(Upcoming(1), Upcoming(2));
        Assert.Empty(Achievements.Update(career, Catalog, Now));

        career.CurrentSeason.Rounds[0] = ProgressionTests.Counted(1, playerRank: 4);
        var earned = Achievements.Update(career, Catalog, Now.AddDays(1));

        var rook = Assert.Single(earned);
        Assert.Equal("rook", rook.Id);
        Assert.Equal("Round 1", rook.EventName);
        Assert.Equal("Lexus RCF LMGT3", rook.CarType);
        Assert.False(rook.Backfilled);

        // Earned once: a second check finds nothing new.
        Assert.Empty(Achievements.Update(career, Catalog, Now.AddDays(2)));
    }

    [Fact]
    public void A_career_from_before_achievements_backfills_what_it_already_did()
    {
        var career = Fresh(ProgressionTests.Counted(1, playerRank: 4), Upcoming(2));

        var earned = Achievements.Update(career, Catalog, Now);

        Assert.Contains("rook", Ids(earned));
        Assert.All(earned, e => Assert.True(e.Backfilled));
        Assert.True(career.AchievementsStarted);
    }

    [Fact]
    public void Century_counts_up_to_a_hundred_races()
    {
        var career = Fresh([.. Enumerable.Range(1, 99).Select(n => ProgressionTests.Counted(n, playerRank: 5)), Upcoming(100)]);
        Achievements.Update(career, Catalog, Now);

        Assert.Equal(new AchievementProgress("century", 99, 100), Achievements.Progress(career, Catalog).Single(p => p.Id == "century"));

        career.CurrentSeason.Rounds[99] = ProgressionTests.Counted(100, playerRank: 5);
        Assert.Contains("century", Ids(Achievements.Update(career, Catalog, Now)));
        Assert.DoesNotContain(Achievements.Progress(career, Catalog), p => p.Id == "century");
    }

    [Theory]
    [InlineData("Porsche 911 GT3 R LMGT3", "Daytona International Speedway Road Course", 1, true)]
    [InlineData("Porsche 911 GT3 R LMGT3", "Daytona International Speedway Road Course", 2, false)] // second isn't a win
    [InlineData("Porsche 911 GT3 R LMGT3", "Sebring International Raceway", 1, false)]              // not Daytona
    [InlineData("Lexus RCF LMGT3", "Daytona International Speedway Road Course", 1, false)]          // not the Porsche
    public void Sonny_Hayes_is_a_Daytona_win_in_the_911_GT3_R(string carType, string track, int rank, bool earned)
    {
        var round = ProgressionTests.Counted(1, playerRank: rank);
        round.TrackCourse = track;
        var career = Fresh(round);
        career.CurrentSeason.Car = Lexus with { CarType = carType };

        Assert.Equal(earned, Ids(Achievements.Update(career, Catalog, Now)).Contains("sonny-hayes"));
    }

    [Fact]
    public void Raced_the_24_is_any_race_at_Le_Mans()
    {
        var round = ProgressionTests.Counted(1, playerRank: 9);
        round.TrackCourse = "Circuit de la Sarthe";

        var earned = Achievements.Update(Fresh(round), Catalog, Now);

        Assert.Equal(round.EventName, earned.Single(e => e.Id == "raced-the-24").EventName);
    }

    [Theory]
    [InlineData("Finished Normally", "Finished Normally", true)]
    [InlineData("Finished Normally", "DNF", false)]
    public void Iron_man_is_a_season_with_every_race_finished(string first, string second, bool earned)
    {
        var career = Fresh(ProgressionTests.Counted(1, playerRank: 3, status: first), ProgressionTests.Counted(2, playerRank: 3, status: second));

        Assert.Equal(earned, Ids(Achievements.Update(career, Catalog, Now)).Contains("iron-man"));
    }

    [Fact]
    public void Iron_man_waits_for_the_season_to_end()
    {
        var career = Fresh(ProgressionTests.Counted(1, playerRank: 3), Upcoming(2));

        Assert.DoesNotContain("iron-man", Ids(Achievements.Update(career, Catalog, Now)));
    }

    [Fact]
    public void The_hard_door_is_a_Hypercar_seat_on_reputation_alone()
    {
        var career = Fresh(Upcoming(1));
        career.CurrentSeason.Car = new CareerCar("Toyota GR010", "Hyper", "8", "Toyota Gazoo Racing");
        career.CurrentSeason.Contract = new Contract { TeamName = "Toyota Gazoo Racing", Tier = 1 };
        Assert.DoesNotContain("hard-door", Ids(Achievements.Update(career, Catalog, Now)));

        career.CurrentSeason.Contract.ReputationDoor = true;
        Assert.Contains("hard-door", Ids(Achievements.Update(career, Catalog, Now)));
    }

    [Theory]
    [InlineData(5, true)]   // the rival (AI 5) finished the season behind the player (P3)
    [InlineData(2, false)]  // the rival (AI 2) beat the player
    public void Nemesis_is_beating_a_rival_in_the_championship(int rivalPosition, bool earned)
    {
        var career = Fresh(ProgressionTests.Counted(1, playerRank: 3));
        career.Rivals.Add(new Rival { Name = $"AI {rivalPosition}", Since = 1 });

        Assert.Equal(earned, Ids(Achievements.Update(career, Catalog, Now)).Contains("nemesis"));
    }

    [Fact]
    public void Loyalty_stages_and_a_burned_bridge_count_as_they_happen()
    {
        var career = Fresh(Upcoming(1));
        career.Loyalty.Add(new Loyalty { Kind = LoyaltyKind.Brand, Id = "lexus", Points = Loyalties.RadarAt });
        Achievements.Update(career, Catalog, Now);

        Assert.Contains("on-their-radar", Ids(career.Achievements));
        Assert.Equal(new AchievementProgress("factory-driver", 1, 3), Achievements.Progress(career, Catalog).Single(p => p.Id == "factory-driver"));

        career.Loyalty[0].Points = Loyalties.FactoryAt;
        career.Loyalty[0].Burned = true;
        var earned = Ids(Achievements.Update(career, Catalog, Now)).ToList();

        Assert.Contains("factory-driver", earned);
        Assert.Contains("burned-bridge", earned);
    }

    [Fact]
    public void New_Game_Plus_achievements_are_never_earned_in_a_normal_career()
    {
        var round = ProgressionTests.Counted(1, playerRank: 1);
        round.TrackCourse = "Circuit de la Sarthe";

        var career = Fresh(round);

        Achievements.Update(career, Catalog, Now);

        Assert.NotEmpty(career.Achievements);
        Assert.DoesNotContain(career.Achievements, e => Achievements.All.Single(a => a.Id == e.Id).NewGamePlus);
    }

    [Fact]
    public void Every_achievement_has_a_unique_id()
    {
        Assert.Equal(20, Achievements.All.Count);
        Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Id).Distinct().Count());
    }
}

public class TrophyRoomTests
{
    [Fact]
    public void Wins_go_on_their_track_and_a_finished_season_on_top_of_the_table_is_a_title()
    {
        var win = ProgressionTests.Counted(1, playerRank: 1);
        win.TrackFolder = "Spa_2023";
        var second = ProgressionTests.Counted(2, playerRank: 2);
        second.TrackFolder = "Monza_2023";
        var career = new Career
        {
            Name = "Test",
            CurrentSeason = new Season { Number = 1, Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"), Rounds = [win, second] },
        };

        var room = TrophyRoom.For(career, ContentCatalog.Default);

        var plate = Assert.Single(room.Wins);
        Assert.Equal(("Spa_2023", "GT3", 1), (plate.TrackFolder, plate.CarClass, plate.Season));
        var title = Assert.Single(room.Titles);
        Assert.Equal(("GT3", 1, ""), (title.CarClass, title.Season, title.SeriesId));

        decimal Record(string id) => room.Records.Single(r => r.Id == id).Value;
        Assert.Equal(1, Record("wins-in-a-season"));
        Assert.Equal(1, Record("win-streak"));
        var table = career.CurrentSeason.Standings().Single().Drivers;
        Assert.Equal(table[0].Points - table[1].Points, Record("title-margin"));
        Assert.DoesNotContain(room.Records, r => r.Id == "fastest-to-hypercar");
    }
}

public class SeasonRecapTests
{
    private static Career Champion(string name, params int[] ranks) => new()
    {
        Name = name,
        CurrentSeason = new Season
        {
            Number = 1, Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
            Rounds = [.. ranks.Select((rank, i) => ProgressionTests.Counted(i + 1, playerRank: rank))],
        },
    };

    [Fact]
    public void The_card_has_the_championship_the_numbers_and_every_finish()
    {
        var career = Champion("Test", 1, 1, 3);
        var room = TrophyRoom.For(career, ContentCatalog.Default);

        var recap = SeasonRecap.For(career, 1, [room]);

        Assert.Equal(1, recap.Position);
        Assert.Equal((3, 2, 3), (recap.Races, recap.Wins, recap.Podiums));
        Assert.Equal([1, 1, 3], recap.Rounds.Select(r => r.ClassRank));
        var table = career.CurrentSeason.Standings().Single().Drivers;
        Assert.Equal(table[0].Points - table[1].Points, recap.Gap);
        Assert.Single(recap.Titles);
    }

    [Fact]
    public void A_record_is_new_only_when_it_beats_every_other_career()
    {
        var career = Champion("Now", 1, 1, 1);
        var older = Champion("Before", 1, 1, 4);
        var rooms = new[] { TrophyRoom.For(career, ContentCatalog.Default), TrophyRoom.For(older, ContentCatalog.Default) };

        var recap = SeasonRecap.For(career, 1, rooms);
        Assert.Contains(recap.NewRecords, r => r.Id == "wins-in-a-season");
        Assert.DoesNotContain(recap.NewRecords, r => r.Id == "hours-raced");

        // The older career's own season beat nothing: three wins beat its two.
        Assert.DoesNotContain(SeasonRecap.For(older, 1, rooms).NewRecords, r => r.Id == "wins-in-a-season");
    }
}
