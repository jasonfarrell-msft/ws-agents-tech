using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ExecutiveDashboard.Pages;

public sealed class MissionControlModel(IDashboardRequestContextAccessor requestContextAccessor) : PageModel
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
            false);

    public void OnGet()
    {
        MissionControl = ToMissionControlViewModel(requestContextAccessor.GetCurrentContext());
    }

    public IActionResult OnPostSetMode(string selectedMode, string? week)
    {
        var mode = MissionControlModeCookie.TryParse(selectedMode, out var parsed)
            ? parsed
            : DashboardOperatingMode.Sample;
        MissionControlModeCookie.Write(Response, mode, Request.IsHttps);

        var requestContext = requestContextAccessor.GetCurrentContext();
        return ShouldChallengeForLiveSignIn(requestContext, mode)
            ? ChallengeForLiveMode(week)
            : RedirectToPage(routeValues: ToWeekRouteValues(week));
    }

    public IActionResult OnPostSignIn(string? week)
    {
        var requestContext = requestContextAccessor.GetCurrentContext();
        if (!requestContext.CanSignIn)
        {
            return RedirectToPage(routeValues: ToWeekRouteValues(week));
        }

        return ChallengeForLiveMode(week);
    }

    public IActionResult OnPostSignOut(string? week)
    {
        var requestContext = requestContextAccessor.GetCurrentContext();
        if (!requestContext.CanSignOut)
        {
            return RedirectToPage(routeValues: ToWeekRouteValues(week));
        }

        MissionControlModeCookie.Delete(Response, Request.IsHttps);

        return SignOut(
            new AuthenticationProperties { RedirectUri = BuildMissionControlRedirectUri(week) },
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
            requestContext.CanSignOut);

    private static bool ShouldChallengeForLiveSignIn(DashboardRequestContext requestContext, DashboardOperatingMode mode) =>
        mode == DashboardOperatingMode.Live
        && requestContext.LiveDataAccessMode == LiveDataAccessMode.Entra
        && requestContext.CanSignIn;

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
