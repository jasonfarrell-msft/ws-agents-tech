using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests;

public sealed class MeetingMetricsServiceTests
{
    [Fact]
    public void Calculate_ReturnsWeeklyMeetingCountAndAverageDuration()
    {
        var service = new MeetingMetricsService();
        var dataSet = TestData.CreateAvailableDataSet(
            TestData.CreateMeeting("meeting-1", "Weekly operating review", 30, 5, MeetingDecisionOutcome.Reached, "Approved rollout."),
            TestData.CreateMeeting("meeting-2", "Pipeline risk review", 60, 10, MeetingDecisionOutcome.NoneReached, "Deferred launch sequencing."));

        var metrics = service.Calculate(dataSet, TestData.UserId);

        Assert.Equal(AvailabilityState.Available, metrics.WeeklyMeetingCount.Availability);
        Assert.Equal(2, metrics.WeeklyMeetingCount.Value);
        Assert.Equal(AvailabilityState.Available, metrics.AverageMeetingLength.Availability);
        Assert.Equal(TimeSpan.FromMinutes(45), metrics.AverageMeetingLength.Value);
    }

    [Theory]
    [InlineData(9, 100d)]
    [InlineData(10, 0d)]
    [InlineData(11, 0d)]
    public void Calculate_UsesAStrictUnderTenPercentThreshold(int userTalkMinutes, double expectedLowTalkPercentage)
    {
        var service = new MeetingMetricsService();
        var dataSet = TestData.CreateAvailableDataSet(
            TestData.CreateMeeting("meeting-1", "Executive review", 100, userTalkMinutes, MeetingDecisionOutcome.Reached, "Decision reached."));

        var metrics = service.Calculate(dataSet, TestData.UserId);

        Assert.Equal(AvailabilityState.Available, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal((decimal)expectedLowTalkPercentage, metrics.LowTalkTimeMeetingPercentage.Value);
    }

    [Fact]
    public void Calculate_OnlyCountsNoneReachedMeetingsForNoDecisionPercentage()
    {
        var service = new MeetingMetricsService();
        var dataSet = TestData.CreateAvailableDataSet(
            TestData.CreateMeeting("meeting-1", "Weekly operating review", 30, 5, MeetingDecisionOutcome.Reached, "Approved rollout."),
            TestData.CreateMeeting("meeting-2", "Pipeline risk review", 30, 5, MeetingDecisionOutcome.NoneReached, "Deferred launch sequencing."),
            TestData.CreateMeeting("meeting-3", "Budget follow-up", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision recorded."));

        var metrics = service.Calculate(dataSet, TestData.UserId);

        Assert.Equal(AvailabilityState.Available, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.NoDecisionReachedMeetingPercentage.Value);
    }

    [Fact]
    public void Calculate_ReturnsAverageAttendees()
    {
        var service = new MeetingMetricsService();
        var dataSet = TestData.CreateAvailableDataSet(
            TestData.CreateMeeting("meeting-1", "Small thread", 30, 5, MeetingDecisionOutcome.Reached, "Done.", attendeeCount: 9),
            TestData.CreateMeeting("meeting-2", "Threshold thread", 30, 5, MeetingDecisionOutcome.Reached, "Done.", attendeeCount: 10),
            TestData.CreateMeeting("meeting-3", "Large thread", 30, 5, MeetingDecisionOutcome.Reached, "Done.", attendeeCount: 11));

        var metrics = service.Calculate(dataSet, TestData.UserId);

        Assert.Equal(AvailabilityState.Available, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal(10m, metrics.AverageAttendeesPerMeeting.Value);
    }

    [Fact]
    public void Calculate_ReturnsZeroCountAndUnknownPercentagesForEmptyData()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            Array.Empty<Meeting>(),
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test source",
            TestData.FixedRetrievedAtUtc);

        var metrics = service.Calculate(dataSet, TestData.UserId);

        Assert.Equal(AvailabilityState.Available, metrics.WeeklyMeetingCount.Availability);
        Assert.Equal(0, metrics.WeeklyMeetingCount.Value);
        Assert.Equal(AvailabilityState.Available, metrics.AverageMeetingLength.Availability);
        Assert.Equal(TimeSpan.Zero, metrics.AverageMeetingLength.Value);
        Assert.Equal(AvailabilityState.Unknown, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal("No meetings were recorded in the current period, so average attendee count is unknown.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Equal(AvailabilityState.Unknown, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal("No meetings include talk-time data for the selected user.", metrics.LowTalkTimeMeetingPercentage.Message);
        Assert.Equal(AvailabilityState.Unknown, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal("No meetings include decision outcome data.", metrics.NoDecisionReachedMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_PreservesUnknownSourceState()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            Array.Empty<Meeting>(),
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            "WorkIQ",
            TestData.FixedRetrievedAtUtc,
            "WorkIQ availability is unknown.");

        var metrics = service.Calculate(dataSet, TestData.UserId);

        AssertUnknown(metrics.WeeklyMeetingCount);
        AssertUnknown(metrics.AverageMeetingLength);
        AssertUnknown(metrics.AverageAttendeesPerMeeting);
        AssertUnknown(metrics.LowTalkTimeMeetingPercentage);
        AssertUnknown(metrics.NoDecisionReachedMeetingPercentage);
    }

    private static void AssertUnknown<T>(MetricValue<T> metric)
        where T : struct
    {
        Assert.Equal(AvailabilityState.Unknown, metric.Availability);
        Assert.Equal("WorkIQ availability is unknown.", metric.Message);
        Assert.Null(metric.Value);
    }
}

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_FormatsAggregateMetricsIntoCards()
    {
        var now = TestData.FixedRetrievedAtUtc;
        var dataSet = TestData.CreateAvailableDataSet(
            TestData.CreateMeeting("meeting-1", "Weekly operating review", 30, 5, MeetingDecisionOutcome.Reached, "Approved rollout.", dayOffset: -2, attendeeCount: 9),
            TestData.CreateMeeting("meeting-2", "Pipeline risk review", 60, 18, MeetingDecisionOutcome.NoneReached, "Deferred launch sequencing.", dayOffset: -1, attendeeCount: 11));
        var provider = new StubMeetingDataProvider(dataSet);
        var service = CreateDashboardService(provider, now);

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero), dashboard.PeriodStartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), dashboard.PeriodEndsAtUtc);
        Assert.Equal("Test source", dashboard.SourceName);
        Assert.Equal(AvailabilityState.Available, dashboard.SourceAvailability);
        Assert.Equal("Test source metrics", dashboard.SourceMessage);
        Assert.False(dashboard.IsSampleData);
        Assert.Equal("2", FindCard(dashboard.Metrics, "Meetings this week").Value);
        Assert.Equal("45 min", FindCard(dashboard.Metrics, "Average meeting length").Value);
        Assert.DoesNotContain(dashboard.Metrics, metric => metric.Title == "Average email replies per meeting");
        Assert.Equal("0%", FindCard(dashboard.Metrics, "Percent informational meetings").Value);
        Assert.Equal("50%", FindCard(dashboard.Metrics, "Percent with no decision reached").Value);
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero), provider.LastQuery!.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), provider.LastQuery!.EndsAtUtc);
        Assert.Equal(TestData.UserId, provider.LastQuery!.UserId);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsUnavailableCardsAndNoMeetingsWhenSourceUnavailable()
    {
        var now = TestData.FixedRetrievedAtUtc;
        var unavailableDataSet = MeetingDataSet.Unavailable(
            new MeetingQuery(now.AddDays(-7), now, TestData.UserId),
            "WorkIQ",
            now,
            "WorkIQ is unavailable.");
        var provider = new StubMeetingDataProvider(unavailableDataSet);
        var service = CreateDashboardService(provider, now);

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal(AvailabilityState.Unavailable, dashboard.SourceAvailability);
        Assert.Equal("WorkIQ", dashboard.SourceName);
        Assert.Equal("WorkIQ is unavailable.", dashboard.SourceMessage);
        Assert.All(dashboard.Metrics, card =>
        {
            Assert.Equal(AvailabilityState.Unavailable, card.Availability);
            Assert.Equal("Unavailable", card.Value);
        });
    }

    private static DashboardService CreateDashboardService(StubMeetingDataProvider provider, DateTimeOffset now)
    {
        return new DashboardService(
            provider,
            new MeetingMetricsService(),
            new StubDashboardRequestContextAccessor(TestData.UserId),
            new FixedTimeProvider(now));
    }

    private static DashboardMetricCard FindCard(IReadOnlyList<DashboardMetricCard> cards, string title) =>
        cards.Single(card => card.Title == title);
}

