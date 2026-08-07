using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExecutiveDashboard.Pages;

public sealed class MissionControlModel(IDashboardRequestContextAccessor requestContextAccessor, TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "week")]
    public string? Week { get; set; }

    public MissionControlViewModel MissionControl { get; private set; } =
        new(
            DashboardOperatingMode.Sample,
            LiveDataAccessMode.None,
            "Mission Control live mode unavailable",
            "Live mode is not configured.",
            "Sample user",
            false,
            false,
            false,
            SampleProfile.HealthyWeek);

    public void OnGet()
    {
        Week = IsoWeekSelection.NormalizeWeekRouteValue(Week, timeProvider.GetUtcNow());
        if (PageContext?.ViewData is not null)
        {
            ViewData["SelectedWeekValue"] = Week;
        }
        MissionControl = ToMissionControlViewModel(requestContextAccessor.GetCurrentContext());
    }

    public IActionResult OnPostSetMode(string selectedMode, string? week, string? selectedSampleProfile = null)
    {
        var requestContext = requestContextAccessor.GetCurrentContext();
        var mode = MissionControlModeCookie.TryParse(selectedMode, out var parsed)
            ? parsed
            : DashboardOperatingMode.Sample;
        MissionControlModeCookie.Write(Response, mode, Request.IsHttps);
        var sampleProfile = ResolveSampleProfileSelection(selectedSampleProfile, requestContext.SelectedSampleProfile);
        SampleProfileCookie.Write(Response, sampleProfile, Request.IsHttps);

        var normalizedWeek = IsoWeekSelection.NormalizeWeekRouteValue(week, timeProvider.GetUtcNow());
        return ShouldChallengeForLiveSignIn(requestContext, mode)
            ? ChallengeForLiveMode(normalizedWeek)
            : RedirectToPage(routeValues: ToWeekRouteValues(normalizedWeek));
    }

    public IActionResult OnPostSignIn(string? week)
    {
        var normalizedWeek = IsoWeekSelection.NormalizeWeekRouteValue(week, timeProvider.GetUtcNow());
        var requestContext = requestContextAccessor.GetCurrentContext();
        if (!requestContext.CanSignIn)
        {
            return RedirectToPage(routeValues: ToWeekRouteValues(normalizedWeek));
        }

        return ChallengeForLiveMode(normalizedWeek);
    }

    public IActionResult OnPostSignOut(string? week)
    {
        var normalizedWeek = IsoWeekSelection.NormalizeWeekRouteValue(week, timeProvider.GetUtcNow());
        var requestContext = requestContextAccessor.GetCurrentContext();
        if (!requestContext.CanSignOut)
        {
            return RedirectToPage(routeValues: ToWeekRouteValues(normalizedWeek));
        }

        MissionControlModeCookie.Delete(Response, Request.IsHttps);

        return SignOut(
            new AuthenticationProperties { RedirectUri = BuildMissionControlRedirectUri(normalizedWeek) },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    private static MissionControlViewModel ToMissionControlViewModel(DashboardRequestContext requestContext) =>
        new(
            requestContext.SelectedMode,
            requestContext.LiveDataAccessMode,
            requestContext.LiveSourceName,
            requestContext.LiveSourceDetail,
            requestContext.EffectiveUserLabel,
            requestContext.IsLocalUserAuthenticated,
            requestContext.CanSignIn,
            requestContext.CanSignOut,
            requestContext.SelectedSampleProfile);

    private static bool ShouldChallengeForLiveSignIn(DashboardRequestContext requestContext, DashboardOperatingMode mode) =>
        mode == DashboardOperatingMode.Live
        && requestContext.LiveDataAccessMode == LiveDataAccessMode.Entra
        && requestContext.CanSignIn;

    private static SampleProfile ResolveSampleProfileSelection(
        string? selectedSampleProfile,
        SampleProfile existingSampleProfile) =>
        SampleProfileCookie.TryParse(selectedSampleProfile, out var parsedProfile)
            ? parsedProfile
            : existingSampleProfile;

    private IActionResult ChallengeForLiveMode(string? week) =>
        Challenge(
            new AuthenticationProperties { RedirectUri = BuildMissionControlRedirectUri(week) },
            OpenIdConnectDefaults.AuthenticationScheme);

    private static object? ToWeekRouteValues(string? week) =>
        string.IsNullOrWhiteSpace(week)
            ? null
            : new { week };

    private static string BuildMissionControlRedirectUri(string? week) =>
        string.IsNullOrWhiteSpace(week)
            ? "/MissionControl"
            : $"/MissionControl?week={Uri.EscapeDataString(week)}";
}
