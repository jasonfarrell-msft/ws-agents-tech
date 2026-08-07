using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Services;

public sealed class MeetingMetricsService : IMeetingMetricsService
{
    public const decimal ExcessiveAttendeeThreshold = 10m;
    public static readonly TimeSpan BackToBackGap = TimeSpan.FromMinutes(10);
    private readonly TimeZoneInfo workTimeZone;

    public MeetingMetricsService(IOptions<WorkIqOptions> options)
        : this(TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone))
    {
    }

    public MeetingMetricsService()
        : this(TimeZoneInfo.Local)
    {
    }

    private MeetingMetricsService(TimeZoneInfo workTimeZone)
    {
        this.workTimeZone = workTimeZone;
    }

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
            CalculateLowTalkTimePercentage(
                meetings,
                userId,
                dataSet.TalkTimeAvailability,
                dataSet.SpeakerDiarizationAvailability,
                dataSet.DiarizedMeetingCount,
                dataSet.ConfirmedZeroUserSpeechMeetingCount),
            CalculateNoDecisionReachedPercentage(
                meetings,
                dataSet.DecisionAvailability,
                dataSet.DecisionAnalysisAvailability,
                dataSet.DecisionRelevantMeetingCount,
                dataSet.NoDecisionReachedMeetingCount),
            CalculateFocusTimeLoss(meetings, workTimeZone),
            CalculateBackToBackMeetingCount(meetings),
            CalculateRecurringMeetingHoursPercentage(meetings, dataSet.RecurrenceAvailability),
            CalculateAttendeeOverlapMeetingPercentage(meetings, userId, dataSet.AttendeeIdentityAvailability),
            CalculateAverageEmailsReceivedPerDay(
                dataSet.EmailVolumeAvailability,
                dataSet.EmailsReceivedCount,
                dataSet.EmailCalendarDayCount),
            CalculateProtractedEmailConversationPercentage(
                dataSet.EmailConversationAnalysisAvailability,
                dataSet.EmailConversationCount,
                dataSet.ProtractedEmailConversationCount));
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
            durationMetric,
            integerMetric,
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
            return MetricValue<decimal>.Unknown(
                "No meetings were recorded in the current period, so average attendee count is unknown.",
                isDiagnosticRelevant: false);
        }

        var averageAttendees = meetings.Sum(meeting => meeting.Participants.Count) / (decimal)meetings.Count;
        return CreateAverageAttendeeMetric(averageAttendees);
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

    private static MetricValue<decimal> CalculateLowTalkTimePercentage(
        IReadOnlyList<Meeting> meetings,
        string userId,
        AvailabilityState talkTimeAvailability,
        AvailabilityState speakerDiarizationAvailability,
        int? diarizedMeetingCount,
        int? confirmedZeroUserSpeechMeetingCount)
    {
        if (meetings.Count == 0)
        {
            return MetricValue<decimal>.Unknown(
                "No meetings include talk-time data for the selected user.",
                isDiagnosticRelevant: false);
        }

        if (talkTimeAvailability != AvailabilityState.Available)
        {
            if (speakerDiarizationAvailability == AvailabilityState.Available)
            {
                if (diarizedMeetingCount is not > 0 || !confirmedZeroUserSpeechMeetingCount.HasValue)
                {
                    return MetricValue<decimal>.Unknown("No meetings include accessible speaker diarization.");
                }

                var warning = "Informational means you spoke for less than 10% of the meeting. Minimum confirmed proxy only: without segment timing, this value counts only diarized meetings with no speech attributed to you and may undercount other under-10% meetings.";
                if (diarizedMeetingCount.Value < meetings.Count)
                {
                    warning += " Coverage warning: Some meetings lacked diarization; this percentage covers diarized meetings only.";
                }

                return new MetricValue<decimal>(
                    AvailabilityState.Available,
                    ToPercentage(confirmedZeroUserSpeechMeetingCount.Value, diarizedMeetingCount.Value),
                    warning);
            }

            return speakerDiarizationAvailability == AvailabilityState.Unknown
                ? MetricValue<decimal>.Unknown("Speaker diarization availability is unknown.")
                : MetricValue<decimal>.Unavailable("Talk-time and speaker diarization data are not supported by the current provider.");
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
        return new MetricValue<decimal>(
            AvailabilityState.Available,
            ToPercentage(lowTalkTimeMeetings, analyzableMeetings.Length),
            "Informational means you spoke for less than 10% of the meeting. Calculated exactly from native talk-time totals.");
    }

    private static MetricValue<decimal> CalculateProtractedEmailConversationPercentage(
        AvailabilityState availability,
        int? conversationCount,
        int? protractedConversationCount)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Email conversation reply analysis is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Email conversation reply analysis availability is unknown.");
        }

        if (!conversationCount.HasValue || !protractedConversationCount.HasValue)
        {
            return MetricValue<decimal>.Unknown("Work IQ did not return complete email conversation counts.");
        }

        if (conversationCount.Value == 0)
        {
            return MetricValue<decimal>.Unknown(
                "No email conversations were received during the selected week.",
                isDiagnosticRelevant: false);
        }

        return MetricValue<decimal>.Available(
            ToPercentage(protractedConversationCount.Value, conversationCount.Value));
    }

    private static MetricValue<decimal> CalculateNoDecisionReachedPercentage(
        IReadOnlyList<Meeting> meetings,
        AvailabilityState decisionAvailability,
        AvailabilityState decisionAnalysisAvailability,
        int? analyzedMeetingCount,
        int? noDecisionReachedMeetingCount)
    {
        if (meetings.Count == 0)
        {
            return MetricValue<decimal>.Unknown(
                "No meetings include decision outcome data.",
                isDiagnosticRelevant: false);
        }

        if (decisionAvailability != AvailabilityState.Available)
        {
            if (decisionAnalysisAvailability == AvailabilityState.Available)
            {
                if (!noDecisionReachedMeetingCount.HasValue)
                {
                    return MetricValue<decimal>.Unknown("No meetings include accessible content for decision analysis.");
                }

                if (analyzedMeetingCount == 0)
                {
                    return MetricValue<decimal>.Unknown("No decision-relevant meetings include accessible content for decision analysis.");
                }

                if (analyzedMeetingCount is not > 0)
                {
                    return MetricValue<decimal>.Unknown("No meetings include accessible content for decision analysis.");
                }

                var message = analyzedMeetingCount.Value < meetings.Count
                    ? "Coverage warning: Some meetings lacked decision content or were not decision-oriented; this percentage covers decision-relevant meetings with accessible content only."
                    : "Calculated from decision-relevant meeting summaries and transcripts.";
                return new MetricValue<decimal>(
                    AvailabilityState.Available,
                    ToPercentage(noDecisionReachedMeetingCount.Value, analyzedMeetingCount.Value),
                    message);
            }

            return decisionAnalysisAvailability == AvailabilityState.Unknown
                ? MetricValue<decimal>.Unknown("Decision analysis availability is unknown.")
                : MetricValue<decimal>.Unavailable("Decision outcome analysis is not supported by the current provider.");
        }

        var analyzableMeetings = meetings
            .Where(meeting => meeting.Decision.Outcome is MeetingDecisionOutcome.Reached or MeetingDecisionOutcome.NoneReached)
            .ToArray();

        if (analyzableMeetings.Length == 0)
        {
            return meetings.Count == 0
                ? MetricValue<decimal>.Unknown("No meetings include decision outcome data.")
                : MetricValue<decimal>.Unknown("No decision-relevant meetings include native decision outcome data.");
        }

        var noDecisionMeetings = analyzableMeetings.Count(meeting => meeting.Decision.Outcome == MeetingDecisionOutcome.NoneReached);
        return new MetricValue<decimal>(
            AvailabilityState.Available,
            ToPercentage(noDecisionMeetings, analyzableMeetings.Length),
            "Calculated from native decision outcomes for decision-relevant meetings only.");
    }

    private static MetricValue<TimeSpan> CalculateFocusTimeLoss(
        IReadOnlyList<Meeting> meetings,
        TimeZoneInfo timeZone)
    {
        var intervals = meetings
            .SelectMany(meeting => GetWorkHoursIntervals(meeting, timeZone))
            .OrderBy(interval => interval.Start)
            .ToArray();
        if (intervals.Length == 0)
        {
            return MetricValue<TimeSpan>.Available(TimeSpan.Zero);
        }

        var total = TimeSpan.Zero;
        var currentStart = intervals[0].Start;
        var currentEnd = intervals[0].End;
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.Start <= currentEnd)
            {
                if (interval.End > currentEnd)
                {
                    currentEnd = interval.End;
                }
                continue;
            }

            total += currentEnd - currentStart;
            currentStart = interval.Start;
            currentEnd = interval.End;
        }

        return MetricValue<TimeSpan>.Available(total + (currentEnd - currentStart));
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> GetWorkHoursIntervals(
        Meeting meeting,
        TimeZoneInfo timeZone)
    {
        var localStart = TimeZoneInfo.ConvertTime(meeting.StartsAtUtc, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(meeting.EndsAtUtc, timeZone);

        for (var date = localStart.Date; date <= localEnd.Date; date = date.AddDays(1))
        {
            var workStart = new DateTimeOffset(date.AddHours(9), timeZone.GetUtcOffset(date.AddHours(9)));
            var workEnd = new DateTimeOffset(date.AddHours(17), timeZone.GetUtcOffset(date.AddHours(17)));
            var overlapStart = localStart > workStart ? localStart : workStart;
            var overlapEnd = localEnd < workEnd ? localEnd : workEnd;
            if (overlapEnd > overlapStart)
            {
                yield return (overlapStart, overlapEnd);
            }
        }
    }

    private static MetricValue<int> CalculateBackToBackMeetingCount(IReadOnlyList<Meeting> meetings)
    {
        var ordered = meetings.OrderBy(meeting => meeting.StartsAtUtc).ToArray();
        if (ordered.Length == 0)
        {
            return MetricValue<int>.Available(0);
        }

        var count = 0;
        var occupiedUntil = ordered[0].EndsAtUtc;
        foreach (var meeting in ordered.Skip(1))
        {
            if (meeting.StartsAtUtc < occupiedUntil)
            {
                if (meeting.EndsAtUtc > occupiedUntil)
                {
                    occupiedUntil = meeting.EndsAtUtc;
                }
                continue;
            }

            if (meeting.StartsAtUtc - occupiedUntil <= BackToBackGap)
            {
                count++;
            }
            occupiedUntil = meeting.EndsAtUtc;
        }

        return MetricValue<int>.Available(count);
    }

    private static MetricValue<decimal> CalculateRecurringMeetingHoursPercentage(
        IReadOnlyList<Meeting> meetings,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Recurring-series data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown || meetings.Any(meeting => !meeting.IsRecurring.HasValue))
        {
            return MetricValue<decimal>.Unknown("Recurring-series data availability is unknown.");
        }

        var totalTicks = meetings.Sum(meeting => meeting.Duration.Ticks);
        if (totalTicks == 0)
        {
            return MetricValue<decimal>.Available(0m);
        }

        var recurringTicks = meetings
            .Where(meeting => meeting.IsRecurring == true)
            .Sum(meeting => meeting.Duration.Ticks);
        return MetricValue<decimal>.Available(Math.Round(recurringTicks * 100m / totalTicks, 1, MidpointRounding.AwayFromZero));
    }

    private static MetricValue<decimal> CalculateAttendeeOverlapMeetingPercentage(
        IReadOnlyList<Meeting> meetings,
        string userId,
        AvailabilityState availability)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Privacy-safe attendee identity data is not supported by the current provider.");
        }

        if (availability == AvailabilityState.Unknown)
        {
            return MetricValue<decimal>.Unknown("Attendee overlap data availability is unknown.");
        }

        if (meetings.Count == 0)
        {
            return MetricValue<decimal>.Available(0m);
        }

        var attendeeSets = meetings
            .Select(meeting => meeting.Participants
                .Where(participant => !string.Equals(participant.Id, userId, StringComparison.Ordinal))
                .Select(participant => participant.Id)
                .ToHashSet(StringComparer.Ordinal))
            .ToArray();
        var attendeeFrequency = attendeeSets
            .SelectMany(attendees => attendees)
            .GroupBy(attendee => attendee, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var overlappingMeetings = attendeeSets.Count(attendees =>
            attendees.Any(attendee => attendeeFrequency[attendee] > 1));
        return MetricValue<decimal>.Available(ToPercentage(overlappingMeetings, meetings.Count));
    }

    private static MetricValue<decimal> CalculateAverageEmailsReceivedPerDay(
        AvailabilityState availability,
        int? emailsReceived,
        int? calendarDays)
    {
        if (availability == AvailabilityState.Unavailable)
        {
            return MetricValue<decimal>.Unavailable("Received email volume is not supported by the current provider.");
        }

        if (availability != AvailabilityState.Available
            || !emailsReceived.HasValue
            || calendarDays is not > 0)
        {
            return MetricValue<decimal>.Unknown("Received email volume availability is unknown.");
        }

        return MetricValue<decimal>.Available(
            RoundOneDecimal(emailsReceived.Value / (decimal)calendarDays.Value));
    }

    private static decimal ToPercentage(int numerator, int denominator) => Math.Round(numerator * 100m / denominator, 1, MidpointRounding.AwayFromZero);

    private static decimal RoundOneDecimal(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