internal static class TestData
{
    public const string UserId = "sample-executive";
    public static readonly DateTimeOffset FixedRetrievedAtUtc = new(2026, 8, 5, 17, 53, 37, TimeSpan.Zero);

    public static MeetingDataSet CreateAvailableDataSet(
        IEnumerable<Meeting> meetings,
        AvailabilityState attendeeAvailability = AvailabilityState.Available) =>
        new(
            meetings.ToArray(),
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            attendeeAvailability,
            "Test source",
            FixedRetrievedAtUtc,
            "Test source metrics");

    public static MeetingDataSet CreateAvailableDataSet(params Meeting[] meetings) => CreateAvailableDataSet((IEnumerable<Meeting>)meetings);

    public static Meeting CreateMeeting(
        string id,
        string title,
        int durationMinutes,
        int userTalkMinutes,
        MeetingDecisionOutcome decisionOutcome,
        string decisionSummary,
        int dayOffset = 0,
        int attendeeCount = 2)
    {
        var startsAtUtc = FixedRetrievedAtUtc.AddDays(dayOffset);
        var participants = Enumerable.Range(1, attendeeCount)
            .Select(index => index == 1
                ? new MeetingParticipant(UserId, "Sample Executive", TimeSpan.FromMinutes(userTalkMinutes))
                : new MeetingParticipant($"attendee-{index}", $"Attendee {index}"))
            .ToArray();

        return new Meeting(
            id,
            title,
            startsAtUtc,
            startsAtUtc.AddMinutes(durationMinutes),
            participants,
            new MeetingDecision(decisionOutcome, decisionSummary));
    }
}

internal sealed class StubMeetingDataProvider(MeetingDataSet dataSet) : IMeetingDataProvider
{
    private readonly MeetingDataSet _dataSet = dataSet;

    public MeetingQuery? LastQuery { get; private set; }

    public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(_dataSet);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class StubDashboardRequestContextAccessor(string queryUserId) : IDashboardRequestContextAccessor
{
    public DashboardRequestContext GetCurrentContext() =>
        new(
            DashboardOperatingMode.Sample,
            queryUserId,
            $"Sample user ({queryUserId})",
            false,
            true,
            false,
            false,
            LiveDataAccessMode.None,
            "Mission Control live mode unavailable",
            "Live mode is not configured.");
}
