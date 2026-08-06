using System.Net;
using System.Text;
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
        Assert.Equal(AvailabilityState.Unknown, dataSet.Availability);
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
    public async Task GetMeetingsAsync_ReturnsUnknown_WhenWorkIqResponseIsMalformed()
    {
        var provider = CreateProvider(WorkIqMeetingDataResult.Malformed("Work IQ returned non-JSON text."));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
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
                    DecisionSummary: null,
                    EmailReplyCount: null)
            ],
            new WorkIqFieldAvailability("available", "unknown", "unknown", "unknown", "unknown"),
            "Only calendar timing was validated by Work IQ.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.AttendeeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.EmailReplyAvailability);
        Assert.Single(dataSet.Meetings);
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
                    DecisionSummary: null,
                    EmailReplyCount: null)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Work IQ returned partial meeting facts.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Available, dataSet.Availability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.TalkTimeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.AttendeeAvailability);
        Assert.Equal(AvailabilityState.Unknown, dataSet.EmailReplyAvailability);
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
                    DecisionSummary: null,
                    EmailReplyCount: 4)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Decision outcome remains unknown.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Equal(AvailabilityState.Unknown, dataSet.DecisionAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.AttendeeAvailability);
        Assert.Equal(AvailabilityState.Available, dataSet.EmailReplyAvailability);
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
                    DecisionSummary: "done",
                    EmailReplyCount: 2),
                new WorkIqMeetingJson(
                    "inside-window",
                    "Current week",
                    Query.StartsAtUtc.AddHours(3),
                    Query.StartsAtUtc.AddHours(4),
                    AttendeeCount: 6,
                    UserTalkTimeSeconds: 180,
                    DecisionOutcome: "noneReached",
                    DecisionSummary: "pending",
                    EmailReplyCount: 5)
            ],
            new WorkIqFieldAvailability("available", "available", "available", "available", "available"),
            "Validated current window.");
        var provider = CreateProvider(WorkIqMeetingDataResult.Available(response));

        var dataSet = await provider.GetMeetingsAsync(Query);

        Assert.Single(dataSet.Meetings);
        Assert.Equal("inside-window", dataSet.Meetings[0].Id);
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
    public void ParseStrictJson_AcceptsValidatedJsonAndUnknownOptionalFields()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"id":"m1","title":"Executive review","startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":null,"userTalkTimeSeconds":null,"decisionOutcome":"unknown","decisionSummary":null,"emailReplyCount":null}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unknown"},"message":"Validated timing only."}
            """);

        Assert.Equal(WorkIqMeetingDataResultStatus.Available, result.Status);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Meetings);
        Assert.Equal("unknown", result.Response.Availability.TalkTime);
    }

    [Fact]
    public void ParseStrictJson_RejectsNonUtcMeetingTimestamps()
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson("""
            {"meetings":[{"id":"m1","title":"Executive review","startsAtUtc":"2026-08-05T17:00:00-04:00","endsAtUtc":"2026-08-05T17:30:00-04:00","attendeeCount":null,"userTalkTimeSeconds":null,"decisionOutcome":"unknown","decisionSummary":null,"emailReplyCount":null}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unknown"},"message":null}
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
