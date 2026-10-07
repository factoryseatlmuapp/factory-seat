using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public class ResultsParserTests
{
    [Fact]
    public void Reads_session_settings_and_entries()
    {
        var session = new ResultsXml()
            .Car("Sam Sample", player: true, team: "Sample Racing", number: "69")
            .Car("Dan Harper", classPos: 2)
            .Parse();

        Assert.Equal(SessionKind.Race, session.Kind);
        Assert.True(session.IsSinglePlayer);
        Assert.Equal("Circuit de la Sarthe", session.TrackCourse);
        Assert.Equal(30, session.RaceMinutes);
        Assert.Equal(3.5, session.FuelMultiplier);
        Assert.Equal(2, session.Entries.Count);

        var player = Assert.IsType<EntryResult>(session.Player);
        Assert.Equal("Sample Racing", player.TeamName);
        Assert.Equal("69", player.CarNumber);
        Assert.Equal(100.5 + 1, player.BestLapSeconds);
        Assert.Equal(FinishStatus.Finished, player.Status);
    }

    [Theory]
    [InlineData("Qualify", SessionKind.Qualifying)]
    [InlineData("Practice1", SessionKind.Practice)]
    [InlineData("Warmup", SessionKind.Warmup)]
    [InlineData("Race", SessionKind.Race)]
    public void Session_kind_comes_from_the_session_element(string element, SessionKind expected)
    {
        var session = new ResultsXml { Session = element }.Car("A").Parse();
        Assert.Equal(expected, session.Kind);
    }

    [Fact]
    public void Race_quit_before_the_flag_is_incomplete()
    {
        var quit = new ResultsXml()
            .Car("Sam Sample", player: true, status: "DNF", classPos: 2)
            .Car("Dan Harper", status: "None")
            .Parse();
        var finished = new ResultsXml()
            .Car("Sam Sample", player: true)
            .Car("Dan Harper", classPos: 2, status: "DNF")
            .Parse();

        Assert.False(quit.IsComplete);
        Assert.True(finished.IsComplete);
    }

    [Fact]
    public void Handover_splits_control_into_segments()
    {
        var session = new ResultsXml()
            .Car("Sam Sample", player: true, laps: 30, control:
            [
                (1, 15, "PlayerControl,TC=2,ABS=2"),
                (16, 30, "AIControl"),
            ])
            .Parse();

        var player = session.Player!;
        Assert.Equal(15, player.LapsUnder(Controller.Player));
        Assert.Equal(15, player.LapsUnder(Controller.AI));
    }

    [Fact]
    public void Invalid_lap_times_are_null()
    {
        var laps = new ResultsXml().Car("A").Parse().Entries[0].LapRecords;
        Assert.Null(laps[0].LapTimeSeconds);
        Assert.Equal(100.5, laps[1].LapTimeSeconds);
    }

    [Fact]
    public void Events_are_attributed_and_served_penalties_are_skipped()
    {
        var session = new ResultsXml()
            .Car("Sam Sample", player: true)
            .Event("""<Incident et="134.1">Sam Sample(0) reported contact (588.53) with another vehicle Dan Harper(13)</Incident>""")
            .Event("""<Penalty Driver="Sam Sample" ID="0" Penalty="Drive Thru" et="3273.5">Sam Sample received Drive Thru penalty</Penalty>""")
            .Event("""<Penalty et="3418.6">Sam Sample served 1st Drive Thru penalty</Penalty>""")
            .Event("""<TrackLimits Driver="Sam Sample" ID="0" Lap="3" et="542.1">Warning</TrackLimits>""")
            .Event("""<Score et="155.7">Sam Sample(0) lap=0 point=1</Score>""")
            .Parse();

        Assert.Collection(session.Events,
            e => Assert.Equal((RaceEventKind.Incident, "Sam Sample"), (e.Kind, e.Driver)),
            e => Assert.Equal((RaceEventKind.Penalty, "Sam Sample"), (e.Kind, e.Driver)),
            e => Assert.Equal((RaceEventKind.TrackLimits, "Sam Sample"), (e.Kind, e.Driver)));
    }

    [Fact]
    public void Loads_a_file_with_lmus_inline_doctype()
    {
        var path = Path.GetTempFileName();
        try
        {
            var xml = new ResultsXml().Car("A").Build().ToString();
            File.WriteAllText(path, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE rF [
                <!ENTITY rFEnt "rFactor Entity">
                ]>
                {xml}
                """);

            Assert.Single(ResultsParser.Load(path).Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
