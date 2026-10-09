using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public sealed class CareerStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lmucareer-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CareerStore _store;

    public CareerStoreTests() => _store = new CareerStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 20, 0, 0, TimeSpan.Zero);

    /// <summary>A career whose first round has been run from the given results file.</summary>
    private static Career CareerWithRace(string name, string raceFile)
    {
        var career = new Career
        {
            Name = name,
            CreatedAt = Now,
            LastPlayedAt = Now,
            CurrentSeason = new Season
            {
                Number = 1,
                Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing"),
                Rounds =
                [
                    new Round { Number = 1, EventName = "Sebring 12 Hours", TrackCourse = "Sebring International Raceway", RaceMinutes = 30, FuelMultiplier = 3.5, TireMultiplier = 3.5 },
                    new Round { Number = 2, EventName = "Spa 6 Hours", TrackCourse = "Circuit de Spa-Francorchamps", RaceMinutes = 30 },
                ],
            },
        };

        var round = RoundFlow.Arm(career.CurrentSeason, Now);
        var race = new ResultsXml { Course = "Sebring International Raceway" }
            .Car("Sam Sample", player: true, number: "69", team: "Sample Racing")
            .Car("Dan Harper", classPos: 2)
            .Parse(raceFile);
        var check = RoundMatcher.Check(race, Now.AddHours(1), round, career.CurrentSeason.Car);
        RoundFlow.Accept(round, RoundFlow.Evaluate([check]), Now.AddHours(1));
        return career;
    }

    [Fact]
    public void Save_and_load_round_trips_the_career_and_its_results()
    {
        var career = CareerWithRace("GT3 rookie", "R1.xml");
        _store.Save(career);

        var loaded = _store.Load(career.Id);

        Assert.False(loaded.RecoveredFromBackup);
        var round = loaded.Career.CurrentSeason.Rounds[0];
        Assert.Equal(RoundState.Completed, round.State);
        Assert.Equal("R1.xml", round.Result!.RaceFile);
        Assert.Equal(25, round.Result.Entries.Single(e => e.Entry.IsPlayer).TotalPoints);
        Assert.Equal(FinishStatus.Finished, round.Result.Entries[0].Entry.Status);
        Assert.Equal(2, loaded.Career.CurrentSeason.NextRound!.Number);

        var standings = loaded.Career.CurrentSeason.Standings().Single();
        Assert.Equal("Sam Sample", standings.Drivers[0].Name);
        Assert.Equal(25, standings.Drivers[0].Points);
    }

    [Fact]
    public void Saved_text_keeps_its_phrases_and_saves_from_before_translations_still_load()
    {
        var career = CareerWithRace("GT3 rookie", "R1.xml");
        var reason = Phrase.Of("your win at the {event}", ("event", "Sebring 12 Hours"));
        career.CurrentSeason.Review = new SeasonReview
        {
            Items =
            [
                ReputationItem.For(Phrase.Count(2, "{n} podium", "{n} podiums"), 2),
                ReputationItem.For(Phrase.Of("{sponsor}: {objective}", ("sponsor", "Motul")).With("objective", Phrase.Count(1, "a pole", "{n} poles")), 3),
            ],
        };
        career.Offers = [new TeamOffer { Id = "o1", TeamName = "Sample Racing", Reason = reason.ToString(), ReasonText = reason }];
        _store.Save(career);

        var loaded = _store.Load(career.Id).Career;
        var items = loaded.CurrentSeason.Review!.Items;
        Assert.Equal(["2 podiums", "Motul: a pole"], items.Select(i => i.Label));
        Assert.Equal(["2 podiums", "Motul: a pole"], items.Select(i => i.Text!.ToString()));
        Assert.Equal("your win at the Sebring 12 Hours", loaded.Offers[0].ReasonText!.ToString());

        // A 1.0 save: the same text, without the phrases.
        var path = Directory.GetFiles(Path.Combine(_root, "careers"), "*.career.json").Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var item in json["currentSeason"]!["review"]!["items"]!.AsArray()) item!.AsObject().Remove("text");
        json["offers"]![0]!.AsObject().Remove("reasonText");
        File.WriteAllText(path, json.ToJsonString());

        var old = _store.Load(career.Id).Career;
        Assert.Equal(["2 podiums", "Motul: a pole"], old.CurrentSeason.Review!.Items.Select(i => i.Label));
        Assert.All(old.CurrentSeason.Review.Items, i => Assert.Null(i.Text));
        Assert.Null(old.Offers[0].ReasonText);
    }

    [Fact]
    public void Lists_careers_with_their_progress()
    {
        _store.Save(CareerWithRace("GT3 rookie", "R1.xml"));

        var summary = Assert.Single(_store.List());

        Assert.Equal(("GT3 rookie", "GT3", 1, 1, 2), (summary.Name, summary.CarClass, summary.SeasonNumber, summary.RoundsDone, summary.RoundsTotal));
    }

    [Fact]
    public void Keeps_the_last_three_versions_and_recovers_from_a_damaged_save()
    {
        var career = CareerWithRace("GT3 rookie", "R1.xml");
        for (var i = 1; i <= 5; i++)
        {
            career.Name = $"Version {i}";
            _store.Save(career);
        }

        var path = Directory.GetFiles(Path.Combine(_root, "careers"), "*.career.json").Single();
        Assert.True(File.Exists(path + ".bak3"));
        Assert.False(File.Exists(path + ".bak4"));

        File.WriteAllText(path, "{ this is not json");
        var loaded = _store.Load(career.Id);

        Assert.True(loaded.RecoveredFromBackup);
        Assert.Equal("Version 4", loaded.Career.Name);
    }

    [Fact]
    public void A_results_file_can_only_count_for_one_career()
    {
        var first = CareerWithRace("GT3 rookie", "R1.xml");
        _store.Save(first);

        var second = new Career { Name = "Hypercar run" };
        _store.Save(second);

        Assert.Equal("GT3 rookie", _store.ClaimedByOther("R1.xml", second.Id));
        Assert.Null(_store.ClaimedByOther("R1.xml", first.Id));

        // The matcher turns that claim into a reason to ignore the file in the other career.
        var round = new Round { TrackCourse = "Sebring International Raceway", RaceMinutes = 30, State = RoundState.Armed, ArmedAt = Now };
        var race = new ResultsXml { Course = "Sebring International Raceway", Fuel = 1, Tires = 1 }.Car("Me", player: true, number: "69").Parse("R1.xml");
        var check = RoundMatcher.Check(race, Now.AddHours(1), round, first.CurrentSeason.Car,
            _store.ClaimedByOther("R1.xml", second.Id));
        Assert.Equal(MatchVerdict.Ignored, check.Verdict);

        _store.Delete(first.Id);

        Assert.Null(_store.ClaimedByOther("R1.xml", second.Id));
        Assert.Single(_store.List());
    }

    [Fact]
    public void First_race_teaches_the_career_its_number_and_team_and_a_counted_livery_change_adopts_the_new_one()
    {
        var results = Path.Combine(_root, "Results");
        Directory.CreateDirectory(results);

        var career = new Career
        {
            Name = "Livery player",
            CurrentSeason = new Season
            {
                Car = new CareerCar("Lexus RCF LMGT3", "GT3", "", ""),
                Rounds =
                [
                    new Round { Number = 1, TrackCourse = "Sebring International Raceway", RaceMinutes = 30, FuelMultiplier = 3, TireMultiplier = 3 },
                    new Round { Number = 2, TrackCourse = "Sebring International Raceway", RaceMinutes = 30, FuelMultiplier = 3, TireMultiplier = 3 },
                ],
            },
        };

        void RaceAs(string number, string team, string file)
        {
            RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now.AddMinutes(-5));
            new ResultsXml { Course = "Sebring International Raceway", Fuel = 3, Tires = 3 }
                .Car("Me", player: true, number: number, team: team)
                .Build().Save(Path.Combine(results, file));
            var evaluation = CareerActions.CheckArmedRound(_store, career, results);
            Assert.Equal(RoundStatus.NeedsConfirmation, evaluation.Status);
            CareerActions.AcceptArmedRound(_store, career, evaluation, takeDnf: false, acceptDifferences: true, DateTimeOffset.Now);
        }

        RaceAs("87", "Akkodis ASP Team", "R1.xml");
        Assert.Equal(("87", "Akkodis ASP Team"), (career.CurrentSeason.Car.CarNumber, career.CurrentSeason.Car.TeamName));

        RaceAs("78", "Akkodis ASP Team", "R2.xml");
        Assert.Equal("78", career.CurrentSeason.Car.CarNumber);
    }

    [Fact]
    public void Rename_and_export_import()
    {
        var career = CareerWithRace("GT3 rookie", "R1.xml");
        _store.Save(career);
        _store.Rename(career.Id, "Sample Racing");

        var exported = Path.Combine(_root, "export.json");
        _store.Export(career.Id, exported);
        var imported = _store.Import(exported);

        Assert.NotEqual(career.Id, imported.Id);
        Assert.Equal("Sample Racing", imported.Name);
        Assert.Equal(2, _store.List().Count);
    }
}
