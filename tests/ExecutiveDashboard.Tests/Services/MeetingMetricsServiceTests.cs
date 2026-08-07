using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using Microsoft.Extensions.Options;

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
                Meeting("m1", TimeSpan.FromMinutes(30), userId, TimeSpan.FromMinutes(2), MeetingDecisionOutcome.Reached, attendeeCount: 9),
                Meeting("m2", TimeSpan.FromMinutes(60), userId, TimeSpan.FromMinutes(12), MeetingDecisionOutcome.NoneReached, attendeeCount: 11)
            },
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
        Assert.Equal(AvailabilityState.Available, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(AvailabilityState.Available, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.NoDecisionReachedMeetingPercentage.Value);
    }

    [Fact]
    public void Calculate_DoesNotTriggerAttendeeThresholdWhenArithmeticMeanIsBelowTenButRoundsUp()
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
                attendeeCount: index == 1 ? 9 : 10))
            .ToArray();
        var dataSet = new MeetingDataSet(
            meetings,
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
    }

    [Fact]
    public void Calculate_TriggersAttendeeThresholdAtAnExactTenPointZeroArithmeticMean()
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
                attendeeCount: 10))
            .ToArray();
        var dataSet = new MeetingDataSet(
            meetings,
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
        Assert.False(metrics.AverageAttendeesPerMeeting.IsDiagnosticRelevant);
        Assert.Equal(AvailabilityState.Unknown, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal("No meetings include talk-time data for the selected user.", metrics.LowTalkTimeMeetingPercentage.Message);
        Assert.False(metrics.LowTalkTimeMeetingPercentage.IsDiagnosticRelevant);
        Assert.Equal(AvailabilityState.Unknown, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal("No meetings include decision outcome data.", metrics.NoDecisionReachedMeetingPercentage.Message);
        Assert.False(metrics.NoDecisionReachedMeetingPercentage.IsDiagnosticRelevant);
    }

    [Fact]
    public void Calculate_ReturnsUnknownMetricAvailabilityWhenAttendeeDataIsUnknown()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), "user-1", TimeSpan.FromMinutes(5), MeetingDecisionOutcome.Reached, attendeeCount: 3)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Unknown,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unknown, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal("Attendee count data availability is unknown.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
    }

    [Fact]
    public void Calculate_ReturnsUnavailableMetricAvailabilityWhenAttendeeDataIsUnavailable()
    {
        var service = new MeetingMetricsService();
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), "user-1", TimeSpan.FromMinutes(5), MeetingDecisionOutcome.Reached, attendeeCount: 3)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = service.Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unavailable, metrics.AverageAttendeesPerMeeting.Availability);
        Assert.Equal("Attendee count data is not supported by the current provider.", metrics.AverageAttendeesPerMeeting.Message);
        Assert.Null(metrics.AverageAttendeesPerMeeting.Value);
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
        Assert.Equal(AvailabilityState.Unavailable, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.LowTalkTimeMeetingPercentage.Message);
        Assert.Null(metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal("WorkIQ is unavailable.", metrics.NoDecisionReachedMeetingPercentage.Message);
        Assert.Null(metrics.NoDecisionReachedMeetingPercentage.Value);
        Assert.Equal(AvailabilityState.Unavailable, metrics.FocusTimeLoss.Availability);
        Assert.Equal(AvailabilityState.Unavailable, metrics.BackToBackMeetingCount.Availability);
        Assert.Equal(AvailabilityState.Unavailable, metrics.RecurringMeetingHoursPercentage.Availability);
        Assert.Equal(AvailabilityState.Unavailable, metrics.AttendeeOverlapMeetingPercentage.Availability);
    }

    [Fact]
    public void Calculate_ReturnsCollaborationQualityMetrics()
    {
        var service = new MeetingMetricsService();
        var userId = "user-1";
        var localDate = new DateTime(2026, 8, 4);
        var firstStart = ToUtc(localDate.AddHours(10));
        var secondStart = ToUtc(localDate.AddHours(11));
        var thirdStart = ToUtc(localDate.AddHours(18));
        var meetings = new[]
        {
            CollaborationMeeting("m1", firstStart, TimeSpan.FromHours(1), userId, true, "attendee-a", "attendee-b"),
            CollaborationMeeting("m2", secondStart, TimeSpan.FromHours(1), userId, true, "attendee-a", "attendee-c"),
            CollaborationMeeting("m3", thirdStart, TimeSpan.FromHours(1), userId, false, "attendee-d")
        };
        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            RecurrenceAvailability: AvailabilityState.Available,
            AttendeeIdentityAvailability: AvailabilityState.Available);

        var metrics = service.Calculate(dataSet, userId);

        Assert.Equal(TimeSpan.FromHours(2), metrics.FocusTimeLoss.Value);
        Assert.Equal(1, metrics.BackToBackMeetingCount.Value);
        Assert.Equal(66.7m, metrics.RecurringMeetingHoursPercentage.Value);
        Assert.Equal(66.7m, metrics.AttendeeOverlapMeetingPercentage.Value);
    }

    [Fact]
    public void Calculate_UsesDiarizationToConfirmMinimumLowTalkPercentage()
    {
        var userId = "user-1";
        var startsAt = DateTimeOffset.Parse("2026-08-04T14:00:00Z");
        var meetings = new[]
        {
            CollaborationMeeting("m1", startsAt, TimeSpan.FromMinutes(30), userId, false, "attendee-a")
                with { HasTranscript = true, UserSpeakingSegmentCount = 0 },
            CollaborationMeeting("m2", startsAt.AddHours(1), TimeSpan.FromMinutes(30), userId, false, "attendee-b")
                with { HasTranscript = true, UserSpeakingSegmentCount = 12 },
            CollaborationMeeting("m3", startsAt.AddHours(2), TimeSpan.FromMinutes(30), userId, false, "attendee-c")
                with { HasTranscript = false, UserSpeakingSegmentCount = null }
        };
        var dataSet = AvailableCollaborationDataSet(meetings) with
        {
            TalkTimeAvailability = AvailabilityState.Unavailable,
            SpeakerDiarizationAvailability = AvailabilityState.Available,
            DiarizedMeetingCount = 2,
            ConfirmedZeroUserSpeechMeetingCount = 1
        };

        var metrics = new MeetingMetricsService().Calculate(dataSet, userId);

        Assert.Equal(AvailabilityState.Available, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(
            "Informational means you spoke for less than 10% of the meeting. Minimum confirmed proxy only: without segment timing, this value counts only diarized meetings with no speech attributed to you and may undercount other under-10% meetings. Coverage warning: Some meetings lacked diarization; this percentage covers diarized meetings only.",
            metrics.LowTalkTimeMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_DescribesNativeInformationalMetricAsExactTalkTimeComputation()
    {
        var dataSet = AvailableCollaborationDataSet(
            [
                CollaborationMeeting("m1", DateTimeOffset.Parse("2026-08-04T14:00:00Z"), TimeSpan.FromMinutes(100), "user-1", false)
                    with
                    {
                        Participants =
                        [
                            new MeetingParticipant("user-1", "Selected user", TimeSpan.FromMinutes(5)),
                            new MeetingParticipant("attendee-a", "Attendee A", TimeSpan.FromMinutes(95))
                        ]
                    }
            ]) with
            {
                TalkTimeAvailability = AvailabilityState.Available
            };

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Available, metrics.LowTalkTimeMeetingPercentage.Availability);
        Assert.Equal(100m, metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(
            "Informational means you spoke for less than 10% of the meeting. Calculated exactly from native talk-time totals.",
            metrics.LowTalkTimeMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_DoesNotDoubleCountOverlappingMeetings()
    {
        var service = new MeetingMetricsService();
        var userId = "user-1";
        var localDate = new DateTime(2026, 8, 4);
        var meetings = new[]
        {
            CollaborationMeeting("m1", ToUtc(localDate.AddHours(10)), TimeSpan.FromHours(1), userId, false, "attendee-a"),
            CollaborationMeeting("m2", ToUtc(localDate.AddHours(10).AddMinutes(30)), TimeSpan.FromMinutes(10), userId, false, "attendee-b"),
            CollaborationMeeting("m3", ToUtc(localDate.AddHours(10).AddMinutes(45)), TimeSpan.FromMinutes(30), userId, false, "attendee-c")
        };
        var dataSet = AvailableCollaborationDataSet(meetings);

        var metrics = service.Calculate(dataSet, userId);

        Assert.Equal(TimeSpan.FromMinutes(75), metrics.FocusTimeLoss.Value);
        Assert.Equal(0, metrics.BackToBackMeetingCount.Value);
    }

    [Fact]
    public void Calculate_UsesConfiguredWorkTimeZone()
    {
        var service = new MeetingMetricsService(Options.Create(new WorkIqOptions { TimeZone = "America/New_York" }));
        var meeting = CollaborationMeeting(
            "m1",
            DateTimeOffset.Parse("2026-08-04T18:00:00Z"),
            TimeSpan.FromHours(1),
            "user-1",
            false,
            "attendee-a");

        var metrics = service.Calculate(AvailableCollaborationDataSet([meeting]), "user-1");

        Assert.Equal(TimeSpan.FromHours(1), metrics.FocusTimeLoss.Value);
    }

    [Fact]
    public void Calculate_AveragesReceivedEmailsAcrossAllCalendarDays()
    {
        var dataSet = AvailableCollaborationDataSet([]) with
        {
            EmailVolumeAvailability = AvailabilityState.Available,
            EmailsReceivedCount = 124,
            EmailCalendarDayCount = 4
        };

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Available, metrics.AverageEmailsReceivedPerDay.Availability);
        Assert.Equal(31m, metrics.AverageEmailsReceivedPerDay.Value);
    }

    [Fact]
    public void Calculate_ComputesPercentOfEmailConversationsWithMoreThanTenReplies()
    {
        var dataSet = AvailableCollaborationDataSet([]) with
        {
            EmailConversationAnalysisAvailability = AvailabilityState.Available,
            EmailConversationCount = 20,
            ProtractedEmailConversationCount = 5
        };

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Available, metrics.ProtractedEmailConversationPercentage.Availability);
        Assert.Equal(25m, metrics.ProtractedEmailConversationPercentage.Value);
    }

    [Fact]
    public void Calculate_UsesContentAnalysisForNoDecisionPercentage()
    {
        var meetings = new[]
        {
            CollaborationMeeting("m1", DateTimeOffset.Parse("2026-08-04T14:00:00Z"), TimeSpan.FromMinutes(30), "user-1", false),
            CollaborationMeeting("m2", DateTimeOffset.Parse("2026-08-04T15:00:00Z"), TimeSpan.FromMinutes(30), "user-1", false),
            CollaborationMeeting("m3", DateTimeOffset.Parse("2026-08-04T16:00:00Z"), TimeSpan.FromMinutes(30), "user-1", false)
        };
        var dataSet = AvailableCollaborationDataSet(meetings) with
        {
            DecisionAvailability = AvailabilityState.Unavailable,
            DecisionAnalysisAvailability = AvailabilityState.Available,
            DecisionRelevantMeetingCount = 2,
            NoDecisionReachedMeetingCount = 1
        };

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Available, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Equal(50m, metrics.NoDecisionReachedMeetingPercentage.Value);
        Assert.Equal(
            "Coverage warning: Some meetings lacked decision content or were not decision-oriented; this percentage covers decision-relevant meetings with accessible content only.",
            metrics.NoDecisionReachedMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_ReturnsDistinctUnknownMessageWhenAccessibleDecisionContentHasNoRelevantMeetings()
    {
        var meetings = new[]
        {
            CollaborationMeeting("m1", DateTimeOffset.Parse("2026-08-04T14:00:00Z"), TimeSpan.FromMinutes(30), "user-1", false),
            CollaborationMeeting("m2", DateTimeOffset.Parse("2026-08-04T15:00:00Z"), TimeSpan.FromMinutes(30), "user-1", false)
        };
        var dataSet = AvailableCollaborationDataSet(meetings) with
        {
            DecisionAvailability = AvailabilityState.Unavailable,
            DecisionAnalysisAvailability = AvailabilityState.Available,
            DecisionRelevantMeetingCount = 0,
            NoDecisionReachedMeetingCount = 0
        };

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unknown, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Null(metrics.NoDecisionReachedMeetingPercentage.Value);
        Assert.Equal(
            "No decision-relevant meetings include accessible content for decision analysis.",
            metrics.NoDecisionReachedMeetingPercentage.Message);
    }

    [Fact]
    public void Calculate_ReturnsDecisionRelevantUnknownMessageWhenNativeDecisionOutcomesAreOnlyNotApplicable()
    {
        var dataSet = new MeetingDataSet(
            new[]
            {
                Meeting("m1", TimeSpan.FromMinutes(30), "user-1", TimeSpan.FromMinutes(5), MeetingDecisionOutcome.NotApplicable),
                Meeting("m2", TimeSpan.FromMinutes(45), "user-1", TimeSpan.FromMinutes(10), MeetingDecisionOutcome.NotApplicable)
            },
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"));

        var metrics = new MeetingMetricsService().Calculate(dataSet, "user-1");

        Assert.Equal(AvailabilityState.Unknown, metrics.NoDecisionReachedMeetingPercentage.Availability);
        Assert.Null(metrics.NoDecisionReachedMeetingPercentage.Value);
        Assert.Equal(
            "No decision-relevant meetings include native decision outcome data.",
            metrics.NoDecisionReachedMeetingPercentage.Message);
    }

    private static Meeting Meeting(
        string id,
        TimeSpan duration,
        string userId,
        TimeSpan? talkTime,
        MeetingDecisionOutcome decisionOutcome,
        int attendeeCount = 1)
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
            new MeetingDecision(decisionOutcome));
    }

    private static Meeting CollaborationMeeting(
        string id,
        DateTimeOffset startsAtUtc,
        TimeSpan duration,
        string userId,
        bool isRecurring,
        params string[] attendeeIds) =>
        new(
            id,
            id,
            startsAtUtc,
            startsAtUtc.Add(duration),
            new[] { new MeetingParticipant(userId, "User") }
                .Concat(attendeeIds.Select(attendeeId => new MeetingParticipant(attendeeId, "Attendee")))
                .ToArray(),
            new MeetingDecision(MeetingDecisionOutcome.Unknown),
            IsRecurring: isRecurring);

    private static DateTimeOffset ToUtc(DateTime localTime)
    {
        var local = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }

    private static MeetingDataSet AvailableCollaborationDataSet(IReadOnlyList<Meeting> meetings) =>
        new(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Available,
            "Test provider",
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            RecurrenceAvailability: AvailabilityState.Available,
            AttendeeIdentityAvailability: AvailabilityState.Available);
}
