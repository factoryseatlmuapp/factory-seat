using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;

namespace LmuCareer.Core.Careers;

/// <summary>A result the paddock talks about. Good and bad ones get different interview questions.</summary>
public enum Moment
{
    FirstWin,
    Win,
    TookLead,
    Comeback,
    BigPodium,
    LostLead,
    Disqualified,
    Retired,
}

public enum InterviewTone { Humble, Confident, BlameTeam }

/// <summary>One way an answer can land: reputation now, and what the next round can still cost.</summary>
public sealed class InterviewOutcome
{
    /// <summary>Out of 100, when the dice are rolled.</summary>
    public int Chance { get; set; }
    public int Now { get; set; }

    /// <summary>Lost if the next championship round misses: the podium after a good moment, the target after a bad one.</summary>
    public int Bet { get; set; }

    public Phrase Effect { get; set; } = new Phrase("");
}

/// <summary>One answer the player can give, with what it says and what it does to reputation.</summary>
public sealed class InterviewOption
{
    public InterviewTone Tone { get; set; }
    public Phrase Line { get; set; } = new Phrase("");

    /// <summary>What it does when the dice aren't rolled: the first of <see cref="Outcomes"/>.</summary>
    public Phrase Effect { get; set; } = new Phrase("");

    /// <summary>How it can land; the first is the one it always gets without the dice. Empty on older saves.</summary>
    public List<InterviewOutcome> Outcomes { get; set; } = [];
}

/// <summary>The interview question in an inbox message, and how it was answered.</summary>
public sealed class InterviewQuestion
{
    public Moment Moment { get; set; }
    public Phrase Question { get; set; } = new Phrase("");
    public List<InterviewOption> Options { get; set; } = [];
    public InterviewTone? Answer { get; set; }

    /// <summary>How the answer landed; null on answers from before the dice.</summary>
    public InterviewOutcome? Landed { get; set; }

    /// <summary>The next race counted before it was answered: it no longer can be.</summary>
    public bool Lapsed { get; set; }
}

/// <summary>An answer given, kept with the season for its review.</summary>
public sealed class InterviewAnswer
{
    public int Round { get; set; }
    public string EventName { get; set; } = "";
    public Moment Moment { get; set; }
    public InterviewTone Tone { get; set; }

    /// <summary>Which of the answer's outcomes it got (0 = the usual one), and what that's worth. Null on older saves.</summary>
    public int Variant { get; set; }
    public int? Now { get; set; }
    public int Bet { get; set; }
}

/// <summary>
/// The press after a race: a headline for a notable moment, and one interview question after the
/// big ones. Interview answers are reputation only, paid at the season review.
/// </summary>
public static class Paddock
{
    /// <summary>A round worth this many points or more is a big one (Le Mans, Daytona, Sebring, Qatar, Petit).</summary>
    public const decimal BigRound = 1.5m;

    /// <summary>Places gained from the grid, finishing in the top 5, that make a comeback.</summary>
    public const int ComebackPlaces = 8;

    public static bool IsGood(Moment moment) => moment is Moment.FirstWin or Moment.Win or Moment.TookLead or Moment.Comeback or Moment.BigPodium;

    /// <summary>Everything notable about the round just counted, most newsworthy first.</summary>
    public static IReadOnlyList<Moment> MomentsOf(Career career, Round round)
    {
        var me = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
        if (me is null) return [];
        var moments = new List<Moment>();
        var season = career.CurrentSeason;

        if (me.ClassRank == 1)
        {
            var wonBefore = career.PastSeasons.Append(season).SelectMany(s => s.Rounds)
                .Any(r => r != round && r.State == RoundState.Completed && r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank == 1);
            moments.Add(wonBefore ? Moment.Win : Moment.FirstWin);
        }

        if (!round.Guest)
        {
            var (before, after) = Leader(season, round);
            if (!before && after) moments.Add(Moment.TookLead);
            if (before && !after) moments.Add(Moment.LostLead);
        }

        if (me.ClassRank is { } finish && finish <= 5 && me.Entry.ClassGridPosition - finish >= ComebackPlaces)
            moments.Add(Moment.Comeback);
        if (me.ClassRank is 2 or 3 && round.PointsWeight >= BigRound) moments.Add(Moment.BigPodium);
        if (me.Entry.Status == FinishStatus.Dq) moments.Add(Moment.Disqualified);
        else if (me.Entry.Status == FinishStatus.Dnf) moments.Add(Moment.Retired);
        return moments;
    }

