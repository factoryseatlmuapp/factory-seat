using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LmuCareer.Host;

namespace LmuCareer.App;

/// <summary>
/// Catches anything that would otherwise close the app without a word (a WebView2 that won't start,
/// an antivirus blocking a file, a bug), writes it to crash.log and tells the player where it is.
/// </summary>
public partial class App : Application
{
    private static int _reported;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Report(ex);
        };
        // A failed background task only gets logged; it doesn't close the app.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write(args.Exception, fatal: false);
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception);
        Shutdown(1);
    }

    /// <summary>Logs a fatal error and shows the player one message about it, however many follow.</summary>
    private static void Report(Exception ex)
    {
        var log = Write(ex, fatal: true);
        if (Interlocked.Exchange(ref _reported, 1) == 1) return;

        var where = log is null
            ? "The details couldn't be saved."
            : $"The details were saved to:\n{log}";
        try
        {
            MessageBox.Show(
                $"Factory Seat hit an error and has to close.\n\n{ex.Message}\n\n{where}\n\n" +
                "Please send that file with your report on GitHub, Discord or OverTake.",
                "Factory Seat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // No message box possible (e.g. the error happened while closing); the log is still there.
        }
    }

    /// <summary>Appends the error to %LocalAppData%\LmuCareer\crash.log; returns its path, or null if that failed.</summary>
    internal static string? Write(Exception ex, bool fatal)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LmuCareer");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "crash.log");

            // Keep the file small: start over once it passes 1 MB.
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);

            string version;
            try { version = Updates.CurrentLabel; } catch { version = "?"; }

            File.AppendAllText(path,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Factory Seat {version} · {Environment.OSVersion} · " +
                $"{(fatal ? "fatal" : "background")} ===\n{ex}\n\n");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
