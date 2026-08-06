using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;

namespace ExecutiveDashboard.Providers;

public sealed class SwitchableMeetingDataProvider(
    SampleMeetingDataProvider sampleMeetingDataProvider,
    ILiveMeetingDataProvider liveMeetingDataProvider,
    TimeProvider timeProvider,
    IDashboardRequestContextAccessor requestContextAccessor) : IMeetingDataProvider
{
    public Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default)
    {
        var requestContext = requestContextAccessor.GetCurrentContext();

        if (requestContext.SelectedMode == DashboardOperatingMode.Live && !requestContext.CanUseLiveData)
        {
            return Task.FromResult(
                MeetingDataSet.Unavailable(
                    query,
                    requestContext.LiveSourceName,
                    timeProvider.GetUtcNow(),
                    requestContext.LiveSourceDetail));
        }

        return requestContext.SelectedMode == DashboardOperatingMode.Live
            ? liveMeetingDataProvider.GetMeetingsAsync(query, cancellationToken)
            : sampleMeetingDataProvider.GetMeetingsAsync(query, cancellationToken);
    }
}
