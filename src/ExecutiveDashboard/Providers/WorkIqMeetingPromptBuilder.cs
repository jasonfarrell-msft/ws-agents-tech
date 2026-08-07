using System.Globalization;
using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public static class WorkIqMeetingPromptBuilder
{
    private const string AttendedMeetingExclusions =
        "Exclude all-day events, out-of-office events, declined meetings, canceled meetings, non-blocking events, and personal appointments.";

    public static string BuildCalendarPrompt(MeetingQuery query) =>
        $$$"""
        {{{BuildAttendedMeetingPopulationPrompt(query)}}}

        Return only strict JSON with exactly this shape and no markdown or explanation:
        {"meetings":[{"startsAtUtc":"2026-08-03T13:00:00Z","endsAtUtc":"2026-08-03T13:30:00Z","isRecurring":true}],"availability":{"meetings":"available","recurrence":"available"}}

        Values illustrate JSON types only. Include one row per attended meeting with actual UTC start and end times plus recurrence status.
        Only return the three meeting properties shown and the two availability properties shown.
        Do not include IDs, titles, people, summaries, transcript content, or extra properties.
        """;

    [Obsolete("Use BuildCalendarPrompt for CLI base flow or BuildDirectMeetingPrompt for direct chat mode.")]
    public static string BuildMeetingPrompt(MeetingQuery query) => BuildCalendarPrompt(query);

    public static string BuildDirectMeetingPrompt(MeetingQuery query) =>
        $$$"""
        {{{BuildAttendedMeetingPopulationPrompt(query)}}}

        Return only strict JSON with exactly this shape and no markdown or explanation:
        {"meetings":[{"startsAtUtc":"2026-08-03T13:00:00Z","endsAtUtc":"2026-08-03T13:30:00Z","attendeeCount":8,"userTalkTimeSeconds":240,"decisionOutcome":"reached","isRecurring":true}],"availability":{"meetings":"available","talkTime":"available","decisions":"available","attendees":"available","emailReplies":"unavailable","recurrence":"available","speakerDiarization":"unavailable","emailVolume":"unavailable","decisionAnalysis":"unavailable","emailConversationAnalysis":"unavailable"}}

        Values illustrate JSON types only. Include one row per attended meeting with actual UTC start and end times.
        Only return the six meeting properties shown and the ten availability properties shown.
        decisionOutcome must be one of "reached", "noneReached", "notApplicable", or "unknown".
        For attendeeCount and userTalkTimeSeconds, use null when the meeting row is included but that native value is unavailable.
        Set talkTime, decisions, attendees, and recurrence to "available", "unknown", or "unavailable" based only on data returned in this same response.
        Keep emailReplies, speakerDiarization, emailVolume, decisionAnalysis, and emailConversationAnalysis as "unavailable" unless this same JSON object includes the supported direct data for them.
        Do not include IDs, titles, attendee identities, job titles, summaries, transcript content, email content, or extra properties.
        """;

    public static string BuildDiarizationPrompt(MeetingQuery query) =>
        $$$"""
        {{{BuildAttendedMeetingPopulationPrompt(query)}}}
        For every attended meeting with an accessible transcript, open the transcript and inspect speaker diarization.

        Return only one JSON object with exactly this shape and no markdown or explanation:
        {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","speakerDiarization":"available"},"diarizationSummary":{"meetingsAnalyzed":23,"meetingsWithDiarization":6,"meetingsWithZeroUserSegments":4}}

        The numbers above illustrate JSON types only; return actual counts.
        meetingsAnalyzed is the number of meetings inspected. meetingsWithDiarization is the number with an accessible speaker-attributed transcript.
        meetingsWithZeroUserSegments is the number of diarized meetings containing zero segments explicitly attributed to the signed-in user.
        This supplemental result is only a minimum confirmed proxy for informational meetings: zero user-attributed segments prove 0% speaking time, but without segment timing it does not identify every meeting below 10% speaking time.
        Never estimate speaking duration or return transcript content, speaker names, meeting titles, meeting rows, or additional properties.
        """;

    public static string BuildEmailVolumePrompt(MeetingQuery query) =>
        $$$"""
        For the signed-in user, count email messages received at or after {{{query.StartsAtUtc:O}}} and before {{{query.EndsAtUtc:O}}}.
        Count incoming email messages only. Exclude messages sent by the signed-in user, drafts, calendar events, Teams messages, and duplicate conversation summaries.

        Return only one JSON object with exactly this shape and no markdown or explanation:
        {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailVolume":"available"},"emailVolumeSummary":{"emailsReceived":124}}

        The number above illustrates the JSON type only; return the complete actual count, not a page-limited sample.
        If Work IQ cannot guarantee a complete count, set emailVolume to "unavailable" and omit emailVolumeSummary.
        """;

    public static string BuildEmailConversationPrompt(MeetingQuery query) =>
        $$$"""
        For the signed-in user, inspect every email conversation containing at least one message received at or after {{{query.StartsAtUtc:O}}} and before {{{query.EndsAtUtc:O}}}.
        Count each distinct email conversation once. Count reply messages in the complete conversation, excluding the initial message. Exclude drafts, calendar events, Teams messages, automated notifications, and duplicate conversation summaries.

        Return only one JSON object with exactly this shape and no markdown or explanation:
        {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","emailConversationAnalysis":"available"},"emailConversationSummary":{"conversationsAnalyzed":40,"conversationsWithMoreThanTenReplies":6}}

        The numbers above illustrate JSON types only; return complete aggregate counts.
        conversationsAnalyzed is the number of distinct qualifying email conversations.
        conversationsWithMoreThanTenReplies is the number whose complete conversation contains strictly more than 10 reply messages.
        Never return subject lines, participants, message bodies, conversation identifiers, or individual conversation rows.
        If Work IQ cannot guarantee complete aggregate counts, set emailConversationAnalysis to "unavailable" and omit emailConversationSummary.
        """;

    public static string BuildDecisionAnalysisPrompt(MeetingQuery query) =>
        $$$"""
        {{{BuildAttendedMeetingPopulationPrompt(query)}}}
        For every attended meeting with accessible transcript or meeting summary content, open and inspect that content.
        Classify a meeting as decision reached when there is a clear final choice, approval, commitment, or selected course of action.
        Classify it as no decision reached only when a decision, choice, approval, commitment, or trade-off was discussed but no final decision occurred.
        Classify informational, status, social, or presentation meetings with no decision expected as not applicable. Do not use meeting titles or calendar metadata.

        Return only one JSON object with exactly this shape and no markdown or explanation:
        {"meetings":[],"availability":{"meetings":"available","talkTime":"unavailable","decisions":"unavailable","attendees":"unavailable","emailReplies":"unavailable","decisionAnalysis":"available"},"decisionAnalysisSummary":{"meetingsAnalyzed":23,"meetingsWithContent":6,"meetingsWithNoDecisionReached":2,"meetingsNotApplicable":1}}

        The numbers above illustrate JSON types only; return actual aggregate counts and no meeting rows.
        meetingsAnalyzed is the total meeting population considered. meetingsWithContent is the number that could be classified from accessible content.
        meetingsWithNoDecisionReached is the number of classified meetings where discussion occurred without a final decision.
        meetingsNotApplicable is the number of classified meetings where no decision was expected.
        """;

    private static string BuildAttendedMeetingPopulationPrompt(MeetingQuery query) =>
        $$"""
        Query the signed-in user's Microsoft 365 calendar for meetings the user attended from {{FormatCalendarDate(query.StartsAtUtc)}} through {{FormatCalendarDate(GetInclusiveCalendarEndDate(query.EndsAtUtc))}} inclusive.
        {{AttendedMeetingExclusions}}
        """;

    private static DateTimeOffset GetInclusiveCalendarEndDate(DateTimeOffset endsAtUtc)
    {
        var normalizedEnd = endsAtUtc.ToUniversalTime();
        return normalizedEnd.TimeOfDay == TimeSpan.Zero
            ? normalizedEnd.AddDays(-1)
            : normalizedEnd;
    }

    private static string FormatCalendarDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("dddd MMMM d, yyyy", CultureInfo.InvariantCulture);
}
