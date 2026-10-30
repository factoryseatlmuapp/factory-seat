using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class NewGamePlusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);

    private static Career Finished(int races, params string[] achievements)
    {
        var career = new Career
        {
            Name = "Old career",
            CurrentSeason = new Season
            {
                Number = 1, Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
                Rounds = [.. Enumerable.Range(1, races).Select(n => ProgressionTests.Counted(n, playerRank: n == 1 ? 1 : 4))],
            },
        };
        career.Achievements.AddRange(achievements.Select(id => new EarnedAchievement { Id = id }));
        return career;
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, false)] // a short season doesn't open New Game+
    public void A_career_retires_after_a_finished_season_of_five_races(int races, bool can)
    {
        Assert.Equal(can, Retirement.CanRetire(Finished(races)));
    }

    [Fact]
    public void A_season_still_running_doesnt_count_yet()
    {
        var career = Finished(5);
        career.CurrentSeason.Rounds.Add(new Round { Number = 6, EventName = "Round 6" });

        Assert.False(Retirement.CanRetire(career));
    }

    [Fact]
    public void Retiring_writes_the_hall_of_fame_entry_and_stops_the_career()
    {
        var career = Finished(5, "on-their-radar", "rook");

        var entry = Retirement.Retire(career, ContentCatalog.Default, Now);

        Assert.Equal(Now, career.RetiredAt);
        Assert.False(Retirement.CanRetire(career));
        Assert.Equal(("Old career", 1, 1, "69"), (entry.Name, entry.Seasons, entry.Wins, entry.CarNumber));
        Assert.Equal(["on-their-radar", "rook"], entry.Achievements);
    }

    [Fact]
    public void Achievements_in_the_hall_of_fame_unlock_head_starts()
    {
        var hall = new[]
        {
            new HallOfFameEntry { Achievements = ["rook", "on-their-radar"] },
            new HallOfFameEntry { Achievements = ["called-up", "on-their-radar"] },
        };

        Assert.Equal([HeadStarts.Reputation5, HeadStarts.FrontRunner], HeadStarts.Unlocked(hall));
    }

    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { HeadStarts.Reputation5 }, 5)]
    [InlineData(new[] { HeadStarts.Reputation5, HeadStarts.Reputation10 }, 10)] // the bigger one, not both
    public void Starting_reputation_takes_the_bigger_head_start(string[] picked, int reputation)
    {
        Assert.Equal(reputation, HeadStarts.StartingReputation(picked));
        Assert.True(HeadStarts.StartingReputation(picked) < Progression.Threshold("LMP3"));
    }

    [Fact]
    public void Head_starts_only_work_while_the_career_is_New_Game_plus()
    {
        var career = Finished(1);
        var before = Sponsorship.OffersFor(career);
        career.NewGamePlus = new NewGamePlusState { HeadStarts = [HeadStarts.ExtraSponsor] };
        Assert.Equal(before + 1, Sponsorship.OffersFor(career));

        career.NewGamePlus.EndedAt = Now;
        Assert.Equal(before, Sponsorship.OffersFor(career));
    }

    [Fact]
    public void The_hall_of_fame_file_keeps_one_entry_per_career()
    {
        var root = Path.Combine(Path.GetTempPath(), "fs-hall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var hall = new HallOfFame(root);
            var id = Guid.NewGuid();
            hall.Add(new HallOfFameEntry { CareerId = id, Name = "A" });
            hall.Add(new HallOfFameEntry { CareerId = id, Name = "A again" });
            hall.Add(new HallOfFameEntry { CareerId = Guid.NewGuid(), Name = "B" });

            Assert.Equal(["A again", "B"], new HallOfFame(root).Entries().Select(e => e.Name));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class FloorTests
{
    private static Career NewGamePlus(string carClass, params string[] tracks)
    {
        var career = new Career
        {
            NewGamePlus = new NewGamePlusState(),
            CurrentSeason = new Season
            {
                Number = 1, Car = new CareerCar("Car", carClass, "1", "Team"),
                Rounds = [.. tracks.Select((track, i) => new Round { Number = i + 1, TrackFolder = track, RaceMinutes = 60 })],
            },
        };
        return career;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    public void A_third_of_the_rounds_are_wet_rounded_up(int rounds, int wet)
    {
        Assert.Equal(wet, Floor.WetRounds(rounds));
    }

    [Fact]
    public void Every_round_gets_strength_in_its_class_band_damage_by_length_and_a_third_wet()
    {
        var career = NewGamePlus("LMP2", "Spa_2023", "Qatar_2024", "Monza_2023", "Bahrain_2023", "Fuji_2023", "LeMans_2023");
        career.CurrentSeason.Rounds[0].RaceMinutes = 20;

        Assert.True(Floor.Fill(career, new Random(1)));

        var floors = career.CurrentSeason.Rounds.Select(r => r.Floor!).ToList();
        Assert.All(floors, f => Assert.InRange(f.AiStrength, 95, 98));
        Assert.Equal(100, floors[0].MinDamage);
        Assert.Equal(75, floors[1].MinDamage);
        Assert.Equal(2, floors.Count(f => f.Wet));
        Assert.False(Floor.Fill(career)); // drawn once
    }

    [Fact]
    public void Rain_falls_where_it_really_rains()
    {
        var wetAtSpa = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var career = NewGamePlus("GT3", "Spa_2023", "Qatar_2024", "BahrainWEC_2023");
            Floor.Fill(career, new Random(seed));
            if (career.CurrentSeason.Rounds[0].Floor!.Wet) wetAtSpa++;
        }
        Assert.True(wetAtSpa > 170, $"Spa wet {wetAtSpa} of 200");
    }

    [Fact]
    public void A_normal_career_has_no_floor()
    {
        var career = NewGamePlus("GT3", "Spa_2023");
        career.NewGamePlus = null;

        Assert.False(Floor.Fill(career));
        Assert.Null(career.CurrentSeason.Rounds[0].Floor);
    }
}
