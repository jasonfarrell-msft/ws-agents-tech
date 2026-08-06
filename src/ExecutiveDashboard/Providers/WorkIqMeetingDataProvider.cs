using ExecutiveDashboard.Models;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Providers;

public sealed class WorkIqMeetingDataProvider(
    IWorkIqMeetingDataClient workIqClient,
    IOptions<WorkIqOptions> options,
    IOptions<WorkIqCliOptions> cliOptions,
    TimeProvider timeProvider,
    ILogger<WorkIqMeetingDataProvider> logger) : ILiveMeetingDataProvider
{
    public WorkIqMeetingDataProvider(
        IWorkIqMeetingDataClient workIqClient,
        IOptions<WorkIqOptions> options,
        TimeProvider timeProvider,
        ILogger<WorkIqMeetingDataProvider> logger)
        : this(workIqClient, options, Options.Create(new WorkIqCliOptions()), timeProvider, logger)
    {
    }

    private const string SourceName = "Work IQ";

    public async Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        var workIqConfiguration = options.Value;
        var cliConfiguration = cliOptions.Value;
        var cliModeActive = workIqConfiguration.WantsCliMode
            || (workIqConfiguration.Mode == WorkIqProviderMode.Auto && cliConfiguration.Enabled);

        if (!cliModeActive && !workIqConfiguration.CanAttemptLiveQuery)
        {
            logger.LogWarning("WorkIQ live mode was selected without usable WorkIQ configuration.");
            return MeetingDataSet.Unavailable(
                query,
                SourceName,
                timeProvider.GetUtcNow(),
                "Live mode is selected, but Work IQ is missing the approved CLI or delegated sign-in configuration.");
        }

        var result = await workIqClient.GetMeetingDataAsync(query, cancellationToken);

        return result.Status switch
        {
            WorkIqMeetingDataResultStatus.Available when result.Response is not null => CreateDataSet(query, result.Response),
            WorkIqMeetingDataResultStatus.AuthorizationFailed => MeetingDataSet.Unavailable(query, SourceName, timeProvider.GetUtcNow(), result.Message),
            WorkIqMeetingDataResultStatus.Unavailable => MeetingDataSet.Unavailable(query, SourceName, timeProvider.GetUtcNow(), result.Message),
            WorkIqMeetingDataResultStatus.Malformed => CreateUnknownDataSet(result.Message),
            _ => CreateUnknownDataSet("Work IQ returned an unknown provider state.")
        };
    }

    private MeetingDataSet CreateDataSet(MeetingQuery query, WorkIqMeetingJsonResponse response)
    {
        var availability = ToAvailabilityState(response.Availability.Meetings);
        if (availability == AvailabilityState.Unavailable)
        {
            return new MeetingDataSet(
                Array.Empty<Meeting>(),
                AvailabilityState.Unavailable,
                AvailabilityState.Unavailable,
                AvailabilityState.Unavailable,
                AvailabilityState.Unavailable,
                AvailabilityState.Unavailable,
                SourceName,
                timeProvider.GetUtcNow(),
                response.Message ?? "Work IQ reported meeting data as unavailable.");
        }

        var meetingsInWindow = response.Meetings
            .Where(meeting => meeting.StartsAtUtc >= query.StartsAtUtc && meeting.EndsAtUtc <= query.EndsAtUtc)
            .ToArray();
        var meetings = meetingsInWindow.Select(meeting => MapMeeting(query.UserId, meeting)).ToArray();
        return new MeetingDataSet(
            meetings,
            availability,
            CoerceAvailability(response.Availability.TalkTime, meetingsInWindow, meeting => meeting.UserTalkTimeSeconds.HasValue),
            CoerceAvailability(response.Availability.Decisions, meetingsInWindow, meeting => !string.IsNullOrWhiteSpace(meeting.DecisionOutcome) && !string.Equals(meeting.DecisionOutcome, "unknown", StringComparison.OrdinalIgnoreCase)),
            CoerceAvailability(response.Availability.Attendees, meetingsInWindow, meeting => meeting.AttendeeCount.HasValue),
            CoerceAvailability(response.Availability.EmailReplies, meetingsInWindow, meeting => meeting.EmailReplyCount.HasValue),
            SourceName,
            timeProvider.GetUtcNow(),
            response.Message);
    }

    private MeetingDataSet CreateUnknownDataSet(string? message) =>
        new(
            Array.Empty<Meeting>(),
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            SourceName,
            timeProvider.GetUtcNow(),
            message ?? "Work IQ availability is unknown.");

    private static Meeting MapMeeting(string userId, WorkIqMeetingJson meeting)
    {
        var participants = new List<MeetingParticipant>();
        if (meeting.UserTalkTimeSeconds.HasValue)
        {
            participants.Add(new MeetingParticipant(userId, "Selected user", TimeSpan.FromSeconds(meeting.UserTalkTimeSeconds.Value)));
        }

        if (meeting.AttendeeCount.HasValue)
        {
            for (var attendeeIndex = participants.Count + 1; attendeeIndex <= meeting.AttendeeCount.Value; attendeeIndex++)
            {
                participants.Add(new MeetingParticipant($"workiq-attendee-{attendeeIndex}", $"Attendee {attendeeIndex}"));
            }
        }

        return new Meeting(
            meeting.Id,
            meeting.Title,
            meeting.StartsAtUtc,
            meeting.EndsAtUtc,
            participants,
            new MeetingDecision(ToDecisionOutcome(meeting.DecisionOutcome), meeting.DecisionSummary),
            meeting.EmailReplyCount.HasValue ? new MeetingEmailThread(meeting.EmailReplyCount.Value) : null);
    }

    private static AvailabilityState ToAvailabilityState(string value) => value switch
    {
        "available" => AvailabilityState.Available,
        "unavailable" => AvailabilityState.Unavailable,
        _ => AvailabilityState.Unknown
    };

    private static AvailabilityState CoerceAvailability(
        string value,
        IReadOnlyCollection<WorkIqMeetingJson> meetings,
        Func<WorkIqMeetingJson, bool> hasBackingValue)
    {
        var availability = ToAvailabilityState(value);
        if (availability != AvailabilityState.Available || meetings.Count == 0)
        {
            return availability;
        }

        return meetings.All(hasBackingValue)
            ? AvailabilityState.Available
            : AvailabilityState.Unknown;
    }

    private static MeetingDecisionOutcome ToDecisionOutcome(string? value) => value switch
    {
        "reached" => MeetingDecisionOutcome.Reached,
        "noneReached" => MeetingDecisionOutcome.NoneReached,
        "notApplicable" => MeetingDecisionOutcome.NotApplicable,
        _ => MeetingDecisionOutcome.Unknown
    };
}
