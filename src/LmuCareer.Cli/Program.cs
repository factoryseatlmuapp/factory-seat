using System.Globalization;
using System.Text.RegularExpressions;
using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;
using LmuCareer.Cli;
using static LmuCareer.Cli.Format;

// Developer tool for checking the results reader, scoring and career saves against real LMU files.
//   list   <results folder> [--all]           race sessions in the folder (offline ones unless --all)
//   season <race.xml[@weight]> ...            score races as rounds of one championship
//   debrief <race.xml>                        what a counted race stores for the debrief (1.2)
//   career ...                                career saves; see CareerCommands.cs

Console.OutputEncoding = System.Text.Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

return args switch
{
    ["list", var dir, .. var rest] => List(dir, rest.Contains("--all")),
    ["season", .. var files] when files.Length > 0 => Season(files),
    ["debrief", var file] => Debrief(file),
    ["career", .. var rest] => CareerCommands.Run(rest),
    ["content"] => ContentCommands.Check(null),
    ["content", var root] => ContentCommands.Check(root),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("usage: lmucareer list <results folder> [--all]");
    Console.WriteLine("       lmucareer season <race.xml[@weight]> [<race.xml[@weight]> ...]");
    Console.WriteLine("       lmucareer debrief <race.xml>");
    Console.WriteLine("       lmucareer career help");
    return 1;
}

static int Debrief(string file)
{
    var race = ResultsParser.Load(file);
    var debrief = LmuCareer.Core.Careers.Debriefs.From(race);
    Console.WriteLine($"{race.TrackCourse}, {race.RaceMinutes} min, {race.Entries.Count} cars");

    Console.WriteLine();
    Console.WriteLine("AI class bests");
    foreach (var b in debrief.ClassBests)
        Console.WriteLine($"  {b.CarClass,-8} lap {LapTime(b.LapSeconds)} ({b.LapDriver})  S1 {b.Sector1:0.000}  S2 {b.Sector2:0.000}  S3 {b.Sector3:0.000}  top {b.TopSpeedKph:0.0} km/h");

    if (race.Player is not { } player)
    {
        Console.WriteLine("No player car.");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"{player.Name}, {player.CarClass} #{player.CarNumber}: grid P{player.ClassGridPosition} -> P{player.ClassPosition} in class, {player.Laps} laps, {player.Pitstops} stops");
    Console.WriteLine($"  {"Lap",3} {"Cls",3} {"Time",9} {"S1",7} {"S2",7} {"S3",7} {"Top",6} {"Fuel",5} {"VE",5} {"Wear FL/FR/RL/RR",19} {"Tyres",-13} Pit");
    foreach (var l in player.LapRecords)
    {
        var wear = l.Wear is { } w ? $"{w.FrontLeft:0.00}/{w.FrontRight:0.00}/{w.RearLeft:0.00}/{w.RearRight:0.00}" : "";
        Console.WriteLine($"  {l.Number,3} {l.ClassPosition,3} {LapTime(l.LapTimeSeconds),9} {l.Sector1,7:0.000} {l.Sector2,7:0.000} {l.Sector3,7:0.000} " +
            $"{l.TopSpeedKph,6:0.0} {l.Fuel,5:0.00} {l.Energy,5:0.00} {wear,19} {Trim($"{l.FrontCompound}/{l.RearCompound}", 13),-13} {(l.Pit ? "pit" : "")}");
    }

    // What the engineer would say, as the inbox writes it.
    var round = new LmuCareer.Core.Careers.Round
    {
        Number = 1,
        EventName = race.TrackCourse,
        State = LmuCareer.Core.Careers.RoundState.Completed,
        Result = new LmuCareer.Core.Careers.RoundResult { Entries = RaceScorer.Score(race).Entries, Debrief = debrief },
    };
    if (LmuCareer.Core.Careers.DebriefReport.For(round) is { } report)
    {
        Console.WriteLine();
        Console.WriteLine("Stints");
        foreach (var s in report.Stints)
            Console.WriteLine($"  {s.Number}: laps {s.FirstLap}-{s.LastLap}, {s.CleanLaps} clean, avg {LapTime(s.Average)}, best {LapTime(s.Best)}, drop-off {s.DropOff:+0.000;-0.000}, {s.Compound}");
        Console.WriteLine();
        Console.WriteLine("Engineer");
        foreach (var note in LmuCareer.Core.Careers.Inbox.EngineerNotes(report)) Console.WriteLine($"  {note}");
    }

    Console.WriteLine();
    Console.WriteLine($"Player events ({debrief.PlayerEvents.Count})");
    foreach (var group in debrief.PlayerEvents.GroupBy(e => e.Kind))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()}");
        foreach (var e in group.Take(8))
            Console.WriteLine($"    {e.ElapsedSeconds,8:0.0}s  {Trim(e.Text, 100)}");
    }
    return 0;
}

static int List(string dir, bool includeMultiplayer)
{
    var races = Directory.EnumerateFiles(dir, "*R?.xml")
        .Select(TryLoad)
        .OfType<SessionResult>()
        .Where(s => s.Kind == SessionKind.Race && (includeMultiplayer || s.IsSinglePlayer))
        .OrderBy(s => s.StartTime);

    Console.WriteLine($"{"File",-30} {"Date",-16} {"Track",-44} {"Cars",4}  {"Done",-4}  You");
    foreach (var s in races)
    {
        var you = s.Player is { } p ? $"{p.CarClass} P{p.ClassPosition} {Status(p.Status)} ({p.CarType})" : "-";
        Console.WriteLine($"{Path.GetFileName(s.SourceFile),-30} {s.StartTime.LocalDateTime:yyyy-MM-dd HH:mm} " +
            $"{Trim(s.TrackCourse, 44),-44} {s.Entries.Count,4}  {(s.IsComplete ? "yes" : "no"),-4}  {you}");
    }
    return 0;
}

