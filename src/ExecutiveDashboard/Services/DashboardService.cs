using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;

namespace ExecutiveDashboard.Services;

public sealed class DashboardService(
    IMeetingDataProvider meetingDataProvider,
    IMeetingMetricsService metricsService,
    IDashboardRequestContextAccessor requestContextAccessor,
    TimeProvider timeProvider) : IDashboardService
{
    public async Task<DashboardViewModel> GetDashboardAsync(
        DateTimeOffset? weekStartsAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().ToUniversalTime();
        var currentWeekStart = StartOfWeek(utcNow);
        var periodStart = NormalizeWeekStart(weekStartsAtUtc ?? currentWeekStart);
        if (periodStart > currentWeekStart)
        {
            periodStart = currentWeekStart;
        }

        var periodEnd = periodStart == currentWeekStart
            ? utcNow
            : periodStart.AddDays(7);
        var requestContext = requestContextAccessor.GetCurrentContext();
        var query = new MeetingQuery(periodStart, periodEnd, requestContext.QueryUserId);
        var dataSet = await meetingDataProvider.GetMeetingsAsync(query, cancellationToken);
        var metrics = metricsService.Calculate(dataSet, requestContext.QueryUserId);

        return new DashboardViewModel(
            periodStart,
            periodEnd,
            dataSet.SourceName,
            dataSet.Availability,
            dataSet.Message,
            dataSet.IsSampleData,
            BuildMetricCards(metrics));
    }

    private static IReadOnlyList<DashboardMetricCard> BuildMetricCards(MeetingDashboardMetrics metrics) =>
        new[]
        {
            new DashboardMetricCard(
                "Meetings this week",
                FormatInteger(metrics.WeeklyMeetingCount),
                metrics.WeeklyMeetingCount.Availability,
                metrics.WeeklyMeetingCount.Message ?? "Meetings found in the selected reporting week."),
            new DashboardMetricCard(
                "Average meeting length",
                FormatDuration(metrics.AverageMeetingLength),
                metrics.AverageMeetingLength.Availability,
                metrics.AverageMeetingLength.Message ?? "Average duration across available meetings."),
            new DashboardMetricCard(
                "Average attendees per meeting",
                FormatDecimal(metrics.AverageAttendeesPerMeeting),
                metrics.AverageAttendeesPerMeeting.Availability,
                metrics.AverageAttendeesPerMeeting.Message ?? "Arithmetic mean attendee count across available meetings.",
                $"Excessive meetings have {MeetingMetricsService.ExcessiveAttendeeThreshold:0} or more attendees.",
                metrics.AverageAttendeesPerMeeting.Threshold),
            new DashboardMetricCard(
                "Average email replies per meeting",
                FormatDecimal(metrics.AverageEmailRepliesPerMeeting),
                metrics.AverageEmailRepliesPerMeeting.Availability,
                metrics.AverageEmailRepliesPerMeeting.Message ?? "Arithmetic mean aggregate email replies across available meeting threads.",
                $"Long threads have {MeetingMetricsService.LongEmailReplyThreshold:0} or more replies.",
                metrics.AverageEmailRepliesPerMeeting.Threshold),
            new DashboardMetricCard(
                "Percent under 10% talk time",
                FormatPercentage(metrics.LowTalkTimeMeetingPercentage),
                metrics.LowTalkTimeMeetingPercentage.Availability,
                metrics.LowTalkTimeMeetingPercentage.Message ?? "Percentage of meetings where the selected user spoke under 10% of the meeting duration."),
            new DashboardMetricCard(
                "Percent with no decision reached",
                FormatPercentage(metrics.NoDecisionReachedMeetingPercentage),
                metrics.NoDecisionReachedMeetingPercentage.Availability,
                metrics.NoDecisionReachedMeetingPercentage.Message ?? "Percentage of meetings where no decision was reached.")
        };

    private static string FormatInteger(MetricValue<int> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? metric.Value.Value.ToString("N0") : FormatAvailability(metric.Availability);

    private static string FormatDuration(MetricValue<TimeSpan> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value.TotalMinutes:N0} min" : FormatAvailability(metric.Availability);

    private static string FormatDecimal(MetricValue<decimal> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value:0.#}" : FormatAvailability(metric.Availability);

    private static string FormatPercentage(MetricValue<decimal> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value:0.#}%" : FormatAvailability(metric.Availability);

    private static string FormatAvailability(AvailabilityState availability) => availability switch
    {
        AvailabilityState.Unavailable => "Unavailable",
        AvailabilityState.Unknown => "Unknown",
        _ => "Unavailable"
    };

    private static DateTimeOffset NormalizeWeekStart(DateTimeOffset weekStartsAtUtc)
    {
        var utcWeekStart = weekStartsAtUtc.ToUniversalTime();
        var startOfDayUtc = new DateTimeOffset(utcWeekStart.Year, utcWeekStart.Month, utcWeekStart.Day, 0, 0, 0, TimeSpan.Zero);
        return StartOfWeek(startOfDayUtc);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset timestampUtc)
    {
        var utcTimestamp = timestampUtc.ToUniversalTime();
        var daysSinceMonday = ((int)utcTimestamp.DayOfWeek + 6) % 7;
        var startOfDay = new DateTimeOffset(utcTimestamp.Year, utcTimestamp.Month, utcTimestamp.Day, 0, 0, 0, TimeSpan.Zero);
        return startOfDay.AddDays(-daysSinceMonday);
    }
}
