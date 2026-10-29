using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LmuCareer.Host;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace LmuCareer.App;

/// <summary>
/// The app is a WebView2 page: its HTML, CSS and scripts are embedded in the exe and served from a
/// private host name, and the page talks to <see cref="ApiHost"/> through web messages.
/// </summary>
public partial class MainWindow : Window, IAppWindow
{
    // A .localhost name: browsers resolve it on the PC itself and never ask a DNS server (RFC 6761),
    // so nothing about the app goes out on the network. The pages never reach it anyway: every
    // request to it is answered from the exe (ServeEmbeddedFile).
    private const string Host = "factory-seat.localhost";
    private const string StartPage = $"https://{Host}/index.html";

    private readonly ApiHost _api;

    public MainWindow()
    {
        InitializeComponent();
        _api = new ApiHost(this);
        Loaded += async (_, _) => await StartAsync();
        Closing += (_, _) => ApiHost.Closing();
    }

    private async Task StartAsync()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupMessage("This app needs the Microsoft Edge WebView2 Runtime, which comes with Windows 11 " +
                "and most Windows 10 PCs. Install it from Microsoft's WebView2 download page, then start the app again.");
            return;
        }

        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LmuCareer", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
        await Web.EnsureCoreWebView2Async(environment);

        var core = Web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        // SmartScreen would send each page's address to Microsoft for a reputation check. The pages
        // all come out of the exe, and links anywhere else open in the player's own browser.
        core.Settings.IsReputationCheckingRequired = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif

        core.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ServeEmbeddedFile;
        core.WebMessageReceived += (_, e) => core.PostWebMessageAsJson(_api.Handle(e.WebMessageAsJson));

        // Anything that isn't the app's own pages opens in the player's browser instead.
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };

        core.Navigate(StartPage);
    }

    private void ServeEmbeddedFile(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        if (path.Length == 0) path = "index.html";

        var stream = WebFiles.Open(path);
        var environment = Web.CoreWebView2.Environment;
        e.Response = stream is null
            ? environment.CreateWebResourceResponse(null, 404, "Not Found", "")
            : environment.CreateWebResourceResponse(stream, 200, "OK",
                $"Content-Type: {WebFiles.ContentType(path)}\r\nCache-Control: no-store");
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
    }

    private void ShowStartupMessage(string message)
    {
        Web.Visibility = Visibility.Collapsed;
        StartupMessage.Text = message;
        StartupMessage.Visibility = Visibility.Visible;
    }

    public void Invoke(Action action) => Dispatcher.InvokeAsync(action);

    public void PostEvent(string json)
    {
        if (Web.CoreWebView2 is { } core) core.PostWebMessageAsJson(json);
    }

    /// <summary>Flashes the taskbar button until the window is brought forward, when it isn't already.</summary>
    public void FlashIfInBackground()
    {
        if (IsActive) return;
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = new WindowInteropHelper(this).EnsureHandle(),
            Flags = FlashAll | FlashUntilForeground,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashAll = 3;
    private const uint FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Matches the Windows title bar to the app's theme.</summary>
    public void SetDarkTitleBar(bool dark)
    {
        const int immersiveDarkMode = 20;
        var value = dark ? 1 : 0;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        _ = DwmSetWindowAttribute(handle, immersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string title, string fileName, FileFilter filter)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = fileName, Filter = $"{filter.Name} ({filter.Pattern})|{filter.Pattern}" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string title, FileFilter filter, string allFiles)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = $"{filter.Name} ({filter.Pattern})|{filter.Pattern}|{allFiles}|*.*" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
