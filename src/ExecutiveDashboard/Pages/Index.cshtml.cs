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

    public IReadOnlyList<DashboardMetricCard> VisibleMetrics =>
        Dashboard.Metrics
            .Where(metric => metric.Availability == AvailabilityState.Available)
            .ToArray();

    private IReadOnlyList<DashboardMetricCard> ActionableMetricDiagnostics =>
        Dashboard.Metrics
            .Where(metric =>
                metric.Availability == AvailabilityState.Unknown
                && metric.IsDiagnosticRelevant
                && !string.IsNullOrWhiteSpace(metric.HelpText))
            .ToArray();

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

    public bool HasMetricDiagnostics =>
        Dashboard.SourceAvailability != AvailabilityState.Available
        || !string.IsNullOrWhiteSpace(Dashboard.SourceMessage)
        || ActionableMetricDiagnostics.Count > 0;

    public string MetricDiagnosticsTitle => Dashboard.SourceAvailability == AvailabilityState.Unavailable
        ? "Metric data unavailable."
        : "Metric data needs attention.";

    public string MetricDiagnosticsMessage => GetMetricDiagnosticsMessage();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var lastCompletedWeekStart = IsoWeekSelection.LastCompletedWeekStart(timeProvider.GetUtcNow());
        var selectedWeekStart = IsoWeekSelection.ResolveSelectedWeekStart(Week, lastCompletedWeekStart);
        Dashboard = await dashboardService.GetDashboardAsync(selectedWeekStart, cancellationToken);
        SelectedWeekValue = IsoWeekSelection.ToWeekPickerValue(Dashboard.PeriodStartsAtUtc);
        MaxSelectableWeekValue = IsoWeekSelection.ToWeekPickerValue(lastCompletedWeekStart);
        if (PageContext?.ViewData is not null)
        {
            ViewData["SelectedWeekValue"] = SelectedWeekValue;
            ViewData["MaxSelectableWeekValue"] = MaxSelectableWeekValue;
            ViewData["PeriodLabel"] = PeriodLabel;
        }
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
        AvailabilityState.Available when Dashboard.IsSampleData => "Rendered from the deterministic sample dataset.",
        AvailabilityState.Available when metric.Threshold is { IsTriggered: true } threshold =>
            $"{threshold.Label} threshold met: {threshold.TriggerValue:0.#}+ {threshold.Unit}.",
        AvailabilityState.Available => "Calculated by the backend meeting metrics service.",
        AvailabilityState.Unknown => "The backend could not confirm the underlying meeting population for this metric.",
        _ => "The current backend provider does not have enough data to populate this metric."
    };

    private static DateTimeOffset GetPeriodEndForDisplay(DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc)
    {
        var isFullWeekWindow = periodEndUtc - periodStartUtc == TimeSpan.FromDays(7)
            && periodEndUtc.TimeOfDay == TimeSpan.Zero;
        return isFullWeekWindow ? periodEndUtc.AddDays(-1) : periodEndUtc;
    }

    private string GetMetricDiagnosticsMessage()
    {
        var diagnostics = new List<string>();
        AppendDiagnostic(diagnostics, GetSourceDiagnosticsMessage());

        foreach (var metric in ActionableMetricDiagnostics)
        {
            AppendDiagnostic(
                diagnostics,
                $"{metric.Title}: {metric.HelpText}",
                metric.HelpText);
        }

        return diagnostics.Count > 0
            ? string.Join(" ", diagnostics)
            : string.Empty;
    }

    private string? GetSourceDiagnosticsMessage()
    {
        if (!string.IsNullOrWhiteSpace(Dashboard.SourceMessage))
        {
            return Dashboard.SourceMessage;
        }

        if (Dashboard.SourceAvailability == AvailabilityState.Available)
        {
            return null;
        }

        return Dashboard.SourceAvailability == AvailabilityState.Unknown
            ? "Meeting data availability is unknown for the selected reporting period."
            : "Meeting data is unavailable for the selected reporting period.";
    }

    private static void AppendDiagnostic(
        List<string> diagnostics,
        string? candidate,
        params string?[] duplicateFragments)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var trimmedCandidate = candidate.Trim();
        var duplicateChecks = duplicateFragments
            .Append(trimmedCandidate)
            .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
            .Select(fragment => fragment!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (diagnostics.Any(existing =>
                duplicateChecks.Any(fragment => existing.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        diagnostics.Add(trimmedCandidate);
    }
}
