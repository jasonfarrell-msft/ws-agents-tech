namespace ExecutiveDashboard.Models;

public enum DashboardOperatingMode
{
    Sample = 0,
    Live = 1
}

public enum LiveDataAccessMode
{
    None = 0,
    Cli = 1,
    Entra = 2
}

public sealed record MissionControlViewModel(
    DashboardOperatingMode SelectedMode,
    LiveDataAccessMode LiveDataAccessMode,
    string LiveSourceName,
    string LiveSourceDetail,
    string EffectiveUserLabel,
    bool IsLocalUserAuthenticated,
    bool CanSignIn,
    bool CanSignOut,
    SampleProfile SelectedSampleProfile = SampleProfile.HealthyWeek)
{
    public bool IsLiveSelected => SelectedMode == DashboardOperatingMode.Live;

    public bool IsSampleSelected => SelectedMode == DashboardOperatingMode.Sample;

    public IReadOnlyList<SampleProfileDefinition> SampleProfiles => SampleProfileCatalog.All;
}
