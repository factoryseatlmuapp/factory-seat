using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LmuCareer.Host;
using Photino.NET;

namespace LmuCareer.Linux;

/// <summary>
/// The Linux app: the same page as on Windows, in a Photino (WebKitGTK) window. The page is
/// served from the app on a loopback-only address, and talks to <see cref="ApiHost"/> through
/// Photino's web messages, which a small script dresses up as WebView2's so the page needn't know.
/// </summary>
public sealed class Program : IAppWindow
{
    // Photino's window.external, made to look like WebView2's window.chrome.webview (all api.js
    // uses), and window.open sent to the app, which opens web links in the player's browser.
    private const string Bridge = """
        <script>
          (() => {
            const ext = window.external;
            if (window.chrome?.webview || !ext?.sendMessage) return;
            const listeners = [];
            ext.receiveMessage((message) => {
              const data = JSON.parse(message);
              for (const listener of listeners) listener({ data });
            });
            window.chrome = {
              webview: {
                postMessage: (message) => ext.sendMessage(JSON.stringify(message)),
                addEventListener: (type, listener) => { if (type === "message") listeners.push(listener); },
              },
            };
            window.open = (url) => { ext.sendMessage(JSON.stringify({ external: String(new URL(url, location.href)) })); return null; };
          })();
        </script>
        """;

    private readonly PhotinoWindow _window;
    private readonly ApiHost _api;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;

    [STAThread]
    public static void Main()
    {
        using var server = new HttpListener();
        var start = Serve(server);
        new Program(start).Run();
    }

    private Program(string startPage)
    {
        _api = new ApiHost(this);
        _window = new PhotinoWindow()
            .SetTitle("Factory Seat")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .SetMinSize(1100, 700)
            .Center()
#if !DEBUG
            .SetDevToolsEnabled(false)
            .SetContextMenuEnabled(false)
#endif
            .RegisterWebMessageReceivedHandler((_, message) => _window!.SendWebMessage(Receive(message) ?? "null"))
            .Load(startPage);
    }

    private void Run()
    {
        _window.WaitForClose();
        ApiHost.Closing();
    }

    /// <summary>A request from the page, or a web link to open; the reply to send back, if any.</summary>
    private string? Receive(string message)
    {
        using (var doc = JsonDocument.Parse(message))
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("external", out var url))
            {
                OpenExternal(url.GetString());
                return null;
            }
        }
        return _api.Handle(message);
    }

    /// <summary>
    /// Serves the page's files on 127.0.0.1 (never the network) and returns the start page's
    /// address. WebKitGTK won't load the page's script modules from a custom scheme, so it's plain HTTP.
    /// </summary>
    private static string Serve(HttpListener server)
    {
        var port = FreePort();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        _ = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                HttpListenerContext context;
                try { context = await server.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                _ = Task.Run(() => ServeFile(context));
            }
        });
        return $"http://127.0.0.1:{port}/index.html";
    }

    private static void ServeFile(HttpListenerContext context)
    {
        using var response = context.Response;
        var path = context.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
        if (path.Length == 0) path = "index.html";

        using var stream = WebFiles.Open(path);
        if (stream is null)
        {
            response.StatusCode = 404;
            return;
        }
        response.ContentType = WebFiles.ContentType(path);
        response.Headers["Cache-Control"] = "no-store";
        if (path == "index.html")
        {
            // The bridge goes first, before the page's own scripts look for it.
            var html = new StreamReader(stream).ReadToEnd().Replace("<head>", "<head>\n" + Bridge);
            var bytes = Encoding.UTF8.GetBytes(html);
            response.OutputStream.Write(bytes);
        }
        else
        {
            stream.CopyTo(response.OutputStream);
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void OpenExternal(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { parsed.AbsoluteUri }, UseShellExecute = false });
    }

    public void Invoke(Action action)
    {
        if (Environment.CurrentManagedThreadId == _uiThread) action();
        else _window.Invoke(action);
    }

    public void PostEvent(string json) => _window.SendWebMessage(json);

    // GTK windows follow the desktop's theme, and Photino has no way to ask for attention.
    public void FlashIfInBackground() { }

    public void SetDarkTitleBar(bool dark) { }

    public string? PickFolder(string title) =>
        _window.ShowOpenFolder(title, null, false) is [var folder, ..] ? folder : null;

    public string? PickSaveFile(string title, string fileName, FileFilter filter) =>
        _window.ShowSaveFile(title, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fileName),
            [(filter.Name, [filter.Pattern])]) is { Length: > 0 } file ? file : null;

    public string? PickOpenFile(string title, FileFilter filter, string allFiles) =>
        _window.ShowOpenFile(title, null, false, [(filter.Name, [filter.Pattern]), (allFiles, ["*"])]) is [var file, ..] ? file : null;

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false });
}
