using ExecutiveDashboard.Models;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Providers;

public sealed class WorkIqCliMeetingDataClient(
    IWorkIqCliRunner cliRunner,
    IOptions<WorkIqOptions> workIqOptions,
    IOptions<WorkIqCliOptions> cliOptions) : IWorkIqMeetingDataClient
{
    public async Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        var cliConfiguration = cliOptions.Value;
        if (!cliConfiguration.HasUsableConfiguration)
        {
            return WorkIqMeetingDataResult.Unavailable(cliConfiguration.UnavailableMessage);
        }

        WorkIqCliExecutionResult result;
        try
        {
            var invocation = WorkIqCliCommandBuilder.Build(query, workIqOptions.Value, cliConfiguration);
            result = await cliRunner.RunAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = WorkIqCliExecutionResult.Canceled("Work IQ CLI request was canceled before a valid response was received.");
        }

        return result.Status switch
        {
            WorkIqCliExecutionStatus.Available => MapCliOutput(result.StandardOutput),
            WorkIqCliExecutionStatus.MissingExecutable => WorkIqMeetingDataResult.Unavailable(
                result.Message ?? "Work IQ CLI executable was not found."),
            WorkIqCliExecutionStatus.NonZeroExit => MapNonZeroExit(result),
            WorkIqCliExecutionStatus.TimedOut => WorkIqMeetingDataResult.Unavailable(
                result.Message ?? "Work IQ CLI timed out before returning a valid response."),
            WorkIqCliExecutionStatus.Canceled => WorkIqMeetingDataResult.Unavailable(
                result.Message ?? "Work IQ CLI request was canceled before a valid response was received."),
            WorkIqCliExecutionStatus.FailedToStart => WorkIqMeetingDataResult.Unavailable(
                result.Message ?? "Work IQ CLI could not be started."),
            _ => WorkIqMeetingDataResult.Malformed("Work IQ CLI returned an unknown provider state.")
        };
    }

    private static WorkIqMeetingDataResult MapCliOutput(string? output)
    {
        var parsed = WorkIqMeetingResponseParser.ParseStrictJson(output);
        return parsed.Status switch
        {
            WorkIqMeetingDataResultStatus.Available when parsed.Response is not null => WorkIqMeetingDataResult.Available(parsed.Response),
            WorkIqMeetingDataResultStatus.Malformed => WorkIqMeetingDataResult.Malformed(parsed.Message ?? "Work IQ CLI returned malformed output."),
            _ => WorkIqMeetingDataResult.Malformed("Work IQ CLI returned an unknown provider state.")
        };
    }

    private static WorkIqMeetingDataResult MapNonZeroExit(WorkIqCliExecutionResult result)
    {
        var diagnosticText = string.Join(
            Environment.NewLine,
            new[] { result.StandardError, result.StandardOutput, result.Message }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (ContainsAny(diagnosticText, "accept-eula", "eula"))
        {
            return WorkIqMeetingDataResult.Unavailable(
                "Work IQ CLI has not accepted the EULA for this local user. Run `workiq accept-eula` and retry live mode.");
        }

        if (ContainsAny(
                diagnosticText,
                "sign-in",
                "sign in",
                "signin",
                "log in",
                "login",
                "not logged",
                "signed out",
                "logged out",
                "not authenticated",
                "authentication required",
                "token expired",
                "unauthorized",
                "forbidden",
                "consent",
                "aadsts"))
        {
            return WorkIqMeetingDataResult.AuthorizationFailed(
                "Work IQ CLI is not signed in with an approved corporate account. Complete sign-in in the local Work IQ CLI session and retry.");
        }

        return WorkIqMeetingDataResult.Unavailable(
            result.Message ?? $"Work IQ CLI exited with code {result.ExitCode ?? -1}.");
    }

    private static bool ContainsAny(string? source, params string[] fragments) =>
        !string.IsNullOrWhiteSpace(source)
        && fragments.Any(fragment => source.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
}
