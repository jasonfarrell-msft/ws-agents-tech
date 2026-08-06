using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Services;

public sealed class MeetingMetricsService : IMeetingMetricsService
{
    public const decimal ExcessiveAttendeeThreshold = 10m;
    public const decimal LongEmailReplyThreshold = 10m;

    public MeetingDashboardMetrics Calculate(MeetingDataSet dataSet, string userId)
    {
        if (dataSet.Availability != AvailabilityState.Available)
        {
            var message = dataSet.Message ?? "Meeting data is not available.";
            return CreateUnavailableMetrics(dataSet.Availability, message);
        }

        var meetings = dataSet.Meetings;
        return new MeetingDashboardMetrics(
            MetricValue<int>.Available(meetings.Count),
            CalculateAverageLength(meetings),
            CalculateAverageAttendees(meetings, dataSet.AttendeeAvailability),
            CalculateAverageEmailReplies(meetings, dataSet.EmailReplyAvailability),
            CalculateLowTalkTimePercentage(meetings, userId, dataSet.TalkTimeAvailability),
            CalculateNoDecisionReachedPercentage(meetings, dataSet.DecisionAvailability));
    }

    private static MeetingDashboardMetrics CreateUnavailableMetrics(AvailabilityState availability, string message)
    {
        var integerMetric = availability == AvailabilityState.Unknown
            ? MetricValue<int>.Unknown(message)
            : MetricValue<int>.Unavailable(message);

        var durationMetric = availability == AvailabilityState.Unknown
            ? MetricValue<TimeSpan>.Unknown(message)
            : MetricValue<TimeSpan>.Unavailable(message);

        var decimalMetric = availability == AvailabilityState.Unknown
            ? MetricValue<decimal>.Unknown(message)
            : MetricValue<decimal>.Unavailable(message);

        return new MeetingDashboardMetrics(
            integerMetric,
            durationMetric,
            decimalMetric,
            decimalMetric,
            decimalMetric,
            decimalMetric);
    }

    private static MetricValue<TimeSpan> CalculateAverageLength(IReadOnlyList<Meeting> meetings)
    {
        if (meetings.Count == 0)
        {
            return MetricValue<TimeSpan>.Available(TimeSpan.Zero);
        }

        var averageTicks = Convert.ToInt64(meetings.Average(meeting => meeting.Duration.Ticks));
        return MetricValue<TimeSpan>.Available(TimeSpan.FromTicks(averageTicks));
    }

    private static MetricValue<decimal> CalculateAverageAttendees(
        IReadOnlyList<Meeting> meetings,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Attendee count data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Attendee count data availability is unknown.");
        }

        if (meetings.Count == 0)
        {
            return MetricValue<decimal>.Unknown("No meetings were recorded in the current period, so average attendee count is unknown.");
        }

        var averageAttendees = meetings.Sum(meeting => meeting.Participants.Count) / (decimal)meetings.Count;
        return CreateAverageAttendeeMetric(averageAttendees);
    }

    private static MetricValue<decimal> CalculateAverageEmailReplies(
        IReadOnlyList<Meeting> meetings,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Email reply data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Email reply data availability is unknown.");
        }

        if (meetings.Count == 0)
        {
            return MetricValue<decimal>.Unknown("No meetings were recorded in the current period, so average email replies are unknown.");
        }

        var analyzableMeetings = meetings
            .Where(meeting => meeting.EmailThread is { ReplyCount: >= 0 })
            .ToArray();

        if (analyzableMeetings.Length == 0)
        {
            return MetricValue<decimal>.Unknown("No meetings include email reply data.");
        }

        if (analyzableMeetings.Length != meetings.Count)
        {
            return MetricValue<decimal>.Unknown("Some meetings are missing aggregate email reply counts.");
        }

        var averageReplies = analyzableMeetings.Sum(meeting => meeting.EmailThread!.ReplyCount) / (decimal)analyzableMeetings.Length;
        return CreateAverageEmailReplyMetric(averageReplies);
    }

    private static MetricValue<decimal> CreateAverageAttendeeMetric(decimal averageAttendees) =>
        MetricValue<decimal>.Available(
            RoundOneDecimal(averageAttendees),
            new MetricThresholdMetadata(
                "Excessive attendees",
                ExcessiveAttendeeThreshold,
                "attendees",
                MetricThresholdComparison.GreaterThanOrEqual,
                averageAttendees >= ExcessiveAttendeeThreshold));

    private static MetricValue<decimal> CreateAverageEmailReplyMetric(decimal averageReplies) =>
        MetricValue<decimal>.Available(
            RoundOneDecimal(averageReplies),
            new MetricThresholdMetadata(
                "Long email thread",
                LongEmailReplyThreshold,
                "replies",
                MetricThresholdComparison.GreaterThanOrEqual,
                averageReplies >= LongEmailReplyThreshold));

    private static MetricValue<decimal> CalculateLowTalkTimePercentage(
        IReadOnlyList<Meeting> meetings,
        string userId,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Talk-time data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Talk-time data availability is unknown.");
        }

        var analyzableMeetings = meetings
            .Select(meeting => new
            {
                Meeting = meeting,
                UserTalkTime = meeting.Participants.FirstOrDefault(participant => participant.Id == userId)?.TalkTime
            })
            .Where(item => item.Meeting.Duration > TimeSpan.Zero && item.UserTalkTime.HasValue)
            .ToArray();

        if (analyzableMeetings.Length == 0)
        {
            return MetricValue<decimal>.Unknown("No meetings include talk-time data for the selected user.");
        }

        var lowTalkTimeMeetings = analyzableMeetings.Count(item => item.UserTalkTime!.Value.TotalSeconds / item.Meeting.Duration.TotalSeconds < 0.10d);
        return MetricValue<decimal>.Available(ToPercentage(lowTalkTimeMeetings, analyzableMeetings.Length));
    }

    private static MetricValue<decimal> CalculateNoDecisionReachedPercentage(
        IReadOnlyList<Meeting> meetings,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Decision outcome data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Decision outcome data availability is unknown.");
        }

        var analyzableMeetings = meetings
            .Where(meeting => meeting.Decision.Outcome is MeetingDecisionOutcome.Reached or MeetingDecisionOutcome.NoneReached)
            .ToArray();

        if (analyzableMeetings.Length == 0)
        {
            return MetricValue<decimal>.Unknown("No meetings include decision outcome data.");
        }

        var noDecisionMeetings = analyzableMeetings.Count(meeting => meeting.Decision.Outcome == MeetingDecisionOutcome.NoneReached);
        return MetricValue<decimal>.Available(ToPercentage(noDecisionMeetings, analyzableMeetings.Length));
    }

    private static decimal ToPercentage(int numerator, int denominator) => Math.Round(numerator * 100m / denominator, 1, MidpointRounding.AwayFromZero);

    private static decimal RoundOneDecimal(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