static int Season(string[] specs)
{
    var qualifyingCache = new Dictionary<string, List<SessionResult>>(StringComparer.OrdinalIgnoreCase);
    var rounds = new List<ScoredRace>();

    foreach (var spec in specs)
    {
        var (file, weight) = ParseSpec(spec);
        var race = ResultsParser.Load(file);
        var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
        if (!qualifyingCache.TryGetValue(dir, out var qualifying))
        {
            qualifying = Directory.EnumerateFiles(dir, "*Q?.xml").Select(TryLoad).OfType<SessionResult>().ToList();
            qualifyingCache[dir] = qualifying;
        }

        var quali = QualifyingMatcher.Find(race, qualifying);
        var round = RaceScorer.Score(race, quali, weight: weight,
            roundName: $"R{rounds.Count + 1} {Trim(race.TrackCourse, 34)}");
        rounds.Add(round);
        PrintRound(round, quali);
    }

    foreach (var table in Standings.Build(rounds).Where(t => t.Drivers.Any(d => d.IsPlayer)))
        PrintStandings(table, rounds);

    return 0;
}

static void PrintRound(ScoredRace round, SessionResult? quali)
{
    var race = round.Race;
    Console.WriteLine();
    Console.WriteLine($"=== {round.RoundName}  ({race.StartTime.LocalDateTime:yyyy-MM-dd HH:mm}, {race.RaceMinutes} min, " +
        $"fuel x{race.FuelMultiplier}, tires x{race.TireMultiplier}{(round.Weight != 1 ? $", points x{round.Weight}" : "")})");
    Console.WriteLine($"    {Path.GetFileName(race.SourceFile)}; qualifying: {(quali is null ? "none found" : Path.GetFileName(quali.SourceFile))}");
    if (!race.IsSinglePlayer) Console.WriteLine("    NOTE: multiplayer session; the career only uses offline race weekends.");
    if (!race.IsComplete) Console.WriteLine("    WARNING: race was quit before the flag; cars still running are unclassified.");

    var playerClass = race.Player?.CarClass;
    foreach (var carClass in race.Classes.Where(c => playerClass is null || c == playerClass))
    {
        Console.WriteLine($"    {carClass}:");
        Console.WriteLine($"    {"Pos",-4} {"Driver",-24} {"#",-4} {"Team",-28} {"Laps",4} {"Best",9} {"Status",-9} {"Pts",6}");
        foreach (var e in round.InClass(carClass))
        {
            var mark = e.Entry.IsPlayer ? ">" : " ";
            var pos = e.ClassRank?.ToString() ?? "NC";
            var pts = e.TotalPoints > 0 ? e.TotalPoints.ToString("0.#") + (e.ClassPole ? "*" : "") : "";
            if (e.Entry.IsPlayer && !e.DriverEligible) pts += " (no driver pts)";
            Console.WriteLine($"   {mark}{pos,-4} {Trim(e.Entry.Name, 24),-24} {e.Entry.CarNumber,-4} {Trim(e.Entry.TeamName, 28),-28} " +
                $"{e.Entry.Laps,4} {LapTime(e.Entry.BestLapSeconds),9} {Status(e.Entry.Status),-9} {pts,6}");
        }
    }

    if (race.Player is { } player)
    {
        var mine = race.Events.Where(ev => ev.Driver == player.Name).ToList();
        Console.WriteLine($"    You: {mine.Count(ev => ev.Kind == RaceEventKind.Incident)} incidents, " +
            $"{mine.Count(ev => ev.Kind == RaceEventKind.Penalty)} penalties, " +
            $"{mine.Count(ev => ev.Kind == RaceEventKind.TrackLimits)} track-limits reports; " +
            $"drove {RaceScorer.DriveShare(player):P0} of the laps.");
    }
    Console.WriteLine("    (* includes +1 for class pole)");
}

static void PrintStandings(ClassStandings table, List<ScoredRace> rounds)
{
    var roundCols = string.Concat(Enumerable.Range(1, rounds.Count).Select(i => $" {"R" + i,3}"));
    Console.WriteLine();
    Console.WriteLine($"=== {table.CarClass} drivers' championship after {rounds.Count} round(s)");
    Console.WriteLine($"    {"Pos",-4} {"Driver",-24} {"Team",-28}{roundCols} {"W",2} {"Pod",3} {"Pole",4} {"Pts",6}");
    var pos = 0;
    foreach (var d in table.Drivers)
    {
        var mark = d.IsPlayer ? ">" : " ";
        var ranks = string.Concat(d.RoundRanks.Select(r => $" {(r?.ToString() ?? "-"),3}"));
        Console.WriteLine($"   {mark}{++pos,-4} {Trim(d.Name, 24),-24} {Trim(d.TeamName, 28),-28}{ranks} {d.Wins,2} {d.Podiums,3} {d.Poles,4} {d.Points,6:0.#}");
    }

    Console.WriteLine();
    Console.WriteLine($"=== {table.CarClass} teams' championship");
    pos = 0;
    foreach (var t in table.Teams)
        Console.WriteLine($"    {++pos,-4} {Trim(t.TeamName, 28),-28} {t.Wins,2} wins {t.Points,6:0.#}");
}

static (string File, decimal Weight) ParseSpec(string spec)
{
    var m = Regex.Match(spec, @"^(?<file>.+?)@(?<w>\d+(\.\d+)?)$");
    return m.Success ? (m.Groups["file"].Value, decimal.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture)) : (spec, 1m);
}