    /// <summary>Whether the player led their class's drivers' standings before this round and after it.</summary>
    private static (bool Before, bool After) Leader(Season season, Round round)
    {
        var counted = season.SeriesRounds(round.SeriesId).Where(r => r.State == RoundState.Completed && r.Result is not null).ToList();
        var carClass = season.CarFor(round).CarClass;
        bool Leads(IEnumerable<Round> rounds)
        {
            var table = Standings.Build(rounds.Select(r => r.Result!.Entries).ToList())
                .FirstOrDefault(s => s.CarClass.Equals(carClass, StringComparison.OrdinalIgnoreCase));
            return table?.Drivers.FirstOrDefault() is { IsPlayer: true, Points: > 0 };
        }
        return (Leads(counted.Where(r => r != round)), Leads(counted));
    }

    /// <summary>The press headline for the round, or null when nothing in it was news.</summary>
    public static InboxMessage? Press(Career career, Round round, DateTimeOffset now)
    {
        var moments = MomentsOf(career, round);
        if (moments.Count == 0) return null;
        var season = career.CurrentSeason;
        var me = round.Result!.Entries.First(e => e.Entry.IsPlayer);
        var driver = ("driver", (object?)DriverName(career, me.Entry));
        var @event = ("event", (object?)round.EventName);
        var team = season.TeamFor(round);

        var headline = moments[0] switch
        {
            Moment.FirstWin => Phrase.Of("{driver} takes a first career win at the {event}", driver, @event),
            Moment.Win when team.Length > 0 => Phrase.Of("{driver} wins the {event} for {team}", driver, @event, ("team", team)),
            Moment.Win => Phrase.Of("{driver} wins the {event}", driver, @event),
            Moment.TookLead => Phrase.Of("{driver} takes the {class} championship lead", driver, ("class", season.CarFor(round).CarClass)),
            Moment.Comeback => Phrase.Of("From P{grid} to P{finish}: {driver} storms through the field at the {event}",
                ("grid", me.Entry.ClassGridPosition), ("finish", me.ClassRank), driver, @event),
            Moment.BigPodium => Phrase.Of("Podium for {driver} at the {event}", driver, @event),
            Moment.LostLead => Phrase.Of("{driver} loses the championship lead at the {event}", driver, @event),
            Moment.Disqualified => Phrase.Of("{driver} disqualified from the {event}", driver, @event),
            _ => Phrase.Of("Heartbreak at the {event}: {driver} retires", @event, driver),
        };

        var body = new List<Phrase>();
        if (!round.Guest)
        {
            var table = season.StandingsFor(round);
            var position = (table?.Drivers.ToList().FindIndex(d => d.IsPlayer) ?? -1) + 1;
            var rounds = season.SeriesRounds(round.SeriesId).ToList();
            var done = rounds.Count(r => r.State == RoundState.Completed);
            if (position > 0)
                body.Add(Phrase.Of("After {done} of {total} rounds, {driver} is P{n} in the {class} championship.",
                    ("done", done), ("total", rounds.Count), driver, ("n", position), ("class", season.CarFor(round).CarClass)));
        }
        else body.Add(Phrase.Of("A guest drive, outside the championship."));

        return Message(season, round, MessageKind.Press, now, Phrase.Of("Paddock press"), headline, body);
    }

