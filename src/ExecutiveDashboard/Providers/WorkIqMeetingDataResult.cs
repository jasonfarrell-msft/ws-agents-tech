namespace ExecutiveDashboard.Providers;

public enum WorkIqMeetingDataResultStatus
{
    Available = 0,
    Unavailable = 1,
    AuthorizationFailed = 2,
    Malformed = 3
}

public sealed record WorkIqMeetingDataResult(
    WorkIqMeetingDataResultStatus Status,
    WorkIqMeetingJsonResponse? Response = null,
    string? Message = null)
{
    public static WorkIqMeetingDataResult Available(WorkIqMeetingJsonResponse response) => new(WorkIqMeetingDataResultStatus.Available, response);

    public static WorkIqMeetingDataResult Unavailable(string message) => new(WorkIqMeetingDataResultStatus.Unavailable, Message: message);

    public static WorkIqMeetingDataResult AuthorizationFailed(string message) => new(WorkIqMeetingDataResultStatus.AuthorizationFailed, Message: message);

    public static WorkIqMeetingDataResult Malformed(string message) => new(WorkIqMeetingDataResultStatus.Malformed, Message: message);
}

public sealed record WorkIqMeetingJsonResponse(
    IReadOnlyList<WorkIqMeetingJson> Meetings,
    WorkIqFieldAvailability Availability,
    string? Message);

public sealed record WorkIqFieldAvailability(
    string Meetings,
    string TalkTime,
    string Decisions,
    string Attendees,
    string EmailReplies);

public sealed record WorkIqMeetingJson(
    string Id,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int? AttendeeCount,
    int? UserTalkTimeSeconds,
    string? DecisionOutcome,
    string? DecisionSummary,
    int? EmailReplyCount);
