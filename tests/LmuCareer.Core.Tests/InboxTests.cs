using System.Globalization;
using System.Text;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public sealed class InboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lmucareer-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Laps back to back from the green flag; a pit lap is given as its number.</summary>
    private static string Laps(double[] times, int[]? pits = null, double[]? energy = null, double s1 = 30)
    {
        var xml = new StringBuilder();
        var et = 100.0;
        for (var i = 0; i < times.Length; i++)
        {
            var n = i + 1;
            var pit = pits?.Contains(n) == true ? " pit=\"1\"" : "";
            var ve = energy is null ? "" : string.Create(CultureInfo.InvariantCulture, $" ve=\"{energy[i]}\"");
            xml.Append(string.Create(CultureInfo.InvariantCulture,
                $"<Lap num=\"{n}\" p=\"1\" et=\"{et}\" s1=\"{s1}\" s2=\"30\" s3=\"{times[i] - s1 - 30}\" topspeed=\"250\" fcompound=\"0,Medium\"{ve}{pit}>{times[i]}</Lap>"));
            et += times[i];
        }
        return xml.ToString();
    }

    private static Career CareerWith(ResultsXml race, int target = 6, int rounds = 3, int count = 1)
    {
        var career = new Career
        {
            Name = "Inbox run",
            CreatedAt = Now,
            LastPlayedAt = Now,
            CurrentSeason = new Season
            {
                Number = 1,
                Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
                Contract = new Contract { TeamName = "Sample Racing", TargetPosition = target },
                Rounds = Enumerable.Range(1, rounds).Select(n => new Round
                {
                    Number = n, EventName = $"Round {n} 6 Hours", TrackCourse = "Circuit de la Sarthe",
                    RaceMinutes = 30, FuelMultiplier = 3.5, TireMultiplier = 3.5,
                }).ToList(),
            },
        };
        for (var i = 0; i < count; i++)
        {
            var round = RoundFlow.Arm(career.CurrentSeason, Now.AddDays(i));
            var check = RoundMatcher.Check(race.Parse($"race{i}.xml"), Now.AddDays(i).AddHours(1), round, career.CurrentSeason.Car);
            CareerActions.AcceptArmedRound(new CareerStore(Path.Combine(Path.GetTempPath(), "lmucareer-inbox-" + Guid.NewGuid().ToString("N"))),
                career, RoundFlow.Evaluate([check]), takeDnf: false, acceptDifferences: false, Now.AddDays(i).AddHours(1));
        }
        return career;
    }

    private static ResultsXml Field(int playerPos = 1, string? playerLaps = null) => new ResultsXml()
        .Car("Sam Sample", player: true, number: "69", team: "Sample Racing", classPos: playerPos, laps: 14,
            lapsXml: playerLaps ?? Laps(Enumerable.Repeat(100.0, 14).ToArray()))
        .Car("Dan Harper", classPos: playerPos == 1 ? 2 : 1, laps: 14);

    [Fact]
    public void Stints_split_at_pit_laps_and_only_clean_laps_count()
    {
        // Lap 1 is the start, lap 7 the pit lap, lap 8 the out-lap, lap 11 a spin: none of those count.
        double[] times = [110, 100, 100, 100, 100, 100, 130, 104, 100, 100, 125, 100, 100, 100];
        var career = CareerWith(Field(playerLaps: Laps(times, pits: [7])));
        var report = DebriefReport.For(career.CurrentSeason.Rounds[0])!;

        Assert.Equal(2, report.Stints.Count);
        Assert.Equal((1, 7), (report.Stints[0].FirstLap, report.Stints[0].LastLap));
        Assert.Equal((8, 14), (report.Stints[1].FirstLap, report.Stints[1].LastLap));
        Assert.Equal(5, report.Stints[0].CleanLaps);
        Assert.Equal(5, report.Stints[1].CleanLaps);
        Assert.Equal(100, report.Stints[1].Average);
        Assert.Equal("Medium", report.Stints[0].Compound);
        Assert.Equal(1, report.Lap1Position);
    }

    [Fact]
    public void Engineer_blames_the_tyres_only_when_the_tank_was_not_empty()
    {
        double[] fading = [110, 100, 100, 100, 100, 101.5, 101.5, 101.5];
        var tyres = DebriefReport.For(CareerWith(Field(playerLaps: Laps(fading))).CurrentSeason.Rounds[0])!;
        Assert.Contains(Inbox.EngineerNotes(tyres), n => n.Text.Contains("tyre wear"));

        double[] energy = [0.9, 0.8, 0.7, 0.6, 0.4, 0.2, 0.1, 0.03];
        var empty = DebriefReport.For(CareerWith(Field(playerLaps: Laps(fading, energy: energy))).CurrentSeason.Rounds[0])!;
        var notes = Inbox.EngineerNotes(empty);
        Assert.Contains(notes, n => n.Text.Contains("virtual energy ran low"));
        Assert.DoesNotContain(notes, n => n.Text.Contains("tyre wear"));
    }

    [Fact]
    public void Engineer_counts_contacts_from_a_real_hit_and_calls_a_clean_race_clean()
    {
        var bumpy = Field()
            .Event("""<Incident et="150.0">Sam Sample(1) reported contact (40.0) with another vehicle Dan Harper(2)</Incident>""")
            .Event("""<Incident et="300.0">Dan Harper(2) reported contact (900.0) with another vehicle Sam Sample(1)</Incident>""")
            .Event("""<Incident et="301.0">Sam Sample(1) reported contact (850.0) with another vehicle Dan Harper(2)</Incident>""")
            .Event("""<Incident et="600.0">Sam Sample(1) reported contact (2500.0) with another vehicle Dan Harper(2)</Incident>""");
        var notes = Inbox.EngineerNotes(DebriefReport.For(CareerWith(bumpy).CurrentSeason.Rounds[0])!).Select(n => n.ToString()).ToList();
        // The brush at 40 doesn't count, and both cars' reports at 300 s are one contact.
        Assert.Contains("2 contacts with other cars, 1 of them hard.", notes);

        var clean = Inbox.EngineerNotes(DebriefReport.For(CareerWith(Field()).CurrentSeason.Rounds[0])!).Select(n => n.ToString());
        Assert.Contains("A clean race: no penalties, no contacts, no track limits strikes.", clean);
    }

    [Fact]
    public void A_counted_race_brings_its_mail_with_the_interview_then_the_debrief_on_top()
    {
        // A first win: the press and an interview join the engineer, the team principal and the manager.
        var career = CareerWith(Field());

        Assert.Equal([MessageKind.Reputation, MessageKind.TeamPrincipal, MessageKind.Press, MessageKind.Debrief, MessageKind.Interview],
            career.Inbox.Select(m => m.Kind));
        Assert.Equal(5, Inbox.Unread(career));
        Assert.All(career.Inbox, m => Assert.Equal((1, 1), (m.Season, m.Round)));
        Assert.Equal("Debrief: Round 1 6 Hours", career.Inbox[^2].Subject.ToString());

        // Writing a round's mail again replaces it rather than doubling up.
        Inbox.AfterRace(career, career.CurrentSeason.Rounds[0], Now);
        Assert.Equal(5, career.Inbox.Count);
    }

    [Fact]
    public void Team_principal_measures_the_result_against_the_target()
    {
        var won = CareerWith(Field()).Inbox.Single(m => m.Kind == MessageKind.TeamPrincipal).Body.Select(b => b.ToString()).ToList();
        Assert.Equal("A win. That's exactly what we signed you for.", won[0]);
        Assert.Equal("You're P1 in the championship. Our target is top 6: on course.", won[1]);
        Assert.Equal("2 rounds to go.", won[2]);

        // Two cars in the class: P2 against a target of 1 isn't enough.
        var short_ = CareerWith(Field(playerPos: 2), target: 1).Inbox.Single(m => m.Kind == MessageKind.TeamPrincipal).Body.Select(b => b.ToString()).ToList();
        Assert.Equal("A podium. That's what we want to see.", short_[0]);
        Assert.Equal("You're P2 in the championship. Our target is top 1: 1 place to find.", short_[1]);

        var last = CareerWith(Field(), rounds: 1).Inbox.Single(m => m.Kind == MessageKind.TeamPrincipal).Body.Select(b => b.ToString());
        Assert.Contains("That's the season. We'll talk at the review.", last);
    }

    [Fact]
    public void Reputation_so_far_lists_what_earned_it()
    {
        var message = CareerWith(Field()).Inbox.Single(m => m.Kind == MessageKind.Reputation);
        var review = Progression.Review(CareerWith(Field()).CurrentSeason, 0);

        Assert.Equal($"Reputation this season so far: +{review.ReputationAfter - review.ReputationBefore:0.#}", message.Subject.ToString().Replace(',', '.'));
        Assert.StartsWith("Paid at the season review.", message.Body[0].ToString());
        Assert.Contains(message.Body, b => b.ToString().StartsWith("1 class win: +"));
    }

    [Fact]
    public void A_career_from_before_the_inbox_opens_with_mail_once()
    {
        var career = CareerWith(Field(), count: 2);
        career.Inbox.Clear();
        career.InboxStarted = false;

        Assert.True(Inbox.Start(career, Now));
        // The last round counted, not every round: a second win, so press and an interview too.
        Assert.Equal(5, career.Inbox.Count);
        Assert.All(career.Inbox, m => Assert.Equal(2, m.Round));
        Assert.False(Inbox.Start(career, Now));

        var fresh = new Career { CurrentSeason = new Season { Number = 1, Rounds = [new Round { Number = 1 }] } };
        Assert.True(Inbox.Start(fresh, Now));
        Assert.Empty(fresh.Inbox);
        Assert.True(fresh.InboxStarted);
    }

    [Fact]
    public void Messages_are_marked_read_one_at_a_time_or_all_at_once_and_survive_a_save()
    {
        var career = CareerWith(Field());
        Inbox.MarkRead(career, career.Inbox[^1].Id);
        Assert.Equal(4, Inbox.Unread(career));
        Inbox.MarkRead(career, null);
        Assert.Equal(0, Inbox.Unread(career));

        var store = new CareerStore(_root);
        store.Save(career);
        var loaded = store.Load(career.Id).Career;
        Assert.True(loaded.InboxStarted);
        Assert.Equal(career.Inbox.Select(m => (m.Id, m.Kind, m.Read)), loaded.Inbox.Select(m => (m.Id, m.Kind, m.Read)));
        Assert.Equal(career.Inbox[^1].Body.Select(b => b.ToString()), loaded.Inbox[^1].Body.Select(b => b.ToString()));
    }
}
