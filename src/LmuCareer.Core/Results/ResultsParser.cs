using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LmuCareer.Core.Results;

/// <summary>Reads the session results XML that LMU writes to UserData\Log\Results after every session.</summary>
public static class ResultsParser
{
    // The files open with an inline DTD that declares an entity nothing uses, so it's skipped
    // rather than processed.
    private static readonly XmlReaderSettings ReaderSettings = new() { DtdProcessing = DtdProcessing.Ignore };

    public static SessionResult Load(string path)
    {
        using var reader = XmlReader.Create(path, ReaderSettings);
        return Parse(XDocument.Load(reader), path);
    }

    public static SessionResult Parse(XDocument doc, string sourceFile = "")
    {
        var results = doc.Root?.Element("RaceResults")
            ?? throw new InvalidDataException($"No RaceResults element in '{sourceFile}'.");

        // The session block is named after the session (Practice1, Qualify, Warmup, Race) and is
        // the only child of RaceResults that holds Driver elements.
        var session = results.Elements().FirstOrDefault(e => e.Element("Driver") is not null)
            ?? throw new InvalidDataException($"No session with drivers in '{sourceFile}'.");

        var entries = WithClassPositions(session.Elements("Driver").Select(ParseEntry).ToList());
        var (trackFolder, layoutFile) = ParseTrackData(Text(results, "TrackData"));

        return new SessionResult(
            SourceFile: sourceFile,
            Setting: Text(results, "Setting"),
            Kind: KindOf(session.Name.LocalName),
            StartTime: DateTimeOffset.FromUnixTimeSeconds(Long(session, "DateTime") ?? Long(results, "DateTime") ?? 0),
            TrackVenue: Text(results, "TrackVenue"),
            TrackCourse: Text(results, "TrackCourse"),
            TrackLengthMeters: Double(results, "TrackLength") ?? 0,
            GameVersion: Text(results, "GameVersion"),
            RaceMinutes: Int(results, "RaceTime") ?? 0,
            FuelMultiplier: Double(results, "FuelMult") ?? 1,
            TireMultiplier: Double(results, "TireMult") ?? 1,
            MostLapsCompleted: Int(session, "MostLapsCompleted") ?? entries.Select(e => e.Laps).DefaultIfEmpty(0).Max(),
            Entries: entries,
            Events: ParseEvents(session.Element("Stream")).ToList())
        {
            TrackFolder = trackFolder,
            LayoutFile = layoutFile,
        };
    }

    /// <summary>
    /// TrackData is the layout's full path, e.g. <c>...\Installed\Locations\Spa_2023\1.31\layoutSpaELMS.mas</c>:
    /// the folder after Locations names the track, and the file names the layout.
    /// </summary>
    private static (string Folder, string Layout) ParseTrackData(string trackData)
    {
        var parts = trackData.Split('\\', '/');
        var locations = Array.FindIndex(parts, p => p.Equals("Locations", StringComparison.OrdinalIgnoreCase));
        var folder = locations >= 0 && locations + 1 < parts.Length ? parts[locations + 1] : "";
        var layout = parts.Length > 0 ? Path.GetFileNameWithoutExtension(parts[^1]) : "";
        return (folder, layout);
    }

    private static SessionKind KindOf(string sessionName) => sessionName switch
    {
        _ when sessionName.StartsWith("Practice", StringComparison.OrdinalIgnoreCase) => SessionKind.Practice,
        _ when sessionName.StartsWith("Qualify", StringComparison.OrdinalIgnoreCase) => SessionKind.Qualifying,
        _ when sessionName.StartsWith("Warmup", StringComparison.OrdinalIgnoreCase) => SessionKind.Warmup,
        _ when sessionName.StartsWith("Race", StringComparison.OrdinalIgnoreCase) => SessionKind.Race,
        _ => SessionKind.Unknown,
    };

