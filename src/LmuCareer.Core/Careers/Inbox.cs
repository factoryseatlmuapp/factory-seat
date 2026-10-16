using System.Globalization;
using System.Text.Json.Serialization;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Careers;

public enum MessageKind
{
    /// <summary>The race engineer on how the race went, from the per-lap data.</summary>
    Debrief,

    /// <summary>The team principal on the result against the contract target.</summary>
    TeamPrincipal,

    /// <summary>The season's reputation so far: the season review worked out mid-season.</summary>
    Reputation,
}

/// <summary>
/// A message in the career's inbox. Everything the player reads is a <see cref="Phrase"/>, so it
/// shows in the app's language when it's read.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>Season, round and kind: one of each per round, so writing a round's mail twice changes nothing.</summary>
    public string Id { get; set; } = "";

    public MessageKind Kind { get; set; }
    public int Season { get; set; }

    /// <summary>The round the message is about.</summary>
    public int Round { get; set; }

    public DateTimeOffset At { get; set; }
    public bool Read { get; set; }
    public Phrase From { get; set; } = Phrase.Of("");
    public Phrase Subject { get; set; } = Phrase.Of("");
    public List<Phrase> Body { get; set; } = [];

    public static string IdFor(int season, int round, MessageKind kind) => $"{season}-{round}-{kind}".ToLowerInvariant();
}

/// <summary>Writes the mail that follows a race: the engineer's debrief, the team principal, and reputation so far.</summary>
public static class Inbox
{
    /// <summary>Messages kept per career; the oldest go first.</summary>
    public const int MaxMessages = 200;

    /// <summary>The mail for a round that just counted.</summary>
    public static void AfterRace(Career career, Round round, DateTimeOffset now)
    {
        // Newest last: the debrief goes in last so it tops the inbox and the strip on Next race.
        var season = career.CurrentSeason;
        if (!round.Guest && ReputationSoFar(career, season, round, now) is { } reputation) Add(career, reputation);

        Add(career, TeamPrincipal(season, round, now));

        if (DebriefReport.For(round) is { } report)
            Add(career, Message(season, round, MessageKind.Debrief, now, Phrase.Of("Your race engineer"),
                Phrase.Of("Debrief: {event}", ("event", round.EventName)), EngineerNotes(report)));
        career.InboxStarted = true;
    }

    /// <summary>
    /// A career from before the inbox opens with mail: the debrief and the team principal's note on
    /// the last round counted, and reputation so far. Once only; a career with nothing counted
    /// yet just starts its inbox.
    /// </summary>
    /// <returns>True when the career changed (it needs saving).</returns>
    public static bool Start(Career career, DateTimeOffset now)
    {
        if (career.InboxStarted) return false;
        career.InboxStarted = true;
        var last = career.CurrentSeason.Rounds.LastOrDefault(r => r.State == RoundState.Completed && r.Result is not null);
        if (last is not null) AfterRace(career, last, now);
        return true;
    }

    public static int Unread(Career career) => career.Inbox.Count(m => !m.Read);

    /// <summary>Marks one message read, or every message when <paramref name="messageId"/> is null.</summary>
    public static void MarkRead(Career career, string? messageId)
    {
        foreach (var m in career.Inbox.Where(m => messageId is null || m.Id == messageId)) m.Read = true;
    }

    private static void Add(Career career, InboxMessage message)
    {
        career.Inbox.RemoveAll(m => m.Id == message.Id);
        career.Inbox.Add(message);
        if (career.Inbox.Count > MaxMessages) career.Inbox.RemoveRange(0, career.Inbox.Count - MaxMessages);
    }

    private static InboxMessage Message(Season season, Round round, MessageKind kind, DateTimeOffset now, Phrase from, Phrase subject, List<Phrase> body) => new()
    {
        Id = InboxMessage.IdFor(season.Number, round.Number, kind),
        Kind = kind,
        Season = season.Number,
        Round = round.Number,
        At = now,
        From = from,
        Subject = subject,
        Body = body,
    };

