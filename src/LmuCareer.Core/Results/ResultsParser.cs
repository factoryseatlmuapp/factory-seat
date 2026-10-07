using System.Globalization;
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

        var entries = session.Elements("Driver").Select(ParseEntry).ToList();
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

    private static LapRecord ParseLap(XElement e) => new(
        Number: ParseInt(e.Attribute("num")?.Value) ?? 0,
        Position: ParseInt(e.Attribute("p")?.Value) ?? 0,
        LapTimeSeconds: ParseDouble(e.Value),
        ElapsedSeconds: ParseDouble(e.Attribute("et")?.Value) ?? 0);

    private static IEnumerable<RaceEvent> ParseEvents(XElement? stream)
    {
        if (stream is null) yield break;

        foreach (var e in stream.Elements())
        {
            var et = ParseDouble(e.Attribute("et")?.Value) ?? 0;
            switch (e.Name.LocalName)
            {
                case "Incident":
                    // "Francesco Castellacci(10) reported contact (588.53) with another vehicle Sébastien Baud(13)"
                    var paren = e.Value.IndexOf('(');
                    yield return new RaceEvent(RaceEventKind.Incident, et,
                        paren > 0 ? e.Value[..paren].Trim() : null, e.Value);
                    break;

                // Penalties are logged twice: when issued (with a Driver attribute) and when served
                // (without one). Only the issued entry counts.
                case "Penalty" when e.Attribute("Driver") is not null:
                    yield return new RaceEvent(RaceEventKind.Penalty, et, e.Attribute("Driver")!.Value, e.Value);
                    break;

                case "TrackLimits":
                    yield return new RaceEvent(RaceEventKind.TrackLimits, et, e.Attribute("Driver")?.Value, e.Value);
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
