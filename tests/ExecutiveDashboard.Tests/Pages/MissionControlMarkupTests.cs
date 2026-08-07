using System.Text.RegularExpressions;

namespace ExecutiveDashboard.Tests.Pages;

public sealed class MissionControlMarkupTests
{
    [Fact]
    public void MissionControl_OffersAllSampleProfiles()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/MissionControl.cshtml"));
        var markup = File.ReadAllText(path);

        Assert.Contains("name=\"selectedSampleProfile\"", markup, StringComparison.Ordinal);
        Assert.Contains("Model.MissionControl.SampleProfiles", markup, StringComparison.Ordinal);
        Assert.Equal(3, ExecutiveDashboard.Models.SampleProfileCatalog.All.Count);
        Assert.Equal(
            ["Healthy Week", "Overloaded Week", "Low-Engagement Week"],
            ExecutiveDashboard.Models.SampleProfileCatalog.All.Select(profile => profile.Title));
    }

    [Fact]
    public void MissionControl_BindsSingleCheckedStateForEachRadioGroup()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/ExecutiveDashboard/Pages/MissionControl.cshtml"));
        var markup = File.ReadAllText(path);

        Assert.Equal(2, Regex.Matches(markup, "name=\"selectedMode\"").Count);
        Assert.Single(Regex.Matches(markup, "checked=\"@Model\\.MissionControl\\.IsSampleSelected\"").Cast<Match>());
        Assert.Single(Regex.Matches(markup, "checked=\"@Model\\.MissionControl\\.IsLiveSelected\"").Cast<Match>());
        Assert.Single(Regex.Matches(markup, "name=\"selectedSampleProfile\"").Cast<Match>());
        Assert.Single(
            Regex.Matches(markup, "checked=\"@\\(profile\\.Id == Model\\.MissionControl\\.SelectedSampleProfile\\)\"").Cast<Match>());
        Assert.Contains("@profile.Description", markup, StringComparison.Ordinal);
    }
}
