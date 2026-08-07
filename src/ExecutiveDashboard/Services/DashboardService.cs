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
        var lastCompletedWeekStart = StartOfWeek(utcNow).AddDays(-7);
        var periodStart = NormalizeWeekStart(weekStartsAtUtc ?? lastCompletedWeekStart);
        if (periodStart > lastCompletedWeekStart)
        {
            periodStart = lastCompletedWeekStart;
        }

        var periodEnd = periodStart.AddDays(7);
        var requestContext = requestContextAccessor.GetCurrentContext();
        var query = new MeetingQuery(periodStart, periodEnd, requestContext.QueryUserId);
        var dataSet = await meetingDataProvider.GetMeetingsAsync(query, cancellationToken);
        var metrics = metricsService.Calculate(dataSet, requestContext.QueryUserId);

        var displayTitle = dataSet.IsSampleData ? dataSet.SampleProfileTitle : null;
        var displayDescription = dataSet.IsSampleData ? dataSet.SampleProfileDescription : null;

        return new DashboardViewModel(
            periodStart,
            periodEnd,
            dataSet.SourceName,
            dataSet.Availability,
            dataSet.Message,
            dataSet.IsSampleData,
            BuildMetricCards(metrics),
            displayTitle,
            displayDescription);
    }

    private static IReadOnlyList<DashboardMetricCard> BuildMetricCards(MeetingDashboardMetrics metrics) =>
        new[]
        {
            new DashboardMetricCard(
                "Meetings this week",
                FormatInteger(metrics.WeeklyMeetingCount),
                metrics.WeeklyMeetingCount.Availability,
                metrics.WeeklyMeetingCount.Message ?? "Meetings found in the selected reporting week.",
                IsDiagnosticRelevant: metrics.WeeklyMeetingCount.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Average meeting length",
                FormatDuration(metrics.AverageMeetingLength),
                metrics.AverageMeetingLength.Availability,
                metrics.AverageMeetingLength.Message ?? "Average duration across available meetings.",
                IsDiagnosticRelevant: metrics.AverageMeetingLength.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Percent informational meetings",
                FormatPercentage(metrics.LowTalkTimeMeetingPercentage),
                metrics.LowTalkTimeMeetingPercentage.Availability,
                metrics.LowTalkTimeMeetingPercentage.Message ?? "Informational means you spoke for less than 10% of the meeting.",
                IsDiagnosticRelevant: metrics.LowTalkTimeMeetingPercentage.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Percent with no decision reached",
                FormatPercentage(metrics.NoDecisionReachedMeetingPercentage),
                metrics.NoDecisionReachedMeetingPercentage.Availability,
                metrics.NoDecisionReachedMeetingPercentage.Message ?? "Percentage of decision-relevant meetings where no decision was reached.",
                IsDiagnosticRelevant: metrics.NoDecisionReachedMeetingPercentage.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Focus-time loss",
                FormatHours(metrics.FocusTimeLoss),
                metrics.FocusTimeLoss.Availability,
                metrics.FocusTimeLoss.Message ?? "Meeting time occurring during local work hours from 9 AM to 5 PM.",
                IsDiagnosticRelevant: metrics.FocusTimeLoss.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Back-to-back meetings",
                FormatInteger(metrics.BackToBackMeetingCount),
                metrics.BackToBackMeetingCount.Availability,
                metrics.BackToBackMeetingCount.Message ?? $"Meetings beginning within {MeetingMetricsService.BackToBackGap.TotalMinutes:0} minutes of the prior meeting.",
                IsDiagnosticRelevant: metrics.BackToBackMeetingCount.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Recurring-meeting load",
                FormatPercentage(metrics.RecurringMeetingHoursPercentage),
                metrics.RecurringMeetingHoursPercentage.Availability,
                metrics.RecurringMeetingHoursPercentage.Message ?? "Percentage of total meeting hours belonging to recurring series.",
                IsDiagnosticRelevant: metrics.RecurringMeetingHoursPercentage.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Emails received per day",
                FormatDecimal(metrics.AverageEmailsReceivedPerDay),
                metrics.AverageEmailsReceivedPerDay.Availability,
                metrics.AverageEmailsReceivedPerDay.Message ?? "Average incoming email messages across all calendar days in the selected reporting interval.",
                IsDiagnosticRelevant: metrics.AverageEmailsReceivedPerDay.IsDiagnosticRelevant),
            new DashboardMetricCard(
                "Percent protracted conversations",
                FormatPercentage(metrics.ProtractedEmailConversationPercentage),
                metrics.ProtractedEmailConversationPercentage.Availability,
                metrics.ProtractedEmailConversationPercentage.Message ?? "Percentage of email conversations containing more than 10 replies.",
                IsDiagnosticRelevant: metrics.ProtractedEmailConversationPercentage.IsDiagnosticRelevant)
        };

    private static string FormatInteger(MetricValue<int> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? metric.Value.Value.ToString("N0") : FormatAvailability(metric.Availability);

    private static string FormatDuration(MetricValue<TimeSpan> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value.TotalMinutes:N0} min" : FormatAvailability(metric.Availability);

    private static string FormatDecimal(MetricValue<decimal> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value:0.#}" : FormatAvailability(metric.Availability);

    private static string FormatPercentage(MetricValue<decimal> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value:0.#}%" : FormatAvailability(metric.Availability);

    private static string FormatHours(MetricValue<TimeSpan> metric) =>
        metric.IsAvailable && metric.Value.HasValue ? $"{metric.Value.Value.TotalHours:0.#} hr" : FormatAvailability(metric.Availability);

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
