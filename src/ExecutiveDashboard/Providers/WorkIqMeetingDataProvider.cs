using System.Security.Cryptography;
using System.Text;
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
            WorkIqMeetingDataResultStatus.Malformed => CreateUnavailableDataSet(result.Message),
            _ => CreateUnavailableDataSet("Work IQ returned an unknown provider state.")
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
                SourceName,
                timeProvider.GetUtcNow(),
                response.Message ?? "Work IQ reported meeting data as unavailable.");
        }

        var meetingsInWindow = WorkIqMeetingWindowFilter.FilterToRequestedWindow(query, response.Meetings);
        var meetings = meetingsInWindow.Select(meeting => MapMeeting(query.UserId, meeting)).ToArray();
        var talkTimeAvailability = CoerceAvailability(response.Availability.TalkTime, meetingsInWindow, meeting => meeting.UserTalkTimeSeconds.HasValue);
        var decisionAvailability = CoerceAvailability(response.Availability.Decisions, meetingsInWindow, meeting => !string.IsNullOrWhiteSpace(meeting.DecisionOutcome) && !string.Equals(meeting.DecisionOutcome, "unknown", StringComparison.OrdinalIgnoreCase));
        var attendeeAvailability = CoerceAvailability(response.Availability.Attendees, meetingsInWindow, meeting => meeting.AttendeeCount.HasValue);
        var recurrenceAvailability = CoerceAvailability(response.Availability.Recurrence, meetingsInWindow, meeting => meeting.IsRecurring.HasValue);
        var attendeeIdentityAvailability = CoerceAvailability(
            response.Availability.AttendeeIdentities,
            meetingsInWindow,
            HasCompleteAttendeeIdentityCoverage);
        var speakerDiarizationAvailability = ToAvailabilityState(response.Availability.SpeakerDiarization);
        if (talkTimeAvailability == AvailabilityState.Available)
        {
            speakerDiarizationAvailability = AvailabilityState.Available;
        }
        else if (speakerDiarizationAvailability == AvailabilityState.Available && response.DiarizationSummary is null)
        {
            speakerDiarizationAvailability = AvailabilityState.Unknown;
        }
        var emailVolumeAvailability = ToAvailabilityState(response.Availability.EmailVolume);
        if (emailVolumeAvailability == AvailabilityState.Available && response.EmailVolumeSummary is null)
        {
            emailVolumeAvailability = AvailabilityState.Unknown;
        }
        var decisionAnalysisAvailability = ToAvailabilityState(response.Availability.DecisionAnalysis);
        if (decisionAvailability == AvailabilityState.Available)
        {
            decisionAnalysisAvailability = AvailabilityState.Available;
        }
        else if (decisionAnalysisAvailability == AvailabilityState.Available && response.DecisionAnalysisSummary is null)
        {
            decisionAnalysisAvailability = AvailabilityState.Unknown;
        }
        var emailConversationAnalysisAvailability =
            ToAvailabilityState(response.Availability.EmailConversationAnalysis);
        if (emailConversationAnalysisAvailability == AvailabilityState.Available
            && response.EmailConversationSummary is null)
        {
            emailConversationAnalysisAvailability = AvailabilityState.Unknown;
        }
        return new MeetingDataSet(
            meetings,
            availability,
            talkTimeAvailability,
            decisionAvailability,
            attendeeAvailability,
            SourceName,
            timeProvider.GetUtcNow(),
            BuildAvailabilityMessage(
                response.Message,
                meetingsInWindow.Length,
                availability,
                talkTimeAvailability,
                decisionAvailability,
                attendeeAvailability,
                recurrenceAvailability,
                attendeeIdentityAvailability,
                speakerDiarizationAvailability,
                emailVolumeAvailability,
                decisionAnalysisAvailability,
                emailConversationAnalysisAvailability),
            RecurrenceAvailability: recurrenceAvailability,
            AttendeeIdentityAvailability: attendeeIdentityAvailability,
            SpeakerDiarizationAvailability: speakerDiarizationAvailability,
            DiarizedMeetingCount: response.DiarizationSummary?.MeetingsWithDiarization,
            ConfirmedZeroUserSpeechMeetingCount: response.DiarizationSummary?.MeetingsWithZeroUserSegments,
            EmailVolumeAvailability: emailVolumeAvailability,
            EmailsReceivedCount: response.EmailVolumeSummary?.EmailsReceived,
            EmailCalendarDayCount: CountCalendarDays(query),
            DecisionAnalysisAvailability: decisionAnalysisAvailability,
            DecisionRelevantMeetingCount: response.DecisionAnalysisSummary is { } decisionSummary
                ? decisionSummary.MeetingsWithContent - decisionSummary.MeetingsNotApplicable
                : null,
            NoDecisionReachedMeetingCount: response.DecisionAnalysisSummary?.MeetingsWithNoDecisionReached,
            EmailConversationAnalysisAvailability: emailConversationAnalysisAvailability,
            EmailConversationCount: response.EmailConversationSummary?.ConversationsAnalyzed,
            ProtractedEmailConversationCount: response.EmailConversationSummary?.ConversationsWithMoreThanTenReplies);
    }

    private static string? BuildAvailabilityMessage(
        string? providerMessage,
        int meetingCount,
        AvailabilityState meetings,
        AvailabilityState talkTime,
        AvailabilityState decisions,
        AvailabilityState attendees,
        AvailabilityState recurrence,
        AvailabilityState attendeeIdentities,
        AvailabilityState speakerDiarization,
        AvailabilityState emailVolume,
        AvailabilityState decisionAnalysis,
        AvailabilityState emailConversationAnalysis)
    {
        var hasMeetings = meetingCount > 0;
        var unknownFields = new List<string>();
        if (meetings == AvailabilityState.Unknown) unknownFields.Add("meetings");
        if (hasMeetings && talkTime == AvailabilityState.Unknown && speakerDiarization != AvailabilityState.Available)
        {
            unknownFields.Add("talk time");
        }
        if (hasMeetings && decisions == AvailabilityState.Unknown && decisionAnalysis != AvailabilityState.Available) unknownFields.Add("decisions");
        if (hasMeetings && recurrence == AvailabilityState.Unknown) unknownFields.Add("recurrence");
        if (hasMeetings && speakerDiarization == AvailabilityState.Unknown) unknownFields.Add("speaker diarization");
        if (emailVolume == AvailabilityState.Unknown) unknownFields.Add("received email volume");
        if (emailConversationAnalysis == AvailabilityState.Unknown) unknownFields.Add("email conversation analysis");
        if (hasMeetings && decisionAnalysis == AvailabilityState.Unknown) unknownFields.Add("decision analysis");

        if (unknownFields.Count == 0)
        {
            return providerMessage;
        }

        var detail = $"Work IQ marked these fields unknown: {string.Join(", ", unknownFields)}.";
        return string.IsNullOrWhiteSpace(providerMessage)
            ? detail
            : $"{providerMessage} {detail}";
    }

    private MeetingDataSet CreateUnavailableDataSet(string? message) =>
        new(
            Array.Empty<Meeting>(),
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            SourceName,
            timeProvider.GetUtcNow(),
            message ?? "Work IQ did not return a valid meeting data response.");

    private static Meeting MapMeeting(string userId, WorkIqMeetingJson meeting)
    {
        var participants = new List<MeetingParticipant>();
        if (meeting.UserTalkTimeSeconds.HasValue)
        {
            participants.Add(new MeetingParticipant(userId, "Selected user", TimeSpan.FromSeconds(meeting.UserTalkTimeSeconds.Value)));
        }

        if (meeting.AttendeeKeys is not null)
        {
            participants.AddRange(
                meeting.AttendeeKeys
                    .Where(key => !string.Equals(key, userId, StringComparison.Ordinal))
                    .Select(key => new MeetingParticipant(ToOpaqueAttendeeId(key), "Meeting attendee")));
        }

        if (meeting.AttendeeCount.HasValue)
        {
            for (var attendeeIndex = participants.Count + 1; attendeeIndex <= meeting.AttendeeCount.Value; attendeeIndex++)
            {
                participants.Add(new MeetingParticipant(
                    $"{meeting.Id}-attendee-{attendeeIndex}",
                    $"Attendee {attendeeIndex}"));
            }
        }

        return new Meeting(
            meeting.Id,
            meeting.Title,
            meeting.StartsAtUtc,
            meeting.EndsAtUtc,
            participants,
            new MeetingDecision(ToDecisionOutcome(meeting.DecisionOutcome), meeting.DecisionSummary),
            meeting.IsRecurring,
            meeting.HasTranscript,
            meeting.UserSpeakingSegmentCount);
    }

    private static bool HasCompleteAttendeeIdentityCoverage(WorkIqMeetingJson meeting)
    {
        if (meeting.AttendeeKeys is null)
        {
            return false;
        }

        if (!meeting.AttendeeCount.HasValue)
        {
            return false;
        }

        var expectedOtherAttendees = Math.Max(0, meeting.AttendeeCount.Value - 1);
        return meeting.AttendeeKeys.Distinct(StringComparer.Ordinal).Count() >= expectedOtherAttendees;
    }

    private static string ToOpaqueAttendeeId(string attendeeKey) =>
        $"workiq-attendee-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(attendeeKey)))}";

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

    private static int CountCalendarDays(MeetingQuery query) =>
        Math.Max(1, (int)Math.Ceiling((query.EndsAtUtc - query.StartsAtUtc).TotalDays));
}
