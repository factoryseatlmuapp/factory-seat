using System.Text.Json;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public sealed class BriefingFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lmucareer-briefing-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Career Career()
    {
        var catalog = ContentCatalog.Default;
        var daytona = catalog.Events.First(e => e.Folder.StartsWith("Daytona", StringComparison.OrdinalIgnoreCase));
        var round = CalendarBuilder.RoundFor(catalog, daytona, "GT3", DriverRating.Silver, minutes: 360);
        round.Number = 1;
        return new Career
        {
            Name = "Road to Hypercar",
            CurrentSeason = new Season
            {
                Number = 2,
                Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing") { CustomTeam = true },
                Contract = new Contract { TeamName = "Sample Racing", TargetPosition = 3 },
                Rounds = [round],
                Sponsors = [new SponsorDeal { SponsorId = "motul", Name = "Motul", Objective = ObjectiveKind.Podiums, Count = 1, Reward = 3 }],
            },
        };
    }

    /// <summary>Every field version 1 had, at its path: 1.2 only adds alongside them (docs/briefing-json.md).</summary>
    private static readonly string[] VersionOneFields =
    [
        "format", "version", "app", "writtenAt", "open", "career", "round", "sponsors",
        "career.id", "career.name", "career.season", "career.carClass", "career.reputation", "career.team", "career.targetPosition",
        "round.number", "round.of", "round.state", "round.armedAt", "round.guest", "round.event", "round.track", "round.car", "round.settings", "round.pitPlan",
        "round.event.id", "round.event.name", "round.event.realHours", "round.event.pointsWeight",
        "round.track.folder", "round.track.name", "round.track.layoutFile", "round.track.layoutName", "round.track.trackCourse",
        "round.car.class", "round.car.folder", "round.car.carTypes", "round.car.name", "round.car.number", "round.car.customTeam", "round.car.team", "round.car.liveryNumbers",
        "round.settings.raceMinutes", "round.settings.fuelUsage", "round.settings.tyreWear", "round.settings.timeScale", "round.settings.startTime",
        "round.pitPlan.stints", "round.pitPlan.stintMinutes", "round.pitPlan.stops",
        "sponsors.0.name", "sponsors.0.kind", "sponsors.0.objective", "sponsors.0.status", "sponsors.0.done", "sponsors.0.needed", "sponsors.0.reward",
    ];

    private static bool Has(JsonElement root, string path)
    {
        var at = root;
        foreach (var part in path.Split('.'))
        {
            if (int.TryParse(part, out var index)) { if (at.ValueKind != JsonValueKind.Array || at.GetArrayLength() <= index) return false; at = at[index]; }
            else if (at.ValueKind != JsonValueKind.Object || !at.TryGetProperty(part, out at)) return false;
        }
        return true;
    }

    [Fact]
    public void Version_1_fields_stay_and_1_2_adds_stakes_history_practice_and_pace()
    {
        var career = Career();
        var round = career.CurrentSeason.Rounds[0];
        RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now);
        round.Practice = new PracticeRead(1, 12, 108.2, 107.9, "AI 1", 107.9);
        career.CurrentSeason.Sponsors[0].Local = true;

        var json = JsonDocument.Parse(BriefingFile.For(career, ContentCatalog.Default, "1.2.0", DateTimeOffset.Now).ToJson()).RootElement;

        Assert.All(VersionOneFields, path => Assert.True(Has(json, path), path));
        Assert.Equal(1, json.GetProperty("version").GetInt32());

        var next = json.GetProperty("round");
        Assert.Equal("No points on the board yet: this is where your championship starts.", next.GetProperty("stakes").GetProperty("lines")[0].GetString());
        Assert.Equal(JsonValueKind.Array, next.GetProperty("history").ValueKind);
        var practice = next.GetProperty("practice");
        Assert.Equal(108.2, practice.GetProperty("bestLap").GetDouble());
        Assert.Equal(107.9, practice.GetProperty("targetLap").GetDouble());
        Assert.Equal("Matched", practice.GetProperty("aiStrength").GetString());
        Assert.Equal(0, practice.GetProperty("aiStrengthStep").GetInt32());
        Assert.Equal("Practice", next.GetProperty("pace").GetProperty("source").GetString());
        Assert.Equal(0, next.GetProperty("pace").GetProperty("step").GetInt32());
        Assert.True(json.GetProperty("sponsors")[0].GetProperty("local").GetBoolean());
        Assert.Equal(0, json.GetProperty("sponsors")[0].GetProperty("signedAfterRound").GetInt32());
    }

    [Fact]
    public void Describes_the_next_race_in_lmus_own_values()
    {
        var career = Career();
        RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now);

        var json = JsonDocument.Parse(BriefingFile.For(career, ContentCatalog.Default, "1.1.0", DateTimeOffset.Now).ToJson()).RootElement;

        Assert.Equal("factory-seat-briefing", json.GetProperty("format").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt32());
        Assert.True(json.GetProperty("open").GetBoolean());
        var round = json.GetProperty("round");
        Assert.Equal("Armed", round.GetProperty("state").GetString());
        Assert.StartsWith("Daytona", round.GetProperty("track").GetProperty("folder").GetString());
        Assert.StartsWith("layout", round.GetProperty("track").GetProperty("layoutFile").GetString());
        Assert.Equal(["Lexus RCF LMGT3"], round.GetProperty("car").GetProperty("carTypes").EnumerateArray().Select(t => t.GetString()));
        Assert.True(round.GetProperty("car").GetProperty("customTeam").GetBoolean());
        // A save from before 1.1 has no folder on its car: the catalog fills it in from the car type.
        Assert.Equal(ContentCatalog.Default.CarByType("Lexus RCF LMGT3")!.Folder, round.GetProperty("car").GetProperty("folder").GetString());
        var settings = round.GetProperty("settings");
        Assert.Equal(360, settings.GetProperty("raceMinutes").GetInt32());
        Assert.Equal(4, settings.GetProperty("timeScale").GetInt32());
        // GT3 at Daytona: about 59 minutes a tank at Real with the margin, so x3 for stints near 20 minutes.
        Assert.Equal(3, settings.GetProperty("fuelUsage").GetInt32());
        var sponsor = json.GetProperty("sponsors")[0];
        Assert.Equal("a podium", sponsor.GetProperty("objective").GetString());
        Assert.Equal("InProgress", sponsor.GetProperty("status").GetString());
    }

    [Fact]
    public void A_new_careers_car_keeps_its_folder_before_any_race_names_it()
    {
        var catalog = ContentCatalog.Default;
        var bmw = catalog.Cars.First(c => c.Folder.StartsWith("BMW_M4", StringComparison.OrdinalIgnoreCase));
        var career = Career();
        career.CurrentSeason.Car = new CareerCar("", "GT3", "", "") { Folder = bmw.Folder };

        var car = JsonDocument.Parse(BriefingFile.For(career, catalog, null, DateTimeOffset.Now).ToJson()).RootElement.GetProperty("round").GetProperty("car");

        Assert.Equal(bmw.Folder, car.GetProperty("folder").GetString());
        Assert.Empty(car.GetProperty("carTypes").EnumerateArray());
    }

    [Fact]
    public void Has_no_round_between_seasons_and_goes_with_its_career()
    {
        var career = Career();
        career.CurrentSeason.Rounds[0].State = RoundState.Skipped;
        BriefingFile.For(career, ContentCatalog.Default, null, DateTimeOffset.Now).Write(_root);

        var path = Path.Combine(_root, BriefingFile.FileName);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("round").ValueKind);
        Assert.False(File.Exists(path + ".tmp"));

        // Leaving the career keeps the file but says it's no longer open.
        BriefingFile.MarkClosed(_root);
        Assert.False(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("open").GetBoolean());
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("round").ValueKind);

        BriefingFile.RemoveFor(_root, Guid.NewGuid());
        Assert.True(File.Exists(path));
        BriefingFile.RemoveFor(_root, career.Id);
        Assert.False(File.Exists(path));
    }
}
