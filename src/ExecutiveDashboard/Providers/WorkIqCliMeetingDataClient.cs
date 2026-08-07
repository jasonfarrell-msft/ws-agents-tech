using System.Text.Json;
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

        var meetingResult = MapExecutionResult(result);
        if (meetingResult.Status != WorkIqMeetingDataResultStatus.Available || meetingResult.Response is null)
        {
            return meetingResult;
        }

        if (!string.Equals(meetingResult.Response.Availability.Meetings, "available", StringComparison.Ordinal))
        {
            WorkIqCliExecutionResult retryExecution;
            try
            {
                var retryInvocation = WorkIqCliCommandBuilder.Build(query, workIqOptions.Value, cliConfiguration);
                retryExecution = await cliRunner.RunAsync(retryInvocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                retryExecution = WorkIqCliExecutionResult.Canceled(
                    "Work IQ CLI calendar retry was canceled before a valid response was received.");
            }

            var retryResult = MapExecutionResult(retryExecution);
            if (retryExecution.Status == WorkIqCliExecutionStatus.Canceled)
            {
                return retryResult;
            }

            if (retryResult.Status == WorkIqMeetingDataResultStatus.Available
                && retryResult.Response is not null
                && string.Equals(retryResult.Response.Availability.Meetings, "available", StringComparison.Ordinal))
            {
                meetingResult = retryResult;
            }
        }

        var response = meetingResult.Response;
        if (!string.Equals(response.Availability.Meetings, "available", StringComparison.Ordinal))
        {
            return WorkIqMeetingDataResult.Available(response);
        }

        var meetingsInWindow = WorkIqMeetingWindowFilter.FilterToRequestedWindow(query, response.Meetings);
        var hasCompleteTalkTime = string.Equals(response.Availability.TalkTime, "available", StringComparison.Ordinal)
            && (meetingsInWindow.Length == 0 || meetingsInWindow.All(meeting => meeting.UserTalkTimeSeconds.HasValue));
        if (meetingsInWindow.Length > 0 && !hasCompleteTalkTime)
        {
            WorkIqCliExecutionResult diarizationExecution;
            try
            {
                var invocation = WorkIqCliCommandBuilder.BuildDiarization(query, workIqOptions.Value, cliConfiguration);
                diarizationExecution = await cliRunner.RunAsync(invocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return WorkIqMeetingDataResult.Unavailable(
                    "Work IQ transcript diarization request was canceled before a valid response was received.");
            }

            var diarizationResult = MapExecutionResult(diarizationExecution);
            response = diarizationResult.Status == WorkIqMeetingDataResultStatus.Available && diarizationResult.Response is not null
                ? MergeDiarization(response, diarizationResult.Response, meetingsInWindow)
                : WithDiarizationFailure(response, diarizationResult.Message, meetingsInWindow);
        }

        WorkIqCliExecutionResult emailExecution;
        try
        {
            var invocation = WorkIqCliCommandBuilder.BuildEmailVolume(query, workIqOptions.Value, cliConfiguration);
            emailExecution = await cliRunner.RunAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return WorkIqMeetingDataResult.Unavailable(
                "Work IQ email volume request was canceled before a valid response was received.");
        }

        var emailResult = MapExecutionResult(emailExecution);
        response = emailResult.Status == WorkIqMeetingDataResultStatus.Available && emailResult.Response is not null
            ? MergeEmailVolume(response, emailResult.Response, meetingsInWindow)
            : WithEmailVolumeFailure(response, emailResult.Message, meetingsInWindow);

        WorkIqCliExecutionResult emailConversationExecution;
        try
        {
            var invocation = WorkIqCliCommandBuilder.BuildEmailConversationAnalysis(
                query,
                workIqOptions.Value,
                cliConfiguration);
            emailConversationExecution = await cliRunner.RunAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return WorkIqMeetingDataResult.Unavailable(
                "Work IQ email conversation analysis was canceled before a valid response was received.");
        }

        var emailConversationResult = MapExecutionResult(emailConversationExecution);
        response = emailConversationResult.Status == WorkIqMeetingDataResultStatus.Available
            && emailConversationResult.Response is not null
                ? MergeEmailConversationAnalysis(response, emailConversationResult.Response, meetingsInWindow)
                : WithEmailConversationAnalysisFailure(response, emailConversationResult.Message, meetingsInWindow);

        var hasCompleteDecisionData = string.Equals(response.Availability.Decisions, "available", StringComparison.Ordinal)
            && (meetingsInWindow.Length == 0
                || meetingsInWindow.All(meeting =>
                    meeting.DecisionOutcome is "reached" or "noneReached" or "notApplicable"));
        if (meetingsInWindow.Length > 0 && !hasCompleteDecisionData)
        {
            WorkIqCliExecutionResult decisionExecution;
            try
            {
                var invocation = WorkIqCliCommandBuilder.BuildDecisionAnalysis(query, workIqOptions.Value, cliConfiguration);
                decisionExecution = await cliRunner.RunAsync(invocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return WorkIqMeetingDataResult.Unavailable(
                    "Work IQ decision analysis request was canceled before a valid response was received.");
            }

            var decisionResult = MapExecutionResult(decisionExecution);
            response = decisionResult.Status == WorkIqMeetingDataResultStatus.Available && decisionResult.Response is not null
                ? MergeDecisionAnalysis(response, decisionResult.Response, meetingsInWindow)
                : WithDecisionAnalysisFailure(response, decisionResult.Message, meetingsInWindow);
        }

        response = response with
        {
            Message = StripStructuredAvailabilityDetail(response.Message, response, meetingsInWindow)
        };

        return WorkIqMeetingDataResult.Available(response);
    }

    private static WorkIqMeetingDataResult MapExecutionResult(WorkIqCliExecutionResult result) =>
        result.Status switch
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

    private static WorkIqMeetingJsonResponse MergeDiarization(
        WorkIqMeetingJsonResponse meetings,
        WorkIqMeetingJsonResponse diarization,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with
            {
                SpeakerDiarization = diarization.Availability.SpeakerDiarization
            },
            DiarizationSummary = diarization.DiarizationSummary,
            Message = MergeSupplementalMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                diarization.Message,
                IsHealthySupplementalState(
                    diarization.Availability.SpeakerDiarization,
                    diarization.DiarizationSummary))
        };

    private static WorkIqMeetingJsonResponse WithDiarizationFailure(
        WorkIqMeetingJsonResponse meetings,
        string? failureMessage,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with { SpeakerDiarization = "unavailable" },
            Message = AppendMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                string.IsNullOrWhiteSpace(failureMessage)
                    ? "Work IQ could not retrieve transcript diarization."
                    : failureMessage)
        };

    private static WorkIqMeetingJsonResponse MergeEmailVolume(
        WorkIqMeetingJsonResponse meetings,
        WorkIqMeetingJsonResponse emailVolume,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with
            {
                EmailVolume = emailVolume.Availability.EmailVolume
            },
            EmailVolumeSummary = emailVolume.EmailVolumeSummary,
            Message = MergeSupplementalMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                emailVolume.Message,
                IsHealthySupplementalState(
                    emailVolume.Availability.EmailVolume,
                    emailVolume.EmailVolumeSummary))
        };

    private static WorkIqMeetingJsonResponse WithEmailVolumeFailure(
        WorkIqMeetingJsonResponse meetings,
        string? failureMessage,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with { EmailVolume = "unavailable" },
            Message = AppendMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                string.IsNullOrWhiteSpace(failureMessage)
                    ? "Work IQ could not retrieve received email volume."
                    : failureMessage)
        };

    private static WorkIqMeetingJsonResponse MergeEmailConversationAnalysis(
        WorkIqMeetingJsonResponse meetings,
        WorkIqMeetingJsonResponse emailConversationAnalysis,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with
            {
                EmailConversationAnalysis = emailConversationAnalysis.Availability.EmailConversationAnalysis
            },
            EmailConversationSummary = emailConversationAnalysis.EmailConversationSummary,
            Message = MergeSupplementalMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                emailConversationAnalysis.Message,
                IsHealthySupplementalState(
                    emailConversationAnalysis.Availability.EmailConversationAnalysis,
                    emailConversationAnalysis.EmailConversationSummary))
        };

    private static WorkIqMeetingJsonResponse WithEmailConversationAnalysisFailure(
        WorkIqMeetingJsonResponse meetings,
        string? failureMessage,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with { EmailConversationAnalysis = "unavailable" },
            Message = AppendMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                string.IsNullOrWhiteSpace(failureMessage)
                    ? "Work IQ could not analyze email conversation reply counts."
                    : failureMessage)
        };

    private static WorkIqMeetingJsonResponse MergeDecisionAnalysis(
        WorkIqMeetingJsonResponse meetings,
        WorkIqMeetingJsonResponse decisionAnalysis,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with
            {
                DecisionAnalysis = decisionAnalysis.Availability.DecisionAnalysis
            },
            DecisionAnalysisSummary = decisionAnalysis.DecisionAnalysisSummary,
            Message = MergeSupplementalMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                decisionAnalysis.Message,
                IsHealthySupplementalState(
                    decisionAnalysis.Availability.DecisionAnalysis,
                    decisionAnalysis.DecisionAnalysisSummary))
        };

    private static WorkIqMeetingJsonResponse WithDecisionAnalysisFailure(
        WorkIqMeetingJsonResponse meetings,
        string? failureMessage,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow) =>
        meetings with
        {
            Availability = meetings.Availability with { DecisionAnalysis = "unavailable" },
            Message = AppendMessage(
                StripStructuredAvailabilityDetail(meetings.Message, meetings, meetingsInWindow),
                string.IsNullOrWhiteSpace(failureMessage)
                    ? "Work IQ could not analyze meeting decision outcomes."
                    : failureMessage)
        };

    private static string AppendMessage(string? existing, string additional) =>
        string.IsNullOrWhiteSpace(existing) ? additional : $"{existing} {additional}";

    private static string? MergeSupplementalMessage(
        string? existing,
        string? additional,
        bool suppressAdditionalMessage)
    {
        if (suppressAdditionalMessage || string.IsNullOrWhiteSpace(additional))
        {
            return existing;
        }

        return AppendMessage(existing, additional);
    }

    private static bool IsHealthySupplementalState(string availability, object? summary) =>
        string.Equals(availability, "available", StringComparison.Ordinal) && summary is not null;

    private static string? StripStructuredAvailabilityDetail(
        string? message,
        WorkIqMeetingJsonResponse response,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var detail = BuildStructuredAvailabilityDetail(response, meetingsInWindow);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return message;
        }

        var sanitized = message.Trim();
        sanitized = sanitized.Replace($"{detail} ", string.Empty, StringComparison.Ordinal);
        sanitized = sanitized.Replace($" {detail}", string.Empty, StringComparison.Ordinal);
        sanitized = sanitized.Replace(detail, string.Empty, StringComparison.Ordinal).Trim();

        return sanitized.Length == 0 ? null : sanitized;
    }

    private static string? BuildStructuredAvailabilityDetail(
        WorkIqMeetingJsonResponse response,
        IReadOnlyCollection<WorkIqMeetingJson> meetingsInWindow)
    {
        var meetingsAvailability = ToAvailabilityState(response.Availability.Meetings);
        var talkTimeAvailability = CoerceAvailability(
            response.Availability.TalkTime,
            meetingsInWindow,
            meeting => meeting.UserTalkTimeSeconds.HasValue);
        var decisionAvailability = CoerceAvailability(
            response.Availability.Decisions,
            meetingsInWindow,
            meeting => !string.IsNullOrWhiteSpace(meeting.DecisionOutcome)
                && !string.Equals(meeting.DecisionOutcome, "unknown", StringComparison.OrdinalIgnoreCase));
        var recurrenceAvailability = CoerceAvailability(
            response.Availability.Recurrence,
            meetingsInWindow,
            meeting => meeting.IsRecurring.HasValue);

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

        var hasMeetings = meetingsInWindow.Count > 0;
        var unknownFields = new List<string>();
        if (meetingsAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("meetings");
        }

        if (hasMeetings && talkTimeAvailability == AvailabilityState.Unknown && speakerDiarizationAvailability != AvailabilityState.Available)
        {
            unknownFields.Add("talk time");
        }

        if (hasMeetings && decisionAvailability == AvailabilityState.Unknown && decisionAnalysisAvailability != AvailabilityState.Available)
        {
            unknownFields.Add("decisions");
        }

        if (hasMeetings && recurrenceAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("recurrence");
        }

        if (hasMeetings && speakerDiarizationAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("speaker diarization");
        }

        if (emailVolumeAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("received email volume");
        }

        if (emailConversationAnalysisAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("email conversation analysis");
        }

        if (hasMeetings && decisionAnalysisAvailability == AvailabilityState.Unknown)
        {
            unknownFields.Add("decision analysis");
        }

        return unknownFields.Count > 0
            ? $"Work IQ marked these fields unknown: {string.Join(", ", unknownFields)}."
            : null;
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

    private static WorkIqMeetingDataResult MapCliOutput(string? output)
    {
        var parsed = WorkIqMeetingResponseParser.ParseStrictJson(ExtractMetricPayload(output));
        return parsed.Status switch
        {
            WorkIqMeetingDataResultStatus.Available when parsed.Response is not null => WorkIqMeetingDataResult.Available(parsed.Response),
            WorkIqMeetingDataResultStatus.Malformed => WorkIqMeetingDataResult.Malformed(parsed.Message ?? "Work IQ CLI returned malformed output."),
            _ => WorkIqMeetingDataResult.Malformed("Work IQ CLI returned an unknown provider state.")
        };
    }

    private static string? ExtractMetricPayload(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("response", out var response))
            {
                return response.ValueKind == JsonValueKind.String
                    ? response.GetString()
                    : output;
            }
        }
        catch (JsonException)
        {
            // Let the strict metrics parser report the actionable JSON error.
        }

        return output;
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

        if (ContainsAny(diagnosticText, "--json")
            && ContainsAny(diagnosticText, "unknown option", "unrecognized option", "unrecognized argument"))
        {
            return WorkIqMeetingDataResult.Unavailable(
                "The installed Work IQ CLI does not support structured JSON output. Upgrade it with `npm install -g @microsoft/workiq@latest` and retry live mode.");
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

        var defaultMessage = $"Work IQ CLI exited with code {result.ExitCode ?? -1}.";
        var actionableMessage = new[] { result.Message, result.StandardError, result.StandardOutput }
            .FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, defaultMessage, StringComparison.Ordinal));

        return WorkIqMeetingDataResult.Unavailable(actionableMessage ?? result.Message ?? defaultMessage);
    }

    private static bool ContainsAny(string? source, params string[] fragments) =>
        !string.IsNullOrWhiteSpace(source)
        && fragments.Any(fragment => source.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
}
