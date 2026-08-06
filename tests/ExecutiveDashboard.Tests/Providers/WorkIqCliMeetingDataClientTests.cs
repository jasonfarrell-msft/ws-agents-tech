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
            {"meetings":[{"id":"m1","title":"Executive review","startsAtUtc":"2026-08-05T17:00:00Z","endsAtUtc":"2026-08-05T17:30:00Z","attendeeCount":null,"userTalkTimeSeconds":null,"decisionOutcome":"unknown","decisionSummary":null,"emailReplyCount":null}],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unknown"},"message":"Validated timing only."}
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
        Assert.Equal("-q", runner.LastInvocation.Arguments[3]);
        var prompt = runner.LastInvocation.Arguments[4];
        Assert.Contains("Return ONLY one strict JSON object", prompt);
        Assert.Contains("weekly meeting count", prompt);
        Assert.Contains("average meeting length", prompt);
        Assert.Contains("percentage of meetings with no decision reached", prompt);
        Assert.DoesNotContain(Query.UserId, prompt);
        Assert.Equal(5, runner.LastInvocation.Arguments.Count);
    }

    [Fact]
    public async Task GetMeetingDataAsync_PassesTenantIdAsGlobalArgumentBeforeAsk()
    {
        var runner = new RecordingWorkIqCliRunner(WorkIqCliExecutionResult.Available("""
            {"meetings":[],"availability":{"meetings":"available","talkTime":"unknown","decisions":"unknown","attendees":"unknown","emailReplies":"unknown"},"message":null}
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
        Assert.Equal("-q", runner.LastInvocation.Arguments[5]);
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
        Assert.Equal("Work IQ CLI exited with code 127.", result.Message);
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

    private sealed class RecordingWorkIqCliRunner(WorkIqCliExecutionResult result) : IWorkIqCliRunner
    {
        public WorkIqCliInvocation? LastInvocation { get; private set; }

        public Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default)
        {
            LastInvocation = invocation;
            return Task.FromResult(result);
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
}
