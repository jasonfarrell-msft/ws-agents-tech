namespace ExecutiveDashboard.Models;

public enum MeetingDecisionOutcome
{
    Unknown = 0,
    Reached = 1,
    NoneReached = 2,
    NotApplicable = 3
}

public sealed record MeetingParticipant(
    string Id,
    string DisplayName,
    TimeSpan? TalkTime = null);

public sealed record MeetingDecision(
    MeetingDecisionOutcome Outcome,
    string? Summary = null);

public sealed record MeetingEmailThread(
    int ReplyCount);

public sealed record Meeting(
    string Id,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    IReadOnlyList<MeetingParticipant> Participants,
    MeetingDecision Decision,
    MeetingEmailThread? EmailThread = null)
{
    public TimeSpan Duration => EndsAtUtc - StartsAtUtc;
}

public sealed record MeetingQuery(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string UserId);

public sealed record MeetingDataSet(
    IReadOnlyList<Meeting> Meetings,
    AvailabilityState Availability,
    AvailabilityState TalkTimeAvailability,
    AvailabilityState DecisionAvailability,
    AvailabilityState AttendeeAvailability,
    AvailabilityState EmailReplyAvailability,
    string SourceName,
    DateTimeOffset RetrievedAtUtc,
    string? Message = null,
    bool IsSampleData = false)
{
    public static MeetingDataSet Unavailable(
        MeetingQuery query,
        string sourceName,
        DateTimeOffset retrievedAtUtc,
        string? message = null) => new(
            Array.Empty<Meeting>(),
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            sourceName,
            retrievedAtUtc,
            message);
}
