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
    string? Message,
    WorkIqDiarizationSummary? DiarizationSummary = null,
    WorkIqEmailVolumeSummary? EmailVolumeSummary = null,
    WorkIqDecisionAnalysisSummary? DecisionAnalysisSummary = null,
    WorkIqEmailConversationSummary? EmailConversationSummary = null);

public sealed record WorkIqDiarizationSummary(
    int MeetingsAnalyzed,
    int MeetingsWithDiarization,
    int MeetingsWithZeroUserSegments);

public sealed record WorkIqEmailVolumeSummary(int EmailsReceived);

public sealed record WorkIqDecisionAnalysisSummary(
    int MeetingsAnalyzed,
    int MeetingsWithContent,
    int MeetingsWithNoDecisionReached,
    int MeetingsNotApplicable);

public sealed record WorkIqEmailConversationSummary(
    int ConversationsAnalyzed,
    int ConversationsWithMoreThanTenReplies);

public sealed record WorkIqFieldAvailability(
    string Meetings,
    string TalkTime,
    string Decisions,
    string Attendees,
    string EmailReplies,
    string Recurrence = "unknown",
    string AttendeeIdentities = "unknown",
    string SpeakerDiarization = "unknown",
    string EmailVolume = "unknown",
    string DecisionAnalysis = "unknown",
    string EmailConversationAnalysis = "unavailable");

public sealed record WorkIqMeetingJson(
    string Id,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int? AttendeeCount,
    int? UserTalkTimeSeconds,
    string? DecisionOutcome,
    string? DecisionSummary,
    bool? IsRecurring = null,
    IReadOnlyList<string>? AttendeeKeys = null,
    bool? HasTranscript = null,
    int? UserSpeakingSegmentCount = null);
