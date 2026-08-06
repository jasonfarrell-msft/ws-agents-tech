using ExecutiveDashboard.Models;
using ExecutiveDashboard.Pages;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;

namespace ExecutiveDashboard.Tests.Pages;

public sealed class MissionControlModelTests
{
    [Fact]
    public void OnPostSignOut_ClearsMissionControlModeCookieBeforeSigningOut()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Live,
                    "11111111-2222-3333-4444-555555555555",
                    "executive@contoso.com",
                    true,
                    true,
                    false,
                    true,
                    LiveDataAccessMode.Entra,
                    "Work IQ delegated live access",
                    "Live mode uses delegated corporate sign-in.")))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSignOut("2026-W31");

        var signOut = Assert.IsType<SignOutResult>(result);
        Assert.Contains(CookieAuthenticationDefaults.AuthenticationScheme, signOut.AuthenticationSchemes);
        Assert.Contains(OpenIdConnectDefaults.AuthenticationScheme, signOut.AuthenticationSchemes);
        Assert.Contains($"{MissionControlModeCookie.CookieName}=;", httpContext.Response.Headers.SetCookie.ToString());
        Assert.Equal("/MissionControl?week=2026-W31", signOut.Properties?.RedirectUri);
    }

    [Fact]
    public void OnPostSetMode_WritesMissionControlModeCookie()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Sample,
                    "sample-executive",
                    "Sample user (sample-executive)",
                    false,
                    true,
                    false,
                    false,
                    LiveDataAccessMode.None,
                    "Mission Control live mode unavailable",
                    "Live mode is not configured.")))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Live", null);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains($"{MissionControlModeCookie.CookieName}=Live", httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void OnPostSetMode_RedirectPreservesSelectedWeek()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Sample,
                    "sample-executive",
                    "Sample user (sample-executive)",
                    false,
                    true,
                    false,
                    false,
                    LiveDataAccessMode.None,
                    "Mission Control live mode unavailable",
                    "Live mode is not configured.")))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Sample", "2026-W31");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("2026-W31", Assert.IsType<string>(redirect.RouteValues?["week"]));
    }

    [Fact]
    public void OnPostSetMode_LiveSelectionChallengesWhenSignInIsRequired()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Sample,
                    "live-mode-corporate-user",
                    "Corporate user sign-in required",
                    false,
                    false,
                    true,
                    false,
                    LiveDataAccessMode.Entra,
                    "Work IQ delegated live access",
                    "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.")))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Live", "2026-W31");

        var challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Contains(OpenIdConnectDefaults.AuthenticationScheme, challenge.AuthenticationSchemes);
        Assert.Contains($"{MissionControlModeCookie.CookieName}=Live", httpContext.Response.Headers.SetCookie.ToString());
        Assert.Equal("/MissionControl?week=2026-W31", challenge.Properties?.RedirectUri);
    }

    [Fact]
    public void OnGet_PopulatesMissionControlContext_WhenLiveSignInIsRequired()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Live,
                    "live-mode-corporate-user",
                    "Corporate user sign-in required",
                    false,
                    false,
                    true,
                    false,
                    LiveDataAccessMode.Entra,
                    "Work IQ delegated live access",
                    "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.")))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        model.OnGet();

        Assert.Equal(DashboardOperatingMode.Live, model.MissionControl.SelectedMode);
        Assert.True(model.MissionControl.CanSignIn);
        Assert.False(model.MissionControl.IsLocalUserAuthenticated);
    }

    private sealed class StubDashboardRequestContextAccessor(DashboardRequestContext context) : IDashboardRequestContextAccessor
    {
        public DashboardRequestContext GetCurrentContext() => context;
    }
}
