using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;

namespace ExecutiveDashboard.Tests.Services;

public sealed class MeetingMetricsServiceTests
{
    [Fact]
    public void Calculate_ReturnsExpectedMeetingMetrics_WhenProviderDataIsAvailable()
    {
        var service = new MeetingMetricsService();
        var userId = "user-1";
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), userId, TimeSpan.FromMinutes(2), MeetingDecisionOutcome.Reached, attendeeCount: 9, replyCount: 4),
                Meeting("m2", TimeSpan.FromMinutes(60), userId, TimeSpan.FromMinutes(12), MeetingDecisionOutcome.NoneReached, attendeeCount: 11, replyCount: 12)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, userId);

        Assert.Equal(AvailabilityState.Available, metrics.WeeklyMeetingCount.Availability);
        Assert.Equal(2, metrics.WeeklyMeetingCount.Value);
        Assert.Equal(TimeSpan.FromMinutes(45), metrics.AverageMeetingLength.Value);
        Assert.Equal(AvailabilityState.Available, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal(10m, metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Available, metrics.AverageEmailRepliesPerMeeting.Availability);
        Assert.Equal(8m, metrics.AverageEmailRepliesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Available, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(AvailabilityState.Available, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.NoDecisionReachedMeetingPercentage.Value);
    }

    [Fact]
    public void Calculate_DoesNotTriggerAttendeeAndReplyThresholdsWhenArithmeticMeanIsBelowTenButRoundsUp()
    {
        var service = new MeetingMetricsService();
        var userId = "user-1";
        var meetings = Enumerable.Range(1, 20)
            .Select(index => Meeting(
                $"m{index}",
                TimeSpan.FromMinutes(30),
                userId,
                TimeSpan.FromMinutes(5),
                MeetingDecisionOutcome.Reached,
                attendeeCount: index == 1 ? 9 : 10,
                replyCount: index == 1 ? 9 : 10))
            .ToArray();
        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, userId);

        Assert.Equal(10m, metrics.AverageAttendeesPerMeeting.Value);
        Assert.NotNull(metrics.AverageAttendeesPerMeeting.Threshold);
        Assert.False(metrics.AverageAttendeesPerMeeting.Threshold!.IsTriggered);
        Assert.Equal(10m, metrics.AverageEmailRepliesPerMeeting.Value);
        Assert.NotNull(metrics.AverageEmailRepliesPerMeeting.Threshold);
        Assert.False(metrics.AverageEmailRepliesPerMeeting.Threshold!.IsTriggered);
    }

    [Fact]
    public void Calculate_TriggersAttendeeAndReplyThresholdsAtAnExactTenPointZeroArithmeticMean()
    {
        var service = new MeetingMetricsService();
        var userId = "user-1";
        var meetings = Enumerable.Range(1, 20)
            .Select(index => Meeting(
                $"m{index}",
                TimeSpan.FromMinutes(30),
                userId,
                TimeSpan.FromMinutes(5),
                MeetingDecisionOutcome.Reached,
                attendeeCount: 10,
                replyCount: 10))
            .ToArray();
        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, userId);

        Assert.Equal(10m, metrics.AverageAttendeesPerMeeting.Value);
        Assert.NotNull(metrics.AverageAttendeesPerMeeting.Threshold);
        Assert.True(metrics.AverageAttendeesPerMeeting.Threshold!.IsTriggered);
        Assert.Equal(10m, metrics.AverageEmailRepliesPerMeeting.Value);
        Assert.NotNull(metrics.AverageEmailRepliesPerMeeting.Threshold);
        Assert.True(metrics.AverageEmailRepliesPerMeeting.Threshold!.IsTriggered);
    }

    [Fact]
    public void Calculate_ReturnsAvailableZeroMetricsForEmptyData()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            Array.Empty<Meeting>(),
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Available, metrics.WeeklyMeetingCount.Availability);
        Assert.Equal(0, metrics.WeeklyMeetingCount.Value);
        Assert.Equal(AvailabilityState.Available, metrics.AverageMeetingLength.Availability);
        Assert.Equal(TimeSpan.Zero, metrics.AverageMeetingLength.Value);
        Assert.Equal(AvailabilityState.Unknown, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal("No meetings were recorded in the current period, so average attendee count is unknown.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Equal(AvailabilityState.Unknown, metrics.AverageEmailRepliesPerMeeting.Availability);
        Assert.Null(metrics.AverageEmailRepliesPerMeeting.Value);
        Assert.Equal("No meetings were recorded in the current period, so average email replies are unknown.", metrics.AverageEmailRepliesPerMeeting.Message);
        Assert.Equal(AvailabilityState.Unknown, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal("No meetings include talk-time data for the selected user.", metrics.LowTalkTimeMeetingPercentage.Message);
        Assert.Equal(AvailabilityState.Unknown, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal("No meetings include decision outcome data.", metrics.NoDecisionReachedMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_ReturnsUnknownMetricAvailabilityWhenAttendeeAndReplyDataAreUnknown()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), "user-1", TimeSpan.FromMinutes(5), MeetingDecisionOutcome.Reached, attendeeCount: 3, replyCount: 8)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unknown, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal("Attendee count data availability is unknown.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Unknown, metrics.AverageEmailRepliesPerMeeting.Availability);
        Assert.Equal("Email reply data availability is unknown.", metrics.AverageEmailRepliesPerMeeting.Message);
        Assert.Null(metrics.AverageEmailRepliesPerMeeting.Value);
    }

    [Fact]
    public void Calculate_ReturnsUnavailableMetricAvailabilityWhenAttendeeAndReplyDataAreUnavailable()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), "user-1", TimeSpan.FromMinutes(5), MeetingDecisionOutcome.Reached, attendeeCount: 3, replyCount: 8)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal("Attendee count data is not supported by the current provider.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageEmailRepliesPerMeeting.Availability);
        Assert.Equal("Email reply data is not supported by the current provider.", metrics.AverageEmailRepliesPerMeeting.Message);
        Assert.Null(metrics.AverageEmailRepliesPerMeeting.Value);
    }

    [Fact]
    public void Calculate_PropagatesUnavailableSourceState()
    {
        var service = new MeetingMetricsService();
        var dataSet = MeetingDataSet.Unavailable(
            new MeetingQuery(DateTimeOffset.Parse("2026-07-29T00:00:00Z"), DateTimeOffset.Parse("2026-08-05T00:00:00Z"), "user-1"),
            "WorkIQ",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            "WorkIQ is unavailable.");

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unavailable, metrics.WeeklyMeetingCount.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.WeeklyMeetingCount.Message);
        Assert.Null(metrics.WeeklyMeetingCount.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageMeetingLength.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.AverageMeetingLength.Message);
        Assert.Null(metrics.AverageMeetingLength.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageEmailRepliesPerMeeting.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.AverageEmailRepliesPerMeeting.Message);
        Assert.Null(metrics.AverageEmailRepliesPerMeeting.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.LowTalkTimeMeetingPercentage.Message);
        Assert.Null(metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.NoDecisionReachedMeetingPercentage.Message);
        Assert.Null(metrics.NoDecisionReachedMeetingPercentage.Value);
    }

    private static Meeting Meeting(
        string id,
        TimeSpan duration,
        string userId,
        TimeSpan? talkTime,
        MeetingDecisionOutcome decisionOutcome,
        int attendeeCount = 1,
        int? replyCount = null)
    {
        var startsAt = DateTimeOffset.Parse("2026-08-04T10:00:00Z");
        var participants = Enumerable.Range(1, attendeeCount)
            .Select(index => index == 1
                ? new MeetingParticipant(userId, "User", talkTime)
                : new MeetingParticipant($"attendee-{index}", $"Attendee {index}"))
            .ToArray();

        return new Meeting(
            id,
            id,
            startsAt,
            startsAt.Add(duration),
            participants,
            new MeetingDecision(decisionOutcome),
            replyCount.HasValue ? new MeetingEmailThread(replyCount.Value) : null);
    }
}
