using System.Globalization;
using System.Text.RegularExpressions;
using LmuCareer.Core.Results;
using LmuCareer.Core.Scoring;
using LmuCareer.Cli;
using static LmuCareer.Cli.Format;

// Developer tool for checking the results reader, scoring and career saves against real LMU files.
//   list   <results folder> [--all]           race sessions in the folder (offline ones unless --all)
//   season <race.xml[@weight]> ...            score races as rounds of one championship
//   career ...                                career saves; see CareerCommands.cs

Console.OutputEncoding = System.Text.Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

return args switch
{
    ["list", var dir, .. var rest] => List(dir, rest.Contains("--all")),
    ["season", .. var files] when files.Length > 0 => Season(files),
    ["career", .. var rest] => CareerCommands.Run(rest),
    ["content"] => ContentCommands.Check(null),
    ["content", var root] => ContentCommands.Check(root),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("usage: lmucareer list <results folder> [--all]");
    Console.WriteLine("       lmucareer season <race.xml[@weight]> [<race.xml[@weight]> ...]");
    Console.WriteLine("       lmucareer career help");
    return 1;
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

