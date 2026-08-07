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
    public void IndexPage_FallsBackToDefaultDashboardTitleAndDescription()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);

        Assert.Contains("@(Model.Dashboard.DisplayTitle ?? \"Executive metrics\")", markup, StringComparison.Ordinal);
        Assert.Contains(
            "@(Model.Dashboard.DisplayDescription ?? \"Each tile isolates one decision signal from the selected reporting week.\")",
            markup,
            StringComparison.Ordinal);
    }
}
