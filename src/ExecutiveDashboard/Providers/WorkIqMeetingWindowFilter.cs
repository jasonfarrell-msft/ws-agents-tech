using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

internal static class WorkIqMeetingWindowFilter
{
    public static WorkIqMeetingJson[] FilterToRequestedWindow(
        MeetingQuery query,
        IReadOnlyList<WorkIqMeetingJson> meetings) =>
        meetings
            .Where(meeting => IsWithinRequestedWindow(query, meeting.StartsAtUtc))
            .ToArray();

    public static bool IsWithinRequestedWindow(MeetingQuery query, DateTimeOffset startsAtUtc) =>
        startsAtUtc >= query.StartsAtUtc && startsAtUtc < query.EndsAtUtc;
}
