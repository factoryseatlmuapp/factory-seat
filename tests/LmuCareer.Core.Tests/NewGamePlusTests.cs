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
