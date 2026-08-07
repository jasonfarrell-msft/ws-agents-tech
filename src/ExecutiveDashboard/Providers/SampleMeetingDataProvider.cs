using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;

namespace ExecutiveDashboard.Providers;

public sealed class SampleMeetingDataProvider(
    TimeProvider timeProvider,
    IDashboardRequestContextAccessor? requestContextAccessor = null) : IMeetingDataProvider
{
    public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var periodStart = query.StartsAtUtc;
        var selectedProfile = requestContextAccessor?.GetCurrentContext().SelectedSampleProfile
            ?? SampleProfile.HealthyWeek;
        var profile = CreateProfile(selectedProfile);
        var definition = SampleProfileCatalog.Get(selectedProfile);
        var meetings = profile.Meetings
            .Select((meeting, index) => CreateMeeting(
                $"sample-{selectedProfile}-{index + 1}",
                meeting.Title,
                periodStart.AddDays(meeting.DayOffset).Add(meeting.StartsAfterMidnight),
                meeting.Duration,
                query.UserId,
                meeting.UserTalkTime,
                meeting.AttendeeCount,
                meeting.IsRecurring,
                meeting.DecisionOutcome,
                meeting.DecisionSummary))
        .Where(meeting => meeting.StartsAtUtc >= query.StartsAtUtc && meeting.StartsAtUtc < query.EndsAtUtc)
        .ToArray();

        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Deterministic sample provider",
            timeProvider.GetUtcNow(),
            $"Sample data only: deterministic {definition.Title.ToLowerInvariant()} profile. Use Mission Control to change profiles or switch to Live mode.",
            IsSampleData: true,
            RecurrenceAvailability: AvailabilityState.Available,
            AttendeeIdentityAvailability: AvailabilityState.Available,
            EmailVolumeAvailability: AvailabilityState.Available,
            EmailsReceivedCount: profile.EmailsReceived,
            EmailCalendarDayCount: Math.Max(1, (int)Math.Ceiling((query.EndsAtUtc - query.StartsAtUtc).TotalDays)),
            SampleProfileTitle: definition.Title,
            SampleProfileDescription: definition.Description);

        return Task.FromResult(dataSet);
    }

    private static SampleProfileData CreateProfile(SampleProfile profile) => profile switch
    {
        SampleProfile.OverloadedWeek => new(
            210,
            [
                MeetingSpec.At(0, 13, 60, 5, 14, true, MeetingDecisionOutcome.NoneReached, "Capacity trade-off remained unresolved.", "Operating review"),
                MeetingSpec.At(0, 14, 60, 8, 10, true, MeetingDecisionOutcome.NoneReached, "Funding decision was deferred.", "Portfolio review"),
                MeetingSpec.At(0, 15, 45, 10, 9, true, MeetingDecisionOutcome.Reached, "Approved the escalation path.", "Risk council"),
                MeetingSpec.At(1, 13, 90, 12, 16, true, MeetingDecisionOutcome.NoneReached, "No final priority decision.", "Planning workshop"),
                MeetingSpec.At(1, 14.5, 60, 5, 11, false, MeetingDecisionOutcome.NoneReached, "Ownership remained open.", "Delivery escalation"),
                MeetingSpec.At(2, 13, 60, 8, 12, true, MeetingDecisionOutcome.Reached, "Approved the recovery plan.", "Leadership sync"),
                MeetingSpec.At(2, 14, 60, 7, 8, true, MeetingDecisionOutcome.NoneReached, "Launch date remained unresolved.", "Product review"),
                MeetingSpec.At(3, 13, 90, 15, 18, true, MeetingDecisionOutcome.NoneReached, "No agreement on sequencing.", "Strategy workshop"),
                MeetingSpec.At(3, 14.5, 60, 4, 7, false, MeetingDecisionOutcome.Reached, "Approved customer remediation.", "Customer escalation"),
                MeetingSpec.At(4, 13, 60, 5, 13, true, MeetingDecisionOutcome.NoneReached, "Budget decision carried forward.", "Weekly close")
            ]),
        SampleProfile.LowEngagementWeek => new(
            35,
            [
                MeetingSpec.At(0, 14, 60, 2, 8, true, MeetingDecisionOutcome.NotApplicable, "Status update only.", "Organization update"),
                MeetingSpec.At(1, 15, 45, 1, 6, true, MeetingDecisionOutcome.NoneReached, "No decision was reached.", "Program review"),
                MeetingSpec.At(2, 13, 30, 0, 5, false, MeetingDecisionOutcome.NotApplicable, "Presentation only.", "Market briefing"),
                MeetingSpec.At(3, 17, 60, 3, 10, true, MeetingDecisionOutcome.NoneReached, "Next step remained unclear.", "Cross-team sync"),
                MeetingSpec.At(4, 14, 30, 1, 4, false, MeetingDecisionOutcome.NoneReached, "The group deferred action.", "Weekly retrospective")
            ]),
        _ => new(
            70,
            [
                MeetingSpec.At(0, 14, 30, 8, 6, true, MeetingDecisionOutcome.Reached, "Approved weekly priorities.", "Weekly priorities"),
                MeetingSpec.At(1, 15, 45, 12, 8, false, MeetingDecisionOutcome.Reached, "Selected the customer response.", "Customer strategy"),
                MeetingSpec.At(2, 18, 30, 6, 5, false, MeetingDecisionOutcome.Reached, "Confirmed the delivery owner.", "Delivery checkpoint"),
                MeetingSpec.At(3, 14, 30, 8, 7, true, MeetingDecisionOutcome.NotApplicable, "Informational update only.", "Business update"),
                MeetingSpec.At(4, 15, 30, 7, 4, false, MeetingDecisionOutcome.Reached, "Approved next week's focus.", "Weekly close")
            ])
    };

    private static Meeting CreateMeeting(
        string id,
        string title,
        DateTimeOffset startsAtUtc,
        TimeSpan duration,
        string userId,
        TimeSpan userTalkTime,
        int attendeeCount,
        bool isRecurring,
        MeetingDecisionOutcome decisionOutcome,
        string decisionSummary)
    {
        var participants = new List<MeetingParticipant>
        {
            new(userId, "Sample Executive", userTalkTime)
        };

        if (attendeeCount >= 2)
        {
            participants.Add(new MeetingParticipant("chief-of-staff", "Chief of Staff", TimeSpan.FromTicks((duration - userTalkTime).Ticks / 2)));
        }

        if (attendeeCount >= 3)
        {
            participants.Add(new MeetingParticipant("vp-operations", "VP Operations", duration - userTalkTime - TimeSpan.FromTicks((duration - userTalkTime).Ticks / 2)));
        }

        for (var attendeeIndex = participants.Count + 1; attendeeIndex <= attendeeCount; attendeeIndex++)
        {
            participants.Add(new MeetingParticipant($"sample-attendee-{attendeeIndex}", $"Sample Attendee {attendeeIndex}"));
        }

        return new Meeting(
            id,
            title,
            startsAtUtc,
            startsAtUtc.Add(duration),
            participants,
            new MeetingDecision(decisionOutcome, decisionSummary),
            isRecurring);
    }

    private sealed record SampleProfileData(
        int EmailsReceived,
        IReadOnlyList<MeetingSpec> Meetings);

    private sealed record MeetingSpec(
        int DayOffset,
        TimeSpan StartsAfterMidnight,
        TimeSpan Duration,
        TimeSpan UserTalkTime,
        int AttendeeCount,
        bool IsRecurring,
        MeetingDecisionOutcome DecisionOutcome,
        string DecisionSummary,
        string Title)
    {
        public static MeetingSpec At(
            int dayOffset,
            double hour,
            int durationMinutes,
            int userTalkMinutes,
            int attendeeCount,
            bool isRecurring,
            MeetingDecisionOutcome decisionOutcome,
            string decisionSummary,
            string title) =>
            new(
                dayOffset,
                TimeSpan.FromHours(hour),
                TimeSpan.FromMinutes(durationMinutes),
                TimeSpan.FromMinutes(userTalkMinutes),
                attendeeCount,
                isRecurring,
                decisionOutcome,
                decisionSummary,
                title);
    }
}
