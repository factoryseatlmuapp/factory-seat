using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public class RivalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A counted round with the player P5 and, optionally, contacts with AI 9 at the given impacts.</summary>
    private static Round Raced(int number, int rank = 5, params double[] contactsWithAi9)
    {
        var round = ProgressionTests.Counted(number, rank);
        round.Result!.Debrief = new RaceDebrief
        {
            PlayerEvents = contactsWithAi9
                .Select((impact, i) => new RaceEvent(RaceEventKind.Incident, 100 + 60 * i, "Me", "contact") { OtherDriver = "AI 9", Impact = impact })
                .ToList(),
        };
        return round;
    }

    [Fact]
    public void Finishing_side_by_side_four_times_makes_a_rival()
    {
        var rounds = Enumerable.Range(1, 4).Select(n => Raced(n)).ToArray();
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);

        var rival = Rivals.AfterRace(career, rounds[3], Now);

        Assert.NotNull(rival);
        Assert.Equal(RivalryKind.Battle, rival!.Kind);
        Assert.Contains(rival.Name, new[] { "AI 4", "AI 6" });
        Assert.Contains(career.Inbox, m => m.Kind == MessageKind.Rivalry);
    }

    [Fact]
    public void Three_finishes_side_by_side_isnt_a_rivalry_yet()
    {
        var rounds = Enumerable.Range(1, 3).Select(n => Raced(n)).ToArray();
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);
        Assert.Null(Rivals.AfterRace(career, rounds[2], Now));
        Assert.Empty(career.Rivals);
    }

    [Fact]
    public void Three_real_contacts_with_one_driver_make_a_rival_first()
    {
        // AI 9 isn't next to the player in the results, but they keep hitting each other.
        var rounds = new[] { Raced(1, 5, 900), Raced(2, 5, 700, 2500), Raced(3, 5, 150) };
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);

        var rival = Rivals.AfterRace(career, rounds[2], Now);

        Assert.Equal("AI 9", rival!.Name);
        Assert.Equal(RivalryKind.Contact, rival.Kind);
        Assert.Equal(1, rival.Since);
    }

    [Fact]
    public void Brushes_in_traffic_dont_count()
    {
        var rounds = new[] { Raced(1, 5, 200), Raced(2, 5, 300, 100), Raced(3, 5, 450) };
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);
        Assert.Null(Rivals.AfterRace(career, rounds[2], Now));
    }

    [Fact]
    public void A_rivalry_is_only_born_once()
    {
        var rounds = Enumerable.Range(1, 6).Select(n => Raced(n, 5, 900)).ToArray();
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);
        foreach (var round in rounds) Rivals.AfterRace(career, round, Now);
        Assert.Equal(career.Rivals.Count, career.Rivals.Select(r => r.Name).Distinct().Count());
        Assert.Single(career.Rivals, r => r.Name == "AI 9");
    }

    [Fact]
    public void Guest_drives_make_no_rivals()
    {
        var rounds = Enumerable.Range(1, 4).Select(n => Raced(n)).ToArray();
        foreach (var r in rounds) r.Guest = true;
        var career = ProgressionTests.CareerWith(10, "GT3", rounds);
        Assert.Null(Rivals.AfterRace(career, rounds[3], Now));
    }
}
