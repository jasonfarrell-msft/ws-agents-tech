using System.Globalization;
using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExecutiveDashboard.Pages;

public sealed class IndexModel(IDashboardService dashboardService, TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "week")]
    public string? Week { get; set; }

    public DashboardViewModel Dashboard { get; private set; } = DashboardViewModel.Empty;

    public string SelectedWeekValue { get; private set; } = string.Empty;

    public string MaxSelectableWeekValue { get; private set; } = string.Empty;

    public string PeriodLabel =>
        Dashboard.PeriodStartsAtUtc == DateTimeOffset.MinValue
            ? "Current reporting window"
            : $"{Dashboard.PeriodStartsAtUtc:MMM d} – {GetPeriodEndForDisplay(Dashboard.PeriodStartsAtUtc, Dashboard.PeriodEndsAtUtc):MMM d, yyyy} UTC";

    public string SourceStateLabel => Dashboard.IsSampleData
        ? "Sample data"
        : Dashboard.SourceAvailability switch
        {
            AvailabilityState.Available => "Live data",
            AvailabilityState.Unknown => "Unknown",
            _ => "Unavailable"
        };

    public string SourceStateCssClass => Dashboard.IsSampleData
        ? "sample"
        : Dashboard.SourceAvailability switch
        {
            AvailabilityState.Available => "live",
            AvailabilityState.Unknown => "unknown",
            _ => "unavailable"
        };

    public string PrivacyNote =>
        "Only aggregate meeting indicators are shown. Participant names, transcript content, and per-meeting details stay off the dashboard.";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var currentWeekStart = StartOfWeek(timeProvider.GetUtcNow());
        var selectedWeekStart = ResolveSelectedWeekStart(Week, currentWeekStart);
        Dashboard = await dashboardService.GetDashboardAsync(selectedWeekStart, cancellationToken);
        SelectedWeekValue = ToWeekPickerValue(Dashboard.PeriodStartsAtUtc);
        MaxSelectableWeekValue = ToWeekPickerValue(currentWeekStart);
    }

    public string GetMetricStateLabel(DashboardMetricCard metric) => metric.Availability switch
    {
        AvailabilityState.Available when Dashboard.IsSampleData => "Sample data",
        AvailabilityState.Available => "Live data",
        AvailabilityState.Unknown => "Unknown",
        _ => "Unavailable"
    };

    public string GetMetricCardCssClass(DashboardMetricCard metric) => metric.Availability switch
    {
        AvailabilityState.Available => "metric-card",
        AvailabilityState.Unknown => "metric-card metric-card--unknown",
        _ => "metric-card metric-card--unavailable"
    };

    public string GetMetricContext(DashboardMetricCard metric) => metric.Availability switch
    {
        AvailabilityState.Available when Dashboard.IsSampleData => "Rendered from the deterministic backend sample provider while WorkIQ credentials are unavailable.",
        AvailabilityState.Available when metric.Threshold is { IsTriggered: true } threshold =>
            $"{threshold.Label} threshold met: {threshold.TriggerValue:0.#}+ {threshold.Unit}.",
        AvailabilityState.Available => "Calculated by the backend meeting metrics service.",
        AvailabilityState.Unknown => "The backend could not confirm the underlying meeting population for this metric.",
        _ => "The current backend provider does not have enough data to populate this metric."
    };

    private static DateTimeOffset ResolveSelectedWeekStart(string? week, DateTimeOffset currentWeekStart)
    {
        if (!TryParseWeekPickerValue(week, out var selectedWeekStart))
        {
            return currentWeekStart;
        }

        return selectedWeekStart > currentWeekStart
            ? currentWeekStart
            : selectedWeekStart;
    }

    private static bool TryParseWeekPickerValue(string? value, out DateTimeOffset weekStartUtc)
    {
        weekStartUtc = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split("-W", StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !int.TryParse(segments[0], out var year)
            || !int.TryParse(segments[1], out var week)
            || year is < 1 or > 9999)
        {
            return false;
        }

        if (week < 1 || week > ISOWeek.GetWeeksInYear(year))
        {
            return false;
        }

        var monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
        weekStartUtc = new DateTimeOffset(monday, TimeSpan.Zero);
        return true;
    }

    private static string ToWeekPickerValue(DateTimeOffset weekStartUtc)
    {
        var utc = weekStartUtc.ToUniversalTime();
        var isoYear = ISOWeek.GetYear(utc.DateTime);
        var isoWeek = ISOWeek.GetWeekOfYear(utc.DateTime);
        return $"{isoYear:0000}-W{isoWeek:00}";
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset timestampUtc)
    {
        var utcTimestamp = timestampUtc.ToUniversalTime();
        var daysSinceMonday = ((int)utcTimestamp.DayOfWeek + 6) % 7;
        var startOfDay = new DateTimeOffset(utcTimestamp.Year, utcTimestamp.Month, utcTimestamp.Day, 0, 0, 0, TimeSpan.Zero);
        return startOfDay.AddDays(-daysSinceMonday);
    }

    private static DateTimeOffset GetPeriodEndForDisplay(DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc)
    {
        var isFullWeekWindow = periodEndUtc - periodStartUtc == TimeSpan.FromDays(7)
            && periodEndUtc.TimeOfDay == TimeSpan.Zero;
        return isFullWeekWindow ? periodEndUtc.AddDays(-1) : periodEndUtc;
    }
}
