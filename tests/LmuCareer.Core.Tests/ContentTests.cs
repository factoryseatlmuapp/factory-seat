using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Tests;

public class ContentTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly string[] AllPacks = Catalog.Packs.Select(p => p.Id).ToArray();

    [Fact]
    public void Every_event_points_at_a_catalogued_track_and_layout()
    {
        foreach (var e in Catalog.Events)
        {
            var track = Catalog.Track(e.Folder);
            Assert.True(track is not null, $"{e.Id}: no track {e.Folder}");
            Assert.True(track!.Layouts.ContainsKey(e.Layout), $"{e.Id}: no layout {e.Layout} at {e.Folder}");
        }
    }

    [Fact]
    public void Every_track_and_car_names_a_known_pack()
    {
        var packs = Catalog.Packs.Select(p => p.Id).ToHashSet();
        Assert.All(Catalog.Tracks, t => Assert.Contains(t.Pack, packs));
        Assert.All(Catalog.Cars.Where(c => c.Pack is not null), c => Assert.Contains(c.Pack!, packs));
    }

    [Fact]
    public void Owning_everything_gives_the_eight_round_world_season_in_calendar_order()
    {
        var rounds = CalendarBuilder.DefaultSeason(Catalog, "GT3", DriverRating.Silver, AllPacks);

        Assert.Equal(
            ["qatar-1812", "imola-6", "spa-6", "le-mans-24", "interlagos-6", "cota-6", "fuji-6", "bahrain-8"],
            rounds.Select(r => r.EventId));
        Assert.Equal(Enumerable.Range(1, 8), rounds.Select(r => r.Number));
    }

    [Fact]
    public void Base_game_only_fills_the_world_season_with_base_track_rounds()
    {
        var rounds = CalendarBuilder.DefaultSeason(Catalog, "Hyper", DriverRating.Silver, []);

        Assert.Equal(
            ["sebring-1000", "portimao-6", "spa-6", "le-mans-24", "monza-6", "fuji-6", "bahrain-8"],
            rounds.Select(r => r.EventId));
    }

    [Fact]
    public void Lmp2_runs_the_european_season_plus_le_mans_on_elms_layouts()
    {
        var rounds = CalendarBuilder.DefaultSeason(Catalog, "LMP2", DriverRating.Silver, AllPacks);

        Assert.Equal(
            ["barcelona-4", "le-castellet-4", "le-mans-24", "imola-4", "spa-4", "silverstone-4", "portimao-4"],
            rounds.Select(r => r.EventId));
        Assert.Equal("layoutSpaELMS", rounds.Single(r => r.EventId == "spa-4").LayoutFile);
    }

    [Fact]
    public void Lmp3_leaves_out_le_mans()
    {
        var rounds = CalendarBuilder.DefaultSeason(Catalog, "LMP3", DriverRating.Silver, AllPacks);
        Assert.DoesNotContain(rounds, r => r.EventId == "le-mans-24");
    }

    [Theory]
    [InlineData(24, null, 360, 4, 2, true)]
    [InlineData(12, null, 180, 4, 2, true)]
    [InlineData(10, null, 30, 20, 3, false)]
    [InlineData(8, null, 30, 16, 3, false)]
    [InlineData(6, null, 30, 12, 3, false)]
    [InlineData(4, null, 30, 8, 3, false)]
    [InlineData(24, 45, 45, 32, 2, false)]
    [InlineData(6, 60, 60, 6, 1, false)]
    [InlineData(24, 50, 45, 32, 2, false)]
    [InlineData(24, 1440, 1440, 1, 1, true)]
    [InlineData(12, 720, 720, 1, 1, true)]
    [InlineData(12, 600, 600, 1, 1, true)]
    [InlineData(24, 720, 720, 2, 2, true)]
    [InlineData(12, 360, 360, 2, 2, true)]
    public void Formats_use_only_values_lmus_menus_offer(
        double hours, int? minutes, int length, int timeScale, int fuel, bool fullPlan)
    {
        var format = RaceFormats.For(hours, minutes);

        Assert.Equal((length, timeScale, fuel, fuel, fullPlan),
            (format.RaceMinutes, format.TimeScale, format.FuelMultiplier, format.TireMultiplier, format.FullStintPlan));
        Assert.Contains(format.RaceMinutes, RaceFormats.RaceLengths);
    }

    [Fact]
    public void A_24_hour_race_in_6_hours_has_about_fifteen_stints_of_25_minutes()
    {
        var format = RaceFormats.For(24);
        Assert.Equal(25, format.TankMinutes);
        Assert.Equal(15, format.Stints);
    }

    [Fact]
    public void Time_scale_is_capped_at_x60()
    {
        Assert.Equal(60, RaceFormats.For(24, 5).TimeScale);
    }

    [Theory]
    [InlineData("Hyper", DriverRating.Silver, 6, 0.125)]
    [InlineData("GT3", DriverRating.Silver, 6, 1.75 / 6)]
    [InlineData("GT3", DriverRating.Bronze, 24, 0.25)]
    [InlineData("LMP2", DriverRating.Gold, 4, 0.125)]
    public void Minimum_drive_share_follows_the_wec_rules(string carClass, DriverRating rating, double hours, double expected)
    {
        Assert.Equal(expected, RaceFormats.MinimumDriveShare(carClass, rating, hours), 6);
    }

    [Fact]
    public void Changing_a_rounds_length_keeps_its_name_and_weight()
    {
        var round = CalendarBuilder.RoundFor(Catalog, Catalog.Event("le-mans-24")!, "GT3", DriverRating.Silver);
        CalendarBuilder.ApplyLength(round, 45);

        Assert.Equal(("Le Mans 24 Hours", 2m, 45, 32), (round.EventName, round.PointsWeight, round.RaceMinutes, (int)round.TimeScale));
    }
}