    private static EntryResult ParseEntry(XElement d) => new(
        Name: Text(d, "Name"),
        TeamName: Text(d, "TeamName"),
        CarNumber: Text(d, "CarNumber"),
        VehicleName: Text(d, "VehName"),
        CarType: Text(d, "CarType"),
        CarClass: Text(d, "CarClass"),
        IsPlayer: Text(d, "isPlayer") == "1",
        GridPosition: Int(d, "GridPos") ?? 0,
        ClassGridPosition: Int(d, "ClassGridPos") ?? 0,
        Position: Int(d, "Position") ?? 0,
        ClassPosition: Int(d, "ClassPosition") ?? 0,
        Laps: Int(d, "Laps") ?? 0,
        BestLapSeconds: Double(d, "BestLapTime"),
        FinishTimeSeconds: Double(d, "FinishTime"),
        Pitstops: Int(d, "Pitstops") ?? 0,
        Status: StatusOf(Text(d, "FinishStatus")),
        Control: d.Elements("ControlAndAids").Select(ParseControl).OfType<ControlSegment>().ToList(),
        LapRecords: d.Elements("Lap").Select(ParseLap).ToList());

    private static FinishStatus StatusOf(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" or "" => FinishStatus.None,
        "finished normally" => FinishStatus.Finished,
        "dnf" => FinishStatus.Dnf,
        "dq" => FinishStatus.Dq,
        _ => FinishStatus.Unknown,
    };

    private static ControlSegment? ParseControl(XElement e)
    {
        var start = ParseInt(e.Attribute("startLap")?.Value);
        var end = ParseInt(e.Attribute("endLap")?.Value);
        if (start is null || end is null) return null;

        // The text is the controller followed by the aids in use, e.g. "PlayerControl,TC=2,ABS=2".
        var controller = e.Value.Split(',')[0].Trim();
        return new ControlSegment(start.Value, end.Value,
            controller.Equals("AIControl", StringComparison.OrdinalIgnoreCase) ? Controller.AI : Controller.Player);
    }

    // <Lap num="2" p="7" et="241.3304" s1="34.1233" s2="25.8770" s3="34.8973" topspeed="307.20" fuel="0.820"
    //   fuelUsed="0.024" ve="0.937" veUsed="0.027" twfl="0.953" twfr="0.961" twrl="0.957" twrr="0.961"
    //   fcompound="0,Medium" rcompound="0,Medium" pit="1">94.8976</Lap>
    private static LapRecord ParseLap(XElement e)
    {
        double? Attr(string name) => ParseDouble(e.Attribute(name)?.Value);
        var wear = (Attr("twfl"), Attr("twfr"), Attr("twrl"), Attr("twrr")) is ({ } fl, { } fr, { } rl, { } rr)
            ? new TyreWear(fl, fr, rl, rr) : null;

        return new LapRecord(
            Number: ParseInt(e.Attribute("num")?.Value) ?? 0,
            Position: ParseInt(e.Attribute("p")?.Value) ?? 0,
            LapTimeSeconds: ParseDouble(e.Value),
            ElapsedSeconds: Attr("et") ?? 0)
        {
            Sector1 = Attr("s1"),
            Sector2 = Attr("s2"),
            Sector3 = Attr("s3"),
            TopSpeedKph = Attr("topspeed"),
            Fuel = Attr("fuel"),
            FuelUsed = Attr("fuelUsed"),
            Energy = Attr("ve"),
            EnergyUsed = Attr("veUsed"),
            Wear = wear,
            FrontCompound = Compound(e.Attribute("fcompound")?.Value),
            RearCompound = Compound(e.Attribute("rcompound")?.Value),
            Pit = e.Attribute("pit")?.Value == "1",
        };
    }

    /// <summary>"0,Medium" is the compound's index and name; the name is what's kept.</summary>
    private static string? Compound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = value[(value.IndexOf(',') + 1)..].Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Each lap's class position, from when every car in the class crossed the line to end it:
    /// the cars that finished that lap sooner are ahead, including any a lap up.
    /// </summary>
    private static List<EntryResult> WithClassPositions(List<EntryResult> entries)
    {
        // A lap ends when the next one starts; the last lap ends at its start plus its time.
        static Dictionary<int, double> LapEnds(EntryResult entry)
        {
            var ends = new Dictionary<int, double>();
            var laps = entry.LapRecords;
            for (var i = 0; i < laps.Count; i++)
            {
                var end = i + 1 < laps.Count ? laps[i + 1].ElapsedSeconds : laps[i].EndSeconds;
                if (end is { } e) ends[laps[i].Number] = e;
            }
            return ends;
        }

        var ends = entries.Select(LapEnds).ToList();
        return entries.Select((entry, i) =>
        {
            var rivals = Enumerable.Range(0, entries.Count)
                .Where(j => j != i && entries[j].CarClass == entry.CarClass).Select(j => ends[j]).ToList();
            var laps = entry.LapRecords.Select(lap => ends[i].TryGetValue(lap.Number, out var mine)
                ? lap with { ClassPosition = 1 + rivals.Count(r => r.TryGetValue(lap.Number, out var theirs) && theirs < mine) }
                : lap).ToList();
            return entry with { LapRecords = laps };
        }).ToList();
    }

