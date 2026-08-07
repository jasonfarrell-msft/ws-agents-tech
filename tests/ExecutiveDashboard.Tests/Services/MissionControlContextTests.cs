using System.Net;
using System.Security.Claims;
using ExecutiveDashboard.Models;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Services;

public sealed class MissionControlContextTests
{
    [Fact]
    public void MissionControlCookie_ReadDefaultsToSampleMode()
    {
        var context = new DefaultHttpContext();

        var mode = MissionControlModeCookie.Read(context.Request.Cookies);

        Assert.Equal(DashboardOperatingMode.Sample, mode);
    }

    [Fact]
    public void MissionControlCookie_WritePersistsLiveMode()
    {
        var context = new DefaultHttpContext();

        MissionControlModeCookie.Write(context.Response, DashboardOperatingMode.Live, secure: true);

        Assert.Contains("executive-dashboard-mode=Live", context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void MissionControlCookie_WriteUsesRootPathForCrossWindowMissionControl()
    {
        var context = new DefaultHttpContext();

        MissionControlModeCookie.Write(context.Response, DashboardOperatingMode.Live, secure: false);

        var setCookieHeader = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("executive-dashboard-mode=Live", setCookieHeader);
        Assert.Contains("path=/", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissionControlCookie_ReadRejectsUndefinedEnumValues()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{MissionControlModeCookie.CookieName}=99";

        var mode = MissionControlModeCookie.Read(context.Request.Cookies);

        Assert.Equal(DashboardOperatingMode.Sample, mode);
    }

    [Fact]
    public void SampleProfileCookie_DefaultsToHealthyAndPersistsSelection()
    {
        var defaultContext = new DefaultHttpContext();
        Assert.Equal(SampleProfile.HealthyWeek, SampleProfileCookie.Read(defaultContext.Request.Cookies));

        var responseContext = new DefaultHttpContext();
        SampleProfileCookie.Write(responseContext.Response, SampleProfile.OverloadedWeek, secure: false);

        Assert.Contains(
            $"{SampleProfileCookie.CookieName}=OverloadedWeek",
            responseContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public void DashboardRequestContextAccessor_ReadsSelectedSampleProfile()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SampleProfileCookie.CookieName}=LowEngagementWeek";

        var context = CreateAccessor(httpContext).GetCurrentContext();

        Assert.Equal(SampleProfile.LowEngagementWeek, context.SelectedSampleProfile);
    }

    [Fact]
    public void DashboardRequestContextAccessor_UsesConfiguredSampleUserByDefault()
    {
        var accessor = CreateAccessor(
            startupState: new DashboardStartupState(
                false,
                false,
                false,
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Live mode is not configured."));

        var context = accessor.GetCurrentContext();

        Assert.Equal(DashboardOperatingMode.Sample, context.SelectedMode);
        Assert.Equal("sample-executive", context.QueryUserId);
        Assert.Equal("Sample user (sample-executive)", context.EffectiveUserLabel);
        Assert.False(context.IsLocalUserAuthenticated);
        Assert.True(context.CanUseLiveData);
    }

    [Fact]
    public void DashboardRequestContextAccessor_UsesCliIdentityWhenLiveModeIsSelected()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        httpContext.Request.Headers.Cookie = $"{MissionControlModeCookie.CookieName}=Live";
        var accessor = CreateAccessor(
            httpContext,
            new DashboardStartupState(
                false,
                true,
                false,
                LiveDataAccessMode.Cli,
                "Work IQ CLI",
                "Live mode uses the installed Work IQ CLI."));

        var context = accessor.GetCurrentContext();

        Assert.Equal(DashboardOperatingMode.Live, context.SelectedMode);
        Assert.Equal("workiq-cli-signed-in-user", context.QueryUserId);
        Assert.Equal("Work IQ CLI signed-in user", context.EffectiveUserLabel);
        Assert.True(context.CanUseLiveData);
        Assert.Contains("Work IQ CLI session", context.LiveSourceDetail);
    }

    [Fact]
    public void DashboardRequestContextAccessor_RejectsRemoteCliLiveMode()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        httpContext.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.5");
        httpContext.Request.Headers.Cookie = $"{MissionControlModeCookie.CookieName}=Live";
        var accessor = CreateAccessor(
            httpContext,
            new DashboardStartupState(
                false,
                true,
                false,
                LiveDataAccessMode.Cli,
                "Work IQ CLI",
                "Live mode uses the installed Work IQ CLI."));

        var context = accessor.GetCurrentContext();

        Assert.Equal(DashboardOperatingMode.Live, context.SelectedMode);
        Assert.False(context.CanUseLiveData);
        Assert.Contains("limited to localhost", context.LiveSourceDetail);
    }

    [Fact]
    public void DashboardRequestContextAccessor_UsesAuthenticatedEntraUserWhenLiveModeIsSelected()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim("oid", "11111111-2222-3333-4444-555555555555"),
                        new Claim("preferred_username", "executive@contoso.com"),
                        new Claim(ClaimTypes.Name, "Executive User")
                    ],
                    authenticationType: "oidc"))
        };
        httpContext.Request.Headers.Cookie = $"{MissionControlModeCookie.CookieName}=Live";

        var accessor = CreateAccessor(
            httpContext,
            new DashboardStartupState(
                true,
                true,
                false,
                LiveDataAccessMode.Entra,
                "Work IQ delegated live access",
                "Live mode uses delegated corporate sign-in."));

        var context = accessor.GetCurrentContext();

        Assert.Equal(DashboardOperatingMode.Live, context.SelectedMode);
        Assert.Equal("11111111-2222-3333-4444-555555555555", context.QueryUserId);
        Assert.Equal("executive@contoso.com", context.EffectiveUserLabel);
        Assert.True(context.IsLocalUserAuthenticated);
        Assert.True(context.CanUseLiveData);
        Assert.False(context.CanSignIn);
        Assert.True(context.CanSignOut);
    }

    [Fact]
    public void DashboardRequestContextAccessor_RequiresSignInBeforeEntraLiveModeCanRun()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{MissionControlModeCookie.CookieName}=Live";

        var accessor = CreateAccessor(
            httpContext,
            new DashboardStartupState(
                true,
                true,
                false,
                LiveDataAccessMode.Entra,
                "Work IQ delegated live access",
                "Live mode uses delegated corporate sign-in."));

        var context = accessor.GetCurrentContext();

        Assert.Equal(DashboardOperatingMode.Live, context.SelectedMode);
        Assert.False(context.IsLocalUserAuthenticated);
        Assert.False(context.CanUseLiveData);
        Assert.True(context.CanSignIn);
        Assert.False(context.CanSignOut);
        Assert.Equal("Corporate user sign-in required", context.EffectiveUserLabel);
        Assert.Contains("requires a local corporate sign-in", context.LiveSourceDetail);
    }

    private static DashboardRequestContextAccessor CreateAccessor(
        DefaultHttpContext? httpContext = null,
        DashboardStartupState? startupState = null) =>
        new(
            new HttpContextAccessor
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            },
            Options.Create(new DashboardOptions { UserId = "sample-executive" }),
            startupState ?? new DashboardStartupState(
                false,
                false,
                false,
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Live mode is not configured."));
}
