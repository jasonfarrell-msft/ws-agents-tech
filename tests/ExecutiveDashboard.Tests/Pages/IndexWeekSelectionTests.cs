using ExecutiveDashboard.Models;
using ExecutiveDashboard.Pages;
using ExecutiveDashboard.Services;

namespace ExecutiveDashboard.Tests.Pages;

public sealed class IndexWeekSelectionTests
{
    [Fact]
    public async Task OnGetAsync_ParsesWeekPickerValueAndRequestsThatWeek()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W31"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero), service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W31", model.SelectedWeekValue);
        Assert.Equal("2026-W32", model.MaxSelectableWeekValue);
    }

    [Fact]
    public async Task OnGetAsync_FallsBackToCurrentWeekWhenWeekValueIsInvalid()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W54"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(currentWeekStart, service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W32", model.SelectedWeekValue);
    }

    [Fact]
    public async Task OnGetAsync_ClampsFutureWeekRequestsToCurrentWeek()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W40"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(currentWeekStart, service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W32", model.SelectedWeekValue);
    }

    private sealed class RecordingDashboardService(DateTimeOffset currentWeekStartUtc) : IDashboardService
    {
        public DateTimeOffset? LastRequestedWeekStartUtc { get; private set; }

        public Task<DashboardViewModel> GetDashboardAsync(
            DateTimeOffset? weekStartsAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestedWeekStartUtc = weekStartsAtUtc;
            var periodStart = weekStartsAtUtc ?? currentWeekStartUtc;
            return Task.FromResult(new DashboardViewModel(
                periodStart,
                periodStart.AddDays(7),
                "Test provider",
                AvailabilityState.Available,
                null,
                false,
                Array.Empty<DashboardMetricCard>()));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
