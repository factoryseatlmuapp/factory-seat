using LmuCareer.Core.Careers;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

public class RoundFlowTests
{
    private static readonly DateTimeOffset ArmedAt = new(2026, 10, 6, 20, 0, 0, TimeSpan.Zero);
    private static readonly CareerCar Car = new("Lexus RCF LMGT3", "GT3", "69", "Sample Racing");

    private static Round ArmedRound() => new()
    {
        Number = 1,
        EventName = "Le Mans 24 Hours",
        TrackCourse = "Circuit de la Sarthe",
        RaceMinutes = 30,
        FuelMultiplier = 3.5,
        TireMultiplier = 3.5,
        PointsWeight = 2,
        State = RoundState.Armed,
        ArmedAt = ArmedAt,
    };

    private static ResultsXml Weekend(string session = "Race", int minutes = 30, double fuel = 3.5,
        string course = "Circuit de la Sarthe", string setting = "Race Weekend") =>
        new() { Session = session, RaceMinutes = minutes, Fuel = fuel, Course = course, Setting = setting };

    private static SessionCheck Check(SessionResult session, int minutesAfterArming = 45, Round? round = null) =>
        RoundMatcher.Check(session, ArmedAt.AddMinutes(minutesAfterArming), round ?? ArmedRound(), Car);

    [Fact]
    public void Matching_session_matches()
    {
        var check = Check(Weekend().Car("Sam Sample", player: true, number: "69").Parse());

        Assert.Equal(MatchVerdict.Match, check.Verdict);
        Assert.Empty(check.Reasons);
    }

    public static TheoryData<string, Func<SessionResult>, int> IgnoredSessions => new()
    {
        { "written before the round was started", () => Weekend().Car("Me", player: true, number: "69").Parse(), -5 },
        { "practice session", () => Weekend(session: "Practice1").Car("Me", player: true, number: "69").Parse(), 45 },
        { "multiplayer session", () => Weekend(setting: "Multiplayer").Car("Me", player: true, number: "69").Parse(), 45 },
        { "wrong track (Sebring International Raceway)", () => Weekend(course: "Sebring International Raceway").Car("Me", player: true, number: "69").Parse(), 45 },
        { "wrong car (Ford Mustang LMGT3)", () => Weekend().Car("Me", player: true, number: "69", carType: "Ford Mustang LMGT3").Parse(), 45 },
        { "no player car in the session", () => Weekend().Car("AI only").Parse(), 45 },
    };

