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

    [Fact]
    public void Describes_the_next_race_in_lmus_own_values()
    {
        var career = Career();
        RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now);

        var json = JsonDocument.Parse(BriefingFile.For(career, ContentCatalog.Default, "1.1.0", DateTimeOffset.Now).ToJson()).RootElement;

        Assert.Equal("factory-seat-briefing", json.GetProperty("format").GetString());
        Assert.Equal(1, json.GetProperty("version").GetInt32());
        var round = json.GetProperty("round");
        Assert.Equal("Armed", round.GetProperty("state").GetString());
        Assert.StartsWith("Daytona", round.GetProperty("track").GetProperty("folder").GetString());
        Assert.StartsWith("layout", round.GetProperty("track").GetProperty("layoutFile").GetString());
        Assert.Equal(["Lexus RCF LMGT3"], round.GetProperty("car").GetProperty("carTypes").EnumerateArray().Select(t => t.GetString()));
        Assert.True(round.GetProperty("car").GetProperty("customTeam").GetBoolean());
        var settings = round.GetProperty("settings");
        Assert.Equal(360, settings.GetProperty("raceMinutes").GetInt32());
        Assert.Equal(4, settings.GetProperty("timeScale").GetInt32());
        Assert.Equal(2, settings.GetProperty("fuelUsage").GetInt32());
        var sponsor = json.GetProperty("sponsors")[0];
        Assert.Equal("a podium", sponsor.GetProperty("objective").GetString());
        Assert.Equal("InProgress", sponsor.GetProperty("status").GetString());
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

        BriefingFile.RemoveFor(_root, Guid.NewGuid());
        Assert.True(File.Exists(path));
        BriefingFile.RemoveFor(_root, career.Id);
        Assert.False(File.Exists(path));
    }
}
