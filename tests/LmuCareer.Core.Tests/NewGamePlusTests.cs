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

public class FloorCheckTests
{
    private static readonly RoundFloor Dry = new(93, Wet: false, MinDamage: 75);

    private static ResultsXml Field(int damage = 100, int mechFail = 1, string aids = "PlayerControl,TC=2", double playerBest = 101.5, string? lapsXml = null) =>
        new ResultsXml { Damage = damage, MechFail = mechFail, Fuel = 1, Tires = 1 }
            .Car("Me", player: true, classPos: 1, best: playerBest, control: [(1, 10, aids)])
            .Car("AI 1", classPos: 2, best: 102.5, lapsXml: lapsXml);

    [Fact]
    public void A_race_on_the_floor_passes()
    {
        var check = FloorChecks.Check(Dry, Field().Parse(), null, aiStrengthSeen: 93);
        Assert.False(check.Reduced);
    }

    [Theory]
    [InlineData(50, 1, "PlayerControl,TC=2", FloorFlag.DamageLow)]
    [InlineData(100, 1, "PlayerControl,TC=2,Invulnerable", FloorFlag.Invulnerable)]
    [InlineData(100, 0, "PlayerControl,TC=2", FloorFlag.FailuresOff)]
    public void Settings_below_the_floor_are_flagged(int damage, int mechFail, string aids, FloorFlag flag)
    {
        var check = FloorChecks.Check(Dry, Field(damage, mechFail, aids).Parse(), null, aiStrengthSeen: 93);
        Assert.Equal([flag], check.Flags);
    }

    [Fact]
    public void A_wet_round_needs_a_car_on_wet_tyres()
    {
        var wet = Dry with { Wet = true };
        Assert.Contains(FloorFlag.StayedDry, FloorChecks.Check(wet, Field().Parse(), null, 93).Flags);

        var wetLap = "<Lap num=\"1\" p=\"2\" et=\"120.5\" fcompound=\"1,Wet\" rcompound=\"1,Wet\">130.1</Lap>";
        var check = FloorChecks.Check(wet, Field(lapsXml: wetLap).Parse(), null, 93);
        Assert.True(check.RanWet);
        Assert.False(check.Reduced);
    }

    [Fact]
    public void The_wrong_AI_strength_is_flagged_when_LMU_proves_it()
    {
        Assert.Equal([FloorFlag.AiStrength], FloorChecks.Check(Dry, Field().Parse(), null, aiStrengthSeen: 85).Flags);
    }

    [Theory]
    [InlineData(98.0, 98.0, true)]    // 4.4% faster in both: the AI was turned down
    [InlineData(98.0, 101.5, false)]  // only fast in qualifying
    public void Without_proof_the_pace_check_flags_only_a_gap_in_both_sessions(double qualifyingBest, double raceBest, bool flagged)
    {
        var qualifying = new ResultsXml { Session = "Qualify" }
            .Car("Me", player: true, classPos: 1, best: qualifyingBest).Car("AI 1", classPos: 2, best: 102.5).Parse();
        var check = FloorChecks.Check(Dry, Field(playerBest: raceBest).Parse(), qualifying, aiStrengthSeen: null);

        Assert.Equal(flagged, check.Flags.Contains(FloorFlag.Pace));
    }

    [Theory]
    [InlineData(300, true)]  // into the wall on the last lap: the wall ended it
    [InlineData(90, false)]  // a tap laps before retiring with something else
    public void Penalties_and_a_barrier_DNF_are_counted_for_reputation(int wallAt, bool barrierDnf)
    {
        var race = new ResultsXml { Fuel = 1, Tires = 1 }
            .Car("Me", player: true, classPos: 2, status: "DNF")
            .Car("AI 1", classPos: 1)
            .Event("<Penalty Driver=\"Me\" ID=\"0\" Penalty=\"Drive Thru\" Reason=\"Track Limits\" et=\"60\">Drive Thru</Penalty>")
            .Event($"<Incident et=\"{wallAt}\">Me(0) reported contact (1948.99) with Immovable</Incident>")
            .Parse();

        var check = FloorChecks.Check(Dry, race, null, 93);

        Assert.Equal(1, check.Penalties);
        Assert.Equal(barrierDnf, check.BarrierDnf);
    }