    private static string Seconds(double value) => Math.Abs(value).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>The engineer's read on the race, most important first.</summary>
    public static List<Phrase> EngineerNotes(DebriefReport r)
    {
        var notes = new List<Phrase>();

        // How the race went, start to finish.
        var grid = ("grid", (object?)r.GridPosition);
        var lap1 = ("lap1", (object?)r.Lap1Position);
        notes.Add((r.Status, r.FinishPosition, r.Lap1Position) switch
        {
            (FinishStatus.Dq, _, _) => Phrase.Of("Started P{grid} and were disqualified.", grid),
            (_, null, { }) => Phrase.Of("Started P{grid}, P{lap1} after lap 1, and retired.", grid, lap1),
            (_, null, null) => Phrase.Of("Started P{grid} and retired.", grid),
            (_, { } finish, { }) => Phrase.Of("Started P{grid}, P{lap1} after lap 1, finished P{finish}.", grid, lap1, ("finish", finish)),
            (_, { } finish, null) => Phrase.Of("Started P{grid}, finished P{finish}.", grid, ("finish", finish)),
        });

        // Pace against the fastest AI in class.
        if (r.BestLap is { } best && r.Gap is { } gap)
        {
            var mine = ("best", (object?)DebriefReport.LapTime(best));
            var ai = ("ai", (object?)DebriefReport.LapTime(r.AiBestLap!.Value));
            notes.Add(gap switch
            {
                < -0.3 => Phrase.Of("Your best lap, {best}, beat the fastest AI in class by {gap} s.", mine, ("gap", Seconds(gap))),
                <= 0.3 => Phrase.Of("Your best lap, {best}, was right on the fastest AI's {ai}.", mine, ai),
                _ => Phrase.Of("Your best lap, {best}, was {gap} s off the fastest AI ({ai}, {driver}).", mine, ("gap", Seconds(gap)), ai, ("driver", r.AiBestDriver)),
            });
        }

        // Where the time went.
        var sectors = r.Sectors.Where(s => s.Gap is not null).ToList();
        if (sectors.Count > 0)
        {
            var worst = sectors.MaxBy(s => s.Gap)!;
            if (worst.Gap > 0.15)
                notes.Add(Phrase.Of("Most of the gap is in sector {n}: {gap} s slower than the best AI there.", ("n", worst.Sector), ("gap", Seconds(worst.Gap!.Value))));
            else if (sectors.All(s => s.Gap <= 0))
                notes.Add(Phrase.Of("You matched or beat the AI's best in every sector."));
        }

        // Stints and how the pace held up.
        var timedStints = r.Stints.Where(s => s.Average is not null).ToList();
        if (timedStints.Count >= 3)
        {
            var quickest = timedStints.MinBy(s => s.Average)!;
            notes.Add(Phrase.Of("Your quickest stint was stint {n}, averaging {avg}.", ("n", quickest.Number), ("avg", DebriefReport.LapTime(quickest.Average!.Value))));
        }
        if (r.Stints.Where(s => s.DropOff >= 1.0).MaxBy(s => s.DropOff) is { } fading)
        {
            var drop = ("drop", (object?)Seconds(fading.DropOff!.Value));
            var n = ("n", (object?)fading.Number);
            notes.Add((fading.RanLow, r.Stints.Count == 1) switch
            {
                (RanLow.Energy, true) => Phrase.Of("Your pace fell off by {drop} s a lap by the end, as the virtual energy ran low.", drop),
                (RanLow.Energy, false) => Phrase.Of("Your pace fell off by {drop} s a lap late in stint {n}, as the virtual energy ran low.", drop, n),
                (RanLow.Fuel, true) => Phrase.Of("Your pace fell off by {drop} s a lap by the end, as the fuel ran low.", drop),
                (RanLow.Fuel, false) => Phrase.Of("Your pace fell off by {drop} s a lap late in stint {n}, as the fuel ran low.", drop, n),
                (_, true) => Phrase.Of("Your pace fell off by {drop} s a lap from the start of the race to the end. Worth a look at tyre wear in the setup.", drop),
                _ => Phrase.Of("Your pace fell off by {drop} s a lap over stint {n}. Worth a look at tyre wear in the setup.", drop, n),
            });
        }

        // Incidents.
        if (r.TrackLimitStrikes > 0)
            notes.Add(Phrase.Count(r.TrackLimitStrikes, "{n} track limits strike.", "{n} track limits strikes."));
        if (r.InvalidatedLaps > 0)
            notes.Add(Phrase.Count(r.InvalidatedLaps, "{n} lap deleted for track limits.", "{n} laps deleted for track limits."));
        if (r.Penalties > 0)
            notes.Add(Phrase.Count(r.Penalties, "{n} penalty from race control.", "{n} penalties from race control."));
        var contacts = r.Contacts.Count(c => c.Impact >= ContactSummary.Counts);
        var hard = r.Contacts.Count(c => c.Impact >= ContactSummary.Hard);
        if (contacts > 0)
            notes.Add(hard > 0
                ? Phrase.Count(contacts, "{n} contact with another car, {hard} of them hard.", "{n} contacts with other cars, {hard} of them hard.", ("hard", hard))
                : Phrase.Count(contacts, "{n} contact with another car.", "{n} contacts with other cars."));
        foreach (var damage in r.Damage.Take(2))
        {
            var part = damage.Part switch
            {
                "suspension" => Phrase.Of("Suspension damage"),
                "engine" => Phrase.Of("Engine damage"),
                _ => Phrase.Of("Damage"),
            };
            notes.Add(damage.Lap is { } lap
                ? Phrase.Of("{damage} on lap {lap}.", ("lap", lap)).With("damage", part)
                : Phrase.Of("{damage}.").With("damage", part));
        }
        if (r.HasDetail && r.TrackLimitStrikes == 0 && r.Penalties == 0 && contacts == 0 && r.Damage.Count == 0)
            notes.Add(Phrase.Of("A clean race: no penalties, no contacts, no track limits strikes."));

        if (r.AiLaps > 0)
            notes.Add(Phrase.Of("The AI drove {ai} of your {laps} laps.", ("ai", r.AiLaps), ("laps", r.Laps)));
        return notes;
    }

