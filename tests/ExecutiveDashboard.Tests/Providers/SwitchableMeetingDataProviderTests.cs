using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;

namespace ExecutiveDashboard.Tests.Providers;

public sealed class SwitchableMeetingDataProviderTests
{
    [Fact]
    public async Task GetMeetingsAsync_ReturnsUnavailable_WhenCliLiveModeIsSelectedFromNonLocalRequest()
    {
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var provider = new SwitchableMeetingDataProvider(
            new SampleMeetingDataProvider(new FixedTimeProvider(now)),
            new StubLiveMeetingDataProvider(MeetingDataSet.Unavailable(CreateQuery(now), "Work IQ", now, "should not be used")),
            new FixedTimeProvider(now),
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Live,
                    "workiq-cli-signed-in-user",
                    "Work IQ CLI signed-in user",
                    false,
                    false,
                    false,
                    false,
                    LiveDataAccessMode.Cli,
                    "Work IQ CLI",
                    "CLI-backed live mode is limited to localhost on the machine that owns the signed-in Work IQ CLI session.")));

        var dataSet = await provider.GetMeetingsAsync(CreateQuery(now));

        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Contains("limited to localhost", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_UsesLiveProvider_WhenLiveModeIsAllowed()
    {
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var liveData = new MeetingDataSet(
            Array.Empty<Meeting>(),
            AvailabilityState.Available,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            AvailabilityState.Unknown,
            "Work IQ CLI",
            now,
            "live");
        var provider = new SwitchableMeetingDataProvider(
            new SampleMeetingDataProvider(new FixedTimeProvider(now)),
            new StubLiveMeetingDataProvider(liveData),
            new FixedTimeProvider(now),
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Live,
                    "workiq-cli-signed-in-user",
                    "Work IQ CLI signed-in user",
                    false,
                    true,
                    false,
                    false,
                    LiveDataAccessMode.Cli,
                    "Work IQ CLI",
                    "Live mode uses the installed Work IQ CLI.")));

        var dataSet = await provider.GetMeetingsAsync(CreateQuery(now));

        Assert.Equal("Work IQ CLI", dataSet.SourceName);
        Assert.Equal("live", dataSet.Message);
    }

    [Fact]
    public async Task GetMeetingsAsync_PreservesAuthChallengeMessage_WhenLiveModeRequiresSignIn()
    {
        var now = DateTimeOffset.Parse("2026-08-05T18:00:00Z");
        var liveProvider = new StubLiveMeetingDataProvider(
            MeetingDataSet.Unavailable(
                CreateQuery(now),
                "Work IQ",
                now,
                "Work IQ authorization failed while trying to ask Work IQ for meeting data."));
        var provider = new SwitchableMeetingDataProvider(
            new SampleMeetingDataProvider(new FixedTimeProvider(now)),
            liveProvider,
            new FixedTimeProvider(now),
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Live,
                    "live-mode-corporate-user",
                    "Corporate user sign-in required",
                    false,
                    false,
                    true,
                    false,
                    LiveDataAccessMode.Entra,
                    "Work IQ delegated live access",
                    "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.")));

        var dataSet = await provider.GetMeetingsAsync(CreateQuery(now));

        Assert.Equal(0, liveProvider.InvocationCount);
        Assert.False(dataSet.IsSampleData);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
        Assert.Equal("Work IQ delegated live access", dataSet.SourceName);
        Assert.Equal(
            "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.",
            dataSet.Message);
    }

    private static MeetingQuery CreateQuery(DateTimeOffset now) =>
        new(now.AddDays(-7), now, "sample-executive");

    private sealed class StubLiveMeetingDataProvider(MeetingDataSet dataSet) : ILiveMeetingDataProvider
    {
        public int InvocationCount { get; private set; }

        public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(RecordInvocationAndReturnDataSet());

        private MeetingDataSet RecordInvocationAndReturnDataSet()
        {
            InvocationCount++;
            return dataSet;
        }
    }

    private sealed class StubDashboardRequestContextAccessor(DashboardRequestContext context) : IDashboardRequestContextAccessor
    {
        public DashboardRequestContext GetCurrentContext() => context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
