namespace ExecutiveDashboard.Tests.Pages;

public sealed class IndexMarkupTests
{
    [Fact]
    public void IndexPage_DoesNotDuplicateMissionControlLaunchSection()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);

        Assert.DoesNotContain("Go to Mission Control", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedLayout_ExposesWorkIqStatusInHeader()
    {
        var layoutMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutMarkupPath);

        Assert.Matches(
            "(?is)<header[^>]*class=\"app-header\"[^>]*>.*<div[^>]*app-navbar__status[^>]*role=\"status\"[^>]*aria-live=\"polite\"[^>]*>.*WorkIQ\\s+@workIqStatusLabel.*</div>.*</header>",
            markup);
    }

    [Fact]
    public void IndexPage_WeekPickerUsesGetAndBindsSelectedWeek()
    {
        var layoutMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutMarkupPath);

        Assert.Matches(
            "(?is)<form[^>]*method=\"get\"[^>]*class=\"week-picker week-picker--compact\"[^>]*data-immediate-loading[^>]*>.*<input[^>]*type=\"week\"[^>]*name=\"week\"[^>]*value=\"@selectedWeekRouteValue\"[^>]*data-submit-on-change[^>]*>.*</form>",
            markup);
        Assert.Contains("Completed reporting week", markup, StringComparison.Ordinal);
        Assert.Contains("max=\"@maxSelectableWeekValue\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexPage_HasAccessibleLoadingStatusRegion()
    {
        var layoutMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutMarkupPath);

        Assert.Matches(
            "(?is)<span[^>]*data-loading-indicator[^>]*role=\"status\"[^>]*aria-live=\"polite\"[^>]*>\\s*Loading…\\s*</span>",
            markup);
    }

    [Fact]
    public void IndexPage_UsesDeferredMetricsFragmentWithRecoverableLoadingState()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var scriptPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/wwwroot/js/site.js"));
        var markup = File.ReadAllText(indexMarkupPath);
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("data-dashboard-url=", markup, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"false\"", markup, StringComparison.Ordinal);
        Assert.Contains("[data-dashboard-region]", markup, StringComparison.Ordinal);
        Assert.Contains("Retrying dashboard metrics", script, StringComparison.Ordinal);
        Assert.Contains("loadingStatus.focus()", script, StringComparison.Ordinal);
        Assert.Contains("region.addEventListener(\"click\"", script, StringComparison.Ordinal);
        Assert.Contains("data-dashboard-retry", script, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexPage_FallsBackToDefaultDashboardTitleAndDescription()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);
        var partialMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_DashboardMetrics.cshtml"));
        var partialMarkup = File.ReadAllText(partialMarkupPath);

        Assert.Contains("@(Model.Dashboard.DisplayTitle ?? \"Executive metrics\")", partialMarkup, StringComparison.Ordinal);
        Assert.Contains(
            "@(Model.Dashboard.DisplayDescription ?? \"Each tile isolates one decision signal from the selected reporting week.\")",
            partialMarkup,
            StringComparison.Ordinal);
    }
}