    /// <summary>The team principal on the result: against the target, and where the championship stands.</summary>
    private static InboxMessage TeamPrincipal(Season season, Round round, DateTimeOffset now)
    {
        var me = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
        var rank = me?.ClassRank;
        var status = me?.Entry.Status ?? FinishStatus.None;
        var body = new List<Phrase>();

        if (round.Guest)
        {
            var guestTeam = round.GuestCar?.TeamName is { Length: > 0 } t ? t : season.Car.TeamName;
            body.Add(rank is { } p
                ? Phrase.Of("Thanks for driving for us at the {event}. P{n} in class.", ("event", round.EventName), ("n", p))
                : Phrase.Of("Thanks for driving for us at the {event}. A shame we didn't see the flag.", ("event", round.EventName)));
            return Message(season, round, MessageKind.TeamPrincipal, now,
                Phrase.Of("Team principal, {team}", ("team", guestTeam)), Phrase.Of("After the {event}", ("event", round.EventName)), body);
        }

        var target = season.Contract?.TargetPosition ?? Progression.DefaultTarget;
        body.Add((status, rank) switch
        {
            (FinishStatus.Dq, _) => Phrase.Of("A disqualification. That can't happen again."),
            (_, null) => Phrase.Of("We didn't see the flag. Those are points we needed."),
            (_, 1) => Phrase.Of("A win. That's exactly what we signed you for."),
            (_, <= 3) => Phrase.Of("A podium. That's what we want to see."),
            (_, { } p) when p <= target => Phrase.Of("P{n}. That's the level we need from you.", ("n", p)),
            (_, { } p) => Phrase.Of("P{n} isn't enough. We need you finishing in the top {target}.", ("n", p), ("target", target)),
        });

        var table = season.Standings().FirstOrDefault(s => s.CarClass.Equals(season.Car.CarClass, StringComparison.OrdinalIgnoreCase));
        var index = table?.Drivers.ToList().FindIndex(d => d.IsPlayer) ?? -1;
        if (index >= 0)
        {
            var position = index + 1;
            body.Add(position <= target
                ? Phrase.Of("You're P{n} in the championship. Our target is top {target}: on course.", ("n", position), ("target", target))
                : Phrase.Count(position - target, "You're P{position} in the championship. Our target is top {target}: {n} place to find.",
                    "You're P{position} in the championship. Our target is top {target}: {n} places to find.", ("position", position), ("target", target)));
        }

        var left = season.ChampionshipRounds.Count(r => r.State is RoundState.Upcoming or RoundState.Armed);
        body.Add(left > 0
            ? Phrase.Count(left, "{n} round to go.", "{n} rounds to go.")
            : Phrase.Of("That's the season. We'll talk at the review."));

        var team = season.Contract?.TeamName is { Length: > 0 } name ? name : season.Car.TeamName;
        return Message(season, round, MessageKind.TeamPrincipal, now,
            team.Length > 0 ? Phrase.Of("Team principal, {team}", ("team", team)) : Phrase.Of("Your team principal"),
            Phrase.Of("After the {event}", ("event", round.EventName)), body);
    }

    /// <summary>The season review worked out now: what the season is worth so far, and its biggest items.</summary>
    private static InboxMessage? ReputationSoFar(Career career, Season season, Round round, DateTimeOffset now)
    {
        var review = Progression.Review(season, career.Reputation);
        var change = review.ReputationAfter - review.ReputationBefore;
        var body = new List<Phrase>
        {
            Phrase.Of("Paid at the season review. As things stand, that takes you from {before} to {after}.",
                ("before", Whole(review.ReputationBefore)), ("after", Whole(review.ReputationAfter))),
        };

        // What earned it, biggest first; the season's cap, if it bites, closes the list.
        var cap = review.Items.LastOrDefault(i => i.Text?.Text is Progression.CapUp or Progression.CapDown);
        foreach (var item in review.Items.Where(i => i != cap && i.Points != 0 && i.Text is not null).OrderByDescending(i => Math.Abs(i.Points)).Take(4))
            body.Add(Phrase.Of("{item}: {points}", ("points", Signed(item.Points))).With("item", item.Text!));
        if (cap?.Text is { } capped) body.Add(capped);

        return Message(season, round, MessageKind.Reputation, now, Phrase.Of("Your manager"),
            Phrase.Of("Reputation this season so far: {change}", ("change", Signed(change))), body);
    }

    // As the page shows reputation: halves round up.
    private static decimal Whole(decimal value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static string Signed(decimal value) =>
        (value > 0 ? "+" : value < 0 ? "−" : "") + Math.Abs(value).ToString("0.#", CultureInfo.InvariantCulture);
}
