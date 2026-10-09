using System.Text.Json;
using System.Text.Json.Serialization;
using LmuCareer.Core.Content;

namespace LmuCareer.Core.Careers;

/// <summary>
/// The open career's next race, written as briefing.json next to the saves for companion apps
/// (an overlay that checks LMU's settings against it, say) to read. It's a file, not a server:
/// the app stays off the network. Values are LMU's own (layout files, car types, multipliers),
/// not the words on screen, so a companion can compare them with what the game reports.
/// </summary>
/// <remarks>
/// <see cref="Version"/> goes up only when something a companion relies on changes meaning or
/// goes away; new fields can appear at any time, so readers should ignore ones they don't know.
/// </remarks>
public sealed record BriefingFile(
    string Format,
    int Version,
    string? App,
    DateTimeOffset WrittenAt,
    BriefingCareer Career,
    // Null between seasons, when there's no race to set up.
    BriefingRound? Round,
    IReadOnlyList<BriefingSponsor> Sponsors)
{
    public const string FormatName = "factory-seat-briefing";
    public const int CurrentVersion = 1;
    public const string FileName = "briefing.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static BriefingFile For(Career career, ContentCatalog catalog, string? app, DateTimeOffset now)
    {
        var season = career.CurrentSeason;
        return new BriefingFile(FormatName, CurrentVersion, app, now,
            new BriefingCareer(career.Id, career.Name, season.Number, season.Car.CarClass, Math.Round(career.Reputation, 1),
                season.Contract?.TeamName is { Length: > 0 } team ? team : season.Car.TeamName,
                season.Contract?.TargetPosition ?? Progression.DefaultTarget),
            season.NextRound is { } round ? RoundOf(round, season, catalog) : null,
            season.Sponsors.Select(deal =>
            {
                var progress = Sponsorship.Progress(season, deal);
                return new BriefingSponsor(deal.Name, deal.Objective, Sponsorship.Describe(deal).ToString(),
                    progress.Status, progress.Done, progress.Needed, deal.Reward);
            }).ToList());
    }

    private static BriefingRound RoundOf(Round round, Season season, ContentCatalog catalog)
    {
        var track = catalog.Track(round.TrackFolder);
        var car = round.GuestCar ?? season.Car;
        var numbers = round.Guest ? round.GuestNumbers : season.Contract?.Numbers ?? [];
        var stintMinutes = (int)Math.Round(round.RaceMinutes / (double)Math.Max(1, round.Stints));

        return new BriefingRound(
            round.Number,
            season.Rounds.Count,
            round.State,
            round.ArmedAt,
            round.Guest,
            new BriefingEvent(round.EventId.Length > 0 ? round.EventId : null, round.EventName, round.RealDurationHours, round.PointsWeight),
            new BriefingTrack(round.TrackFolder, track?.Name ?? round.TrackCourse, round.LayoutFile,
                track?.Layouts.GetValueOrDefault(round.LayoutFile) ?? round.LayoutFile, round.TrackCourse),
            new BriefingCar(car.CarClass, new[] { car.CarType }.Concat(car.OtherCarTypes).Where(t => t.Length > 0).Distinct().ToList(),
                catalog.CarByType(car.CarType)?.Name ?? car.CarType,
                car.CarNumber.Length > 0 ? car.CarNumber : null,
                car.CustomTeam,
                car.TeamName.Length > 0 ? car.TeamName : null,
                numbers),
            new BriefingSettings(round.RaceMinutes, (int)Math.Round(round.FuelMultiplier), (int)Math.Round(round.TireMultiplier),
                (int)Math.Round(round.TimeScale), round.StartTime.Length > 0 ? round.StartTime : null),
            new BriefingPitPlan(round.Stints, stintMinutes, Math.Max(0, round.Stints - 1)));
    }

    /// <summary>Writes the file whole, so a companion reading it never sees half of it.</summary>
    public void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Removes the file when it describes the given career (it was deleted).</summary>
    public static void RemoveFor(string folder, Guid careerId)
    {
        var path = Path.Combine(folder, FileName);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<BriefingFile>(File.ReadAllText(path), Json)?.Career.Id == careerId)
                File.Delete(path);
        }
        catch (JsonException)
        {
            File.Delete(path);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed record BriefingCareer(Guid Id, string Name, int Season, string CarClass, decimal Reputation, string Team, int TargetPosition);

/// <param name="Of">Rounds on the season's calendar.</param>
/// <param name="State">Upcoming, or Armed once the race weekend is started in the app (only races after <see cref="ArmedAt"/> count).</param>
public sealed record BriefingRound(
    int Number,
    int Of,
    RoundState State,
    DateTimeOffset? ArmedAt,
    bool Guest,
    BriefingEvent Event,
    BriefingTrack Track,
    BriefingCar Car,
    BriefingSettings Settings,
    BriefingPitPlan PitPlan);

/// <param name="Id">The catalog event; null for a custom event.</param>
/// <param name="RealHours">Length of the real-world race the round stands in for.</param>
public sealed record BriefingEvent(string? Id, string Name, double RealHours, decimal PointsWeight);

/// <param name="Folder">The track's folder under LMU's Installed\Locations.</param>
/// <param name="LayoutFile">The layout's file without .mas: what results files and the round match on.</param>
/// <param name="TrackCourse">The layout as LMU names it in results files.</param>
public sealed record BriefingTrack(string Folder, string Name, string LayoutFile, string LayoutName, string TrackCourse);

/// <param name="CarTypes">How LMU names the car in results files (a car can have more than one); the first is the one last raced. Empty until a race shows it.</param>
/// <param name="Number">The number to race, or null when the first race sets it (any livery).</param>
/// <param name="CustomTeam">A Race Control custom team car: any number on it counts.</param>
/// <param name="LiveryNumbers">The team's numbers on LMU's grid, to find its livery; empty when any will do.</param>
public sealed record BriefingCar(string Class, IReadOnlyList<string> CarTypes, string Name, string? Number, bool CustomTeam, string? Team, IReadOnlyList<string> LiveryNumbers);

/// <param name="RaceMinutes">One of LMU's race length steps.</param>
/// <param name="FuelUsage">1 = Real, 2 = x2, 3 = x3.</param>
/// <param name="TyreWear">1 = Real, 2 = x2, 3 = x3.</param>
/// <param name="TimeScale">1 = Normal, else Xn.</param>
/// <param name="StartTime">"HH:mm" of the real race, or null for LMU's default.</param>
public sealed record BriefingSettings(int RaceMinutes, int FuelUsage, int TyreWear, int TimeScale, string? StartTime);

/// <param name="StintMinutes">About how long a tank lasts at the briefing's Fuel Usage.</param>
public sealed record BriefingPitPlan(int Stints, int StintMinutes, int Stops);

/// <param name="Objective">The deal's objective in English, e.g. "3 podiums".</param>
/// <param name="Done">Progress so far, where counting makes sense (podiums so far, championship position…).</param>
public sealed record BriefingSponsor(string Name, ObjectiveKind Kind, string Objective, ObjectiveStatus Status, int Done, int Needed, decimal Reward);
