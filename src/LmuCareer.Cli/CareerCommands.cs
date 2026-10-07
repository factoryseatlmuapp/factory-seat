using System.Globalization;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using LmuCareer.Core.Results;
using static LmuCareer.Cli.Format;

namespace LmuCareer.Cli;

/// <summary>
/// Career save commands for testing the flow end to end before there's a GUI. Saves go to
/// --home, then LMUCAREER_HOME, then the app's normal folder under %AppData%.
/// </summary>
internal static class CareerCommands
{
    private static readonly HashSet<string> Flags = ["accept", "dnf", "despite", "default"];

    public static int Run(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Dispatch(string[] args)
    {
        var (positional, options, flags) = ParseOptions(args);
        var home = One(options, "home") ?? CareerStore.DefaultRoot;
        var store = new CareerStore(home);

        return positional switch
        {
            ["new", var name] => New(store, name, options, flags.Contains("default")),
            ["list"] => List(store),
            ["show", var name] => Show(store, name),
            ["arm", var name] => Arm(store, name, One(options, "at")),
            ["disarm", var name] => Update(store, name, c => RoundFlow.Disarm(c.CurrentSeason.ArmedRound ?? throw Fail("No round is armed."))),
            ["skip", var name] => Update(store, name, c => RoundFlow.Skip(c.CurrentSeason.NextRound ?? throw Fail("No rounds left."))),
            ["check", var name, var dir] => Check(store, name, dir, flags),
            ["delete", var name] => Delete(store, name),
            _ => Help(),
        };
    }

    private static int Help()
    {
        Console.WriteLine("""
            lmucareer career new <name> --car <CarType> --class <class> --number <n> --team <team>
                                        [--default --owns all|<pack,pack>] [--event <id>[@minutes] ...]
                                        [--round "<TrackCourse>|<minutes>|<fuel>|<tires>|<weight>|<event>|<min drive share>" ...]
            lmucareer career list
            lmucareer career show <name>
            lmucareer career arm <name> [--at "yyyy-MM-dd HH:mm"]     start the next round (--at backdates it, for testing)
            lmucareer career disarm|skip <name>
            lmucareer career check <name> <results folder> [--accept] [--dnf] [--despite]
            lmucareer career delete <name>
              --home <folder> keeps saves somewhere other than %AppData%\LmuCareer
            """);
        return 1;
    }

    private static int New(CareerStore store, string name, Dictionary<string, List<string>> options, bool defaultSeason)
    {
        var catalog = ContentCatalog.Default;
        var carClass = One(options, "class") ?? throw Fail("--class is required.");
        var owned = One(options, "owns") is { } owns
            ? owns == "all" ? catalog.Packs.Select(p => p.Id).ToList() : owns.Split(',').ToList()
            : [];

        // --default builds the class's default season; --event <id>[@minutes] adds catalog events;
        // --round adds hand-written rounds.
        var rounds = defaultSeason
            ? CalendarBuilder.DefaultSeason(catalog, carClass, DriverRating.Silver, owned)
            : [];
        foreach (var spec in options.GetValueOrDefault("event", []))
        {
            var (id, minutes) = spec.Split('@') is [var i, var m] ? (i, (int?)int.Parse(m, CultureInfo.InvariantCulture)) : (spec, null);
            var e = catalog.Event(id) ?? throw Fail($"No event \"{id}\" in the catalog.");
            rounds.Add(CalendarBuilder.RoundFor(catalog, e, carClass, DriverRating.Silver, minutes));
        }
        rounds.AddRange(options.GetValueOrDefault("round", []).Select(ParseRound));
        rounds = CalendarBuilder.Number(rounds);

        var now = DateTimeOffset.Now;
        var career = new Career
        {
            Name = name,
            CreatedAt = now,
            LastPlayedAt = now,
            DriverName = One(options, "driver") ?? "",
            CurrentSeason = new Season
            {
                Number = 1,
                Car = CarFor(catalog, One(options, "car") ?? throw Fail("--car is required."), carClass,
                    One(options, "number") ?? throw Fail("--number is required."), One(options, "team") ?? ""),
                Rounds = rounds,
            },
        };

        store.Save(career);
        Console.WriteLine($"Created \"{name}\" with {rounds.Count} rounds.");
        return 0;
    }

    /// <summary>The career car, picking up the catalog's other names for the model (Toyota GR010 / TR010).</summary>
    private static CareerCar CarFor(ContentCatalog catalog, string carType, string carClass, string number, string team)
    {
        var known = catalog.CarByType(carType);
        return new CareerCar(carType, carClass, number, team)
        {
            OtherCarTypes = known?.CarTypes.Where(t => t != carType).ToList() ?? [],
        };
    }

    private static Round ParseRound(string spec)
    {
        var p = spec.Split('|');
        string Part(int i, string fallback) => p.Length > i && p[i].Length > 0 ? p[i] : fallback;
        var fuel = double.Parse(Part(2, "1"), CultureInfo.InvariantCulture);

        return new Round
        {
            TrackCourse = p[0],
            RaceMinutes = int.Parse(Part(1, "30"), CultureInfo.InvariantCulture),
            FuelMultiplier = fuel,
            TireMultiplier = double.Parse(Part(3, fuel.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
            PointsWeight = decimal.Parse(Part(4, "1"), CultureInfo.InvariantCulture),
            EventName = Part(5, p[0]),
            MinimumDriveShare = double.Parse(Part(6, "0"), CultureInfo.InvariantCulture),
        };
    }

    private static int List(CareerStore store)
    {
        var careers = store.List();
        if (careers.Count == 0) Console.WriteLine($"No careers in {store.Root}.");
        foreach (var c in careers)
            Console.WriteLine($"{c.Name,-28} {c.CarClass,-6} season {c.SeasonNumber}, {c.RoundsDone}/{c.RoundsTotal} rounds   last played {c.LastPlayedAt.LocalDateTime:yyyy-MM-dd HH:mm}");
        return 0;
    }

    private static int Show(CareerStore store, string name)
    {
        var career = Find(store, name);
        var season = career.CurrentSeason;
        var car = season.Car;
        Console.WriteLine($"{career.Name}: season {season.Number}, {car.CarClass}, #{car.CarNumber} {car.CarType} ({car.TeamName})");
        Console.WriteLine();
        Console.WriteLine($"  {"#",2} {"Event",-26} {"Track",-34} {"Min",4} {"Fuel",5} {"Tires",5} {"Pts",4}  {"State",-9} Result");

        foreach (var r in season.Rounds)
        {
            var me = r.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer);
            var result = me is null ? ""
                : $"{(me.ClassRank is int rank ? "P" + rank : Status(me.Entry.Status))}, {me.DriverPoints:0.#} pts" +
                  (r.Result!.QuitEarly ? " (quit early)" : "") +
                  (r.Result.AcceptedDespite.Count > 0 ? " (settings differed)" : "");
            var state = r.State == RoundState.Armed ? $"armed {r.ArmedAt!.Value.LocalDateTime:MM-dd HH:mm}" : r.State.ToString();
            Console.WriteLine($"  {r.Number,2} {Trim(r.EventName, 26),-26} {Trim(r.TrackCourse, 34),-34} {r.RaceMinutes,4} {r.FuelMultiplier,5:0.##} {r.TireMultiplier,5:0.##} {"x" + r.PointsWeight.ToString("0.#"),4}  {state,-9} {result}");
        }

        var table = season.Standings().FirstOrDefault(t => t.CarClass == car.CarClass);
        if (table is null) return 0;

        Console.WriteLine();
        Console.WriteLine($"  {car.CarClass} standings:");
        var shown = table.Drivers.Take(10).ToList();
        var player = table.Drivers.FirstOrDefault(d => d.IsPlayer);
        if (player is not null && !shown.Contains(player)) shown.Add(player);
        foreach (var d in shown)
        {
            var pos = table.Drivers.ToList().IndexOf(d) + 1;
            Console.WriteLine($"  {(d.IsPlayer ? ">" : " ")}{pos,3} {Trim(d.Name, 24),-24} {Trim(d.TeamName, 28),-28} {d.Points,6:0.#}");
        }
        return 0;
    }

    private static int Arm(CareerStore store, string name, string? at)
    {
        var when = at is null ? DateTimeOffset.Now : new DateTimeOffset(DateTime.Parse(at, CultureInfo.CurrentCulture));
        var career = Find(store, name);
        var round = RoundFlow.Arm(career.CurrentSeason, when);
        store.Save(career);
        Console.WriteLine($"Round {round.Number} ({round.EventName}) armed at {round.ArmedAt!.Value.LocalDateTime:yyyy-MM-dd HH:mm}.");
        Console.WriteLine($"  Briefing: {round.TrackCourse}, {round.RaceMinutes} min, fuel x{round.FuelMultiplier:0.##}, tires x{round.TireMultiplier:0.##}, " +
            $"car #{career.CurrentSeason.Car.CarNumber} {career.CurrentSeason.Car.CarType}");
        return 0;
    }

    private static int Check(CareerStore store, string name, string dir, HashSet<string> flags)
    {
        var career = Find(store, name);
        var round = career.CurrentSeason.ArmedRound ?? throw Fail("No round is armed; run `career arm` first.");
        var evaluation = CareerActions.CheckArmedRound(store, career, dir);
        Console.WriteLine($"Round {round.Number} ({round.EventName}): {evaluation.Status}");
        if (evaluation.Race is { } race)
            Console.WriteLine($"  race:       {race.FileName} ({race.Verdict}{(race.Reasons.Count > 0 ? ": " + string.Join("; ", race.Reasons) : "")})");
        if (evaluation.Qualifying is { } quali)
            Console.WriteLine($"  qualifying: {quali.FileName} ({quali.Verdict})");
        foreach (var ignored in evaluation.Ignored)
            Console.WriteLine($"  ignored:    {ignored.FileName} ({string.Join("; ", ignored.Reasons)})");

        if (!flags.Contains("accept") || evaluation.Status == RoundStatus.Waiting) return 0;

        var scored = CareerActions.AcceptArmedRound(store, career, evaluation,
            takeDnf: flags.Contains("dnf"), acceptDifferences: flags.Contains("despite"), DateTimeOffset.Now);

        var me = scored.Entries.Single(e => e.Entry.IsPlayer);
        Console.WriteLine($"  Accepted: {(me.ClassRank is int r ? "P" + r : Status(me.Entry.Status))} in class, {me.DriverPoints:0.#} points" +
            $"{(me.PolePoints > 0 ? " (incl. pole)" : "")}.");
        return 0;
    }

    private static int Delete(CareerStore store, string name)
    {
        var career = Find(store, name);
        store.Delete(career.Id);
        Console.WriteLine($"Deleted \"{career.Name}\".");
        return 0;
    }

    private static int Update(CareerStore store, string name, Action<Career> change)
    {
        var career = Find(store, name);
        change(career);
        store.Save(career);
        return Show(store, name);
    }

    private static Career Find(CareerStore store, string name)
    {
        var match = store.List().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw Fail($"No career called \"{name}\".");
        var loaded = store.Load(match.Id);
        if (loaded.RecoveredFromBackup) Console.WriteLine("(The save was damaged; loaded the most recent backup.)");
        return loaded.Career;
    }

    private static (string[] Positional, Dictionary<string, List<string>> Options, HashSet<string> Flags) ParseOptions(string[] args)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) { positional.Add(args[i]); continue; }

            var key = args[i][2..];
            if (Flags.Contains(key)) { flags.Add(key); continue; }
            if (i + 1 >= args.Length) throw Fail($"--{key} needs a value.");
            if (!options.TryGetValue(key, out var values)) options[key] = values = [];
            values.Add(args[++i]);
        }

        return (positional.ToArray(), options, flags);
    }

    private static string? One(Dictionary<string, List<string>> options, string key) =>
        options.TryGetValue(key, out var values) ? values[^1] : null;

    private static InvalidOperationException Fail(string message) => new(message);
}
