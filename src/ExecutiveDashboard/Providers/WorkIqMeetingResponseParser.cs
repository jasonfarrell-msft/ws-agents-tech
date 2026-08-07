using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExecutiveDashboard.Providers;

public static class WorkIqMeetingResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static WorkIqMeetingDataResult ParseStrictJson(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ returned an empty response.");
        }

        var trimmed = ExtractResponsePayload(responseText.Trim()).Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ returned non-JSON text; the dashboard only accepts a strict JSON object.");
        }

        WorkIqMeetingJsonDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorkIqMeetingJsonDocument>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            var path = string.IsNullOrWhiteSpace(ex.Path) ? "$" : ex.Path;
            return WorkIqMeetingDataResult.Malformed(
                $"Work IQ returned JSON that could not be parsed at {path}. Verify the response matches the required Work IQ meeting schema.");
        }

        if (document?.Meetings is null || document.Availability is null)
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON omitted the required meetings or availability object.");
        }
        if (document.DiarizationSummary is { } summary
            && (summary.MeetingsAnalyzed < 0
                || summary.MeetingsWithDiarization < 0
                || summary.MeetingsWithZeroUserSegments < 0
                || summary.MeetingsWithDiarization > summary.MeetingsAnalyzed
                || summary.MeetingsWithZeroUserSegments > summary.MeetingsWithDiarization))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included invalid diarization summary counts.");
        }
        if (document.EmailVolumeSummary is { EmailsReceived: < 0 })
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included an invalid received email count.");
        }
        if (document.DecisionAnalysisSummary is { } decisionSummary
            && (decisionSummary.MeetingsAnalyzed < 0
                || decisionSummary.MeetingsWithContent < 0
                || decisionSummary.MeetingsWithNoDecisionReached < 0
                || decisionSummary.MeetingsNotApplicable < 0
                || decisionSummary.MeetingsWithContent > decisionSummary.MeetingsAnalyzed
                || decisionSummary.MeetingsWithNoDecisionReached + decisionSummary.MeetingsNotApplicable
                    > decisionSummary.MeetingsWithContent))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included invalid decision analysis counts.");
        }
        if (document.EmailConversationSummary is { } conversationSummary
            && (conversationSummary.ConversationsAnalyzed < 0
                || conversationSummary.ConversationsWithMoreThanTenReplies < 0
                || conversationSummary.ConversationsWithMoreThanTenReplies > conversationSummary.ConversationsAnalyzed))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included invalid email conversation summary counts.");
        }

        if (!TryNormalizeAvailability(document.Availability.Meetings, out var meetingsAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.TalkTime, "unavailable", out var talkTimeAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.Decisions, "unavailable", out var decisionsAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.Attendees, "unavailable", out var attendeesAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.EmailReplies, "unavailable", out var emailRepliesAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.Recurrence, "unknown", out var recurrenceAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.AttendeeIdentities, "unknown", out var attendeeIdentitiesAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.SpeakerDiarization, "unknown", out var speakerDiarizationAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.EmailVolume, "unknown", out var emailVolumeAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.DecisionAnalysis, "unknown", out var decisionAnalysisAvailability)
            || !TryNormalizeAvailabilityOrDefault(document.Availability.EmailConversationAnalysis, "unavailable", out var emailConversationAnalysisAvailability))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included an unsupported availability value.");
        }

        var meetings = new List<WorkIqMeetingJson>(document.Meetings.Count);
        for (var meetingIndex = 0; meetingIndex < document.Meetings.Count; meetingIndex++)
        {
            var meeting = document.Meetings[meetingIndex];
            var decisionOutcome = NormalizeDecision(meeting.DecisionOutcome);
            if (!TryParseUtcTimestamp(meeting.StartsAtUtc, out var startsAtUtc)
                || !TryParseUtcTimestamp(meeting.EndsAtUtc, out var endsAtUtc)
                || endsAtUtc <= startsAtUtc
                || meeting.AttendeeCount is < 0
                || meeting.UserTalkTimeSeconds is < 0
                || meeting.UserSpeakingSegmentCount is < 0
                || !IsSupportedDecision(decisionOutcome))
            {
                return WorkIqMeetingDataResult.Malformed("Work IQ JSON included a meeting with invalid or unsupported fields.");
            }

            meetings.Add(new WorkIqMeetingJson(
                string.IsNullOrWhiteSpace(meeting.Id) ? $"workiq-meeting-{meetingIndex + 1}" : meeting.Id,
                string.IsNullOrWhiteSpace(meeting.Title) ? "Work IQ meeting" : meeting.Title,
                startsAtUtc.ToUniversalTime(),
                endsAtUtc.ToUniversalTime(),
                meeting.AttendeeCount,
                meeting.UserTalkTimeSeconds,
                decisionOutcome ?? "unknown",
                meeting.DecisionSummary,
                meeting.IsRecurring,
                meeting.AttendeeKeys?
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => key.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                meeting.HasTranscript,
                meeting.UserSpeakingSegmentCount));
        }

        return WorkIqMeetingDataResult.Available(
            new WorkIqMeetingJsonResponse(
                meetings,
                new WorkIqFieldAvailability(
                    meetingsAvailability,
                    talkTimeAvailability,
                    decisionsAvailability,
                    attendeesAvailability,
                    emailRepliesAvailability,
                    recurrenceAvailability,
                    attendeeIdentitiesAvailability,
                    speakerDiarizationAvailability,
                    emailVolumeAvailability,
                    decisionAnalysisAvailability,
                    emailConversationAnalysisAvailability),
                document.Message,
                document.DiarizationSummary,
                document.EmailVolumeSummary,
                document.DecisionAnalysisSummary,
                document.EmailConversationSummary));
    }

    private static string ExtractResponsePayload(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("response", out var response)
                && response.ValueKind == JsonValueKind.String)
            {
                return response.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Preserve the original text so the strict parser reports its normal error.
        }

        return responseText;
    }

    private static bool TryNormalizeAvailability(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() switch
        {
            "available" => "available",
            "unavailable" => "unavailable",
            "unknown" => "unknown",
            _ => string.Empty
        };

        return normalized.Length > 0;
    }

    private static bool TryNormalizeAvailabilityOrDefault(string? value, string defaultValue, out string normalized)
    {
        if (value is null)
        {
            normalized = defaultValue;
            return true;
        }

        return TryNormalizeAvailability(value, out normalized);
    }

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null
            && value.EndsWith('Z')
            && DateTimeOffset.TryParse(value, out timestamp)
            && timestamp.Offset == TimeSpan.Zero;
    }

    private static bool IsSupportedDecision(string? value) =>
        value is null or "reached" or "noneReached" or "notApplicable" or "unknown";

    private static string? NormalizeDecision(string? value) =>
        value?.Trim() switch
        {
            null or "" => null,
            "reached" => "reached",
            "noneReached" => "noneReached",
            "notApplicable" => "notApplicable",
            "unknown" => "unknown",
            _ => value
        };

    private sealed record WorkIqMeetingJsonDocument(
        List<WorkIqMeetingJsonDocumentMeeting> Meetings,
        WorkIqMeetingJsonDocumentAvailability Availability,
        [property: JsonConverter(typeof(FlexibleWorkIqTextConverter))]
        string? Message,
        WorkIqDiarizationSummary? DiarizationSummary,
        WorkIqEmailVolumeSummary? EmailVolumeSummary,
        WorkIqDecisionAnalysisSummary? DecisionAnalysisSummary,
        WorkIqEmailConversationSummary? EmailConversationSummary);

    private sealed record WorkIqMeetingJsonDocumentAvailability(
        string? Meetings,
        string? TalkTime,
        string? Decisions,
        string? Attendees,
        string? EmailReplies,
        string? Recurrence,
        string? AttendeeIdentities,
        string? SpeakerDiarization,
        string? EmailVolume,
        string? DecisionAnalysis,
        string? EmailConversationAnalysis);

    private sealed record WorkIqMeetingJsonDocumentMeeting(
        [property: JsonConverter(typeof(FlexibleWorkIqTextConverter))]
        string? Id,
        [property: JsonConverter(typeof(FlexibleWorkIqTextConverter))]
        string? Title,
        string? StartsAtUtc,
        string? EndsAtUtc,
        int? AttendeeCount,
        int? UserTalkTimeSeconds,
        string? DecisionOutcome,
        string? DecisionSummary,
        bool? IsRecurring,
        List<string>? AttendeeKeys,
        bool? HasTranscript,
        int? UserSpeakingSegmentCount);

    private sealed class FlexibleWorkIqTextConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            using var value = JsonDocument.ParseValue(ref reader);
            if (value.RootElement.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return value.RootElement.GetRawText();
            }

            if (value.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "text", "value", "name", "title", "detail", "message", "error" })
                {
                    if (value.RootElement.TryGetProperty(propertyName, out var property)
                        && property.ValueKind == JsonValueKind.String)
                    {
                        return property.GetString();
                    }
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