    [Fact]
    public void A_car_hit_by_another_after_the_wall_is_not_a_barrier_DNF()
    {
        var race = new ResultsXml { Fuel = 1, Tires = 1 }
            .Car("Me", player: true, classPos: 2, status: "DNF")
            .Car("AI 1", classPos: 1)
            .Event("<Incident et=\"300\">Me(0) reported contact (40.0) with Immovable</Incident>")
            .Event("<Incident et=\"302\">Me(0) reported contact (80.0) with another vehicle AI 1(1)</Incident>")
            .Parse();

        Assert.False(FloorChecks.Check(Dry, race, null, 93).BarrierDnf);
    }
}

public class AiProofTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fs-aiproof-" + Guid.NewGuid().ToString("N"));

    public AiProofTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteSave(string name, string json)
    {
        using var packed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(System.Text.Encoding.UTF8.GetBytes(json));
        var file = Path.Combine(_dir, name);
        File.WriteAllText(file, $"{{\"compressedData\":\"{Convert.ToBase64String(packed.ToArray())}\",\"unCompressedDataSize\":1}}sig:abc=");
        return file;
    }

    [Fact]
    public void A_weekend_save_gives_the_players_AI_strength()
    {
        var file = WriteSave("1.json", "{\"entries\":[{\"selectedAIStrength\":-1},{\"selectedAIStrength\":93},{\"selectedAIStrength\":-1}]}");

        Assert.Equal(93, LmuCareer.Core.Content.AiProof.Save(file)?.Strength);
        Assert.Equal(93, LmuCareer.Core.Content.AiProof.NewestSave([_dir], DateTimeOffset.Now.AddMinutes(-5))?.Strength);
        Assert.Null(LmuCareer.Core.Content.AiProof.NewestSave([_dir], DateTimeOffset.Now.AddMinutes(5))); // older than the weekend
    }

    [Fact]
    public void Settings_give_strength_and_aggression()
    {
        var file = Path.Combine(_dir, "Settings.JSON");
        File.WriteAllText(file, "{\"Game Options\":{\"AI Driver Strength\":94,\"AI Aggression Mode\":2,\"AI Mistakes\":0.0}}");

        var seen = LmuCareer.Core.Content.AiProof.Settings(file);
        Assert.Equal((94, 2), (seen?.Strength, seen?.Aggression));
    }

    [Fact]
    public void Settings_only_prove_a_weekend_when_LMU_wrote_them_after_it_started()
    {
        var install = new LmuCareer.Core.Content.LmuInstall(_dir, [], []);
        Directory.CreateDirectory(Path.GetDirectoryName(install.SettingsFile)!);
        File.WriteAllText(install.SettingsFile, "{\"Game Options\":{\"AI Driver Strength\":87,\"AI Aggression Mode\":2}}");
        var round = new Round { Floor = new RoundFloor(94, false, 100) };

        // LMU still open: the slider moved to 94 for the weekend, but the file still has the old 87.
        File.SetLastWriteTimeUtc(install.SettingsFile, DateTime.UtcNow.AddHours(-1));
        round.ArmedAt = DateTimeOffset.Now.AddMinutes(-30);
        Assert.Equal((null, null), FloorChecks.Proof(round, install));

        // LMU closed after the race: the file is the weekend's.
        File.SetLastWriteTimeUtc(install.SettingsFile, DateTime.UtcNow);
        Assert.Equal((87, 2), FloorChecks.Proof(round, install));
    }

    [Fact]
    public void A_broken_file_proves_nothing()
    {
        var file = Path.Combine(_dir, "2.json");
        File.WriteAllText(file, "not a save");
        Assert.Null(LmuCareer.Core.Content.AiProof.Save(file));
    }
}

public class NewGamePlusEndsTests
{
    [Fact]
    public void A_third_flagged_round_turns_the_career_normal()
    {
        var career = new Career
        {
            NewGamePlus = new NewGamePlusState(),
            CurrentSeason = new Season { Number = 1, Car = new CareerCar("Lexus RCF LMGT3", "GT3", "1", "Team") },
        };
        var floor = new RoundFloor(93, false, 75);
        for (var n = 1; n <= 3; n++)
        {
            var round = ProgressionTests.Counted(n, playerRank: 1);
            round.Floor = floor;
            career.CurrentSeason.Rounds.Add(round);
            var race = new ResultsXml { Damage = 0, Fuel = 1, Tires = 1 }.Car("Me", player: true).Car("AI 1", classPos: 2).Parse();
            FloorChecks.AfterRace(career, round, race, null, DateTimeOffset.Now);

            Assert.True(round.Result!.Floor!.Reduced);
            Assert.Equal(n < 3, career.IsNewGamePlus);
        }
    }
}

