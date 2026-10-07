using LmuCareer.Core.Results;

namespace LmuCareer.Cli;

internal static class Format
{
    public static SessionResult? TryLoad(string path)
    {
        try { return ResultsParser.Load(path); }
        catch (Exception ex) { Console.Error.WriteLine($"skipped {Path.GetFileName(path)}: {ex.Message}"); return null; }
    }

    public static string Status(FinishStatus s) => s switch
    {
        FinishStatus.Finished => "Finished",
        FinishStatus.Dnf => "DNF",
        FinishStatus.Dq => "DQ",
        FinishStatus.None => "Running",
        _ => "?",
    };

    public static string LapTime(double? seconds) =>
        seconds is double s ? $"{(int)(s / 60)}:{s % 60:00.000}" : "-";

    public static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
