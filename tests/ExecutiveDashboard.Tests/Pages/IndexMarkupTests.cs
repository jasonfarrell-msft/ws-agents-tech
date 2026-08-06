namespace ExecutiveDashboard.Tests.Pages;

public sealed class IndexMarkupTests
{
    [Fact]
    public void IndexPage_MissionControlLinkNavigatesInSameTabWindow()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);

        Assert.Matches(
            "(?is)<a[^>]*asp-page=\"/MissionControl\"[^>]*asp-route-week=\"@Model.SelectedWeekValue\"[^>]*>\\s*Go\\s+to\\s+Mission\\s+Control\\s*</a>",
            markup);
        Assert.DoesNotContain("target=\"_blank\"", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"noopener\"", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opens in a separate browser tab", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new browser tab", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedLayout_ExposesWorkIqStatusInNavbar()
    {
        var layoutMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutMarkupPath);

        Assert.Matches(
            "(?is)<nav[^>]*class=\"app-navbar\"[^>]*>.*<div[^>]*app-navbar__status[^>]*role=\"status\"[^>]*aria-live=\"polite\"[^>]*>.*WorkIQ\\s+@workIqStatusLabel.*</div>.*</nav>",
            markup);
    }

    [Fact]
    public void IndexPage_WeekPickerUsesGetAndBindsSelectedWeek()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);

        Assert.Matches(
            "(?is)<form[^>]*method=\"get\"[^>]*data-immediate-loading[^>]*>.*<input[^>]*type=\"week\"[^>]*name=\"week\"[^>]*value=\"@Model.SelectedWeekValue\"[^>]*data-submit-on-change[^>]*>.*</form>",
            markup);
    }

    [Fact]
    public void IndexPage_HasAccessibleLoadingStatusRegion()
    {
        var indexMarkupPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Index.cshtml"));
        var markup = File.ReadAllText(indexMarkupPath);

        Assert.Matches(
            "(?is)<p[^>]*data-loading-indicator[^>]*role=\"status\"[^>]*aria-live=\"polite\"[^>]*>\\s*Loading\\s+selected\\s+week\\s+metrics\\W*\\s*</p>",
            markup);
    }
}
