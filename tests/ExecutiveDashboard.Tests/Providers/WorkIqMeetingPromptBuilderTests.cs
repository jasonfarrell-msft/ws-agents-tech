using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class WorkIqMeetingPromptBuilderTests
{
    private static readonly MeetingQuery MidnightExclusiveWeekQuery = new(
        new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
        "sample-user");

    public static TheoryData<string, string> MeetingDerivedPrompts => new()
    {
        { nameof(WorkIqMeetingPromptBuilder.BuildCalendarPrompt), WorkIqMeetingPromptBuilder.BuildCalendarPrompt(MidnightExclusiveWeekQuery) },
        { nameof(WorkIqMeetingPromptBuilder.BuildDirectMeetingPrompt), WorkIqMeetingPromptBuilder.BuildDirectMeetingPrompt(MidnightExclusiveWeekQuery) },
        { nameof(WorkIqMeetingPromptBuilder.BuildDiarizationPrompt), WorkIqMeetingPromptBuilder.BuildDiarizationPrompt(MidnightExclusiveWeekQuery) },
        { nameof(WorkIqMeetingPromptBuilder.BuildDecisionAnalysisPrompt), WorkIqMeetingPromptBuilder.BuildDecisionAnalysisPrompt(MidnightExclusiveWeekQuery) }
    };

    [Theory]
    [MemberData(nameof(MeetingDerivedPrompts))]
    public void MeetingDerivedPrompts_ApplySharedAttendedMeetingPopulationAndExclusions(string promptName, string prompt)
    {
        AssertContainsAttendedMeetingPopulation(promptName, prompt);
    }

    [Fact]
    public void BuildCalendarPrompt_UsesSameUtcCalendarDate_WhenEndTimeIsNotMidnight()
    {
        var query = new MeetingQuery(
            new DateTimeOffset(2026, 7, 29, 19, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 5, 19, 0, 0, TimeSpan.Zero),
            "sample-user");

        var prompt = WorkIqMeetingPromptBuilder.BuildCalendarPrompt(query);

        Assert.Contains(
            "Query the signed-in user's Microsoft 365 calendar for meetings the user attended from Wednesday July 29, 2026 through Wednesday August 5, 2026 inclusive.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarPrompt_PreservesInclusiveMidnightRule_AcrossYearBoundary()
    {
        var query = new MeetingQuery(
            new DateTimeOffset(2026, 12, 29, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero),
            "sample-user");

        var prompt = WorkIqMeetingPromptBuilder.BuildCalendarPrompt(query);

        Assert.Contains(
            "Query the signed-in user's Microsoft 365 calendar for meetings the user attended from Tuesday December 29, 2026 through Monday January 4, 2027 inclusive.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCalendarPrompt_UsesMinimalTimingAndRecurrenceOnlyShape()
    {
        var prompt = WorkIqMeetingPromptBuilder.BuildCalendarPrompt(MidnightExclusiveWeekQuery);

        Assert.Contains(
            "{\"meetings\":[{\"startsAtUtc\":\"2026-08-03T13:00:00Z\",\"endsAtUtc\":\"2026-08-03T13:30:00Z\",\"isRecurring\":true}],\"availability\":{\"meetings\":\"available\",\"recurrence\":\"available\"}}",
            prompt,
            StringComparison.Ordinal);
        Assert.DoesNotContain("userTalkTimeSeconds", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("decisionOutcome", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("attendeeCount", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("emailReplyCount", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"talkTime\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"decisions\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"attendees\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"emailReplies\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDirectMeetingPrompt_UsesRichNativeMetricsShapeWithExplicitUnsupportedAggregates()
    {
        var prompt = WorkIqMeetingPromptBuilder.BuildDirectMeetingPrompt(MidnightExclusiveWeekQuery);

        Assert.Contains(
            "{\"meetings\":[{\"startsAtUtc\":\"2026-08-03T13:00:00Z\",\"endsAtUtc\":\"2026-08-03T13:30:00Z\",\"attendeeCount\":8,\"userTalkTimeSeconds\":240,\"decisionOutcome\":\"reached\",\"isRecurring\":true}],\"availability\":{\"meetings\":\"available\",\"talkTime\":\"available\",\"decisions\":\"available\",\"attendees\":\"available\",\"emailReplies\":\"unavailable\",\"recurrence\":\"available\",\"speakerDiarization\":\"unavailable\",\"emailVolume\":\"unavailable\",\"decisionAnalysis\":\"unavailable\",\"emailConversationAnalysis\":\"unavailable\"}}",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains("decisionOutcome must be one of", prompt, StringComparison.Ordinal);
        Assert.Contains("Keep emailReplies, speakerDiarization, emailVolume, decisionAnalysis, and emailConversationAnalysis as \"unavailable\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("emailsReceived", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("conversationsWithMoreThanTenReplies", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("attendeeKeys", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("decisionSummary", prompt, StringComparison.Ordinal);
    }

    private static void AssertContainsAttendedMeetingPopulation(string promptName, string prompt)
    {
        Assert.True(
            prompt.Contains(
                "Query the signed-in user's Microsoft 365 calendar for meetings the user attended from Monday July 27, 2026 through Sunday August 2, 2026 inclusive.",
                StringComparison.Ordinal),
            $"{promptName} should apply the shared attended-meeting population and completed calendar week.");
        Assert.True(
            prompt.Contains(
                "Exclude all-day events, out-of-office events, declined meetings, canceled meetings, non-blocking events, and personal appointments.",
                StringComparison.Ordinal),
            $"{promptName} should apply the shared exclusions.");
    }
}
