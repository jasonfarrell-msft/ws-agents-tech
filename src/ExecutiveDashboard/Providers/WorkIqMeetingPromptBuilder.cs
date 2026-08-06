using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public static class WorkIqMeetingPromptBuilder
{
    public static string BuildMeetingPrompt(MeetingQuery query) =>
        $$"""
        Return ONLY one strict JSON object for the signed-in user's Work IQ meeting data needed to render all six executive dashboard metrics for this UTC interval:
        startsAtUtc={{query.StartsAtUtc:O}}
        endsAtUtc={{query.EndsAtUtc:O}}

        Metrics covered by this single aggregate response:
        1. weekly meeting count
        2. average meeting length
        3. average attendees per meeting
        4. average email replies per meeting
        5. percentage of meetings where the signed-in user spoke under 10% of the time
        6. percentage of meetings with no decision reached

        Required shape:
        {"meetings":[{"id":"opaque string","title":"string","startsAtUtc":"ISO-8601 UTC","endsAtUtc":"ISO-8601 UTC","attendeeCount":null,"userTalkTimeSeconds":null,"decisionOutcome":"unknown","decisionSummary":null,"emailReplyCount":null}],"availability":{"meetings":"available|unavailable|unknown","talkTime":"available|unavailable|unknown","decisions":"available|unavailable|unknown","attendees":"available|unavailable|unknown","emailReplies":"available|unavailable|unknown"},"message":null}

        Use null and availability "unknown" for fields you cannot validate from Work IQ. Do not estimate, infer, include meeting transcript content, or add markdown.
        """;
}