    /// <summary>One question after a big moment in a championship round; null when there's nothing to ask.</summary>
    public static InboxMessage? Interview(Career career, Round round, DateTimeOffset now)
    {
        if (round.Guest || career.ChampionshipOnly) return null;
        var moments = MomentsOf(career, round);
        // Asked about: wins and the lead, a podium or a retirement at a big round, losing the lead. A comeback makes the news but isn't asked about.
        var askable = moments.Where(m => m switch
        {
            Moment.FirstWin or Moment.Win or Moment.TookLead or Moment.BigPodium or Moment.LostLead => true,
            Moment.Disqualified or Moment.Retired => round.PointsWeight >= BigRound,
            _ => false,
        }).ToList();
        if (askable.Count == 0) return null;
        var asked = askable[0];

        var season = career.CurrentSeason;
        var @event = ("event", (object?)round.EventName);
        var question = asked switch
        {
            Moment.FirstWin => Phrase.Of("Your first win! What made the difference at the {event}?", @event),
            Moment.Win => Phrase.Of("Another win at the {event}. What's working so well?", @event),
            Moment.TookLead => Phrase.Of("You lead the championship now. Can you hold on to it?"),
            Moment.BigPodium => Phrase.Of("A podium at the {event}. Are you happy with that?", @event),
            Moment.LostLead => Phrase.Of("You've lost the championship lead. What happens now?"),
            Moment.Disqualified => Phrase.Of("Disqualified from the {event}. What went wrong?", @event),
            _ => Phrase.Of("Out of the {event}. What happened?", @event),
        };

        var good = IsGood(asked);
        var blamedAlready = season.Interviews.Any(a => a.Tone == InterviewTone.BlameTeam);
        var options = new List<InterviewOption>
        {
            Option(InterviewTone.Humble, good
                    ? Phrase.Of("The team gave me a great car. I just tried not to waste it.")
                    : Phrase.Of("That's on me. I'll learn from it and come back stronger."),
                Outcome(75, 1, 0, Phrase.Of("Safe: +1 reputation")),
                Outcome(25, 0, 0, Phrase.Of("No change: the paddock found it bland"))),
            good
                ? Option(InterviewTone.Confident, Phrase.Of("We were the class of the field. Expect more of the same."),
                    Outcome(50, 2, 2, Phrase.Of("+2 now, −2 if you miss the podium in the next round")),
                    Outcome(50, 3, 3, Phrase.Of("The quote goes viral: +3 now, −3 if you miss the podium in the next round")))
                : Option(InterviewTone.Confident, Phrase.Of("One bad day changes nothing. We'll be back on top next time."),
                    Outcome(50, 1, 2, Phrase.Of("+1 now, −2 if you miss the team's target again in the next round")),
                    Outcome(50, 0, 2, Phrase.Of("It sounds like denial: no change now, −2 if you miss the team's target again in the next round"))),
        };
        if (!good)
        {
            var line = Phrase.Of("The car wasn't where it needed to be. The team has work to do.");
            options.Add(blamedAlready
                ? Option(InterviewTone.BlameTeam, line, Outcome(100, -3, 0, Phrase.Of("−3: you've blamed the team once already this season")))
                : Option(InterviewTone.BlameTeam, line,
                    Outcome(50, 0, 0, Phrase.Of("No change, once: a second time this season costs 3")),
                    Outcome(50, -1, 0, Phrase.Of("The team reads it: −1, and a second time this season costs 3"))));
        }

        var message = Message(season, round, MessageKind.Interview, now, Phrase.Of("Paddock press"),
            Phrase.Of("One question after the {event}", @event), [question]);
        message.Interview = new InterviewQuestion { Moment = asked, Question = question, Options = options };
        return message;
    }

    private static InterviewOption Option(InterviewTone tone, Phrase line, params InterviewOutcome[] outcomes) =>
        new() { Tone = tone, Line = line, Effect = outcomes[0].Effect, Outcomes = [.. outcomes] };

    private static InterviewOutcome Outcome(int chance, int now, int bet, Phrase effect) =>
        new() { Chance = chance, Now = now, Bet = bet, Effect = effect };

