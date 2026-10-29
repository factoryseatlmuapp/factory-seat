namespace LmuCareer.Host;

/// <summary>The page's HTML, CSS, scripts and fonts, embedded in the app.</summary>
public static class WebFiles
{
    /// <summary>A file by its path under wwwroot ("index.html", "js/app.js"); null when there's no such file.</summary>
    public static Stream? Open(string path) =>
        typeof(WebFiles).Assembly.GetManifestResourceStream("wwwroot/" + (path.Length == 0 ? "index.html" : path));

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ttf" => "font/ttf",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}
