using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using ExecutiveDashboard.Tests;

namespace ExecutiveDashboard.Tests.Services;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_ExposesBindableMetricCards()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var dataSet = new MeetingDataSet(
            new[]
            {
                new Meeting(
                    "meeting-1",
                    "Executive sync",
                    now.AddHours(-2),
                    now.AddHours(-1),
                    new[]
                    {
                        new MeetingParticipant(userId, "Sample Executive", TimeSpan.FromMinutes(5)),
                        new MeetingParticipant("chief-of-staff", "Chief of Staff"),
                        new MeetingParticipant("vp-ops", "VP Operations")
                    },
                    new MeetingDecision(MeetingDecisionOutcome.NoneReached),
                    IsRecurring: true)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            now,
            RecurrenceAvailability: AvailabilityState.Available,
            AttendeeIdentityAvailability: AvailabilityState.Available,
            EmailVolumeAvailability: AvailabilityState.Available,
            EmailsReceivedCount: 124,
            EmailCalendarDayCount: 4,
            EmailConversationAnalysisAvailability: AvailabilityState.Available,
            EmailConversationCount: 20,
            ProtractedEmailConversationCount: 5);

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal("Test provider", dashboard.SourceName);
        Assert.False(dashboard.IsSampleData);
        Assert.Equal(9, dashboard.Metrics.Count);
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Meetings this week" && metric.Value == "1");
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average attendees per meeting");
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average email replies per meeting");
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Focus-time loss");
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Back-to-back meetings" && metric.Value == "0");
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Recurring-meeting load" && metric.Value == "100%");
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Attendee overlap");
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Emails received per day" && metric.Value == "31");
        Assert.Contains(dashboard.Metrics, metric =>
            metric.Title == "Percent protracted conversations"
            && metric.Value == "25%"
            && metric.HelpText.Contains("more than 10 replies", StringComparison.Ordinal));
        Assert.Contains(dashboard.Metrics, metric =>
            metric.Title == "Percent informational meetings"
            && metric.HelpText.Contains("you spoke for less than 10%", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero), dashboard.PeriodStartsAtUtc);
    }

    [Fact]
    public async Task GetDashboardAsync_UsesMinimumConfirmedProxyHelpText_WhenInformationalMetricFallsBackToDiarization()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var meetings = new[]
        {
            new Meeting(
                "meeting-1",
                "Executive sync",
                now.AddHours(-4),
                now.AddHours(-3),
                [new MeetingParticipant(userId, "Sample Executive")],
                new MeetingDecision(MeetingDecisionOutcome.Unknown),
                IsRecurring: false),
            new Meeting(
                "meeting-2",
                "Program review",
                now.AddHours(-2),
                now.AddHours(-1),
                [new MeetingParticipant(userId, "Sample Executive")],
                new MeetingDecision(MeetingDecisionOutcome.Unknown),
                IsRecurring: false)
        };
        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            now,
            SpeakerDiarizationAvailability: AvailabilityState.Available,
            DiarizedMeetingCount: 1,
            ConfirmedZeroUserSpeechMeetingCount: 1);

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        var metric = dashboard.Metrics.Single(card => card.Title == "Percent informational meetings");
        Assert.Equal("100%", metric.Value);
        Assert.Contains("Minimum confirmed proxy only", metric.HelpText, StringComparison.Ordinal);
        Assert.Contains("may undercount other under-10% meetings", metric.HelpText, StringComparison.Ordinal);
        Assert.Contains("Coverage warning", metric.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDashboardAsync_DoesNotExposeLegacyAverageEmailRepliesCard()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var dataSet = ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet(
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-1", "Executive sync", 30, 5, MeetingDecisionOutcome.Reached, "Done.", attendeeCount: 9),
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-2", "Executive sync 2", 30, 5, MeetingDecisionOutcome.NoneReached, "Done.", attendeeCount: 11));

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average email replies per meeting");
    }

    [Fact]
    public async Task GetDashboardAsync_PreservesSampleProviderState()
    {
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var service = new DashboardService(
            new SampleMeetingDataProvider(new FixedTimeProvider(now)),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor("sample-executive"),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        Assert.True(dashboard.IsSampleData);
        Assert.Equal("Deterministic sample provider", dashboard.SourceName);
        Assert.Contains("Sample data only", dashboard.SourceMessage);
        Assert.Equal("Healthy Week", dashboard.DisplayTitle);
        Assert.Equal(
            "A sustainable schedule with focused meetings, clear decisions, and protected work time.",
            dashboard.DisplayDescription);
        Assert.Equal("5", dashboard.Metrics.Single(metric => metric.Title == "Meetings this week").Value);
        Assert.Equal("33 min", dashboard.Metrics.Single(metric => metric.Title == "Average meeting length").Value);
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average attendees per meeting");
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average email replies per meeting");
        Assert.Equal("10", dashboard.Metrics.Single(metric => metric.Title == "Emails received per day").Value);
        Assert.Equal("0%", dashboard.Metrics.Single(metric => metric.Title == "Percent informational meetings").Value);
        Assert.Equal(
            "Informational means you spoke for less than 10% of the meeting. Calculated exactly from native talk-time totals.",
            dashboard.Metrics.Single(metric => metric.Title == "Percent informational meetings").HelpText);
        var protractedMetric = dashboard.Metrics.Single(metric => metric.Title == "Percent protracted conversations");
        Assert.Equal(AvailabilityState.Unavailable, protractedMetric.Availability);
        Assert.Equal("Unavailable", protractedMetric.Value);
        Assert.Equal("0%", dashboard.Metrics.Single(metric => metric.Title == "Percent with no decision reached").Value);
    }

    [Fact]
    public async Task GetDashboardAsync_LeavesDisplayTitleAndDescriptionNullForNonSampleData()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var dataSet = new MeetingDataSet(
            new[]
            {
                ExecutiveDashboard.Tests.TestData.CreateMeeting(
                    "meeting-1",
                    "Executive sync",
                    30,
                    5,
                    MeetingDecisionOutcome.Reached,
                    "Done.")
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Live provider",
            now,
            IsSampleData: false,
            SampleProfileTitle: "Malicious sample override",
            SampleProfileDescription: "This should never reach the live dashboard.");

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        Assert.False(dashboard.IsSampleData);
        Assert.Null(dashboard.DisplayTitle);
        Assert.Null(dashboard.DisplayDescription);
    }

    [Fact]
    public async Task GetDashboardAsync_DescribesNoDecisionMetricAsDecisionRelevantWhenNotApplicableMeetingsExist()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var dataSet = ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet(
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-1", "Decision reached", 30, 5, MeetingDecisionOutcome.Reached, "Approved."),
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-2", "No decision", 30, 5, MeetingDecisionOutcome.NoneReached, "Deferred."),
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-3", "Informational update", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision expected."));

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        var metric = dashboard.Metrics.Single(card => card.Title == "Percent with no decision reached");
        Assert.Equal("50%", metric.Value);
        Assert.Equal("Calculated from native decision outcomes for decision-relevant meetings only.", metric.HelpText);
    }

    [Fact]
    public async Task GetDashboardAsync_UsesRequestedWeekWindowForHistoricalWeeks()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var requestedWeekStart = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var provider = new StubMeetingDataProvider(ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet());
        var service = new DashboardService(
            provider,
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync(requestedWeekStart);

        Assert.NotNull(provider.LastQuery);
        Assert.Equal(requestedWeekStart, provider.LastQuery!.StartsAtUtc);
        Assert.Equal(requestedWeekStart.AddDays(7), provider.LastQuery!.EndsAtUtc);
        Assert.Equal(requestedWeekStart, dashboard.PeriodStartsAtUtc);
        Assert.Equal(requestedWeekStart.AddDays(7), dashboard.PeriodEndsAtUtc);
    }

    [Fact]
    public async Task GetDashboardAsync_ClampsCurrentWeekToLastCompletedFullWeek()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var provider = new StubMeetingDataProvider(ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet());
        var service = new DashboardService(
            provider,
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync(currentWeekStart);

        Assert.NotNull(provider.LastQuery);
        var lastCompletedWeekStart = currentWeekStart.AddDays(-7);
        Assert.Equal(lastCompletedWeekStart, provider.LastQuery!.StartsAtUtc);
        Assert.Equal(currentWeekStart, provider.LastQuery!.EndsAtUtc);
        Assert.Equal(currentWeekStart, dashboard.PeriodEndsAtUtc);
    }

    private sealed class StubMeetingDataProvider(MeetingDataSet dataSet) : IMeetingDataProvider
    {
        public MeetingQuery? LastQuery { get; private set; }

        public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(dataSet);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
