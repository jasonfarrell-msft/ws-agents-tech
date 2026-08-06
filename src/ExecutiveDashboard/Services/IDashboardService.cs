using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardAsync(
        DateTimeOffset? weekStartsAtUtc = null,
        CancellationToken cancellationToken = default);
}
