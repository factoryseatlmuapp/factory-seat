using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using static LmuCareer.Core.Tests.ProgressionTests;

namespace LmuCareer.Core.Tests;

/// <summary>1.5 contract perks: what each one does once it's signed. The Expert engineer is in InboxTests.</summary>
public class PerkTests
{
    private static readonly ContentCatalog Catalog = ContentCatalog.Default;

    private static Career Holding(Career career, params Perk[] perks)
    {
        career.CurrentSeason.Contract = new Contract { TeamName = "Sample Racing", Tier = 2, TargetPosition = 6, Perks = [.. perks] };
        return career;
    }

    [Fact]
    public void Team_backing_forgives_the_first_dnf_only()
    {
        Round[] Rounds() => [Counted(1, 9, status: "DNF"), Counted(2, 9, status: "DNF"), .. Enumerable.Range(3, 4).Select(n => Counted(n, 5))];
        var plain = Holding(CareerWith(30, "GT3", Rounds()));
        var backed = Holding(CareerWith(30, "GT3", Rounds()), Perk.TeamBacking);
        plain.CurrentSeason.ReputationRules = backed.CurrentSeason.ReputationRules = Progression.CurrentReputationRules;

        var without = Progression.Review(plain.CurrentSeason, 30).Items.Single(i => i.Label == "2 DNFs").Points;
        var review = Progression.Review(backed.CurrentSeason, 30);

        Assert.Equal(without / 2, review.Items.Single(i => i.Label == "2 DNFs").Points);
        Assert.Contains(review.Items, i => i.Label == "Team backing: your DNF at the Round 1 cost nothing" && i.Points == 0);
    }

    [Fact]
    public void Preferred_sponsors_bring_one_more_offer_and_a_third_slot()
    {
        var plain = Holding(CareerWith(45, "GT3", new Round { Number = 1 }));
        var preferred = Holding(CareerWith(45, "GT3", new Round { Number = 1 }), Perk.PreferredSponsors);

        Assert.Equal(Sponsorship.MaxDeals, Sponsorship.MaxDealsFor(plain.CurrentSeason));
        Assert.Equal(Sponsorship.MaxDeals + 1, Sponsorship.MaxDealsFor(preferred.CurrentSeason));
        Assert.Equal(Sponsorship.OffersFor(plain) + 1, Sponsorship.OffersFor(preferred));

        Sponsorship.EnsureOffers(preferred, Catalog, builtLiveries: [], seed: 1);
        foreach (var offer in preferred.CurrentSeason.SponsorOffers!.Take(3)) Sponsorship.Sign(preferred.CurrentSeason, offer.SponsorId);
        Assert.Equal(3, preferred.CurrentSeason.Sponsors.Count);
    }

    [Fact]
    public void Guest_drive_priority_adds_a_seat_even_for_an_unknown()
    {
        Career Upcoming(decimal reputation, params Perk[] perks)
        {
            var career = Holding(CareerWith(reputation, "GT3"), perks);
            career.CurrentSeason.Rounds = CalendarBuilder.DefaultSeason(Catalog, "GT3", DriverRating.Silver, career.OwnedContent);
            GuestDrives.EnsureOffers(career, Catalog, install: null, seed: 1);
            return career;
        }

        Assert.Empty(Upcoming(0).CurrentSeason.GuestOffers!);
        Assert.Single(Upcoming(0, Perk.GuestDrivePriority).CurrentSeason.GuestOffers!);
        Assert.Equal(3, Upcoming(30, Perk.GuestDrivePriority).CurrentSeason.GuestOffers!.Count);
    }

    [Fact]
    public void A_development_driver_tests_the_makes_car_a_class_up_whatever_the_reputation()
    {
        Career PreSeason(params Perk[] perks)
        {
            var career = Holding(CareerWith(0, "GT3", Enumerable.Range(1, 3).Select(n => new Round { Number = n, EventName = $"Round {n}" }).ToArray()), perks);
            career.Path = CareerPath.Gt;
            return career;
        }

        Assert.Empty(TestDays.MakeOffers(PreSeason(), Catalog, null, new Random(1)));

        // A Lexus GT3 driver on the GT road: the make's Hypercar.
        var offer = Assert.Single(TestDays.MakeOffers(PreSeason(Perk.DevelopmentDriver), Catalog, null, new Random(1)));
        Assert.True(offer.Development);
        Assert.Equal("Hyper", offer.CarClass);
        Assert.Equal(Catalog.CarByType("Lexus RCF LMGT3")!.Brand, Catalog.Car(offer.CarFolder)!.Brand);
    }
}
