using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class WorkIqCliMeetingDataClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 19, 0, 0, TimeSpan.Zero);
    private static readonly MeetingQuery Query = new(Now.AddDays(-7), Now, "sample-executive; rm -rf /");

    [Fact]
    public async Task GetMeetingDataAsync_ConstructsOfficialAskArgumentsWithoutShellInjection()
    {
        var runner = new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","isRecurring":false}],"availability":{"meetings":"available","recurrence":"available"},"message":"Validated timing only."}
            """));
        var client = CreateClient(
            runner,
            new WorkIqOptions { Mode = WorkIqProviderMode.Cli },
            new WorkIqCliOptions
            {
                Enabled = true,
                ExecutablePath = "workiq-cli",
                TimeoutSeconds = 15,
                AdditionalArguments = ["--preview", "alpha; rm -rf /"]
            });

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Meetings);
        Assert.NotNull(runner.LastInvocation);
        Assert.Equal("workiq-cli", runner.LastInvocation!.ExecutablePath);
        Assert.Equal(TimeSpan.FromSeconds(15), runner.LastInvocation.Timeout);
        Assert.Equal("--preview", runner.LastInvocation.Arguments[0]);
        Assert.Equal("alpha; rm -rf /", runner.LastInvocation.Arguments[1]);
        Assert.Equal("ask", runner.LastInvocation.Arguments[2]);
        Assert.Equal("--json", runner.LastInvocation.Arguments[3]);
        Assert.Equal("-q", runner.LastInvocation.Arguments[4]);
        var prompt = runner.LastInvocation.Arguments[5];
        Assert.Contains("Query the signed-in user's Microsoft 365 calendar", prompt);
        Assert.Contains("meetings the user attended", prompt);
        Assert.Contains("Wednesday July 29, 2026", prompt);
        Assert.Contains("Wednesday August 5, 2026 inclusive", prompt);
        Assert.Contains("Exclude all-day events, out-of-office events", prompt);
        Assert.Contains("Return only strict JSON", prompt);
        Assert.Contains(
            "{\"meetings\":[{\"startsAtUtc\":\"2026-08-03T13:00:00Z\",\"endsAtUtc\":\"2026-08-03T13:30:00Z\",\"isRecurring\":true}],\"availability\":{\"meetings\":\"available\",\"recurrence\":\"available\"}}",
            prompt);
        Assert.DoesNotContain("userTalkTimeSeconds", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("decisionOutcome", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("attendeeCount", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("emailReplyCount", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"talkTime\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"decisions\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"attendees\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"emailReplies\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Query.UserId, prompt);
        Assert.Equal(6, runner.LastInvocation.Arguments.Count);
    }

    [Fact]
    public async Task GetMeetingDataAsync_PassesTenantIdAsGlobalArgumentBeforeAsk()
    {
        var runner = new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","recurrence":"unknown"},"message":null}
            """));
        var client = CreateClient(
            runner,
            new WorkIqOptions { Mode = WorkIqProviderMode.Cli, TenantId = "tenant-123" },
            new WorkIqCliOptions
            {
                Enabled = true,
                ExecutablePath = "npx",
                AdditionalArguments = ["-y", "@microsoft/workiq"]
            });

        await client.GetMeetingDataAsync(Query);

        Assert.NotNull(runner.LastInvocation);
        Assert.Equal("npx", runner.LastInvocation!.ExecutablePath);
        Assert.Equal("-y", runner.LastInvocation.Arguments[0]);
        Assert.Equal("@microsoft/workiq", runner.LastInvocation.Arguments[1]);
        Assert.Equal("--tenant-id", runner.LastInvocation.Arguments[2]);
        Assert.Equal("tenant-123", runner.LastInvocation.Arguments[3]);
        Assert.Equal("ask", runner.LastInvocation.Arguments[4]);
        Assert.Equal("--json", runner.LastInvocation.Arguments[5]);
        Assert.Equal("-q", runner.LastInvocation.Arguments[6]);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsMalformed_WhenCliOutputIsNotJson()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.Available("not-json")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Malformed, result.Status);
        Assert.Equal("Work IQ returned non-JSON text; the dashboard only accepts a strict JSON object.", result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_UnwrapsJsonCliEnvelopeBeforeParsingMetrics()
    {
        var runner = new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.Available("""
            {"response":"{\"meetings\":[{\"startsAtUtc\":\"2026-08-05T17:00:00Z\",\"endsAtUtc\":\"2026-08-05T17:30:00Z\",\"isRecurring\":false}],\"availability\":{\"meetings\":\"available\",\"recurrence\":\"available\"}}","conversationId":"conversation-1"}
            """));
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Meetings);
        Assert.False(result.Response.Meetings[0].IsRecurring);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsBaseResponseWithoutSupplementals_WhenMeetingsAreUnavailable()
    {
        const string preservedMessage = "Calendar access is unavailable for this account.";
        var meetingOutput = WorkIqCliExecutionResult.Available($$"""
            {"meetings":[],"availability":{"meetings":"unavailable","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"unknown","emailConversationAnalysis":"unknown"},"message":"{{preservedMessage}}"}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(2, runner.Invocations.Count);
        Assert.NotNull(result.Response);
        Assert.Equal("unavailable", result.Response!.Availability.Meetings);
        Assert.Equal("unknown", result.Response.Availability.EmailVolume);
        Assert.Equal("unknown", result.Response.Availability.EmailConversationAnalysis);
        Assert.Equal(preservedMessage, result.Response.Message);
        Assert.Null(result.Response.EmailVolumeSummary);
        Assert.Null(result.Response.EmailConversationSummary);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsBaseResponseWithoutSupplementals_WhenMeetingsAreUnknown()
    {
        const string preservedMessage = "Work IQ marked these fields unknown: meetings.";
        var meetingOutput = WorkIqCliExecutionResult.Available($$"""
            {"meetings":[],"availability":{"meetings":"unknown","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"unavailable","emailConversationAnalysis":"unavailable"},"message":"{{preservedMessage}}"}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(2, runner.Invocations.Count);
        Assert.NotNull(result.Response);
        Assert.Equal("unknown", result.Response!.Availability.Meetings);
        Assert.Equal("unavailable", result.Response.Availability.EmailVolume);
        Assert.Equal("unavailable", result.Response.Availability.EmailConversationAnalysis);
        Assert.Equal(preservedMessage, result.Response.Message);
        Assert.Null(result.Response.EmailVolumeSummary);
        Assert.Null(result.Response.EmailConversationSummary);
    }

    [Fact]
    public async Task GetMeetingDataAsync_RecoversWhenCalendarRetryBecomesAvailable()
    {
        var unavailableOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"unavailable","recurrence":"unavailable"},"message":"Transient calendar failure."}
            """);
        var availableOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","recurrence":"available"}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20}}
            """);
        var runner = new RecordingWorkIqCliRunner(unavailableOutput, availableOutput, emailOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("available", result.Response!.Availability.Meetings);
        Assert.Equal(20, result.Response.EmailVolumeSummary!.EmailsReceived);
        Assert.Equal(4, runner.Invocations.Count);
        Assert.Equal(runner.Invocations[0].Arguments[^1], runner.Invocations[1].Arguments[^1]);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenCalendarRetryIsCanceled()
    {
        var unavailableOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"unavailable","recurrence":"unavailable"},"message":"Transient calendar failure."}
            """);
        var runner = new RetryCancellationWorkIqCliRunner(unavailableOutput);
        var client = CreateClient(runner);
        using var cancellationTokenSource = new CancellationTokenSource();

        var task = client.GetMeetingDataAsync(Query, cancellationTokenSource.Token);
        await runner.RetryStarted.Task;
        cancellationTokenSource.Cancel();

        var result = await task;

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("Work IQ CLI calendar retry was canceled before a valid response was received.", result.Message);
        Assert.Equal(2, runner.Invocations.Count);
    }

    [Fact]
    public async Task GetMeetingDataAsync_MergesDedicatedDiarizationResponse()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","isRecurring":false}],"availability":{"meetings":"available","recurrence":"available"}}
            """);
        var diarizationOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","speakerDiarization":"available"},"diarizationSummary":{"meetingsAnalyzed":23,"meetingsWithDiarization":6,"meetingsWithZeroUserSegments":4}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":124}}
            """);
        var decisionOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","decisionAnalysis":"available"},"decisionAnalysisSummary":{"meetingsAnalyzed":23,"meetingsWithContent":6,"meetingsWithNoDecisionReached":2,"meetingsNotApplicable":1}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, diarizationOutput, emailOutput, decisionOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(6, result.Response!.DiarizationSummary!.MeetingsWithDiarization);
        Assert.Equal(4, result.Response.DiarizationSummary.MeetingsWithZeroUserSegments);
        Assert.Equal(5, runner.Invocations.Count);
        Assert.Contains("speaker diarization", runner.Invocations[1].Arguments[^1]);
        Assert.Contains("minimum confirmed proxy", runner.Invocations[1].Arguments[^1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not identify every meeting below 10% speaking time", runner.Invocations[1].Arguments[^1], StringComparison.Ordinal);
        Assert.Equal(124, result.Response.EmailVolumeSummary!.EmailsReceived);
        Assert.Equal(20, result.Response.EmailConversationSummary!.ConversationsAnalyzed);
        Assert.Equal(4, result.Response.EmailConversationSummary.ConversationsWithMoreThanTenReplies);
        Assert.Contains("Never return subject lines", runner.Invocations[3].Arguments[^1]);
        Assert.Equal(2, result.Response.DecisionAnalysisSummary!.MeetingsWithNoDecisionReached);
    }

    [Fact]
    public async Task GetMeetingDataAsync_SkipsDiarizationButQueriesEmailVolume_WhenExactTalkTimeIsAvailable()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":120,"decisionOutcome":"unknown"}],"availability":{"meetings":"available","talkTime":"available","decisions":"unknown","attendees":"available","emailReplies":"unavailable"}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20},"message":"Complete mailbox count."}
            """);
        var decisionOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","decisionAnalysis":"available"},"decisionAnalysisSummary":{"meetingsAnalyzed":1,"meetingsWithContent":1,"meetingsWithNoDecisionReached":0,"meetingsNotApplicable":0},"message":"Decision analysis completed."}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, emailOutput, decisionOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(4, runner.Invocations.Count);
        Assert.DoesNotContain("speaker diarization", runner.Invocations[1].Arguments[^1]);
        Assert.Equal(20, result.Response!.EmailVolumeSummary!.EmailsReceived);
        Assert.Null(result.Response.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_QueriesDiarization_WhenClaimedTalkTimeIsIncomplete()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":null,"decisionOutcome":"unknown"}],"availability":{"meetings":"available","talkTime":"available","decisions":"unknown","attendees":"available","emailReplies":"unavailable"}}
            """);
        var diarizationOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","speakerDiarization":"available"},"diarizationSummary":{"meetingsAnalyzed":1,"meetingsWithDiarization":1,"meetingsWithZeroUserSegments":1}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, diarizationOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(5, runner.Invocations.Count);
        Assert.Equal(1, result.Response!.DiarizationSummary!.MeetingsWithZeroUserSegments);
    }

    [Fact]
    public async Task GetMeetingDataAsync_RemovesStaleUnknownFieldMessage_WhenSupplementalQueriesCompensate()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":null,"decisionOutcome":"unknown"}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"available","emailReplies":"unavailable","recurrence":"unavailable","speakerDiarization":"unavailable","emailVolume":"unknown","decisionAnalysis":"unavailable"},"message":"Work IQ marked these fields unknown: talk time, decisions, received email volume."}
            """);
        var diarizationOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","speakerDiarization":"available"},"diarizationSummary":{"meetingsAnalyzed":1,"meetingsWithDiarization":1,"meetingsWithZeroUserSegments":0}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20}}
            """);
        var decisionOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","decisionAnalysis":"available"},"decisionAnalysisSummary":{"meetingsAnalyzed":1,"meetingsWithContent":1,"meetingsWithNoDecisionReached":0,"meetingsNotApplicable":0}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, diarizationOutput, emailOutput, decisionOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Null(result.Response!.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_SkipsMeetingSupplementalQueries_WhenBaseResponseHasNoMeetings()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","recurrence":"unknown"}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, emailOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(3, runner.Invocations.Count);
        Assert.Contains("Count incoming email messages only.", runner.Invocations[1].Arguments[^1]);
        Assert.Contains("distinct email conversation", runner.Invocations[2].Arguments[^1]);
        Assert.Equal("unknown", result.Response!.Availability.SpeakerDiarization);
        Assert.Equal("available", result.Response.Availability.EmailVolume);
        Assert.Equal("unknown", result.Response.Availability.DecisionAnalysis);
        Assert.Equal(20, result.Response.EmailVolumeSummary!.EmailsReceived);
        var prompts = string.Join(Environment.NewLine, runner.Invocations.Select(invocation => invocation.Arguments[^1]));
        Assert.DoesNotContain("speaker diarization", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Classify a meeting as decision reached", prompts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMeetingDataAsync_SkipsMeetingSupplementals_WhenBaseMeetingsFallOutsideRequestedWindow()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available(
            $"{{\"meetings\":[{{\"startsAtUtc\":\"{Query.StartsAtUtc.AddHours(-2).UtcDateTime:O}\",\"endsAtUtc\":\"{Query.StartsAtUtc.AddHours(-1).UtcDateTime:O}\",\"isRecurring\":false}}],\"availability\":{{\"meetings\":\"available\",\"recurrence\":\"available\"}}}}");
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, emailOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal(3, runner.Invocations.Count);
        Assert.Contains("Count incoming email messages only.", runner.Invocations[1].Arguments[^1]);
        Assert.Contains("distinct email conversation", runner.Invocations[2].Arguments[^1]);
        Assert.Equal("unknown", result.Response!.Availability.SpeakerDiarization);
        Assert.Equal("unknown", result.Response.Availability.DecisionAnalysis);
        var prompts = string.Join(Environment.NewLine, runner.Invocations.Select(invocation => invocation.Arguments[^1]));
        Assert.DoesNotContain("speaker diarization", prompts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Classify a meeting as decision reached", prompts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMeetingDataAsync_PreservesSupplementalFailureMessage_WhenDiarizationQueryFails()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":null,"decisionOutcome":"reached"}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"available","attendees":"available","emailReplies":"unavailable","recurrence":"unavailable","speakerDiarization":"unavailable","emailVolume":"unknown","decisionAnalysis":"unavailable"},"message":"Work IQ marked these fields unknown: talk time, received email volume."}
            """);
        var diarizationFailure = WorkIqCliExecutionResult.NonZeroExit(1, "Transcript access denied.");
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20},"message":"Complete mailbox count."}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, diarizationFailure, emailOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("unavailable", result.Response!.Availability.SpeakerDiarization);
        Assert.Contains("Transcript access denied.", result.Response.Message);
        Assert.DoesNotContain("Work IQ marked these fields unknown", result.Response.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Complete mailbox count.", result.Response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMeetingDataAsync_PreservesGenuineBaseWarning_WhenStructuredUnknownsAreResolved()
    {
        var meetingOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[{"startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":4,"userTalkTimeSeconds":null,"decisionOutcome":"unknown"}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"available","emailReplies":"unavailable","recurrence":"unavailable","speakerDiarization":"unavailable","emailVolume":"unknown","decisionAnalysis":"unavailable"},"message":"Attendee identity consent is still required. Work IQ marked these fields unknown: talk time, decisions, received email volume."}
            """);
        var diarizationOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","speakerDiarization":"available"},"diarizationSummary":{"meetingsAnalyzed":1,"meetingsWithDiarization":1,"meetingsWithZeroUserSegments":0}}
            """);
        var emailOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":20}}
            """);
        var decisionOutput = WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","decisionAnalysis":"available"},"decisionAnalysisSummary":{"meetingsAnalyzed":1,"meetingsWithContent":1,"meetingsWithNoDecisionReached":0,"meetingsNotApplicable":0}}
            """);
        var runner = new RecordingWorkIqCliRunner(meetingOutput, diarizationOutput, emailOutput, decisionOutput);
        var client = CreateClient(runner);

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.Equal("Attendee identity consent is still required.", result.Response!.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenExecutableIsMissing()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.MissingExecutable("workiq-cli not found")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("workiq-cli not found", result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenCliExitsNonZero()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.NonZeroExit(127, "permission denied")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("permission denied", result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUpgradeGuidance_WhenCliDoesNotSupportJsonOutput()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(
            WorkIqCliExecutionResult.NonZeroExit(1, "Unrecognized option '--json'")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Contains("Upgrade", result.Message);
        Assert.Contains("@microsoft/workiq@latest", result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsAuthorizationFailed_WhenCliExitsNonZeroAndRequiresSignIn()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(
            WorkIqCliExecutionResult.NonZeroExit(1, "Please complete sign-in and grant consent before continuing.")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.AuthorizationFailed, result.Status);
        Assert.Equal(
            "Work IQ CLI is not signed in with an approved corporate account. Complete sign-in in the local Work IQ CLI session and retry.",
            result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsAuthorizationFailed_WhenCliExitsNonZeroAndAuthHintIsOnStandardOutput()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(
            new WorkIqCliExecutionResult(
                WorkIqCliExecutionStatus.NonZeroExit,
                StandardOutput: "Request failed: user is not authenticated. Run sign-in first.",
                ExitCode: 1,
                Message: "Work IQ CLI exited with code 1.")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.AuthorizationFailed, result.Status);
        Assert.Equal(
            "Work IQ CLI is not signed in with an approved corporate account. Complete sign-in in the local Work IQ CLI session and retry.",
            result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenCliExitsNonZeroAndRequiresEulaAcceptance()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(
            WorkIqCliExecutionResult.NonZeroExit(1, "Run `workiq accept-eula` before using ask.")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal(
            "Work IQ CLI has not accepted the EULA for this local user. Run `workiq accept-eula` and retry live mode.",
            result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenCliTimesOut()
    {
        var client = CreateClient(new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.TimedOut("timed out after 15 seconds")));

        var result = await client.GetMeetingDataAsync(Query);

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("timed out after 15 seconds", result.Message);
    }

    [Fact]
    public async Task GetMeetingDataAsync_ReturnsUnavailable_WhenCliIsCanceled()
    {
        var client = CreateClient(new CancellableWorkIqCliRunner());
        using var cancellationTokenSource = new CancellationTokenSource();

        var task = client.GetMeetingDataAsync(Query, cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        var result = await task;

        Assert.Equal(WorkIqMeetingDataResultStatus.Unavailable, result.Status);
        Assert.Equal("Work IQ CLI request was canceled before a valid response was received.", result.Message);
    }

    private static WorkIqCliMeetingDataClient CreateClient(
        IWorkIqCliRunner runner,
        WorkIqOptions? workIqOptions = null,
        WorkIqCliOptions? cliOptions = null) =>
        new(
            runner,
            Options.Create(workIqOptions ?? new WorkIqOptions { Mode = WorkIqProviderMode.Cli }),
            Options.Create(cliOptions ?? new WorkIqCliOptions { Enabled = true, ExecutablePath = "workiq-cli", TimeoutSeconds = 15 }));

    private sealed class RecordingWorkIqCliRunner(params WorkIqCliExecutionResult[] results) : IWorkIqCliRunner
    {
        private int nextResultIndex;

        public WorkIqCliInvocation? LastInvocation { get; private set; }
        public List<WorkIqCliInvocation> Invocations { get; } = [];

        public Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default)
        {
            LastInvocation ??= invocation;
            Invocations.Add(invocation);
            if (invocation.Arguments[^1].Contains("conversationsWithMoreThanTenReplies", StringComparison.Ordinal))
            {
                return Task.FromResult(WorkIqCliExecutionResult.Available("""
                    {"meetings":[],"availability":{"meetings":"available","emailConversationAnalysis":"available"},"emailConversationSummary":{"conversationsAnalyzed":20,"conversationsWithMoreThanTenReplies":4}}
                    """));
            }

            var resultIndex = Math.Min(nextResultIndex++, results.Length - 1);
            return Task.FromResult(results[resultIndex]);
        }
    }

    private sealed class CancellableWorkIqCliRunner : IWorkIqCliRunner
    {
        public async Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return WorkIqCliExecutionResult.Available("unreachable");
        }
    }

    private sealed class RetryCancellationWorkIqCliRunner(WorkIqCliExecutionResult firstResult) : IWorkIqCliRunner
    {
        private int invocationCount;

        public List<WorkIqCliInvocation> Invocations { get; } = [];
        public TaskCompletionSource RetryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            if (Interlocked.Increment(ref invocationCount) == 1)
            {
                return firstResult;
            }

            RetryStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return WorkIqCliExecutionResult.Available("unreachable");
        }
    }
}
