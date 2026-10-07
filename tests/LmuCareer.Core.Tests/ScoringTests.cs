using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Tests;

public class ScoringTests
{
    private static ScoredEntry Of(ScoredRace race, string name) => race.Entries.Single(e => e.Entry.Name == name);

    [Fact]
    public void Each_class_scores_its_own_top_ten()
    {
        var race = new ResultsXml()
            .Car("Hyper winner", carClass: "Hyper")
            .Car("Hyper second", carClass: "Hyper", classPos: 2)
            .Car("GT3 winner", carClass: "GT3")
            .Parse();

        var scored = RaceScorer.Score(race);

        Assert.Equal(25, Of(scored, "Hyper winner").TotalPoints);
        Assert.Equal(18, Of(scored, "Hyper second").TotalPoints);
        Assert.Equal(25, Of(scored, "GT3 winner").TotalPoints);
    }

    [Fact]
    public void Nothing_beyond_tenth()
    {
        var xml = new ResultsXml();
        for (var p = 1; p <= 11; p++) xml.Car($"Car {p}", classPos: p);

        var scored = RaceScorer.Score(xml.Parse());

        Assert.Equal(1, Of(scored, "Car 10").TotalPoints);
        Assert.Equal(0, Of(scored, "Car 11").TotalPoints);
        Assert.Equal(11, Of(scored, "Car 11").ClassRank);
    }

    [Fact]
    public void Weight_scales_race_points_but_not_the_pole_bonus()
    {
        var race = new ResultsXml().Car("Pole and win").Parse();
        var quali = new ResultsXml { Session = "Qualify" }.Car("Pole and win").Parse();

        var scored = Of(RaceScorer.Score(race, quali, weight: 2), "Pole and win");

        Assert.True(scored.ClassPole);
        Assert.Equal(50, scored.RacePoints);
        Assert.Equal(1, scored.PolePoints);
        Assert.Equal(51, scored.TotalPoints);
    }

    [Fact]
    public void Disqualified_cars_score_nothing_and_move_everyone_up()
    {
        var race = new ResultsXml()
            .Car("Disqualified", status: "DQ")
            .Car("Inherits the win", classPos: 2)
            .Parse();
        var quali = new ResultsXml { Session = "Qualify" }.Car("Disqualified").Parse();

        var scored = RaceScorer.Score(race, quali);

        Assert.Equal(0, Of(scored, "Disqualified").TotalPoints);
        Assert.Null(Of(scored, "Disqualified").ClassRank);
        Assert.Equal(1, Of(scored, "Inherits the win").ClassRank);
        Assert.Equal(25, Of(scored, "Inherits the win").TotalPoints);
    }

    [Fact]
    public void Finishers_under_seventy_percent_of_the_winners_laps_are_not_classified()
    {
        var race = new ResultsXml()
            .Car("Winner", laps: 10)
            .Car("Seven laps", classPos: 2, laps: 7)
            .Car("Six laps", classPos: 3, laps: 6)
            .Car("Retired", classPos: 4, laps: 9, status: "DNF")
            .Parse();

        var scored = RaceScorer.Score(race);

        Assert.True(Of(scored, "Seven laps").Classified);
        Assert.False(Of(scored, "Six laps").Classified);
        Assert.False(Of(scored, "Retired").Classified);
    }

    [Fact]
    public void Player_below_minimum_drive_share_loses_driver_points_but_the_car_keeps_them()
    {
        var race = new ResultsXml()
            .Car("Sam Sample", player: true, laps: 20, control:
            [
                (1, 4, "PlayerControl"),
                (5, 20, "AIControl"),
            ])
            .Parse();

        var scored = Of(RaceScorer.Score(race, playerMinimumDriveShare: 0.25), "Sam Sample");

        Assert.Equal(0.2, scored.DriveShare, 3);
        Assert.False(scored.DriverEligible);
        Assert.Equal(25, scored.TotalPoints);
        Assert.Equal(0, scored.DriverPoints);
    }

    [Fact]
    public void Half_and_half_clears_the_minimum()
    {
        var race = new ResultsXml()
            .Car("Sam Sample", player: true, laps: 20, control:
            [
                (1, 10, "PlayerControl"),
                (11, 20, "AIControl"),
            ])
            .Parse();

        var scored = Of(RaceScorer.Score(race, playerMinimumDriveShare: 0.29), "Sam Sample");

        Assert.True(scored.DriverEligible);
        Assert.Equal(25, scored.DriverPoints);
    }

    [Fact]
    public void Standings_merge_accented_and_plain_spellings_of_one_driver()
    {
        var round1 = RaceScorer.Score(new ResultsXml()
            .Car("François Hériau", team: "Vista AF Corsa")
            .Car("Dan Harper", classPos: 2)
            .Parse());
        var round2 = RaceScorer.Score(new ResultsXml()
            .Car("Dan Harper")
            .Car("Francois Heriau", classPos: 2, team: "Vista AF Corse")
            .Parse());

        var table = Standings.Build([round1, round2]).Single();

        Assert.Equal(2, table.Drivers.Count);

        // One driver, shown under the most recent spelling and team.
        var heriau = table.Drivers.Single(d => d.Name.StartsWith("Fran"));
        Assert.Equal("Francois Heriau", heriau.Name);
        Assert.Equal("Vista AF Corse", heriau.TeamName);
        Assert.Equal([1, 2], heriau.RoundRanks);
        Assert.Equal(43, heriau.Points);
    }

    [Fact]
    public void Countback_puts_more_wins_ahead_on_equal_points()
    {
        // A: P1 then P10 = 25 + 1. B: P2 then P6 = 18 + 8. Level on 26; A has the win.
        var round1 = RaceScorer.Score(new ResultsXml()
            .Car("A")
            .Car("B", classPos: 2)
            .Parse());
        var xml = new ResultsXml();
        for (var p = 1; p <= 10; p++) xml.Car(p switch { 6 => "B", 10 => "A", _ => $"Filler {p}" }, classPos: p);
        var round2 = RaceScorer.Score(xml.Parse());

        var drivers = Standings.Build([round1, round2]).Single().Drivers;
        var a = drivers.Single(d => d.Name == "A");
        var b = drivers.Single(d => d.Name == "B");

        Assert.Equal(a.Points, b.Points);
        Assert.True(drivers.ToList().IndexOf(a) < drivers.ToList().IndexOf(b));
    }

    [Fact]
    public void Qualifying_match_takes_the_latest_session_at_the_same_course_within_the_weekend()
    {
        var race = new ResultsXml { DateTime = 1_790_010_000 }.Car("A").Parse();
        var earlier = new ResultsXml { Session = "Qualify", DateTime = 1_790_009_000 }.Car("A").Parse();
        var latest = new ResultsXml { Session = "Qualify", DateTime = 1_790_009_500 }.Car("A").Parse();
        var elsewhere = new ResultsXml { Session = "Qualify", DateTime = 1_790_009_900, Course = "Spa" }.Car("A").Parse();
        var lastWeek = new ResultsXml { Session = "Qualify", DateTime = 1_789_400_000 }.Car("A").Parse();
        var afterRace = new ResultsXml { Session = "Qualify", DateTime = 1_790_010_100 }.Car("A").Parse();

        Assert.Same(latest, QualifyingMatcher.Find(race, [earlier, latest, elsewhere, lastWeek, afterRace]));
        Assert.Null(QualifyingMatcher.Find(race, [elsewhere, lastWeek, afterRace]));
    }
}
