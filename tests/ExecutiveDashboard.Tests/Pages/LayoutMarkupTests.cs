namespace ExecutiveDashboard.Tests.Pages;

public sealed class LayoutMarkupTests
{
    [Fact]
    public void Layout_IncludesWorkIqStatusNavbar()
    {
        var layoutPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutPath);

        Assert.Contains("WorkIQ @workIqStatusLabel", markup);
        Assert.Contains("app-navbar__status", markup);
        Assert.Contains("role=\"status\"", markup);
    }

    [Fact]
    public void Layout_PrimaryNavUsesTabbedMissionControlNavigation()
    {
        var layoutPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutPath);

        Assert.Matches("(?is)<a[^>]*asp-page=\"/Index\"[^>]*aria-current=", markup);
        Assert.Matches("(?is)<a[^>]*asp-page=\"/MissionControl\"[^>]*aria-current=", markup);
        Assert.Contains("app-navbar__link--active", markup);
    }

    [Fact]
    public void Layout_MissionControlLinkNavigatesInSameTabWindow()
    {
        var layoutPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/Shared/_Layout.cshtml"));
        var markup = File.ReadAllText(layoutPath);

        Assert.Contains("asp-page=\"/MissionControl\"", markup);
        Assert.DoesNotContain("target=\"_blank\"", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"noopener\"", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new browser tab", markup, StringComparison.OrdinalIgnoreCase);
    }
}
