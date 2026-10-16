using System.Globalization;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public sealed class DebriefTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lmucareer-tests-" + Guid.NewGuid().ToString("N"));

    public DebriefTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private static string Lap(int num, double et, double time, int p = 1, double s1 = 30, string extra = "") =>
        string.Create(CultureInfo.InvariantCulture,
            $"<Lap num=\"{num}\" p=\"{p}\" et=\"{et}\" s1=\"{s1}\" s2=\"{time - s1 - 30}\" s3=\"30\" topspeed=\"{250 + num}\" {extra}>{time}</Lap>");

    [Fact]
    public void Reads_the_lap_details_LMU_writes()
    {
        const string laps = """
            <Lap num="1" p="8" et="137.0025" s1="41.9641" s2="26.1698" s3="36.1941" topspeed="283.52" fuel="0.843" fuelUsed="0.030" ve="0.965" veUsed="0.035" twfl="0.965" twfr="0.969" twrl="0.969" twrr="0.968" fcompound="0,Medium" rcompound="1,Soft" FL="0,Medium" FR="0,Medium" RL="1,Soft" RR="1,Soft">104.3279</Lap>
            <Lap num="2" p="7" et="241.3304" s1="34.1233" s2="25.8770" s3="34.8973" topspeed="307.20" fuel="0.820" fuelUsed="0.024" pit="1">94.8976</Lap>
            """;
        var player = new ResultsXml().Car("Sam Sample", player: true, lapsXml: laps).Parse().Player!;

        var first = player.LapRecords[0];
        Assert.Equal(41.9641, first.Sector1);
        Assert.Equal(26.1698, first.Sector2);
        Assert.Equal(36.1941, first.Sector3);
        Assert.Equal(283.52, first.TopSpeedKph);
        Assert.Equal(0.843, first.Fuel);
        Assert.Equal(0.030, first.FuelUsed);
        Assert.Equal(0.965, first.Energy);
        Assert.Equal(0.035, first.EnergyUsed);
        Assert.Equal(new TyreWear(0.965, 0.969, 0.969, 0.968), first.Wear);
        Assert.Equal("Medium", first.FrontCompound);
        Assert.Equal("Soft", first.RearCompound);
        Assert.False(first.Pit);

        // A car without virtual energy or wear readings just has none.
        var second = player.LapRecords[1];
        Assert.True(second.Pit);
        Assert.Null(second.Energy);
        Assert.Null(second.Wear);
    }

    [Fact]
    public void Class_position_comes_from_when_each_class_car_crossed_the_line()
    {
        var session = new ResultsXml()
            .Car("Sam Sample", player: true, lapsXml: Lap(1, 100, 100) + Lap(2, 200, 100))
            .Car("Dan Harper", lapsXml: Lap(1, 100, 95) + Lap(2, 195, 110))
            // A lap down: never ahead on a lap it hasn't finished.
            .Car("Lee Slow", lapsXml: Lap(1, 100, 300))
            // Faster, but in another class.
            .Car("Hyper Fast", carClass: "Hyper", lapsXml: Lap(1, 100, 50) + Lap(2, 150, 50))
            .Parse();

        int?[] Positions(string name) => session.Entries.Single(e => e.Name == name).LapRecords.Select(l => l.ClassPosition).ToArray();
        Assert.Equal([2, 1], Positions("Sam Sample"));
        Assert.Equal([1, 2], Positions("Dan Harper"));
        Assert.Equal([3], Positions("Lee Slow"));
        Assert.Equal([1, 1], Positions("Hyper Fast"));
    }

    [Fact]
    public void Reads_contacts_damage_and_track_limits_from_the_stream()
    {
        var events = new ResultsXml()
            .Car("Sam Sample", player: true)
            .Event("""<Incident et="10.0">Sam Sample(3) reported contact (588.53) with another vehicle Dan Harper(4)</Incident>""")
            .Event("""<Incident et="11.0">Dan Harper(4) reported contact (12.07) with Immovable</Incident>""")
            .Event("""<Sector et="12.0">Sam Sample(3) reports new suspension damage</Sector>""")
            .Event("""<Sector Driver="Dan Harper" ID="4" Sector="2" Class="GT3" et="13.0">Dan Harper(4) set new best for sector 2</Sector>""")
            .Event("""<TrackLimits Driver="Sam Sample" ID="3" Lap="2" WarningPoints="10.25" CurrentPoints="12.5" Resolution="3" et="14.0">Invalid Lap Cut Track</TrackLimits>""")
            .Parse().Events;

        Assert.Equal([RaceEventKind.Incident, RaceEventKind.Incident, RaceEventKind.Damage, RaceEventKind.TrackLimits], events.Select(e => e.Kind));

        var contact = events[0];
        Assert.Equal("Sam Sample", contact.Driver);
        Assert.Equal("Dan Harper", contact.OtherDriver);
        Assert.Null(contact.Object);
        Assert.Equal(588.53, contact.Impact);

        var wall = events[1];
        Assert.Equal("Dan Harper", wall.Driver);
        Assert.Null(wall.OtherDriver);
        Assert.Equal("Immovable", wall.Object);

        Assert.Equal("Sam Sample", events[2].Driver);

        var limits = events[3];
        Assert.Equal(2, limits.Lap);
        Assert.Equal(3, limits.Resolution);
        Assert.Equal(10.25, limits.WarningPoints);
        Assert.Equal(12.5, limits.CurrentPoints);
        Assert.Equal("Invalid Lap Cut Track", limits.Text);
    }

    private static ResultsXml Race() => new ResultsXml { Course = "Sebring International Raceway" }
        .Car("Sam Sample", player: true, number: "69", team: "Sample Racing", laps: 2,
            lapsXml: Lap(1, 100, 99, s1: 29) + Lap(2, 199, 101, s1: 31, extra: "pit=\"1\""))
        .Car("Dan Harper", classPos: 2, laps: 2, lapsXml: Lap(1, 100, 100, s1: 30.5) + Lap(2, 200, 100, s1: 30))
        .Car("Lee Quick", classPos: 3, laps: 2, lapsXml: Lap(1, 100, 102, s1: 29.5) + Lap(2, 202, 103, s1: 31))
        .Car("Hyper Fast", carClass: "Hyper", laps: 2, lapsXml: Lap(1, 100, 80) + Lap(2, 180, 80))
        .Event("""<Incident et="150.0">Dan Harper(4) reported contact (40.2) with another vehicle Sam Sample(3)</Incident>""")
        .Event("""<Incident et="151.0">Lee Quick(5) reported contact (12.07) with Immovable</Incident>""")
        .Event("""<Penalty Driver="Sam Sample" et="160.0">Sam Sample received Drive Thru penalty</Penalty>""");

    [Fact]
    public void Debrief_keeps_the_class_bests_of_the_AI_and_the_players_own_events()
    {
        var debrief = Debriefs.From(Race().Parse());

        var gt3 = debrief.ClassBests.Single(b => b.CarClass == "GT3");
        // ResultsXml gives each car a best lap of 100.5 + its class position.
        Assert.Equal(102.5, gt3.LapSeconds);
        Assert.Equal("Dan Harper", gt3.LapDriver);
        // The player's 29 s first sector doesn't count: these are the AI's bests, from any AI car.
        Assert.Equal(29.5, gt3.Sector1);
        Assert.Equal(252, gt3.TopSpeedKph);
        Assert.Contains(debrief.ClassBests, b => b.CarClass == "Hyper");

        // The player as the other car in a contact counts; someone else's wall doesn't.
        Assert.Equal([RaceEventKind.Incident, RaceEventKind.Penalty], debrief.PlayerEvents.Select(e => e.Kind));
        Assert.False(debrief.Backfilled);
    }

    private static Career CareerWithRace(string raceFile)
    {
        var career = new Career
        {
            Name = "Sebring run",
            CreatedAt = Now,
            LastPlayedAt = Now,
            CurrentSeason = new Season
            {
                Number = 1,
                Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
                Rounds = [new Round { Number = 1, EventName = "Sebring 12 Hours", TrackCourse = "Sebring International Raceway", RaceMinutes = 30, FuelMultiplier = 3.5, TireMultiplier = 3.5 }],
            },
        };
        var round = RoundFlow.Arm(career.CurrentSeason, Now);
        var check = RoundMatcher.Check(Race().Parse(raceFile), Now.AddHours(1), round, career.CurrentSeason.Car);
        RoundFlow.Accept(round, RoundFlow.Evaluate([check]), Now.AddHours(1));
        return career;
    }

    private static IReadOnlyList<LapRecord> PlayerLaps(Career career) =>
        career.CurrentSeason.Rounds[0].Result!.Entries.Single(e => e.Entry.IsPlayer).Entry.LapRecords;

    [Fact]
    public void A_counted_race_stores_its_debrief_and_the_players_full_laps()
    {
        var career = CareerWithRace("race.xml");
        var result = career.CurrentSeason.Rounds[0].Result!;

        Assert.NotNull(result.Debrief);
        Assert.Equal([29.0, 31.0], PlayerLaps(career).Select(l => l.Sector1));
        Assert.Equal([false, true], PlayerLaps(career).Select(l => l.Pit));
        // Other cars still keep no laps.
        Assert.All(result.Entries.Where(e => !e.Entry.IsPlayer), e => Assert.Empty(e.Entry.LapRecords));
    }

    /// <summary>A round as 1.1 saved it: lap times only, no debrief.</summary>
    private static Career CountedBefore12(string raceFile)
    {
        var career = CareerWithRace(raceFile);
        var result = career.CurrentSeason.Rounds[0].Result!;
        result.Debrief = null;
        result.Entries = result.Entries
            .Select(e => e with { Entry = e.Entry with { LapRecords = e.Entry.LapRecords.Select(l => new LapRecord(l.Number, l.Position, l.LapTimeSeconds, l.ElapsedSeconds)).ToList() } })
            .ToList();
        return career;
    }

    [Fact]
    public void Backfill_reads_a_counted_race_from_its_file_once()
    {
        Race().Build().Save(Path.Combine(_root, "race.xml"));
        var career = CountedBefore12("race.xml");

        Assert.True(Debriefs.Backfill(career, _root));
        var result = career.CurrentSeason.Rounds[0].Result!;
        Assert.True(result.Debrief!.Backfilled);
        Assert.Equal([29.0, 31.0], PlayerLaps(career).Select(l => l.Sector1));
        Assert.Equal(2, result.Debrief.PlayerEvents.Count);

        Assert.False(Debriefs.Backfill(career, _root));
    }

    [Fact]
    public void Backfill_leaves_a_round_alone_when_the_file_is_gone_or_is_another_race()
    {
        var gone = CountedBefore12("race.xml");
        Assert.False(Debriefs.Backfill(gone, _root));
        Assert.False(Debriefs.Backfill(gone, null));
        Assert.Null(gone.CurrentSeason.Rounds[0].Result!.Debrief);

        // Same file name, but the player ran a different number of laps in it.
        new ResultsXml { Course = "Sebring International Raceway" }
            .Car("Sam Sample", player: true, number: "69", team: "Sample Racing", laps: 5)
            .Build().Save(Path.Combine(_root, "race.xml"));
        var other = CountedBefore12("race.xml");
        Assert.False(Debriefs.Backfill(other, _root));
        Assert.Null(other.CurrentSeason.Rounds[0].Result!.Debrief);
    }

    [Fact]
    public void Debrief_survives_a_save_and_old_laps_save_without_empty_fields()
    {
        var store = new CareerStore(_root);
        var career = CareerWithRace("race.xml");
        store.Save(career);

        var loaded = store.Load(career.Id).Career;
        var result = loaded.CurrentSeason.Rounds[0].Result!;
        Assert.Equal(29.0, PlayerLaps(loaded)[0].Sector1);
        Assert.Equal(1, PlayerLaps(loaded)[0].ClassPosition);
        Assert.Equal("Sam Sample", result.Debrief!.PlayerEvents[0].OtherDriver);
        Assert.Equal(29.5, result.Debrief.ClassBests.Single(b => b.CarClass == "GT3").Sector1);

        var old = CountedBefore12("old.xml");
        store.Save(old);
        var json = File.ReadAllText(Directory.EnumerateFiles(Path.Combine(_root, "careers"), $"{old.Id:N}.career.json").Single());
        Assert.DoesNotContain("sector1", json);
        Assert.DoesNotContain("\"pit\"", json);
    }
}
