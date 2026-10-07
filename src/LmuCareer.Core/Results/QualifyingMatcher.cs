namespace LmuCareer.Core.Results;

public static class QualifyingMatcher
{
    /// <summary>How far before the race a qualifying session can start and still belong to the same weekend.</summary>
    public static readonly TimeSpan WeekendWindow = TimeSpan.FromHours(6);

    /// <summary>
    /// The qualifying session for a race: the latest one at the same course and in the same mode
    /// that started before the race, within <see cref="WeekendWindow"/>. Restarts can leave several
    /// race files per weekend, and they all pair with the same qualifying.
    /// </summary>
    public static SessionResult? Find(SessionResult race, IEnumerable<SessionResult> sessions) =>
        sessions
            .Where(s => s.Kind == SessionKind.Qualifying
                && s.TrackCourse == race.TrackCourse
                && s.Setting == race.Setting
                && s.StartTime <= race.StartTime
                && race.StartTime - s.StartTime <= WeekendWindow)
            .MaxBy(s => s.StartTime);
}
