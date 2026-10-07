using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

public class GuestDriveTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    /// <summary>A GT3 career about to start the default world season.</summary>
    private static Career Upcoming(decimal reputation)
    {
        var career = CareerWith(reputation, "GT3");
        career.CurrentSeason.Rounds = CalendarBuilder.DefaultSeason(Catalog, "GT3", DriverRating.Silver, career.OwnedContent);
        return career;
    }

    [Fact]
    public void Guest_seats_go_to_drivers_who_have_made_a_name()
    {
        var rookie = Upcoming(0);
        GuestDrives.EnsureOffers(rookie, Catalog, install: null, seed: 1);
        Assert.Empty(rookie.CurrentSeason.GuestOffers!);

        var known = Upcoming(30);
        GuestDrives.EnsureOffers(known, Catalog, install: null, seed: 1);
        var offers = known.CurrentSeason.GuestOffers!;
        Assert.Equal(2, offers.Count);
        Assert.All(offers, o => Assert.Equal("guest", Catalog.Event(o.EventId)!.Series));
        Assert.All(offers, o => Assert.Equal("GT3", o.CarClass));
    }

    [Fact]
    public void A_driver_near_the_next_bar_gets_an_audition_at_the_biggest_race()
    {
        var career = Upcoming(38);

        GuestDrives.EnsureOffers(career, Catalog, install: null, seed: 1);

        var audition = Assert.Single(career.CurrentSeason.GuestOffers!, o => o.Audition);
        // GT3 to LMP2: IMSA's big races have no LMP3 class.
        Assert.Equal("LMP2", audition.CarClass);
        Assert.Equal(career.CurrentSeason.GuestOffers!.Max(o => Catalog.Event(o.EventId)!.Hours), Catalog.Event(audition.EventId)!.Hours);
    }

    [Fact]
    public void Accepting_puts_the_race_in_the_calendar_by_month_and_moves_pinned_deals()
    {
        var career = Upcoming(50);
        var season = career.CurrentSeason;
        GuestDrives.EnsureOffers(career, Catalog, install: null, seed: 1);
        var leMans = season.Rounds.Single(r => r.EventId == "le-mans-24");
        season.Sponsors.Add(new SponsorDeal { Objective = ObjectiveKind.PodiumAt, RoundNumber = leMans.Number, EventName = leMans.EventName });
        var daytona = season.GuestOffers!.Single(o => o.EventId == "daytona-24");

        var round = GuestDrives.Accept(career, daytona.Id, minutes: 60, Catalog);

        Assert.Equal(1, round.Number);
        Assert.True(round.Guest);
        Assert.Equal(60, round.RaceMinutes);
        Assert.Equal(daytona.TeamName, round.GuestCar!.TeamName);
        Assert.Equal(leMans.Number, season.Sponsors[0].RoundNumber);
        Assert.Equal(Enumerable.Range(1, season.Rounds.Count), season.Rounds.Select(r => r.Number));

        GuestDrives.Withdraw(career, daytona.Id);
        Assert.DoesNotContain(season.Rounds, r => r.Guest);
        Assert.Equal(leMans.Number, season.Sponsors[0].RoundNumber);
        Assert.Equal(4, leMans.Number);
    }

    [Fact]
    public void A_guest_race_is_matched_on_the_guest_car_and_leaves_the_season_car_alone()
    {
        var root = Directory.CreateTempSubdirectory("lmucareer-guest-");
        try
        {
            var store = new CareerStore(Path.Combine(root.FullName, "saves"));
            var results = Directory.CreateDirectory(Path.Combine(root.FullName, "Results"));
            var career = Upcoming(50);
            GuestDrives.EnsureOffers(career, Catalog, install: null, seed: 1);
            var offer = career.CurrentSeason.GuestOffers!.Single(o => o.EventId == "daytona-24");
            var round = GuestDrives.Accept(career, offer.Id, minutes: 30, Catalog);
            var seasonCar = career.CurrentSeason.Car;
            RoundFlow.Arm(career.CurrentSeason, DateTimeOffset.Now.AddMinutes(-5));

            var guestCarType = round.GuestCar!.CarType;
            new ResultsXml
            {
                Course = "Daytona International Speedway",
                TrackData = @"C:\LMU\Installed\Locations\Daytona_2026\1.0\layoutDaytona.mas",
                RaceMinutes = round.RaceMinutes,
                Fuel = round.FuelMultiplier,
                Tires = round.TireMultiplier,
            }
                .Car("Me", carClass: round.GuestCar.CarClass, carType: guestCarType, player: true, number: "59", team: offer.TeamName)
                .Build().Save(Path.Combine(results.FullName, "R1.xml"));

            var evaluation = CareerActions.CheckArmedRound(store, career, results.FullName);
            Assert.Equal(RoundStatus.NeedsConfirmation, evaluation.Status);
            Assert.Equal(["first race of the season: you raced as #59 for " + offer.TeamName], evaluation.Race!.Reasons);

            CareerActions.AcceptArmedRound(store, career, evaluation, takeDnf: false, acceptDifferences: true, DateTimeOffset.Now);

            Assert.Equal("59", round.GuestCar!.CarNumber);
            Assert.Equal(seasonCar, career.CurrentSeason.Car);

            // A guest drive doesn't start the season: sponsors can still sign, but it can't be withdrawn now.
            Assert.True(Sponsorship.CanSign(career.CurrentSeason));
            Assert.Throws<InvalidOperationException>(() => GuestDrives.Withdraw(career, offer.Id));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Guest_results_stay_out_of_the_championship_but_count_for_reputation()
    {
        var career = CareerWith(0, "GT3", Counted(1, 5), Counted(2, 5));
        var guest = Counted(3, 1, weight: 2);
        guest.Guest = true;
        guest.EventName = "Daytona 24 Hours";
        guest.GuestCar = new CareerCar("Lexus RCF LMGT3", "GT3", "87", "Akkodis ASP Team");
        career.CurrentSeason.Rounds.Add(guest);

        var standings = career.CurrentSeason.Standings().Single();
        Assert.All(standings.Drivers, d => Assert.Equal(2, d.RoundRanks.Count));
        Assert.Equal(0, standings.Drivers.Single(d => d.IsPlayer).Wins);

        var review = Progression.Review(career.CurrentSeason, 0);
        Assert.Contains(new ReputationItem("Guest win: Daytona 24 Hours for Akkodis ASP Team", 6), review.Items);
        Assert.DoesNotContain(review.Items, i => i.Label.StartsWith("Won the"));
    }
}
