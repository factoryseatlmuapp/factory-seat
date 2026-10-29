namespace LmuCareer.Host;

/// <summary>
/// What <see cref="ApiHost"/> needs from the window around the page. The Windows app is WPF +
/// WebView2; the Linux one is Photino (WebKitGTK).
/// </summary>
public interface IAppWindow
{
    /// <summary>Runs an action on the window's thread, which is where the page's requests are handled.</summary>
    void Invoke(Action action);

    /// <summary>Sends the page a message it didn't ask for, such as a race arriving in the results folder.</summary>
    void PostEvent(string json);

    /// <summary>Gets the player's attention when the window isn't in front.</summary>
    void FlashIfInBackground();

    /// <summary>Matches the title bar to the app's theme, where the system allows it.</summary>
    void SetDarkTitleBar(bool dark);

    /// <summary>Asks for a folder; null when the player cancels.</summary>
    string? PickFolder(string title);

    /// <summary>Asks where to save a file; null when the player cancels.</summary>
    string? PickSaveFile(string title, string fileName, FileFilter filter);

    /// <summary>Asks for a file to open; null when the player cancels.</summary>
    string? PickOpenFile(string title, FileFilter filter, string allFiles);

    /// <summary>Shows a folder in the system's file manager.</summary>
    void OpenFolder(string path);
}

/// <summary>A file type a dialog offers: "Career save" and "*.career.json".</summary>
public sealed record FileFilter(string Name, string Pattern);
