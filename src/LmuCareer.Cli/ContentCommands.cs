using LmuCareer.Core.Content;

namespace LmuCareer.Cli;

/// <summary>Checks the shipped catalog against a real LMU install.</summary>
internal static class ContentCommands
{
    public static int Check(string? root)
    {
        root ??= LmuInstall.FindViaSteam(@"C:\Program Files (x86)\Steam");
        if (root is null)
        {
            Console.Error.WriteLine("Couldn't find LMU through Steam; pass the install folder.");
            return 1;
        }

        var install = LmuInstall.Scan(root);
        var catalog = ContentCatalog.Default;
        Console.WriteLine($"LMU install: {root}");
        Console.WriteLine($"  {install.Tracks.Count} track folders, {install.Tracks.Sum(t => t.Layouts.Count)} layouts, {install.CarFolders.Count} car folders");

        var problems = 0;
        void Problem(string message) { Console.WriteLine("  ! " + message); problems++; }

        foreach (var track in install.Tracks.Where(t => !catalog.IsNotRacing(t.Folder)))
        {
            var known = catalog.Track(track.Folder);
            if (known is null) { Problem($"track {track.Folder} isn't in the catalog ({string.Join(", ", track.Layouts)})"); continue; }
            foreach (var layout in track.Layouts.Where(l => !known.Layouts.ContainsKey(l)))
                Problem($"layout {track.Folder}/{layout} isn't in the catalog");
        }

        foreach (var track in catalog.Tracks)
        {
            foreach (var layout in track.Layouts.Keys.Where(l => !install.HasLayout(track.Folder, l)))
                Problem($"catalog layout {track.Folder}/{layout} isn't installed");
        }

        foreach (var folder in install.CarFolders.Where(f => !catalog.IsNotRacing(f)
            && catalog.Cars.All(c => !c.Folder.Equals(f, StringComparison.OrdinalIgnoreCase))))
            Problem($"car {folder} isn't in the catalog");

        foreach (var e in catalog.Events.Where(e => !install.HasLayout(e.Folder, e.Layout)))
            Problem($"event {e.Id} uses {e.Folder}/{e.Layout}, which isn't installed");

        Console.WriteLine(problems == 0 ? "  Catalog matches the install." : $"  {problems} difference(s).");
        return 0;
    }
}
