using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class SampleMeetingDataProviderTests
{
    private static readonly MeetingQuery Query = new(
        DateTimeOffset.Parse("2026-07-27T00:00:00Z"),
        DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
        "sample-executive");

    public static TheoryData<SampleProfile, string, string, int, int, decimal, decimal> Profiles => new()
    {
        {
            SampleProfile.HealthyWeek,
            "Healthy Week",
            "A sustainable schedule with focused meetings, clear decisions, and protected work time.",
            5,
            70,
            0m,
            0m
        },
        {
            SampleProfile.OverloadedWeek,
            "Overloaded Week",
            "A meeting-heavy schedule with long sessions, recurring load, and limited recovery time.",
            10,
            210,
            40m,
            70m
        },
        {
            SampleProfile.LowEngagementWeek,
            "Low-Engagement Week",
            "A passive meeting pattern with limited speaking time and unresolved decisions.",
            5,
            35,
            100m,
            100m
        }
    };

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task GetMeetingsAsync_ProducesDistinctDeterministicProfile(
        SampleProfile profile,
        string title,
        string description,
        int meetingCount,
        int emailsReceived,
        decimal informationalPercentage,
        decimal noDecisionPercentage)
    {
        var provider = new SampleMeetingDataProvider(
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-06T00:00:00Z")),
            new ProfileContextAccessor(profile));

        var dataSet = await provider.GetMeetingsAsync(Query);
        var metrics = new MeetingMetricsService(
            Options.Create(new WorkIqOptions { TimeZone = "America/New_York" }))
            .Calculate(dataSet, Query.UserId);

        Assert.True(dataSet.IsSampleData);
        Assert.Equal(title, dataSet.SampleProfileTitle);
        Assert.Equal(description, dataSet.SampleProfileDescription);
        Assert.Equal(meetingCount, dataSet.Meetings.Count);
        Assert.Equal(emailsReceived, dataSet.EmailsReceivedCount);
        Assert.Equal(informationalPercentage, metrics.LowTalkTimeMeetingPercentage.Value);
        Assert.Equal(noDecisionPercentage, metrics.NoDecisionReachedMeetingPercentage.Value);
    }

    private sealed class ProfileContextAccessor(SampleProfile profile) : IDashboardRequestContextAccessor
    {
        public DashboardRequestContext GetCurrentContext() =>
            new(
                DashboardOperatingMode.Sample,
                Query.UserId,
                "Sample user",
                false,
                true,
                false,
                false,
                LiveDataAccessMode.None,
                "Sample",
                "Sample mode",
                profile);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