public class ConsequencesTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);

    /// <summary>An NG+ career two rounds into an eight-round season in the class.</summary>
    private static Career Racing(decimal reputation, string carClass)
    {
        var rounds = Enumerable.Range(1, 8).Select(n => n <= 2 ? ProgressionTests.Counted(n, 3, carClass) : new Round { Number = n }).ToArray();
        var career = ProgressionTests.CareerWith(reputation, carClass, rounds);
        career.NewGamePlus = new NewGamePlusState();
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = 2 };
        return career;
    }

    [Fact]
    public void A_race_costs_reputation_as_it_is_counted()
    {
        var career = Racing(20, "GT3");
        var check = new FloorCheck { Penalties = 2, BarrierDnf = true };

        Consequences.AfterRace(career, career.CurrentSeason.Rounds[1], check, Now);

        Assert.Equal(16, career.Reputation);
        Assert.Equal(4, check.ReputationCost);
        Assert.False(check.LetGo);
    }

    [Fact]
    public void Reputation_never_goes_below_zero_and_GT3_never_fires_mid_season()
    {
        var career = Racing(2, "GT3");
        var check = new FloorCheck { Disqualified = true, Penalties = 3 };

        Consequences.AfterRace(career, career.CurrentSeason.Rounds[1], check, Now);

        Assert.Equal(0, career.Reputation);
        Assert.Equal(2, check.ReputationCost);
        Assert.False(check.LetGo);
        Assert.Null(career.CurrentSeason.Contract!.LetGoAt);
    }

    [Theory]
    [InlineData(17, 1, false)] // 16: still within 10 of LMP3's bar (25)
    [InlineData(16, 2, true)]  // 14: let go
    public void From_LMP3_up_falling_10_below_the_bar_loses_the_seat_mid_season(decimal reputation, int penalties, bool letGo)
    {
        var career = Racing(reputation, "LMP3");
        var check = new FloorCheck { Penalties = penalties };

        Consequences.AfterRace(career, career.CurrentSeason.Rounds[1], check, Now);

        Assert.Equal(letGo, check.LetGo);
        Assert.Equal(letGo, career.CurrentSeason.Contract!.LetGoAt is not null);
        Assert.Equal(letGo, career.CurrentSeason.IsFinished); // the rest of the season is skipped
    }

    [Theory]
    [InlineData("LMP2", 30, "LMP3")]
    [InlineData("LMP2", 10, "GT3")]
    [InlineData("Hyper", 45, "LMP2")]
    public void Let_go_the_calls_come_from_the_class_the_reputation_fits(string carClass, decimal reputation, string expected)
    {
        var career = Racing(reputation, carClass);
        Assert.Equal(expected, Consequences.ClassAfterLetGo(career, carClass));

        career.Path = CareerPath.Prototype; // the prototype road starts at LMP3
        Assert.Equal(expected == "GT3" ? "LMP3" : expected, Consequences.ClassAfterLetGo(career, carClass));
    }

    [Fact]
    public void A_let_go_season_gets_no_re_sign_and_offers_a_class_down()
    {
        var career = Racing(16, "LMP2");
        Consequences.AfterRace(career, career.CurrentSeason.Rounds[1], new FloorCheck { Disqualified = true }, Now);

        Progression.EndSeason(career, Catalog, install: null, seed: 1);

        Assert.DoesNotContain(career.Offers, o => o.Kind == OfferKind.ReSign);
        Assert.NotEmpty(career.Offers);
        Assert.All(career.Offers, o => Assert.NotEqual("LMP2", o.CarClass));
    }

    [Fact]
    public void The_review_lists_what_the_races_cost()
    {
        var career = Racing(20, "GT3");
        var round = career.CurrentSeason.Rounds[1];
        round.Result!.Floor = new FloorCheck { Penalties = 2 };
        Consequences.AfterRace(career, round, round.Result.Floor, Now);
        foreach (var rest in career.CurrentSeason.Rounds.Skip(2)) rest.State = RoundState.Skipped;

        var review = Progression.Review(career.CurrentSeason, career.Reputation);

        Assert.Equal(20, review.ReputationBefore);
        Assert.Contains(review.Items, i => i.Label == Progression.RaceCostsItem && i.Points == -2);
        Assert.Equal(review.ReputationBefore + review.Items.Sum(i => i.Points), review.ReputationAfter);
    }

    [Theory]
    [InlineData(80, 50)]
    [InlineData(60, 25)]
    [InlineData(30, 0)]
    [InlineData(10, 10)] // no stage to lose
    public void Let_go_drops_one_loyalty_stage(decimal points, decimal after) =>
        Assert.Equal(after, Consequences.StageDown(points));
}
