using ExecutiveDashboard.Models;
using ExecutiveDashboard.Pages;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Pages;

public sealed class MissionControlModelTests
{
    public static TheoryData<DashboardOperatingMode, SampleProfile> SelectionStates => new()
    {
        { DashboardOperatingMode.Sample, SampleProfile.HealthyWeek },
        { DashboardOperatingMode.Sample, SampleProfile.OverloadedWeek },
        { DashboardOperatingMode.Sample, SampleProfile.LowEngagementWeek },
        { DashboardOperatingMode.Live, SampleProfile.HealthyWeek },
        { DashboardOperatingMode.Live, SampleProfile.OverloadedWeek },
        { DashboardOperatingMode.Live, SampleProfile.LowEngagementWeek }
    };

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
                    "Live mode uses delegated corporate sign-in.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
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
                    "Live mode is not configured.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Live", null);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains($"{MissionControlModeCookie.CookieName}=Live", httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void OnPostSetMode_WritesSelectedSampleProfileCookie()
    {
        var httpContext = new DefaultHttpContext();
        var model = new MissionControlModel(
            new StubDashboardRequestContextAccessor(
                new DashboardRequestContext(
                    DashboardOperatingMode.Sample,
                    "sample-executive",
                    "Sample user",
                    false,
                    true,
                    false,
                    false,
                    LiveDataAccessMode.None,
                    "Sample",
                    "Sample mode")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Sample", "2026-W31", "OverloadedWeek");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=OverloadedWeek",
            httpContext.Response.Headers.SetCookie.ToString());
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
                    "Live mode is not configured.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Sample", "2026-W31");

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("2026-W31", Assert.IsType<string>(redirect.RouteValues?["week"]));
    }

    [Fact]
    public void OnPostSetMode_RedirectNormalizesInvalidWeekToLastCompletedWeek()
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
                    "Live mode is not configured.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Sample", "2026-W54");

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
                    "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        var result = model.OnPostSetMode("Live", "2026-W31");

        var challenge = Assert.IsType<ChallengeResult>(result);
        Assert.Contains(OpenIdConnectDefaults.AuthenticationScheme, challenge.AuthenticationSchemes);
        Assert.Contains($"{MissionControlModeCookie.CookieName}=Live", httpContext.Response.Headers.SetCookie.ToString());
        Assert.Equal("/MissionControl?week=2026-W31", challenge.Properties?.RedirectUri);
    }

    [Theory]
    [MemberData(nameof(SelectionStates))]
    public void OnGet_ExposesExactlyOneSelectedModeAndExpectedSampleProfile(
        DashboardOperatingMode selectedMode,
        SampleProfile selectedSampleProfile)
    {
        var httpContext = new DefaultHttpContext();
        var model = CreateModel(
            new StubDashboardRequestContextAccessor(
                CreateContext(selectedMode, selectedSampleProfile)),
            httpContext);

        model.OnGet();

        Assert.Equal(selectedMode, model.MissionControl.SelectedMode);
        Assert.Equal(selectedSampleProfile, model.MissionControl.SelectedSampleProfile);
        Assert.Equal(selectedMode == DashboardOperatingMode.Live, model.MissionControl.IsLiveSelected);
        Assert.Equal(selectedMode == DashboardOperatingMode.Sample, model.MissionControl.IsSampleSelected);
        Assert.NotEqual(model.MissionControl.IsLiveSelected, model.MissionControl.IsSampleSelected);
        Assert.Equal(
            SampleProfileCatalog.All.Select(profile => profile.Id),
            model.MissionControl.SampleProfiles.Select(profile => profile.Id));
        Assert.Equal(
            SampleProfileCatalog.All.Select(profile => profile.Description),
            model.MissionControl.SampleProfiles.Select(profile => profile.Description));
    }

    [Fact]
    public void OnPostSetMode_SelectingLiveWithoutExplicitProfile_PreservesExistingSampleProfileCookie()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SampleProfileCookie.CookieName}=LowEngagementWeek";
        var model = CreateModel(CreateRequestContextAccessor(httpContext), httpContext);

        var result = model.OnPostSetMode("Live", "2026-W31");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=LowEngagementWeek",
            httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void OnPostSetMode_ReturningToSampleWithoutExplicitProfile_PreservesExistingSampleProfileCookie()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{MissionControlModeCookie.CookieName}=Live; {SampleProfileCookie.CookieName}=OverloadedWeek";
        var model = CreateModel(CreateRequestContextAccessor(httpContext), httpContext);

        var result = model.OnPostSetMode("Sample", "2026-W31");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=OverloadedWeek",
            httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void OnPostSetMode_InvalidSubmittedProfile_PreservesExistingValidSampleProfileCookie()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SampleProfileCookie.CookieName}=OverloadedWeek";
        var model = CreateModel(CreateRequestContextAccessor(httpContext), httpContext);

        var result = model.OnPostSetMode("Live", "2026-W31", "NotAProfile");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=OverloadedWeek",
            httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void OnPostSetMode_InvalidSubmittedProfile_UsesDefaultWhenNoExistingValidCookieExists()
    {
        var httpContext = new DefaultHttpContext();
        var model = CreateModel(CreateRequestContextAccessor(httpContext), httpContext);

        var result = model.OnPostSetMode("Sample", "2026-W31", "NotAProfile");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=HealthyWeek",
            httpContext.Response.Headers.SetCookie.ToString());
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
                    "Live mode is wired for delegated corporate sign-in, but the local user must sign in before Work IQ can request access tokens.")),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

        model.OnGet();

        Assert.Equal(DashboardOperatingMode.Live, model.MissionControl.SelectedMode);
        Assert.True(model.MissionControl.CanSignIn);
        Assert.False(model.MissionControl.IsLocalUserAuthenticated);
    }

    private static MissionControlModel CreateModel(
        IDashboardRequestContextAccessor requestContextAccessor,
        DefaultHttpContext httpContext) =>
        new(
            requestContextAccessor,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 6, 15, 0, 0, TimeSpan.Zero)))
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };

    private static DashboardRequestContext CreateContext(
        DashboardOperatingMode selectedMode,
        SampleProfile selectedSampleProfile) =>
        new(
            selectedMode,
            "sample-executive",
            selectedMode == DashboardOperatingMode.Live ? "Corporate user sign-in required" : "Sample user (sample-executive)",
            false,
            true,
            false,
            false,
            LiveDataAccessMode.None,
            "Mission Control live mode unavailable",
            "Live mode is not configured.",
            selectedSampleProfile);

    private static IDashboardRequestContextAccessor CreateRequestContextAccessor(DefaultHttpContext httpContext) =>
        new DashboardRequestContextAccessor(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(new DashboardOptions { UserId = "sample-executive" }),
            new DashboardStartupState(
                false,
                false,
                false,
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Live mode is not configured."));

    private sealed class StubDashboardRequestContextAccessor(DashboardRequestContext context) : IDashboardRequestContextAccessor
    {
        public DashboardRequestContext GetCurrentContext() => context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
