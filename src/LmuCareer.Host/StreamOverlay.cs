using System.Text.Json;

namespace LmuCareer.Host;

/// <summary>
/// The OBS overlay: one HTML file that reads briefing.json from the folder it sits in. Built from
/// the template in wwwroot with the app's fonts and track maps inlined, so it needs no network,
/// and written fresh each time so it keeps up with the app.
/// </summary>
public static class StreamOverlay
{
    public const string FileName = "factory-seat-overlay.html";
    public const string ReadmeName = "factory-seat-overlay README.txt";

    /// <summary>Writes the overlay and its setup notes into a folder; returns the overlay's path.</summary>
    public static string Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        File.WriteAllText(path, Build());
        File.WriteAllText(Path.Combine(folder, ReadmeName), Readme);
        return path;
    }

    public static string Build()
    {
        var html = Text("overlay/overlay.template.html");
        var fonts = new[] { (500, "Medium"), (600, "SemiBold"), (700, "Bold") }.Select(f =>
            $"@font-face {{ font-family: \"Barlow Condensed\"; font-weight: {f.Item1}; " +
            $"src: url(data:font/ttf;base64,{Convert.ToBase64String(Bytes($"fonts/BarlowCondensed-{f.Item2}.ttf"))}) format(\"truetype\"); }}");
        using var maps = JsonDocument.Parse(Text("data/trackmaps.json"));
        return html
            .Replace("/*FONTS*/", string.Join("\n", fonts))
            .Replace("/*MAPS*/null", maps.RootElement.GetProperty("layouts").GetRawText());
    }

    private static byte[] Bytes(string path)
    {
        using var stream = WebFiles.Open(path) ?? throw new FileNotFoundException(path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string Text(string path) => System.Text.Encoding.UTF8.GetString(Bytes(path));

    private const string Readme =
        "Factory Seat stream overlay (for OBS)\r\n" +
        "=====================================\r\n\r\n" +
        "Shows your Factory Seat race briefing on stream: event, round and track map, your car,\r\n" +
        "championship position with the drivers either side, pit plan, sponsor goals, and how you did\r\n" +
        "here last time. It updates by itself a few seconds after anything changes in Factory Seat.\r\n\r\n" +
        "Setup\r\n-----\r\n" +
        "1. Leave factory-seat-overlay.html in this folder (it reads briefing.json, which lives here).\r\n" +
        "2. In OBS: Sources > + > Browser. Tick \"Local file\" and pick factory-seat-overlay.html.\r\n" +
        "3. Width 460, height 780. Done.\r\n\r\n" +
        "Factory Seat writes a fresh copy each time you press Set up in Settings, so after an update,\r\n" +
        "press it once more. Your OBS settings stay as they are.\r\n\r\n" +
        "Bigger\r\n------\r\n" +
        "Don't stretch it with the red handles in OBS (that blurs the text). Double the size in the\r\n" +
        "source settings (920 x 1560) and paste this into the \"Custom CSS\" box:\r\n" +
        "    body { zoom: 2; }\r\n" +
        "For 1.5x use 690 x 1170 and zoom: 1.5.\r\n\r\n" +
        "Hide a card\r\n-----------\r\n" +
        "Add any of these to the Custom CSS box:\r\n" +
        "    #car { display: none; }\r\n" +
        "    #champ { display: none; }\r\n" +
        "    #plan { display: none; }\r\n" +
        "    #sponsors { display: none; }\r\n" +
        "    #history { display: none; }\r\n\r\n" +
        "On track\r\n--------\r\n" +
        "- See-through cards, text stays sharp (lower = more see-through), in the Custom CSS box:\r\n" +
        "    :root { --panel: rgba(11, 15, 26, 0.6); --panel-2: rgba(19, 26, 41, 0.65); }\r\n" +
        "- Only the championship card:\r\n" +
        "    #head, #car, #plan, #sponsors, #history { display: none; }\r\n" +
        "- Or hide it while driving: leave it out of your on-track scene, or set a show/hide hotkey\r\n" +
        "  for the source in OBS Settings > Hotkeys.\r\n\r\n" +
        "Notes\r\n-----\r\n" +
        "- It shows the career you last opened in Factory Seat, and keeps showing it after you close the app.\r\n" +
        "- One self-contained file: no internet, no server, nothing installed.\r\n";
}
