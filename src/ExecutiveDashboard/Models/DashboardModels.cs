namespace ExecutiveDashboard.Models;

public enum MetricThresholdComparison
{
    GreaterThanOrEqual = 0
}

public sealed record MetricThresholdMetadata(
    string Label,
    decimal TriggerValue,
    string Unit,
    MetricThresholdComparison Comparison,
    bool IsTriggered);

public sealed record MetricValue<T>(AvailabilityState Availability, T? Value, string? Message = null, MetricThresholdMetadata? Threshold = null)
    where T : struct
{
    public bool IsAvailable => Availability == AvailabilityState.Available;

    public static MetricValue<T> Available(T value, MetricThresholdMetadata? threshold = null) => new(AvailabilityState.Available, value, Threshold: threshold);

    public static MetricValue<T> Unavailable(string? message = null) => new(AvailabilityState.Unavailable, default, message);

    public static MetricValue<T> Unknown(string? message = null) => new(AvailabilityState.Unknown, default, message);
}

public sealed record MeetingDashboardMetrics(
    MetricValue<int> WeeklyMeetingCount,
    MetricValue<TimeSpan> AverageMeetingLength,
    MetricValue<decimal> AverageAttendeesPerMeeting,
    MetricValue<decimal> AverageEmailRepliesPerMeeting,
    MetricValue<decimal> LowTalkTimeMeetingPercentage,
    MetricValue<decimal> NoDecisionReachedMeetingPercentage);

public sealed record DashboardMetricCard(
    string Title,
    string Value,
    AvailabilityState Availability,
    string HelpText,
    string? ThresholdGuidance = null,
    MetricThresholdMetadata? Threshold = null);

public sealed record DashboardViewModel(
    DateTimeOffset PeriodStartsAtUtc,
    DateTimeOffset PeriodEndsAtUtc,
    string SourceName,
    AvailabilityState SourceAvailability,
    string? SourceMessage,
    bool IsSampleData,
    IReadOnlyList<DashboardMetricCard> Metrics)
{
    public static DashboardViewModel Empty { get; } = new(
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue,
        "Unknown",
        AvailabilityState.Unknown,
        null,
        false,
        Array.Empty<DashboardMetricCard>());
}
