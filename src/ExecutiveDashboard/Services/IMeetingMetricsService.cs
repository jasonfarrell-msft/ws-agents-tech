using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Services;

public interface IMeetingMetricsService
{
    MeetingDashboardMetrics Calculate(MeetingDataSet dataSet, string userId);
}
