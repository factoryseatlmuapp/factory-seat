using System.IO;

namespace LmuCareer.Host;

/// <summary>
/// Watches LMU's results folder and reports, on the UI thread, once new files have stopped
/// changing. LMU writes a results file in several bursts at the end of a session, so each change
/// restarts a short quiet period before <see cref="Settled"/> fires.
/// </summary>
public sealed class ResultsWatcher : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);

    private readonly Action<Action> _invoke;
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;

    /// <param name="invoke">Runs an action on the UI thread.</param>
    public ResultsWatcher(Action<Action> invoke)
    {
        _invoke = invoke;
        _timer = new Timer(_ => _invoke(() => Settled?.Invoke()));
    }

    public event Action? Settled;

    /// <summary>Starts watching a folder, replacing any earlier one. Null stops watching.</summary>
    public void Watch(string? folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (folder is null || !Directory.Exists(folder)) return;

        _watcher = new FileSystemWatcher(folder, "*.xml")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, _) => Restart();
        _watcher.Changed += (_, _) => Restart();
        _watcher.Renamed += (_, _) => Restart();
    }

    private void Restart() => _timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }
}
