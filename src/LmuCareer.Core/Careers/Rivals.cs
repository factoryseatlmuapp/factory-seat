using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>Why an AI driver became the player's rival.</summary>
public enum RivalryKind
{
    /// <summary>They kept hitting each other.</summary>
    Contact,

    /// <summary>They kept finishing side by side.</summary>
    Battle,
}

/// <summary>An AI driver the player has a rivalry with. Rivalries outlast the season they started in.</summary>
public sealed class Rival
{
    public string Name { get; set; } = "";

    /// <summary>The team they drove for when last seen: joining it counts as joining a rival.</summary>
    public string TeamName { get; set; } = "";

    public RivalryKind Kind { get; set; }

    /// <summary>The season the rivalry started.</summary>
    public int Since { get; set; }
}

/// <summary>
/// Rivalries grow out of a season's races: an AI driver the player keeps making contact with, or
/// one they keep finishing next to. A rival's team counts as a rival when the player leaves a
/// make or team to join it, the same as a rival make.
/// </summary>
public static class Rivals
{
    /// <summary>Contacts worth a mention with the same driver in one championship's season.</summary>
    public const int ContactsForRivalry = 3;

    /// <summary>Races finished right next to the same driver in class in one championship's season.</summary>
    public const int BattlesForRivalry = 4;

    /// <summary>
    /// After a counted race: any driver the season has now made a rival (at most one new one a
    /// race), with a line in the inbox. Known rivals' teams are kept up to date.
    /// </summary>
    public static Rival? AfterRace(Career career, Round round, DateTimeOffset now)
    {
        if (round.Guest || round.Result is null) return null;
        var season = career.CurrentSeason;
        var raced = season.SeriesRounds(round.SeriesId).Where(r => r.State == RoundState.Completed && r.Result is not null).ToList();

        // Who the player keeps hitting.
        var contacts = raced
            .SelectMany(r => DebriefReport.For(r)?.Contacts.Where(c => c.Impact >= ContactSummary.Counts) ?? [])
            .GroupBy(c => Standings.NameKey(c.OtherDriver))
            .ToDictionary(g => g.Key, g => g.Count());

        // Who they keep finishing next to.
        var battles = new Dictionary<string, int>();
        foreach (var r in raced)
        {
            var me = r.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
            if (me?.ClassRank is not { } mine) continue;
            foreach (var other in r.Result.Entries.Where(e => !e.Entry.IsPlayer && e.Entry.CarClass == me.Entry.CarClass
                && e.ClassRank is { } theirs && Math.Abs(theirs - mine) == 1))
            {
                var key = Standings.NameKey(other.Entry.Name);
                battles[key] = battles.GetValueOrDefault(key) + 1;
            }
        }

        // Teams as of this race: a known rival's is kept current.
        var teams = round.Result.Entries.Where(e => !e.Entry.IsPlayer)
            .GroupBy(e => Standings.NameKey(e.Entry.Name))
            .ToDictionary(g => g.Key, g => (g.First().Entry.Name, g.First().Entry.TeamName));
        foreach (var known in career.Rivals)
            if (teams.TryGetValue(Standings.NameKey(known.Name), out var seen)) known.TeamName = seen.TeamName;

        var candidates = teams.Keys
            .Where(k => !career.Rivals.Any(r => Standings.NameKey(r.Name) == k))
            .Select(k => (Key: k, Contacts: contacts.GetValueOrDefault(k), Battles: battles.GetValueOrDefault(k)))
            .Where(c => c.Contacts >= ContactsForRivalry || c.Battles >= BattlesForRivalry)
            .OrderByDescending(c => c.Contacts >= ContactsForRivalry)
            .ThenByDescending(c => c.Contacts + c.Battles)
            .ToList();
        if (candidates.Count == 0) return null;

        var pick = candidates[0];
        var (name, team) = teams[pick.Key];
        var rival = new Rival
        {
            Name = name,
            TeamName = team,
            Kind = pick.Contacts >= ContactsForRivalry ? RivalryKind.Contact : RivalryKind.Battle,
            Since = season.Number,
        };
        career.Rivals.Add(rival);

        var driver = ("rival", (object?)name);
        Inbox.Deliver(career, new InboxMessage
        {
            Id = InboxMessage.IdFor(season.Number, round.Number, MessageKind.Rivalry),
            Kind = MessageKind.Rivalry,
            Season = season.Number,
            Round = round.Number,
            At = now,
            From = Phrase.Of("Paddock press"),
            Subject = Phrase.Of("A rivalry is born: {rival}", driver),
            Body =
            [
                rival.Kind == RivalryKind.Contact
                    ? Phrase.Count(pick.Contacts, "{n} contact with {rival} this season, and neither is backing down.",
                        "{n} contacts with {rival} this season, and neither is backing down.", driver)
                    : Phrase.Count(pick.Battles, "{n} race finishing side by side with {rival} this season. The paddock has noticed.",
                        "{n} races finishing side by side with {rival} this season. The paddock has noticed.", driver),
                team.Length > 0
                    ? Phrase.Of("{team} won't be forgetting it either: signing for them would be signing for your rival.", ("team", team))
                    : Phrase.Of("Signing for their team would be signing for your rival."),
            ],
        });
        return rival;
    }

    /// <summary>Whether a team is a rival driver's.</summary>
    public static bool IsRivalTeam(Career career, string teamName) =>
        teamName.Length > 0 && career.Rivals.Any(r => r.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase));
}
