using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

public class ChampionshipOnlyTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private static Career Championship(params Round[] rounds)
    {
        var career = CareerWith(30, "GT3", rounds);
        career.ChampionshipOnly = true;
        career.CurrentSeason.SponsorRules = Sponsorship.CurrentRules;
        return career;
    }

    [Fact]
    public void No_sponsors_guest_drives_team_interest_or_sponsor_approaches()
    {
        var upcoming = Championship(new Round { Number = 1 }, new Round { Number = 2 });
        Assert.False(Sponsorship.EnsureOffers(upcoming, Catalog, [], seed: 1));
        Assert.Null(upcoming.CurrentSeason.SponsorOffers);
        Assert.False(GuestDrives.EnsureOffers(upcoming, Catalog, install: null, seed: 1));

        // A Le Mans win would get a career driver noticed and a sponsor calling.
        var won = Championship(Counted(1, 1, weight: 2), new Round { Number = 2 });
        Assert.Null(Progression.NoticeResult(won, won.CurrentSeason.Rounds[0], Catalog, install: null));
        Assert.Null(Sponsorship.Approach(won, won.CurrentSeason.Rounds[0], Catalog, [], [], "en", Now, seed: 1));
    }

    [Fact]
    public void The_inbox_keeps_the_debrief_the_team_and_the_press_but_no_interview_or_reputation()
    {
        var career = Championship(Counted(1, 1, weight: 2), new Round { Number = 2 });
        Inbox.AfterRace(career, career.CurrentSeason.Rounds[0], Now);

        var kinds = career.Inbox.Select(m => m.Kind).ToList();
        Assert.Contains(MessageKind.TeamPrincipal, kinds);
        Assert.Contains(MessageKind.Press, kinds);
        Assert.DoesNotContain(MessageKind.Interview, kinds);
        Assert.DoesNotContain(MessageKind.Reputation, kinds);
    }

    [Fact]
    public void The_season_review_moves_no_reputation_and_makes_no_offers()
    {
        var career = Championship(Counted(1, 1), Counted(2, 1), Counted(3, 2));
        Assert.True(Progression.EndSeason(career, Catalog, install: null, seed: 1));

        var review = career.CurrentSeason.Review!;
        Assert.Equal(1, review.ChampionshipPosition);
        Assert.True(review.TargetMet);
        Assert.Empty(review.Items);
        Assert.Equal(30, review.ReputationAfter);
        Assert.Equal(30, career.Reputation);
        Assert.Empty(career.Offers);
    }

    [Fact]
    public void The_next_season_is_any_car_the_player_picks()
    {
        var career = Championship(Counted(1, 1), Counted(2, 1));
        Progression.EndSeason(career, Catalog, install: null, seed: 1);
        var hypercar = Catalog.Cars.First(c => c.Class == "Hyper");

        var next = Progression.StartFreeSeason(career, hypercar, "7", "Sample Hypercar", [new Round { Number = 1 }, new Round { Number = 2 }]);

        Assert.Same(next, career.CurrentSeason);
        Assert.Equal(2, next.Number);
        Assert.Equal(("Hyper", hypercar.Folder, "7", "Sample Hypercar"), (next.Car.CarClass, next.Car.Folder, next.Car.CarNumber, next.Car.TeamName));
        Assert.Single(career.PastSeasons);

        // A career driver's next seat comes from the offers.
        var normal = CareerWith(0, "GT3", Counted(1, 1));
        Progression.EndSeason(normal, Catalog, install: null, seed: 1);
        Assert.Throws<InvalidOperationException>(() => Progression.StartFreeSeason(normal, hypercar, "", "", [new Round { Number = 1 }]));
    }
}
