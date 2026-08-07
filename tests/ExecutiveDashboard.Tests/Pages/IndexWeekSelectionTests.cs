using ExecutiveDashboard.Models;
using ExecutiveDashboard.Pages;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ExecutiveDashboard.Tests.Pages;

public sealed class IndexWeekSelectionTests
{
    [Fact]
    public void OnGet_ParsesWeekPickerValueWithoutRequestingMetrics()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W31"
        };

        model.OnGet();

        Assert.Null(service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W31", model.SelectedWeekValue);
        Assert.Equal("2026-W31", model.MaxSelectableWeekValue);
    }

    [Fact]
    public void OnGet_FallsBackToLastCompletedWeekWhenWeekValueIsInvalid()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W54"
        };

        model.OnGet();

        Assert.Null(service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W31", model.SelectedWeekValue);
    }

    [Fact]
    public void OnGet_ClampsCurrentAndFutureWeekRequestsToLastCompletedWeek()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var currentWeekStart = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(currentWeekStart);
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W32"
        };

        model.OnGet();

        Assert.Null(service.LastRequestedWeekStartUtc);
        Assert.Equal("2026-W31", model.SelectedWeekValue);
    }

    [Fact]
    public async Task OnGetAsync_DoesNotShowGlobalError_WhenOnlySomeMetricsAreUnsupported()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dashboard = new DashboardViewModel(
            now.AddDays(-3),
            now,
            "Work IQ",
            AvailabilityState.Available,
            null,
            false,
            [
                new DashboardMetricCard("Meetings this week", "10", AvailabilityState.Available, "Available"),
                new DashboardMetricCard("Email replies", "Unavailable", AvailabilityState.Unavailable, "Not supported")
            ]);
        var model = new IndexModel(new StaticDashboardService(dashboard), new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.False(model.HasMetricDiagnostics);
        Assert.Single(model.VisibleMetrics);
        Assert.Equal("Meetings this week", model.VisibleMetrics[0].Title);
    }

    [Fact]
    public async Task OnGetMetricsAsync_ReturnsUncachedMetricsPartial()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var service = new RecordingDashboardService(now);
        var httpContext = new DefaultHttpContext();
        var model = new IndexModel(service, new FixedTimeProvider(now))
        {
            Week = "2026-W30",
            PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary(
                    new EmptyModelMetadataProvider(),
                    new ModelStateDictionary())
            }
        };

        var result = await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.Equal("Shared/_DashboardMetrics", result.ViewName);
        Assert.Same(model, result.ViewData.Model);
        Assert.Equal("no-store", httpContext.Response.Headers.CacheControl);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            service.LastRequestedWeekStartUtc);
    }

    [Fact]
    public async Task OnGetAsync_DoesNotShowDiagnostics_WhenNoMeetingsMakeMeetingAnalysisInapplicable()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dataSet = new MeetingDataSet(
            Array.Empty<Meeting>(),
            AvailabilityState.Available,
            AvailabilityState.Unavailable,
            AvailabilityState.Unavailable,
            AvailabilityState.Available,
            "Work IQ",
            now,
            null,
            RecurrenceAvailability: AvailabilityState.Available,
            AttendeeIdentityAvailability: AvailabilityState.Available,
            SpeakerDiarizationAvailability: AvailabilityState.Unknown,
            EmailVolumeAvailability: AvailabilityState.Available,
            EmailsReceivedCount: 12,
            EmailCalendarDayCount: 3,
            DecisionAnalysisAvailability: AvailabilityState.Unknown);
        var service = new DashboardService(
            new ExecutiveDashboard.Tests.StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new ExecutiveDashboard.Tests.StubDashboardRequestContextAccessor(ExecutiveDashboard.Tests.TestData.UserId),
            new ExecutiveDashboard.Tests.FixedTimeProvider(now));
        var model = new IndexModel(service, new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.False(model.HasMetricDiagnostics);
        Assert.Contains(model.VisibleMetrics, metric => metric.Title == "Meetings this week" && metric.Value == "0");
        Assert.Contains(model.VisibleMetrics, metric => metric.Title == "Emails received per day" && metric.Value == "4");
        Assert.DoesNotContain(model.VisibleMetrics, metric => metric.Title == "Percent informational meetings");
        Assert.DoesNotContain(model.VisibleMetrics, metric => metric.Title == "Percent with no decision reached");
    }

    [Fact]
    public async Task OnGetAsync_HidesLiveOnlyProtractedConversationMetricInSampleMode()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var service = new DashboardService(
            new SampleMeetingDataProvider(new FixedTimeProvider(now)),
            new MeetingMetricsService(),
            new ExecutiveDashboard.Tests.StubDashboardRequestContextAccessor(ExecutiveDashboard.Tests.TestData.UserId),
            new ExecutiveDashboard.Tests.FixedTimeProvider(now));
        var model = new IndexModel(service, new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.Contains(model.VisibleMetrics, metric => metric.Title == "Percent informational meetings" && metric.Value == "0%");
        Assert.DoesNotContain(model.VisibleMetrics, metric => metric.Title == "Percent protracted conversations");
    }

    [Fact]
    public async Task OnGetAsync_ShowsActionableUnknownMetricMessage_WhenHiddenDecisionMetricNeedsAttention()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dataSet = ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet(
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-1", "Informational update", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision expected.") with { IsRecurring = false },
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-2", "Status review", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision expected.") with { IsRecurring = false })
            with
            {
                Message = null,
                DecisionAvailability = AvailabilityState.Unavailable,
                DecisionAnalysisAvailability = AvailabilityState.Available,
                RecurrenceAvailability = AvailabilityState.Available,
                EmailVolumeAvailability = AvailabilityState.Available,
                EmailsReceivedCount = 12,
                EmailCalendarDayCount = 3,
                DecisionRelevantMeetingCount = 0,
                NoDecisionReachedMeetingCount = 0
            };
        var service = new DashboardService(
            new ExecutiveDashboard.Tests.StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new ExecutiveDashboard.Tests.StubDashboardRequestContextAccessor(ExecutiveDashboard.Tests.TestData.UserId),
            new ExecutiveDashboard.Tests.FixedTimeProvider(now));
        var model = new IndexModel(service, new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.True(model.HasMetricDiagnostics);
        Assert.Equal("Metric data needs attention.", model.MetricDiagnosticsTitle);
        Assert.Equal(
            "Percent with no decision reached: No decision-relevant meetings include accessible content for decision analysis.",
            model.MetricDiagnosticsMessage);
        Assert.Contains(model.VisibleMetrics, metric => metric.Title == "Meetings this week");
        Assert.DoesNotContain(model.VisibleMetrics, metric => metric.Title == "Percent with no decision reached");
    }

    [Fact]
    public async Task OnGetAsync_SurfacesNativeDecisionRelevantUnknownMessage_WhenOnlyNotApplicableMeetingsExist()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dataSet = ExecutiveDashboard.Tests.TestData.CreateAvailableDataSet(
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-1", "Informational update", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision expected.") with { IsRecurring = false },
            ExecutiveDashboard.Tests.TestData.CreateMeeting("meeting-2", "Status review", 30, 5, MeetingDecisionOutcome.NotApplicable, "No decision expected.") with { IsRecurring = false })
            with
            {
                Message = null,
                RecurrenceAvailability = AvailabilityState.Available,
                EmailVolumeAvailability = AvailabilityState.Available,
                EmailsReceivedCount = 12,
                EmailCalendarDayCount = 3
            };
        var service = new DashboardService(
            new ExecutiveDashboard.Tests.StubMeetingDataProvider(dataSet),
            new MeetingMetricsService(),
            new ExecutiveDashboard.Tests.StubDashboardRequestContextAccessor(ExecutiveDashboard.Tests.TestData.UserId),
            new ExecutiveDashboard.Tests.FixedTimeProvider(now));
        var model = new IndexModel(service, new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.True(model.HasMetricDiagnostics);
        Assert.Equal("Metric data needs attention.", model.MetricDiagnosticsTitle);
        Assert.Equal(
            "Percent with no decision reached: No decision-relevant meetings include native decision outcome data.",
            model.MetricDiagnosticsMessage);
        Assert.Contains(model.VisibleMetrics, metric => metric.Title == "Meetings this week");
        Assert.DoesNotContain(model.VisibleMetrics, metric => metric.Title == "Percent with no decision reached");
    }

    [Fact]
    public async Task OnGetAsync_ShowsGlobalError_WhenMeetingSourceIsUnavailable()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dashboard = new DashboardViewModel(
            now.AddDays(-3),
            now,
            "Work IQ",
            AvailabilityState.Unavailable,
            "Work IQ request failed.",
            false,
            []);
        var model = new IndexModel(new StaticDashboardService(dashboard), new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.True(model.HasMetricDiagnostics);
        Assert.Equal("Work IQ request failed.", model.MetricDiagnosticsMessage);
    }

    [Fact]
    public async Task OnGetAsync_ShowsActionableProviderMessage_WhenMeetingDataIsPartiallyAvailable()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dashboard = new DashboardViewModel(
            now.AddDays(-3),
            now,
            "Work IQ",
            AvailabilityState.Available,
            "Work IQ consent is required for attendee identities.",
            false,
            [new DashboardMetricCard("Meetings this week", "10", AvailabilityState.Available, "Available")]);
        var model = new IndexModel(new StaticDashboardService(dashboard), new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.True(model.HasMetricDiagnostics);
        Assert.Equal("Metric data needs attention.", model.MetricDiagnosticsTitle);
        Assert.Equal("Work IQ consent is required for attendee identities.", model.MetricDiagnosticsMessage);
    }

    [Fact]
    public async Task OnGetAsync_ComposesSourceAndHiddenMetricDiagnostics()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dashboard = new DashboardViewModel(
            now.AddDays(-3),
            now,
            "Work IQ",
            AvailabilityState.Available,
            "Work IQ consent is required for attendee identities.",
            false,
            [
                new DashboardMetricCard("Meetings this week", "10", AvailabilityState.Available, "Available"),
                new DashboardMetricCard(
                    "Percent with no decision reached",
                    "Unknown",
                    AvailabilityState.Unknown,
                    "No decision-relevant meetings include accessible content for decision analysis.")
            ]);
        var model = new IndexModel(new StaticDashboardService(dashboard), new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.Equal(
            "Work IQ consent is required for attendee identities. Percent with no decision reached: No decision-relevant meetings include accessible content for decision analysis.",
            model.MetricDiagnosticsMessage);
    }

    [Fact]
    public async Task OnGetAsync_SuppressesDuplicateHiddenMetricDiagnosticsAlreadyPresentInSourceMessage()
    {
        var now = new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);
        var dashboard = new DashboardViewModel(
            now.AddDays(-3),
            now,
            "Work IQ",
            AvailabilityState.Available,
            "Work IQ consent is required for attendee identities. Percent with no decision reached: No decision-relevant meetings include accessible content for decision analysis.",
            false,
            [
                new DashboardMetricCard("Meetings this week", "10", AvailabilityState.Available, "Available"),
                new DashboardMetricCard(
                    "Percent with no decision reached",
                    "Unknown",
                    AvailabilityState.Unknown,
                    "No decision-relevant meetings include accessible content for decision analysis.")
            ]);
        var model = new IndexModel(new StaticDashboardService(dashboard), new FixedTimeProvider(now));

        await model.OnGetMetricsAsync(CancellationToken.None);

        Assert.Equal(
            "Work IQ consent is required for attendee identities. Percent with no decision reached: No decision-relevant meetings include accessible content for decision analysis.",
            model.MetricDiagnosticsMessage);
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

    private sealed class StaticDashboardService(DashboardViewModel dashboard) : IDashboardService
    {
        public Task<DashboardViewModel> GetDashboardAsync(
            DateTimeOffset? weekStartsAtUtc = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(dashboard);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
