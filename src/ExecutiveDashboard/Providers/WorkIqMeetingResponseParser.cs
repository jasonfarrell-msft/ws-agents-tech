using System.Text.Json;

namespace ExecutiveDashboard.Providers;

public static class WorkIqMeetingResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WorkIqMeetingDataResult ParseStrictJson(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ returned an empty response.");
        }

        var trimmed = responseText.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ returned non-JSON text; the dashboard only accepts a strict JSON object.");
        }

        WorkIqMeetingJsonDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WorkIqMeetingJsonDocument>(trimmed, JsonOptions);
        }
        catch (JsonException)
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ returned JSON that could not be parsed.");
        }

        if (document?.Meetings is null || document.Availability is null)
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON omitted the required meetings or availability object.");
        }

        if (!TryNormalizeAvailability(document.Availability.Meetings, out var meetingsAvailability)
            || !TryNormalizeAvailability(document.Availability.TalkTime, out var talkTimeAvailability)
            || !TryNormalizeAvailability(document.Availability.Decisions, out var decisionsAvailability)
            || !TryNormalizeAvailability(document.Availability.Attendees, out var attendeesAvailability)
            || !TryNormalizeAvailability(document.Availability.EmailReplies, out var emailRepliesAvailability))
        {
            return WorkIqMeetingDataResult.Malformed("Work IQ JSON included an unsupported availability value.");
        }

        var meetings = new List<WorkIqMeetingJson>(document.Meetings.Count);
        foreach (var meeting in document.Meetings)
        {
            if (string.IsNullOrWhiteSpace(meeting.Id)
                || string.IsNullOrWhiteSpace(meeting.Title)
                || !TryParseUtcTimestamp(meeting.StartsAtUtc, out var startsAtUtc)
                || !TryParseUtcTimestamp(meeting.EndsAtUtc, out var endsAtUtc)
                || endsAtUtc <= startsAtUtc
                || meeting.AttendeeCount is < 0
                || meeting.UserTalkTimeSeconds is < 0
                || meeting.EmailReplyCount is < 0
                || !IsSupportedDecision(meeting.DecisionOutcome))
            {
                return WorkIqMeetingDataResult.Malformed("Work IQ JSON included a meeting with invalid or unsupported fields.");
            }

            meetings.Add(new WorkIqMeetingJson(
                meeting.Id,
                meeting.Title,
                startsAtUtc.ToUniversalTime(),
                endsAtUtc.ToUniversalTime(),
                meeting.AttendeeCount,
                meeting.UserTalkTimeSeconds,
                NormalizeDecision(meeting.DecisionOutcome),
                meeting.DecisionSummary,
                meeting.EmailReplyCount));
        }

        return WorkIqMeetingDataResult.Available(
            new WorkIqMeetingJsonResponse(
                meetings,
                new WorkIqFieldAvailability(
                    meetingsAvailability,
                    talkTimeAvailability,
                    decisionsAvailability,
                    attendeesAvailability,
                    emailRepliesAvailability),
                document.Message));
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

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null
            && value.EndsWith('Z')
            && DateTimeOffset.TryParse(value, out timestamp)
            && timestamp.Offset == TimeSpan.Zero;
    }

    private static bool IsSupportedDecision(string? value) =>
        value is null
        || NormalizeDecision(value) is "reached" or "noneReached" or "notApplicable" or "unknown";

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
        string? Message);

    private sealed record WorkIqMeetingJsonDocumentAvailability(
        string? Meetings,
        string? TalkTime,
        string? Decisions,
        string? Attendees,
        string? EmailReplies);

    private sealed record WorkIqMeetingJsonDocumentMeeting(
        string? Id,
        string? Title,
        string? StartsAtUtc,
        string? EndsAtUtc,
        int? AttendeeCount,
        int? UserTalkTimeSeconds,
        string? DecisionOutcome,
        string? DecisionSummary,
        int? EmailReplyCount);
}
