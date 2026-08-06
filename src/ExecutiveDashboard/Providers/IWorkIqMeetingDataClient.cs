using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public interface IWorkIqMeetingDataClient
{
    Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default);
}
