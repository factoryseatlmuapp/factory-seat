using System.Text.Json;
using System.Text.Json.Serialization;

namespace LmuCareer.Core.Careers;

public sealed record CareerSummary(
    Guid Id,
    string Name,
    string CarClass,
    string CarType,
    int SeasonNumber,
    int RoundsDone,
    int RoundsTotal,
    DateTimeOffset LastPlayedAt,
    decimal Reputation = 0);

public sealed record LoadedCareer(Career Career, bool RecoveredFromBackup);

/// <summary>
/// Career save files: one JSON file per career under <c>careers\</c>, each keeping its last few
/// versions as backups, plus a shared record of which results files each career has counted.
/// </summary>
public sealed class CareerStore
{
    public const int BackupsKept = 3;

    private const string Extension = ".career.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _careersDir;
    private readonly string _claimsFile;

    public CareerStore(string root)
    {
        Root = root;
        _careersDir = Path.Combine(root, "careers");
        _claimsFile = Path.Combine(root, "claimed-results.json");
        Directory.CreateDirectory(_careersDir);
    }

    public string Root { get; }

    /// <summary>%AppData%\LmuCareer, unless LMUCAREER_HOME points somewhere else (for testing).</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("LMUCAREER_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LmuCareer");

    public IReadOnlyList<CareerSummary> List() =>
        Directory.EnumerateFiles(_careersDir, "*" + Extension)
            .Select(TryRead)
            .OfType<Career>()
            .Select(Summarize)
            .OrderByDescending(s => s.LastPlayedAt)
            .ToList();

    /// <summary>Loads a career, falling back to its newest readable backup if the save itself is damaged.</summary>
    public LoadedCareer Load(Guid id)
    {
        var path = PathFor(id);
        if (TryRead(path) is { } career) return new LoadedCareer(career, false);

        foreach (var backup in BackupPaths(path))
        {
            if (TryRead(backup) is { } recovered) return new LoadedCareer(recovered, true);
        }

        throw new FileNotFoundException($"No readable save for career {id}.", path);
    }

    /// <summary>
    /// Writes the career, keeping the previous versions as backups, and records the results files
    /// it has counted. A file another career counted first stays with that career; that only happens
    /// with an imported copy, since <see cref="RoundMatcher"/> ignores claimed files.
    /// </summary>
    public void Save(Career career)
    {
        WriteWithBackups(PathFor(career.Id), JsonSerializer.Serialize(career, Json));

        var claims = ReadClaims();
        foreach (var stale in claims.Where(c => c.Value == career.Id).Select(c => c.Key).ToList())
            claims.Remove(stale);
        foreach (var file in ClaimedFiles(career))
            claims.TryAdd(file, career.Id);
        WriteClaims(claims);
    }

    public void Rename(Guid id, string name)
    {
        var career = Load(id).Career;
        career.Name = name;
        Save(career);
    }

    /// <summary>Deletes a career and its backups, and frees the results files it had counted.</summary>
    public void Delete(Guid id)
    {
        var path = PathFor(id);
        foreach (var file in BackupPaths(path).Prepend(path).Where(File.Exists))
            File.Delete(file);

        var claims = ReadClaims();
        foreach (var stale in claims.Where(c => c.Value == id).Select(c => c.Key).ToList())
            claims.Remove(stale);
        WriteClaims(claims);
    }

    public void Export(Guid id, string destination) =>
        File.WriteAllText(destination, JsonSerializer.Serialize(Load(id).Career, Json));

    /// <summary>Adds a career from an exported file, under a new identity if this PC already has it.</summary>
    public Career Import(string source)
    {
        Career? career;
        try
        {
            career = JsonSerializer.Deserialize<Career>(File.ReadAllText(source), Json);
        }
        catch (JsonException)
        {
            career = null;
        }
        if (career is null) throw new PlayerError("The file isn't a career save.");
        if (File.Exists(PathFor(career.Id))) career.Id = Guid.NewGuid();

        Save(career);
        return career;
    }

    /// <summary>The name of the career that has counted this results file, other than <paramref name="except"/>.</summary>
    public string? ClaimedByOther(string resultsFileName, Guid except)
    {
        if (!ReadClaims().TryGetValue(resultsFileName, out var owner) || owner == except) return null;
        return TryRead(PathFor(owner))?.Name ?? "another career";
    }

    private static IEnumerable<string> ClaimedFiles(Career career) =>
        career.PastSeasons.Append(career.CurrentSeason)
            .SelectMany(s => s.Rounds)
            .Select(r => r.Result)
            .OfType<RoundResult>()
            .SelectMany(r => new[] { r.RaceFile, r.QualifyingFile })
            .OfType<string>()
            .Where(f => f.Length > 0);

    private static CareerSummary Summarize(Career c) => new(
        c.Id,
        c.Name,
        c.CurrentSeason.Car.CarClass,
        c.CurrentSeason.Car.CarType,
        c.CurrentSeason.Number,
        c.CurrentSeason.Rounds.Count(r => r.State is RoundState.Completed or RoundState.Skipped),
        c.CurrentSeason.Rounds.Count,
        c.LastPlayedAt,
        c.Reputation);

    private string PathFor(Guid id) => Path.Combine(_careersDir, id.ToString("N") + Extension);

    private static IEnumerable<string> BackupPaths(string path) =>
        Enumerable.Range(1, BackupsKept).Select(i => $"{path}.bak{i}");

    private static Career? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Career>(File.ReadAllText(path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes to a temporary file first and swaps it in, so a crash mid-write leaves the previous
    /// save intact. The previous save becomes .bak1, .bak1 becomes .bak2, and so on.
    /// </summary>
    private static void WriteWithBackups(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);

        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        var backups = BackupPaths(path).ToList();
        for (var i = backups.Count - 1; i > 0; i--)
        {
            if (File.Exists(backups[i - 1])) File.Move(backups[i - 1], backups[i], overwrite: true);
        }
        File.Replace(temp, path, backups[0]);
    }

    private Dictionary<string, Guid> ReadClaims()
    {
        if (!File.Exists(_claimsFile)) return new(StringComparer.OrdinalIgnoreCase);
        var claims = JsonSerializer.Deserialize<Dictionary<string, Guid>>(File.ReadAllText(_claimsFile), Json) ?? [];
        return new(claims, StringComparer.OrdinalIgnoreCase);
    }

    private void WriteClaims(Dictionary<string, Guid> claims)
    {
        var temp = _claimsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(claims, Json));
        File.Move(temp, _claimsFile, overwrite: true);
    }
}
