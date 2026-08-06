using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public sealed class SampleMeetingDataProvider(TimeProvider timeProvider) : IMeetingDataProvider
{
    public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var periodStart = query.StartsAtUtc;
        var meetings = new[]
        {
            CreateMeeting(
                "sample-1",
                "Weekly operating review",
                periodStart,
                TimeSpan.FromMinutes(45),
                query.UserId,
                TimeSpan.FromMinutes(3),
                attendeeCount: 9,
                emailReplyCount: 2,
                MeetingDecisionOutcome.Reached,
                "Approved rollout checkpoint."),
            CreateMeeting(
                "sample-2",
                "Pipeline risk review",
                periodStart,
                TimeSpan.FromMinutes(60),
                query.UserId,
                TimeSpan.FromMinutes(18),
                attendeeCount: 10,
                emailReplyCount: 10,
                MeetingDecisionOutcome.NoneReached,
                "Escalated unresolved budget trade-off."),
            CreateMeeting(
                "sample-3",
                "Customer health sync",
                periodStart,
                TimeSpan.FromMinutes(30),
                query.UserId,
                TimeSpan.FromMinutes(2),
                attendeeCount: 3,
                emailReplyCount: 8,
                MeetingDecisionOutcome.Reached,
                "Confirmed executive sponsor follow-up."),
            CreateMeeting(
                "sample-4",
                "Product strategy standup",
                periodStart,
                TimeSpan.FromMinutes(30),
                query.UserId,
                TimeSpan.FromMinutes(7),
                attendeeCount: 12,
                emailReplyCount: 12,
                MeetingDecisionOutcome.NoneReached,
                "Deferred launch sequencing decision.")
        }
        .Where(meeting => meeting.StartsAtUtc >= query.StartsAtUtc && meeting.StartsAtUtc <= query.EndsAtUtc)
        .ToArray();

        var dataSet = new MeetingDataSet(
            meetings,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            AvailabilityState.Available,
            "Deterministic sample provider",
            timeProvider.GetUtcNow(),
            "Sample data only; use Mission Control to switch to Live mode when the approved corporate Work IQ source is available.",
            IsSampleData: true);

        return Task.FromResult(dataSet);
    }

    private static Meeting CreateMeeting(
        string id,
        string title,
        DateTimeOffset startsAtUtc,
        TimeSpan duration,
        string userId,
        TimeSpan userTalkTime,
        int attendeeCount,
        int emailReplyCount,
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
            new MeetingEmailThread(emailReplyCount));
    }
}