    // "Francesco Castellacci(10) reported contact (588.53) with another vehicle Sébastien Baud(13)"
    // "Francesco Castellacci(10) reported contact (12.07) with Immovable"
    private static readonly Regex Contact = new(
        @"^(?<driver>.+?)\(\d+\) reported contact \((?<impact>[\d.]+)\) with (?:another vehicle (?<other>.+)\(\d+\)|(?<object>.+))$",
        RegexOptions.CultureInvariant);

    // "Bill Strube(0) reports new suspension damage"
    private static readonly Regex Damage = new(@"^(?<driver>.+?)\(\d+\) reports new .*damage$", RegexOptions.CultureInvariant);

    private static IEnumerable<RaceEvent> ParseEvents(XElement? stream)
    {
        if (stream is null) yield break;

        foreach (var e in stream.Elements())
        {
            var et = ParseDouble(e.Attribute("et")?.Value) ?? 0;
            var text = e.Value.Trim();
            switch (e.Name.LocalName)
            {
                case "Incident":
                    if (Contact.Match(text) is { Success: true } contact)
                    {
                        yield return new RaceEvent(RaceEventKind.Incident, et, contact.Groups["driver"].Value.Trim(), text)
                        {
                            OtherDriver = contact.Groups["other"].Success ? contact.Groups["other"].Value.Trim() : null,
                            Object = contact.Groups["object"].Success ? contact.Groups["object"].Value.Trim() : null,
                            Impact = ParseDouble(contact.Groups["impact"].Value),
                        };
                    }
                    else
                    {
                        var paren = text.IndexOf('(');
                        yield return new RaceEvent(RaceEventKind.Incident, et, paren > 0 ? text[..paren].Trim() : null, text);
                    }
                    break;

                // Penalties are logged twice: when issued (with a Driver attribute) and when served
                // (without one). Only the issued entry counts.
                case "Penalty" when e.Attribute("Driver") is not null:
                    yield return new RaceEvent(RaceEventKind.Penalty, et, e.Attribute("Driver")!.Value, text);
                    break;

                // <TrackLimits Driver="Bill Strube" ID="0" Lap="3" WarningPoints="10.25" CurrentPoints="10.25" Resolution="3">Invalid Lap Cut Track</TrackLimits>
                case "TrackLimits":
                    yield return new RaceEvent(RaceEventKind.TrackLimits, et, e.Attribute("Driver")?.Value, text)
                    {
                        Lap = ParseInt(e.Attribute("Lap")?.Value),
                        Resolution = ParseInt(e.Attribute("Resolution")?.Value),
                        WarningPoints = ParseDouble(e.Attribute("WarningPoints")?.Value),
                        CurrentPoints = ParseDouble(e.Attribute("CurrentPoints")?.Value),
                    };
                    break;

                // Sector bests carry a Driver attribute; damage reports share the element name without one.
                case "Sector" when e.Attribute("Driver") is null && Damage.Match(text) is { Success: true } damage:
                    yield return new RaceEvent(RaceEventKind.Damage, et, damage.Groups["driver"].Value.Trim(), text);
                    break;
            }
        }
    }

    private static string Text(XElement parent, string name) => parent.Element(name)?.Value.Trim() ?? "";
    private static int? Int(XElement parent, string name) => ParseInt(parent.Element(name)?.Value);
    private static long? Long(XElement parent, string name) =>
        long.TryParse(parent.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static double? Double(XElement parent, string name) => ParseDouble(parent.Element(name)?.Value);

    private static int? ParseInt(string? s) =>
        int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    // Invalid lap times are written as "--.----", which fails the parse and comes back null.
    private static double? ParseDouble(string? s) =>
        double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
