using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Providers;

public interface IMeetingDataProvider
{
    Task<MeetingDataSet> GetMeetingsAsync(MeetingQuery query, CancellationToken cancellationToken = default);
}
