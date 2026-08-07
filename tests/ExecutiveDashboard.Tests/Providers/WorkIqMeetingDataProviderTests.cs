using System.Net;
using System.Text;
using System.Text.Json;
using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class WorkIqMeetingDataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 19, 0, 0, TimeSpan.Zero);
    private static readonly MeetingQuery Query = new(Now.AddDays(-7), Now, "user-1");

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenWorkIqConfigurationIsMissing()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.Malformed("Should not be called.")),
            Options.Create(new WorkIqOptions { Enabled = true, Provider = WorkIqOptions.DirectProvider, Endpoint = "", Scopes = [] }),
            Options.Create(new WorkIqCliOptions()),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ", dataSet.SourceName);
    }


    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenLiveProviderIsInvokedWhileModeIsSample()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.Malformed("Should not be called.")),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Sample }),
            Options.Create(new WorkIqCliOptions { Enabled = false }),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ", dataSet.SourceName);
    }

    [Fact]
    public async Task GetMeetingsAsync_AttemptsCliLiveQuery_WhenCliModeIsEnabledWithoutEntra()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.Malformed("cli attempted")),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Cli }),
            Options.Create(new WorkIqCliOptions { Enabled = true }),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("cli attempted", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenCliModeFailsWithoutLegacyEnabledFlag()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.AuthorizationFailed("Work IQ CLI authentication failed.")),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Cli }),
            Options.Create(new WorkIqCliOptions { Enabled = false }),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ CLI authentication failed.", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenDelegatedAuthorizationFails()
    {
        var provider = CreateProvider(WorkIqMeetingDataResult.AuthorizationFailed("Work IQ delegated token acquisition failed."));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ", dataSet.SourceName);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenCliAuthorizationFails()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.AuthorizationFailed("Work IQ CLI authentication failed.")),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Cli }),
            Options.Create(new WorkIqCliOptions { Enabled = true }),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ CLI authentication failed.", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenAutoModeSelectsCliAndAuthorizationFails()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.AuthorizationFailed("Work IQ CLI authentication failed.")),
            Options.Create(new WorkIqOptions { Mode = WorkIqProviderMode.Auto }),
            Options.Create(new WorkIqCliOptions { Enabled = true }),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ CLI authentication failed.", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenWorkIqResponseIsMalformed()
    {
        var provider = CreateProvider(WorkIqMeetingDataResult.Malformed("Work IQ returned non-JSON text."));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.TalkTimeAvailability);
        Assert.Equal("Work IQ returned non-JSON text.", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenWorkIqReportsMeetingDataUnavailable()
    {
        var response = new WorkIqMeetingJsonResponse(
            [],
            new WorkIqFieldAvailability("unavailable", "unavailable", "unavailable", "unavailable", "unavailable"),
            "Work IQ could not access meetings for the signed-in user.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.DecisionAvailability);
        Assert.Equal("Work IQ could not access meetings for the signed-in user.", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_PreservesUnknownAvailabilityForUnsupportedFields()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Executive review",
                    Now.AddHours(-2),
                    Now.AddHours(-1),
                    AttendeeCount: null,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null)
            ],
            new WorkIqFieldAvailability("available", "unknown", "unknown", "unknown", "unavailable"),
            "Only calendar timing was validated by Work IQ.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.AttendeeAvailability);
        Assert.Single(dataSet.Meetings);
        Assert.NotNull(dataSet.Message);
        Assert.DoesNotContain("email replies", dataSet.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("speaker diarization", dataSet.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decision analysis", dataSet.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMeetingsAsync_DoesNotFlagTalkTimeUnknown_WhenSpeakerDiarizationCompensates()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Executive review",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: 8,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "reached",
                    DecisionSummary: "Approved.")
            ],
            new WorkIqFieldAvailability(
                "available",
                "unknown",
                "available",
                "available",
                "available",
                Recurrence: "unavailable",
                AttendeeIdentities: "unavailable",
                SpeakerDiarization: "available",
                EmailVolume: "unavailable",
                DecisionAnalysis: "unavailable"),
            null,
            DiarizationSummary: new WorkIqDiarizationSummary(1, 1, 1));
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.SpeakerDiarizationAvailability);
        Assert.Equal(1, dataSet.ConfirmedZeroUserSpeechMeetingCount);
        Assert.Null(dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_FlagsTalkTimeUnknown_WhenInWindowMeetingsLackDiarizationCompensation()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Executive review",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: 8,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "reached",
                    DecisionSummary: "Approved.")
            ],
            new WorkIqFieldAvailability(
                "available",
                "unknown",
                "available",
                "available",
                "available",
                Recurrence: "unavailable",
                AttendeeIdentities: "unavailable",
                SpeakerDiarization: "unknown",
                EmailVolume: "unavailable",
                DecisionAnalysis: "unavailable"),
            null);
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.NotNull(dataSet.Message);
        Assert.Contains("talk time", dataSet.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMeetingsAsync_DowngradesAvailableFieldFlagsWhenMeetingValuesAreMissing()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Executive review",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: null,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: null,
                    DecisionSummary: null)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Work IQ returned partial meeting facts.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.AttendeeAvailability);
    }

    [Fact]
    public async Task GetMeetingsAsync_DowngradesDecisionAvailability_WhenDecisionOutcomeRemainsUnknown()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Executive review",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: 8,
                    UserTalkTimeSeconds: 300,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Decision outcome remains unknown.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.AttendeeAvailability);
    }

    [Fact]
    public async Task GetMeetingsAsync_IgnoresMeetingsOutsideRequestedWindow()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "outside-window",
                    "Last month",
                    Query.StartsAtUtc.AddDays(-10),
                    Query.StartsAtUtc.AddDays(-10).AddHours(1),
                    AttendeeCount: 4,
                    UserTalkTimeSeconds: 120,
                    DecisionOutcome: "reached",
                    DecisionSummary: "done"),
                new WorkIqMeetingJson(
                    "inside-window",
                    "Current week",
                    Query.StartsAtUtc.AddHours(3),
                    Query.StartsAtUtc.AddHours(4),
                    AttendeeCount: 6,
                    UserTalkTimeSeconds: 180,
                    DecisionOutcome: "noneReached",
                    DecisionSummary: "pending"),
                new WorkIqMeetingJson(
                    "started-inside-window",
                    "In progress",
                    Query.EndsAtUtc.AddMinutes(-15),
                    Query.EndsAtUtc.AddMinutes(15),
                    AttendeeCount: 3,
                    UserTalkTimeSeconds: 60,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null),
                new WorkIqMeetingJson(
                    "next-window-boundary",
                    "Next week",
                    Query.EndsAtUtc,
                    Query.EndsAtUtc.AddHours(1),
                    AttendeeCount: 3,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Validated current window.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(2, dataSet.Meetings.Count);
        Assert.Contains(dataSet.Meetings, meeting => meeting.Id == "inside-window");
        Assert.Contains(dataSet.Meetings, meeting => meeting.Id == "started-inside-window");
        Assert.DoesNotContain(dataSet.Meetings, meeting => meeting.Id == "next-window-boundary");
    }

    [Fact]
    public async Task GetMeetingsAsync_MapsRecurrenceAndPrivacySafeAttendeeIdentitySignals()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Work IQ meeting",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: 2,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null,
                    IsRecurring: true,
                    AttendeeKeys: ["person@example.com"])
            ],
            new WorkIqFieldAvailability(
                "available",
                "unavailable",
                "unavailable",
                "available",
                "unavailable",
                "available",
                "available"),
            null);
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.RecurrenceAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.AttendeeIdentityAvailability);
        Assert.True(dataSet.Meetings[0].IsRecurring);
        Assert.DoesNotContain(dataSet.Meetings[0].Participants, participant => participant.Id.Contains('@'));
    }

    [Fact]
    public async Task GetMeetingsAsync_MarksAttendeeOverlapUnknown_WhenIdentityCoverageIsPartial()
    {
        var response = new WorkIqMeetingJsonResponse(
            [
                new WorkIqMeetingJson(
                    "m1",
                    "Work IQ meeting",
                    Query.StartsAtUtc.AddHours(1),
                    Query.StartsAtUtc.AddHours(2),
                    AttendeeCount: null,
                    UserTalkTimeSeconds: null,
                    DecisionOutcome: "unknown",
                    DecisionSummary: null,
                    IsRecurring: false,
                    AttendeeKeys: ["only-one-identity"])
            ],
            new WorkIqFieldAvailability(
                "available",
                "unavailable",
                "unavailable",
                "available",
                "unavailable",
                "available",
                "available"),
            null);
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.AttendeeIdentityAvailability);
    }

    [Fact]
    public async Task GetMeetingsAsync_MapsReceivedEmailVolumeAndCalendarDayCount()
    {
        var response = new WorkIqMeetingJsonResponse(
            [],
            new WorkIqFieldAvailability(
                "available",
                "unavailable",
                "unavailable",
                "unavailable",
                "unavailable",
                EmailVolume: "available"),
            null,
            EmailVolumeSummary: new WorkIqEmailVolumeSummary(124));
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.EmailVolumeAvailability);
        Assert.Equal(124, dataSet.EmailsReceivedCount);
        Assert.Equal(7, dataSet.EmailCalendarDayCount);
    }

    [Fact]
    public async Task GetMeetingsAsync_DoesNotPromoteMeetingAnalysisUnknowns_WhenNoMeetingsWereFound()
    {
        var response = new WorkIqMeetingJsonResponse(
            [],
            new WorkIqFieldAvailability(
                "available",
                "unknown",
                "unknown",
                "unknown",
                "unknown",
                Recurrence: "unknown",
                SpeakerDiarization: "unknown",
                EmailVolume: "available",
                DecisionAnalysis: "unknown"),
            null,
            EmailVolumeSummary: new WorkIqEmailVolumeSummary(20));
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.SpeakerDiarizationAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAnalysisAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.EmailVolumeAvailability);
        Assert.Equal(20, dataSet.EmailsReceivedCount);
        Assert.Null(dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_MapsDecisionAnalysisSummary()
    {
        var response = new WorkIqMeetingJsonResponse(
            [],
            new WorkIqFieldAvailability(
                "available",
                "unavailable",
                "unavailable",
                "unavailable",
                "unavailable",
                DecisionAnalysis: "available"),
            null,
            DecisionAnalysisSummary: new WorkIqDecisionAnalysisSummary(23, 6, 2, 1));
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.DecisionAnalysisAvailability);
        Assert.Equal(5, dataSet.DecisionRelevantMeetingCount);
        Assert.Equal(2, dataSet.NoDecisionReachedMeetingCount);
    }

    [Fact]
    public async Task GetMeetingsAsync_MapsEmailConversationSummary()
    {
        var response = new WorkIqMeetingJsonResponse(
            [],
            new WorkIqFieldAvailability(
                "available",
                "unavailable",
                "unavailable",
                "unavailable",
                "unavailable",
                EmailConversationAnalysis: "available"),
            null,
            EmailConversationSummary: new WorkIqEmailConversationSummary(40, 6));
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.EmailConversationAnalysisAvailability);
        Assert.Equal(40, dataSet.EmailConversationCount);
        Assert.Equal(6, dataSet.ProtractedEmailConversationCount);
    }

    [Fact]
    public void ParseStrictJson_RejectsEmailConversationCountAboveDenominator()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailConversationAnalysis":"available"},"emailConversationSummary":{"conversationsAnalyzed":4,"conversationsWithMoreThanTenReplies":5}}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Malformed, result.Status);
        Assert.Equal("Work IQ JSON included invalid email conversation summary counts.", result.Message);
    }

    [Fact]
    public void ParseStrictJson_RejectsMarkdownWrappedJson()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            ```json
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unknown"},"message":null}
            ```
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Malformed, result.Status);
        Assert.Contains("non-JSON text", result.Message);
    }

    [Fact]
    public void ParseStrictJson_AcceptsExactMinimalBaseResponse()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","isRecurring":false}],"availability":{"meetings":"available","recurrence":"available"}}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Meetings);
        Assert.Equal("unavailable", result.Response.Availability.TalkTime);
        Assert.Equal("unavailable", result.Response.Availability.Decisions);
        Assert.Equal("unavailable", result.Response.Availability.Attendees);
        Assert.Equal("unavailable", result.Response.Availability.EmailReplies);
        Assert.Equal("available", result.Response.Availability.Recurrence);
        Assert.Null(result.Response.Meetings[0].AttendeeCount);
        Assert.Null(result.Response.Meetings[0].UserTalkTimeSeconds);
        Assert.Equal("unknown", result.Response.Meetings[0].DecisionOutcome);
        Assert.False(result.Response.Meetings[0].IsRecurring);
    }

    [Fact]
    public void ParseStrictJson_AcceptsValidatedJsonAndUnknownOptionalFields()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","userTalkTimeSeconds":null,"decisionOutcome":"unknown","isRecurring":false}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unavailable"},"message":"Validated timing only."}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Meetings);
        Assert.Equal("unknown", result.Response.Availability.TalkTime);
    }

    [Fact]
    public void ParseStrictJson_AcceptsNumericValuesReturnedAsStrings()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":"4","userTalkTimeSeconds":"120","decisionOutcome":"reached"}],"availability":{"meetings":"available","talkTime":"available","decisions":"available","attendees":"available","emailReplies":"available"},"message":null}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(4, result.Response!.Meetings[0].AttendeeCount);
        Assert.Equal(120, result.Response.Meetings[0].UserTalkTimeSeconds);
    }

    [Fact]
    public void ParseStrictJson_IgnoresUnexpectedPerMeetingEmailReplyCount()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"id":"m1","title":"Executive review","startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":120,"decisionOutcome":"reached","emailReplyCount":2}],"availability":{"meetings":"available","talkTime":"available","decisions":"available","attendees":"available","emailReplies":"available"},"message":null}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("m1", result.Response!.Meetings[0].Id);
        Assert.Equal("Executive review", result.Response.Meetings[0].Title);
        Assert.Equal(4, result.Response.Meetings[0].AttendeeCount);
        Assert.Equal(120, result.Response.Meetings[0].UserTalkTimeSeconds);
    }

    [Fact]
    public void ParseStrictJson_AcceptsStructuredMeetingMetadata()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"id":{"value":"m1"},"title":{"text":"Executive review"},"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":120,"decisionOutcome":"reached","decisionSummary":null}],"availability":{"meetings":"available","talkTime":"available","decisions":"available","attendees":"available","emailReplies":"available"},"message":null}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("m1", result.Response!.Meetings[0].Id);
        Assert.Equal("Executive review", result.Response.Meetings[0].Title);
    }

    [Fact]
    public void ParseStrictJson_AcceptsMetricsOnlyMeetingRows()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-03T00:00:00Z","endsAtUtc":"2026-08-04T00:00:00Z","attendeeCount":0,"userTalkTimeSeconds":null,"decisionOutcome":"unknown"},{"startsAtUtc":"2026-08-03T08:30:00Z","endsAtUtc":"2026-08-03T10:30:00Z","attendeeCount":15,"userTalkTimeSeconds":null,"decisionOutcome":"unknown"}],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"available","emailReplies":"unavailable"},"message":null}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(2, result.Response!.Meetings.Count);
        Assert.Equal("Work IQ meeting", result.Response.Meetings[0].Title);
        Assert.Equal("Work IQ meeting", result.Response.Meetings[1].Title);
    }

    [Fact]
    public void ParseStrictJson_AcceptsCollaborationSignals()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-03T13:00:00Z","endsAtUtc":"2026-08-03T13:30:00Z","attendeeCount":3,"userTalkTimeSeconds":null,"decisionOutcome":"unknown","isRecurring":true,"attendeeKeys":["opaque-a","opaque-b"],"hasTranscript":true,"userSpeakingSegmentCount":0}],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"available","emailReplies":"unavailable","recurrence":"available","attendeeIdentities":"available","speakerDiarization":"available"}}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.True(result.Response!.Meetings[0].IsRecurring);
        Assert.Equal(["opaque-a", "opaque-b"], result.Response.Meetings[0].AttendeeKeys);
        Assert.Equal("available", result.Response.Availability.Recurrence);
        Assert.Equal("available", result.Response.Availability.AttendeeIdentities);
        Assert.True(result.Response.Meetings[0].HasTranscript);
        Assert.Equal(0, result.Response.Meetings[0].UserSpeakingSegmentCount);
        Assert.Equal("available", result.Response.Availability.SpeakerDiarization);
    }

    [Fact]
    public void ParseStrictJson_UnwrapsResponseEnvelope()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"response":"{\"meetings\":[],\"availability\":{\"meetings\":\"available\",\"recurrence\":\"unknown\"}}","conversationId":"conversation-1"}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Empty(result.Response.Meetings);
    }

    [Fact]
    public void ParseStrictJson_PreservesStructuredDiagnosticMessage()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable"},"message":{"detail":"consent required"}}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("consent required", result.Response!.Message);
    }

    [Fact]
    public void ParseStrictJson_RejectsNonUtcMeetingTimestamps()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00-04:00","endsAtUtc":"2026-08-05T17:30:00-04:00","userTalkTimeSeconds":null,"decisionOutcome":"unknown","isRecurring":false}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unavailable"},"message":null}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Malformed, result.Status);
    }

    [Fact]
    public void WorkIqOptions_RejectsNonHttpsEndpoint()
    {
        var options = CreateOptions();
        options.Provider = WorkIqOptions.DirectProvider;
        options.Endpoint = "http://workiq.svc.cloud.microsoft/rest";

        Assert.False(options.HasUsableConfiguration);
    }

    [Fact]
    public async Task WorkIqChatClient_UsesRichDirectPromptAndParsesNativeMeetingMetrics()
    {
        var handler = new RecordingDirectConversationHandler("""
            {"messages":[{"text":"{\"meetings\":[{\"startsAtUtc\":\"2026-08-05T17:00:00Z\",\"endsAtUtc\":\"2026-08-05T17:30:00Z\",\"attendeeCount\":6,\"userTalkTimeSeconds\":180,\"decisionOutcome\":\"noneReached\",\"isRecurring\":true}],\"availability\":{\"meetings\":\"available\",\"talkTime\":\"available\",\"decisions\":\"available\",\"attendees\":\"available\",\"emailReplies\":\"unavailable\",\"recurrence\":\"available\",\"speakerDiarization\":\"unavailable\",\"emailVolume\":\"unavailable\",\"decisionAnalysis\":\"unavailable\",\"emailConversationAnalysis\":\"unavailable\"}}"}]}
            """);
        using var httpClient = new HttpClient(handler);
        var client = new WorkIqChatClient(
            httpClient,
            new StubAccessTokenProvider("delegated-token"),
            Options.Create(CreateOptions()));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response!.Meetings);
        Assert.Equal(6, result.Response.Meetings[0].AttendeeCount);
        Assert.Equal(180, result.Response.Meetings[0].UserTalkTimeSeconds);
        Assert.Equal("noneReached", result.Response.Meetings[0].DecisionOutcome);
        Assert.True(result.Response.Meetings[0].IsRecurring);
        Assert.Equal("available", result.Response.Availability.TalkTime);
        Assert.Equal("available", result.Response.Availability.Decisions);
        Assert.Equal("available", result.Response.Availability.Attendees);
        Assert.Equal("available", result.Response.Availability.Recurrence);
        Assert.Equal("unavailable", result.Response.Availability.EmailVolume);
        Assert.Equal("unavailable", result.Response.Availability.EmailConversationAnalysis);
        Assert.NotNull(handler.ChatPrompt);
        Assert.Contains("\"attendeeCount\":8", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.Contains("\"userTalkTimeSeconds\":240", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.Contains("\"decisionOutcome\":\"reached\"", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.Contains("\"speakerDiarization\":\"unavailable\"", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.Contains("\"emailVolume\":\"unavailable\"", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.Contains("\"emailConversationAnalysis\":\"unavailable\"", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("emailsReceived", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("conversationsWithMoreThanTenReplies", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("attendeeKeys", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("decisionSummary", handler.ChatPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Query.UserId, handler.ChatPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkIqChatClient_ReturnsAuthorizationFailed_WhenRestEndpointRejectsToken()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.Forbidden, "{}"));
        var client = new WorkIqChatClient(
            httpClient,
            new StubAccessTokenProvider("delegated-token"),
            Options.Create(CreateOptions()));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.AuthorizationFailed, result.Status);
        Assert.Contains("authorization failed", result.Message);
    }

    [Fact]
    public async Task WorkIqChatClient_ReturnsUnavailable_WhenTransportThrowsHttpRequestException()
    {
        using var httpClient = new HttpClient(new ThrowingHandler(new HttpRequestException("network unavailable")));
        var client = new WorkIqChatClient(
            httpClient,
            new StubAccessTokenProvider("delegated-token"),
            Options.Create(CreateOptions()));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("Work IQ request failed before a valid response was received.", result.Message);
    }

    [Fact]
    public async Task WorkIqChatClient_ReturnsUnavailable_WhenTransportTimesOut()
    {
        using var httpClient = new HttpClient(new ThrowingHandler(new TaskCanceledException("The request timed out.")));
        var client = new WorkIqChatClient(
            httpClient,
            new StubAccessTokenProvider("delegated-token"),
            Options.Create(CreateOptions()));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("Work IQ request timed out before a valid response was received.", result.Message);
    }

    [Fact]
    public async Task WorkIqChatClient_PreservesCallerCancellation_WhenTransportIsCanceled()
    {
        using var httpClient = new HttpClient(new CancellableHandler());
        var client = new WorkIqChatClient(
            httpClient,
            new StubAccessTokenProvider("delegated-token"),
            Options.Create(CreateOptions()));
        using var cancellationTokenSource = new CancellationTokenSource();

        var task = client.GetMeetingDataAsync(Query, cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static WorkIqMeetingDataProvider CreateProvider(WorkIqMeetingDataResult result) =>
        new(
            new StubWorkIqChatClient(result),
            Options.Create(CreateOptions()),
            Options.Create(new WorkIqCliOptions()),
            new FixedTimeProvider(Now),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

    private static WorkIqOptions CreateOptions() =>
        new()
        {
            Enabled = true,
            Provider = WorkIqOptions.DirectProvider,
            Endpoint = "https://workiq.svc.cloud.microsoft/rest",
            Scopes = ["api://workiq-resource/WorkIQAgent.Ask"],
            TimeZone = "America/New_York"
        };

    private sealed class StubWorkIqChatClient(WorkIqMeetingDataResult result) : IWorkIqChatClient
    {
        public Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubAccessTokenProvider(string token) : IWorkIqAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }

    private sealed class RecordingDirectConversationHandler(string chatResponseContent) : HttpMessageHandler
    {
        public string? ChatPrompt { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var absolutePath = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (absolutePath.EndsWith("/conversations", StringComparison.Ordinal))
            {
                return CreateJsonResponse(HttpStatusCode.OK, """{"id":"conversation-1"}""");
            }

            if (absolutePath.EndsWith("/conversations/conversation-1/chat", StringComparison.Ordinal))
            {
                var requestContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(requestContent);
                ChatPrompt = document.RootElement.GetProperty("message").GetProperty("text").GetString();
                return CreateJsonResponse(HttpStatusCode.OK, chatResponseContent);
            }

            return CreateJsonResponse(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string content) =>
            new(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellableHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