    [Theory]
    [MemberData(nameof(IgnoredSessions))]
    public void Sessions_outside_the_round_are_ignored_with_a_reason(string reason, Func<SessionResult> session, int minutesAfterArming)
    {
        var check = Check(session(), minutesAfterArming);

        Assert.Equal(MatchVerdict.Ignored, check.Verdict);
        Assert.Equal([reason], check.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void Files_counted_by_another_career_are_ignored()
    {
        var session = Weekend().Car("Me", player: true, number: "69").Parse();
        var check = RoundMatcher.Check(session, ArmedAt.AddMinutes(45), ArmedRound(), Car, claimedBy: "Hypercar run");

        Assert.Equal(MatchVerdict.Ignored, check.Verdict);
        Assert.Equal(["already counted in the career \"Hypercar run\""], check.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void Wrong_settings_on_the_right_track_and_car_are_a_near_miss()
    {
        var check = Check(Weekend(minutes: 20, fuel: 1).Car("Me", player: true, number: "7").Parse());

        Assert.Equal(MatchVerdict.NearMiss, check.Verdict);
        Assert.Equal(
            ["car number #7 for Team, career car is #69", "race length 20 min, briefing says 30", "fuel x1, briefing says x3.5"],
            check.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void Rounds_with_a_layout_match_on_the_layout_file_not_the_shared_track_name()
    {
        var round = ArmedRound();
        round.TrackFolder = "Spa_2023";
        round.LayoutFile = "layoutSpaELMS";

        ResultsXml Spa(string layout) => new()
        {
            Course = "Circuit de Spa-Francorchamps",
            TrackData = $@"C:\Games\Le Mans Ultimate\Installed\Locations\Spa_2023\1.31\{layout}.mas",
        };

        var elms = Check(Spa("layoutSpaELMS").Car("Me", player: true, number: "69").Parse(), round: round);
        var grandPrix = Check(Spa("layoutSpa").Car("Me", player: true, number: "69").Parse(), round: round);

        Assert.Equal(MatchVerdict.Match, elms.Verdict);
        Assert.Equal(MatchVerdict.Ignored, grandPrix.Verdict);
        Assert.Equal(["wrong track (Circuit de Spa-Francorchamps, layoutSpa)"], grandPrix.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void Car_known_by_another_name_matches_and_an_unnamed_car_is_learned_by_class()
    {
        var toyota = new CareerCar("Toyota GR010", "Hyper", "69", "") { OtherCarTypes = ["Toyota TR010"] };
        var session2026 = Weekend().Car("Me", player: true, number: "69", carClass: "Hyper", carType: "Toyota TR010").Parse();
        Assert.Equal(MatchVerdict.Match, RoundMatcher.Check(session2026, ArmedAt.AddHours(1), ArmedRound(), toyota).Verdict);

        var lmp3 = new CareerCar("", "LMP3", "69", "");
        var ligier = Weekend().Car("Me", player: true, number: "69", carClass: "LMP3", carType: "Ligier JS P325").Parse();
        var gt3 = Weekend().Car("Me", player: true, number: "69").Parse();

        var learned = RoundMatcher.Check(ligier, ArmedAt.AddHours(1), ArmedRound(), lmp3);
        Assert.Equal(MatchVerdict.NearMiss, learned.Verdict);
        Assert.Equal(["first race in this car: LMU calls it \"Ligier JS P325\""], learned.Reasons.Select(r => r.ToString()));
        Assert.Equal(MatchVerdict.Ignored, RoundMatcher.Check(gt3, ArmedAt.AddHours(1), ArmedRound(), lmp3).Verdict);
    }

    [Fact]
    public void Career_with_no_number_yet_asks_on_the_first_race()
    {
        var noNumber = new CareerCar("Lexus RCF LMGT3", "GT3", "", "");
        var race = Weekend().Car("Me", player: true, number: "87", team: "Akkodis ASP Team").Parse();

        var check = RoundMatcher.Check(race, ArmedAt.AddHours(1), ArmedRound(), noNumber);

        Assert.Equal(MatchVerdict.NearMiss, check.Verdict);
        Assert.Equal(["first race of the season: you raced as #87 for Akkodis ASP Team"], check.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void A_custom_team_car_counts_whatever_number_is_on_it()
    {
        var custom = Car with { CustomTeam = true };
        var meme = RoundMatcher.Check(Weekend().Car("Me", player: true, number: "420", vehicle: "Lexus Custom Team 2026 #397").Parse(),
            ArmedAt.AddHours(1), ArmedRound(), custom);
        Assert.Equal(MatchVerdict.Match, meme.Verdict);

        var stock = RoundMatcher.Check(Weekend().Car("Me", player: true, number: "87", team: "Akkodis ASP Team", vehicle: "Akkodis ASP Team 2026 #87:LM").Parse(),
            ArmedAt.AddHours(1), ArmedRound(), custom);
        Assert.Equal(MatchVerdict.NearMiss, stock.Verdict);
        Assert.Equal(["raced #87 for Akkodis ASP Team, not your custom team car"], stock.Reasons.Select(r => r.ToString()));
    }

    [Fact]
    public void Older_saves_recognise_their_custom_team_car_from_the_last_race()
    {
        var career = new Career { CurrentSeason = new Season { Car = new CareerCar("Lexus RCF LMGT3", "GT3", "69", "Sample Racing") } };
        career.CurrentSeason.Rounds.Add(ProgressionTests.Counted(1, 1, customCar: true));

        Assert.True(CareerActions.RecognizeCustomTeam(career));
        Assert.True(career.CurrentSeason.Car.CustomTeam);
        Assert.False(CareerActions.RecognizeCustomTeam(career));
    }

    [Fact]
    public void A_race_at_the_right_track_in_another_car_is_flagged_as_the_wrong_car()
    {
        var mustang = Check(Weekend().Car("Me", player: true, number: "69", carType: "Ford Mustang LMGT3").Parse("R-mustang.xml"));
        var practice = Check(Weekend(session: "Practice1").Car("Me", player: true, number: "69", carType: "Ford Mustang LMGT3").Parse("P.xml"));
        var elsewhere = Check(Weekend(course: "Spa").Car("Me", player: true, number: "69", carType: "Ford Mustang LMGT3").Parse("R-spa.xml"));

        var evaluation = RoundFlow.Evaluate([mustang, practice, elsewhere]);

        Assert.Equal(RoundStatus.Waiting, evaluation.Status);
        Assert.Same(mustang, CareerActions.WrongCarRace(evaluation));
    }

    [Fact]
    public void The_car_can_change_within_its_class_until_the_first_round_counts()
    {
        var catalog = Content.ContentCatalog.Default;
        var career = new Career
        {
            CurrentSeason = new Season
            {
                Car = new CareerCar("Ford Mustang LMGT3", "GT3", "69", "Sample Racing"),
                Rounds = [new Round { Number = 1 }, new Round { Number = 2 }],
            },
        };

        CareerActions.ChangeCar(career, catalog.Cars.Single(c => c.Folder == "LexusRCF_GT3_2024"));
        Assert.Equal(("Lexus RCF LMGT3", "69"), (career.CurrentSeason.Car.CarType, career.CurrentSeason.Car.CarNumber));

        Assert.ThrowsAny<InvalidOperationException>(() =>
            CareerActions.ChangeCar(career, catalog.Cars.Single(c => c.Folder == "Toyota_GR10_2023")));

        career.CurrentSeason.Rounds[0].State = RoundState.Completed;
        Assert.ThrowsAny<InvalidOperationException>(() =>
            CareerActions.ChangeCar(career, catalog.Cars.Single(c => c.Folder == "BMW_M4_LMGT3_2023")));

        // From the second season the car comes with the seat, even before a round counts.
        career.PastSeasons.Add(career.CurrentSeason);
        career.CurrentSeason = new Season { Number = 2, Car = career.CurrentSeason.Car, Rounds = [new Round { Number = 1 }] };
        Assert.ThrowsAny<InvalidOperationException>(() =>
            CareerActions.ChangeCar(career, catalog.Cars.Single(c => c.Folder == "BMW_M4_LMGT3_2023")));
    }

    [Theory]
    [InlineData(-2, "SavedToResume")]   // saved in the pits two minutes before leaving: finish it later
    [InlineData(null, "QuitEarly")]     // no save at all: a real quit
    [InlineData(-120, "QuitEarly")]     // only a save from before the weekend started
    public void A_race_saved_part_way_waits_to_be_finished_instead_of_asking_for_a_dnf(int? saveMinutesBeforeQuit, string expected)
    {
        var root = Directory.CreateTempSubdirectory("lmucareer-save-");
        try
        {
            var store = new CareerStore(Path.Combine(root.FullName, "saves"));
            var results = Directory.CreateDirectory(Path.Combine(root.FullName, "Results"));
            var raceSaves = Directory.CreateDirectory(Path.Combine(root.FullName, "Race Weekend Saves"));
            var career = new Career { CurrentSeason = new Season { Car = Car, Rounds = [ArmedRound()] } };
            var quitAt = ArmedAt.AddMinutes(90);

            var race = Path.Combine(results.FullName, "R1.xml");
            Weekend().Car("Me", player: true, number: "69", status: "DNF").Car("AI", classPos: 2, status: "None").Build().Save(race);
            File.SetLastWriteTimeUtc(race, quitAt.UtcDateTime);
            if (saveMinutesBeforeQuit is int minutes)
            {
                var save = Path.Combine(raceSaves.FullName, "2616150417-Career Race 1.json");
                File.WriteAllText(save, "{}");
                File.SetLastWriteTimeUtc(save, quitAt.AddMinutes(minutes).UtcDateTime);
            }

            var evaluation = CareerActions.CheckArmedRound(store, career, results.FullName, raceSaves.FullName);

            Assert.Equal(expected, evaluation.Status.ToString());
            Assert.Equal(expected == "SavedToResume" ? "Career Race 1" : null, evaluation.SavedAs);

            // Either way, the player can still give up on it and take the DNF.
            var scored = RoundFlow.Accept(career.CurrentSeason.Rounds[0], evaluation, quitAt, takeDnf: true);
            Assert.True(career.CurrentSeason.Rounds[0].Result!.QuitEarly);
            Assert.Equal(FinishStatus.Dnf, scored.Race.Player!.Status);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Nothing_written_yet_is_waiting()
    {
        var evaluation = RoundFlow.Evaluate([Check(Weekend(setting: "Multiplayer").Car("Me", player: true, number: "69").Parse())]);

        Assert.Equal(RoundStatus.Waiting, evaluation.Status);
        Assert.Single(evaluation.Ignored);
    }

    [Fact]
    public void Last_finished_race_counts_and_pairs_with_the_qualifying_before_it()
    {
        var quali = Check(Weekend(session: "Qualify").Car("Me", player: true, number: "69").Parse("Q.xml"), 10);
        var quitRestart = Check(Weekend().Car("Me", player: true, number: "69", status: "DNF").Car("AI", status: "None").Parse("R-quit.xml"), 15);
        var first = Check(Weekend().Car("Me", player: true, number: "69").Parse("R-first.xml"), 50);
        var second = Check(Weekend().Car("Me", player: true, number: "69").Parse("R-second.xml"), 90);

        var evaluation = RoundFlow.Evaluate([quali, quitRestart, first, second]);

        Assert.Equal(RoundStatus.Ready, evaluation.Status);
        Assert.Same(second, evaluation.Race);
        Assert.Same(quali, evaluation.Qualifying);
    }

    [Fact]
    public void Finished_near_miss_needs_confirmation_and_quit_race_needs_a_decision()
    {
        var near = RoundFlow.Evaluate([Check(Weekend(minutes: 20).Car("Me", player: true, number: "69").Parse())]);
        var quit = RoundFlow.Evaluate([Check(Weekend().Car("Me", player: true, number: "69", status: "DNF").Car("AI", status: "None").Parse())]);

        Assert.Equal(RoundStatus.NeedsConfirmation, near.Status);
        Assert.Equal(RoundStatus.QuitEarly, quit.Status);
    }

    [Fact]
    public void Leaving_right_after_the_flag_still_counts_and_cars_on_their_last_lap_keep_their_places()
    {
        // No cool-down lap: LMU writes the cars that hadn't crossed the line yet as "None".
        var round = ArmedRound();
        var evaluation = RoundFlow.Evaluate([Check(Weekend()
            .Car("Leader", laps: 12)
            .Car("Me", player: true, number: "69", classPos: 2, laps: 12)
            .Car("Last lap", classPos: 3, laps: 11, status: "None")
            .Car("Retired", classPos: 4, laps: 5, status: "DNF")
            .Parse())]);

        Assert.Equal(RoundStatus.Ready, evaluation.Status);
        RoundFlow.Accept(round, evaluation, ArmedAt);

        var entries = round.Result!.Entries;
        Assert.False(round.Result.QuitEarly);
        Assert.Equal((FinishStatus.Finished, 2), (entries.Single(e => e.Entry.IsPlayer).Entry.Status, entries.Single(e => e.Entry.IsPlayer).ClassRank));
        Assert.Equal((FinishStatus.Finished, 3), (entries.Single(e => e.Entry.Name == "Last lap").Entry.Status, entries.Single(e => e.Entry.Name == "Last lap").ClassRank));
        Assert.Equal(FinishStatus.Dnf, entries.Single(e => e.Entry.Name == "Retired").Entry.Status);
    }

    [Fact]
    public void A_retired_player_in_a_race_that_ran_to_the_flag_is_a_plain_dnf()
    {
        var evaluation = RoundFlow.Evaluate([Check(Weekend()
            .Car("Leader", laps: 12)
            .Car("Me", player: true, number: "69", classPos: 2, laps: 5, status: "DNF")
            .Parse())]);

        Assert.Equal(RoundStatus.Ready, evaluation.Status);
        Assert.False(evaluation.EndedEarly);
    }

    [Fact]
    public void Accepting_scores_the_round_with_its_weight_and_keeps_laps_only_for_the_player()
    {
        var round = ArmedRound();
        var evaluation = RoundFlow.Evaluate([
            Check(Weekend(session: "Qualify").Car("Me", player: true, number: "69").Parse("Q.xml"), 10),
            Check(Weekend().Car("Me", player: true, number: "69").Car("AI", classPos: 2).Parse("R.xml"), 50),
        ]);

        RoundFlow.Accept(round, evaluation, ArmedAt.AddHours(1));

        Assert.Equal(RoundState.Completed, round.State);
        var result = round.Result!;
        Assert.Equal(("R.xml", "Q.xml"), (result.RaceFile, result.QualifyingFile));
        var me = result.Entries.Single(e => e.Entry.IsPlayer);
        Assert.Equal(51, me.TotalPoints);
        Assert.NotEmpty(me.Entry.LapRecords);
        Assert.Empty(result.Entries.Single(e => !e.Entry.IsPlayer).Entry.LapRecords);
    }

    [Fact]
    public void Near_miss_and_quit_races_are_only_accepted_when_the_player_says_so()
    {
        var nearRound = ArmedRound();
        var near = RoundFlow.Evaluate([Check(Weekend(minutes: 20).Car("Me", player: true, number: "69").Parse())]);
        Assert.Throws<InvalidOperationException>(() => RoundFlow.Accept(nearRound, near, ArmedAt));
        RoundFlow.Accept(nearRound, near, ArmedAt, acceptDifferences: true);
        Assert.Equal(["race length 20 min, briefing says 30"], nearRound.Result!.AcceptedDespite);

        var quitRound = ArmedRound();
        var quit = RoundFlow.Evaluate([Check(Weekend().Car("Me", player: true, number: "69", status: "DNF").Car("AI", status: "None").Parse())]);
        Assert.Throws<InvalidOperationException>(() => RoundFlow.Accept(quitRound, quit, ArmedAt));
        RoundFlow.Accept(quitRound, quit, ArmedAt, takeDnf: true);
        Assert.True(quitRound.Result!.QuitEarly);
    }

    [Fact]
    public void Taking_the_dnf_freezes_the_running_order_for_everyone_else()
    {
        var round = ArmedRound();
        var quit = RoundFlow.Evaluate([Check(Weekend()
            .Car("Leader", status: "None", laps: 10)
            .Car("Me", player: true, number: "69", classPos: 2, laps: 10, status: "DNF")
            .Car("Third", classPos: 3, status: "None", laps: 10)
            .Parse())]);

        RoundFlow.Accept(round, quit, ArmedAt, takeDnf: true);

        var entries = round.Result!.Entries;
        Assert.Equal(1, entries.Single(e => e.Entry.Name == "Leader").ClassRank);
        Assert.Equal(2, entries.Single(e => e.Entry.Name == "Third").ClassRank);
        var me = entries.Single(e => e.Entry.IsPlayer);
        Assert.Equal(FinishStatus.Dnf, me.Entry.Status);
        Assert.Equal(0, me.TotalPoints);
    }

    [Fact]
    public void A_disqualified_player_who_quits_stays_disqualified()
    {
        var round = ArmedRound();
        var quit = RoundFlow.Evaluate([Check(Weekend()
            .Car("Me", player: true, number: "69", status: "DQ")
            .Car("AI", classPos: 2, status: "None")
            .Parse())]);

        RoundFlow.Accept(round, quit, ArmedAt, takeDnf: true);

        Assert.Equal(FinishStatus.Dq, round.Result!.Entries.Single(e => e.Entry.IsPlayer).Entry.Status);
        Assert.Equal(1, round.Result.Entries.Single(e => e.Entry.Name == "AI").ClassRank);
    }

    [Fact]
    public void Arming_takes_the_next_round_and_disarming_puts_it_back()
    {
        var season = new Season
        {
            Rounds =
            [
                new Round { Number = 1, State = RoundState.Completed },
                new Round { Number = 2 },
                new Round { Number = 3 },
            ],
        };

        var armed = RoundFlow.Arm(season, ArmedAt);
        Assert.Equal(2, armed.Number);
        Assert.Same(armed, RoundFlow.Arm(season, ArmedAt.AddHours(1)));
        Assert.Equal(ArmedAt, armed.ArmedAt);

        RoundFlow.Disarm(armed);
        Assert.Equal(RoundState.Upcoming, armed.State);
        Assert.Null(armed.ArmedAt);

        RoundFlow.Skip(armed);
        Assert.Equal(3, season.NextRound!.Number);
    }
}