    /// <summary>
    /// Records the player's answer to an interview still open. With dice, the answer lands one of
    /// its outcomes by chance; without, it gets the usual one. Either way, what it got is kept, so
    /// changing the setting later never changes an answer already given.
    /// </summary>
    public static void Answer(Career career, string messageId, InterviewTone tone, Random? dice = null)
    {
        var message = career.Inbox.FirstOrDefault(m => m.Id == messageId)
            ?? throw new PlayerError("That message isn't in your inbox any more.");
        if (message.Interview is not { } interview) throw new PlayerError("That message isn't an interview.");
        if (interview.Answer is not null) throw new PlayerError("You've already answered that one.");
        if (interview.Lapsed || message.Season != career.CurrentSeason.Number || career.CurrentSeason.Review is not null)
            throw new PlayerError("Too late: the next race has counted since.");
        if (interview.Options.All(o => o.Tone != tone)) throw new PlayerError("That answer isn't one of the options.");

        var option = interview.Options.First(o => o.Tone == tone);
        var variant = dice is null || option.Outcomes.Count < 2 ? 0 : Roll(option.Outcomes, dice);
        var landed = option.Outcomes.ElementAtOrDefault(variant);

        interview.Answer = tone;
        interview.Landed = landed;
        message.Read = true;
        var round = career.CurrentSeason.Rounds.FirstOrDefault(r => r.Number == message.Round);
        career.CurrentSeason.Interviews.Add(new InterviewAnswer
        {
            Round = message.Round,
            EventName = round?.EventName ?? "",
            Moment = interview.Moment,
            Tone = tone,
            Variant = variant,
            Now = landed?.Now,
            Bet = landed?.Bet ?? 0,
        });
    }

    private static int Roll(List<InterviewOutcome> outcomes, Random dice)
    {
        var roll = dice.NextDouble() * outcomes.Sum(o => o.Chance);
        for (var i = 0; i < outcomes.Count; i++)
        {
            roll -= outcomes[i].Chance;
            if (roll < 0) return i;
        }
        return outcomes.Count - 1;
    }

    /// <summary>A championship round counted: questions from earlier rounds can't be answered any more.</summary>
    public static void LapseOpenQuestions(Career career, Round round)
    {
        if (round.Guest) return;
        foreach (var m in career.Inbox.Where(m => m.Interview is { Answer: null, Lapsed: false }
            && (m.Season < career.CurrentSeason.Number || (m.Season == career.CurrentSeason.Number && m.Round < round.Number))))
        {
            m.Interview!.Lapsed = true;
        }
    }

    /// <summary>What the season's interviews are worth at its review: the answers, and the bets the next round settled.</summary>
    public static IEnumerable<ReputationItem> ReviewItems(Season season)
    {
        var target = season.Contract?.TargetPosition ?? Progression.DefaultTarget;
        var blamed = false;
        foreach (var answer in season.Interviews.OrderBy(a => a.Round))
        {
            var @event = ("event", (object?)answer.EventName);
            var good = IsGood(answer.Moment);

            // Answers from before the dice were worth fixed amounts, worked out here.
            var now = answer.Now ?? answer.Tone switch
            {
                InterviewTone.Humble => 1,
                InterviewTone.Confident => good ? 2 : 1,
                _ => blamed ? -3 : 0,
            };
            var bet = answer.Now is null ? (answer.Tone == InterviewTone.Confident ? 2 : 0) : answer.Bet;

            if (now != 0)
            {
                yield return ReputationItem.For(answer.Tone switch
                {
                    InterviewTone.Humble => Phrase.Of("Humble interview after the {event}", @event),
                    InterviewTone.Confident when answer.Variant == 1 && good => Phrase.Of("Confident interview after the {event} went viral", @event),
                    InterviewTone.Confident => Phrase.Of("Confident interview after the {event}", @event),
                    _ when blamed => Phrase.Of("Blamed the team again after the {event}", @event),
                    _ => Phrase.Of("Blamed the team after the {event}, and the team read it", @event),
                }, now);
            }
            if (answer.Tone == InterviewTone.BlameTeam) blamed = true;
            if (bet == 0) continue;

            // The bet: the next championship round that counted, in either series, settles it.
            var next = season.Rounds.Where(r => !r.Guest)
                .Where(r => r.Number > answer.Round && r.State == RoundState.Completed && r.Result is not null)
                .OrderBy(r => r.Number).FirstOrDefault();
            var rank = next?.Result!.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.ClassRank;
            if (next is not null && (good ? rank is not (1 or 2 or 3) : rank is null || rank > target))
                yield return ReputationItem.For(good
                    ? Phrase.Of("Talked big after the {event}, then missed the podium", @event)
                    : Phrase.Of("Promised a comeback after the {event}, then missed the target again", @event), -bet);
        }
    }

    private static string DriverName(Career career, EntryResult player) =>
        career.DriverName is { Length: > 0 } name ? name : player.Name;

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
}
