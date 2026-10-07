using System.Text.RegularExpressions;

namespace LmuCareer.Core.Content;

public sealed record InstalledTrack(string Folder, string Version, IReadOnlyList<string> Layouts);

/// <summary>
/// What's on disk in an LMU install. Every DLC pack's files are installed whether the player owns
/// it or not (it's one Steam download), so this says what exists, not what can be driven.
/// </summary>
public sealed partial record LmuInstall(string Root, IReadOnlyList<InstalledTrack> Tracks, IReadOnlyList<string> CarFolders)
{
    public const string SteamAppId = "2399420";

    public string ResultsFolder => Path.Combine(Root, "UserData", "Log", "Results");

    public bool HasLayout(string folder, string layout) =>
        Tracks.Any(t => t.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)
            && t.Layouts.Contains(layout, StringComparer.OrdinalIgnoreCase));

    public static bool LooksLikeInstall(string root) =>
        Directory.Exists(Path.Combine(root, "Installed", "Locations"))
        && Directory.Exists(Path.Combine(root, "UserData"));

    /// <summary>
    /// Reads the track layouts and car folders. Each track folder holds one subfolder per version;
    /// the newest one's manifest (.mft) lists the layouts as <c>MASFile=layoutX.mas</c> lines.
    /// The .mas packages themselves are encrypted and never opened.
    /// </summary>
    public static LmuInstall Scan(string root)
    {
        if (!LooksLikeInstall(root)) throw new DirectoryNotFoundException($"'{root}' doesn't look like a Le Mans Ultimate install.");

        var tracks = new List<InstalledTrack>();
        foreach (var location in Directory.EnumerateDirectories(Path.Combine(root, "Installed", "Locations")))
        {
            var latest = Directory.EnumerateDirectories(location)
                .Select(dir => (Dir: dir, Version: Version.TryParse(Path.GetFileName(dir), out var v) ? v : null))
                .Where(x => x.Version is not null && Directory.EnumerateFiles(x.Dir, "*.mft").Any())
                .MaxBy(x => x.Version);
            if (latest.Dir is null) continue;

            var manifest = Directory.EnumerateFiles(latest.Dir, "*.mft").First();
            var layouts = File.ReadLines(manifest)
                .Select(line => LayoutLine().Match(line))
                .Where(m => m.Success)
                .Select(m => m.Groups["layout"].Value)
                .ToList();
            if (layouts.Count > 0)
                tracks.Add(new InstalledTrack(Path.GetFileName(location), Path.GetFileName(latest.Dir), layouts));
        }

        var vehicles = Path.Combine(root, "Installed", "Vehicles");
        var cars = Directory.Exists(vehicles) ? Directory.EnumerateDirectories(vehicles).Select(Path.GetFileName).OfType<string>().ToList() : [];

        return new LmuInstall(root, tracks.OrderBy(t => t.Folder).ToList(), cars);
    }

    /// <summary>
    /// Finds LMU through Steam: each library listed in libraryfolders.vdf that has LMU's app
    /// manifest holds the game under steamapps\common.
    /// </summary>
    public static string? FindViaSteam(string steamRoot)
    {
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(libraryFile)
            ? File.ReadLines(libraryFile).Select(line => VdfPath().Match(line)).Where(m => m.Success)
                .Select(m => m.Groups["path"].Value.Replace(@"\\", @"\"))
                .Prepend(steamRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
            : [steamRoot];

        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var installDir = File.ReadLines(manifest).Select(l => VdfInstallDir().Match(l)).FirstOrDefault(m => m.Success)?.Groups["dir"].Value
                ?? "Le Mans Ultimate";
            var root = Path.Combine(library, "steamapps", "common", installDir);
            if (LooksLikeInstall(root)) return root;
        }
        return null;
    }

    [GeneratedRegex(@"^MASFile=(?<layout>layout[^.\s]*)\.mas\b", RegexOptions.IgnoreCase)]
    private static partial Regex LayoutLine();

    [GeneratedRegex(@"^\s*""path""\s+""(?<path>[^""]+)""")]
    private static partial Regex VdfPath();

    [GeneratedRegex(@"^\s*""installdir""\s+""(?<dir>[^""]+)""")]
    private static partial Regex VdfInstallDir();
}
