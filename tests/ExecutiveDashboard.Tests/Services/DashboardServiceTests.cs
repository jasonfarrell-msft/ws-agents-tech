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
                    new MeetingEmailThread(12))
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            now);

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal("Test provider", dashboard.SourceName);
        Assert.False(dashboard.IsSampleData);
        Assert.Equal(6, dashboard.Metrics.Count);
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Meetings this week" && metric.Value == "1");
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Average attendees per meeting" && metric.Value == "3" && metric.Threshold is not null && metric.Threshold.Label == "Excessive attendees" && !metric.Threshold.IsTriggered);
        Assert.Contains(dashboard.Metrics, metric => metric.Title == "Average email replies per meeting" && metric.Value == "12" && metric.Threshold is not null && metric.Threshold.Label == "Long email thread" && metric.Threshold.IsTriggered);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), dashboard.PeriodStartsAtUtc);
    }

    [Fact]
    public async Task GetDashboardAsync_MarksExactTenThresholdsAsTriggered()
    {
        var userId = "sample-executive";
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var dataSet = ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet(
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-1", "Executive sync", 30, 5, MeetingDecisionOutcome.Reached, "Done.", attendeeCount: 9, replyCount: 9),
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-2", "Executive sync 2", 30, 5, MeetingDecisionOutcome.NoneReached, "Done.", attendeeCount: 11, replyCount: 11));

        var service = new DashboardService(
            new StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(userId),
            new FixedTimeProvider(now));

        var dashboard = await service.GetDashboardAsync();

        var attendees = dashboard.Metrics.Single(metric => metric.Title == "Average attendees per meeting");
        Assert.Equal("10", attendees.Value);
        Assert.Equal("Excessive attendees", attendees.Threshold!.Label);
        Assert.True(attendees.Threshold.IsTriggered);
        Assert.Equal("Excessive meetings have 10 or more attendees.", attendees.ThresholdGuidance);

        var replies = dashboard.Metrics.Single(metric => metric.Title == "Average email replies per meeting");
        Assert.Equal("10", replies.Value);
        Assert.Equal("Long email thread", replies.Threshold!.Label);
        Assert.True(replies.Threshold.IsTriggered);
        Assert.Equal("Long threads have 10 or more replies.", replies.ThresholdGuidance);
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
        Assert.Equal("4", dashboard.Metrics.Single(metric => metric.Title == "Meetings this week").Value);
        Assert.Equal("41 min", dashboard.Metrics.Single(metric => metric.Title == "Average meeting length").Value);
        Assert.Equal("8.5", dashboard.Metrics.Single(metric => metric.Title == "Average attendees per meeting").Value);
        Assert.Equal("8", dashboard.Metrics.Single(metric => metric.Title == "Average email replies per meeting").Value);
        Assert.Equal("50%", dashboard.Metrics.Single(metric => metric.Title == "Percent under 10% talk time").Value);
        Assert.Equal("50%", dashboard.Metrics.Single(metric => metric.Title == "Percent with no decision reached").Value);
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
    public async Task GetDashboardAsync_UsesCurrentTimeAsWindowEndForCurrentWeek()
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
        Assert.Equal(currentWeekStart, provider.LastQuery!.StartsAtUtc);
        Assert.Equal(now, provider.LastQuery!.EndsAtUtc);
        Assert.Equal(now, dashboard.PeriodEndsAtUtc);
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
