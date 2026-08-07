namespace ExecutiveDashboard.Models;

public enum SampleProfile
{
    HealthyWeek = 0,
    OverloadedWeek = 1,
    LowEngagementWeek = 2
}

public sealed record SampleProfileDefinition(
    SampleProfile Id,
    string Title,
    string Description);

public static class SampleProfileCatalog
{
    public static IReadOnlyList<SampleProfileDefinition> All { get; } =
    [
        new(
            SampleProfile.HealthyWeek,
            "Healthy Week",
            "A sustainable schedule with focused meetings, clear decisions, and protected work time."),
        new(
            SampleProfile.OverloadedWeek,
            "Overloaded Week",
            "A meeting-heavy schedule with long sessions, recurring load, and limited recovery time."),
        new(
            SampleProfile.LowEngagementWeek,
            "Low-Engagement Week",
            "A passive meeting pattern with limited speaking time and unresolved decisions.")
    ];

    public static SampleProfileDefinition Get(SampleProfile profile) =>
        All.First(definition => definition.Id == profile);
}
