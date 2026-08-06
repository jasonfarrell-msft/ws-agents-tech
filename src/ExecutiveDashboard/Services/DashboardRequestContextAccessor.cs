using System.Net;
using System.Security.Claims;
using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Services;

public sealed record DashboardRequestContext(
    DashboardOperatingMode SelectedMode,
    string QueryUserId,
    string EffectiveUserLabel,
    bool IsLocalUserAuthenticated,
    bool CanUseLiveData,
    bool CanSignIn,
    bool CanSignOut,
    LiveDataAccessMode LiveDataAccessMode,
    string LiveSourceName,
    string LiveSourceDetail);

public interface IDashboardRequestContextAccessor
{
    DashboardRequestContext GetCurrentContext();
}

public sealed class DashboardRequestContextAccessor(
    IHttpContextAccessor httpContextAccessor,
    IOptions<DashboardOptions> dashboardOptions,
    DashboardStartupState startupState) : IDashboardRequestContextAccessor
{
    public DashboardRequestContext GetCurrentContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var selectedMode = MissionControlModeCookie.Read(httpContext?.Request.Cookies);
        var isAuthenticated = httpContext?.User.Identity?.IsAuthenticated == true;
        var isLocalMachineRequest = IsLocalMachineRequest(httpContext);
        var canSignIn = startupState.EntraSignInEnabled && !isAuthenticated;
        var canSignOut = startupState.EntraSignInEnabled && isAuthenticated;
        var canUseLiveData = selectedMode != DashboardOperatingMode.Live
            || IsLiveModeAllowed(startupState.LiveDataAccessMode, isAuthenticated, isLocalMachineRequest);

        var (queryUserId, effectiveUserLabel) = ResolveUserIdentity(
            httpContext?.User,
            selectedMode,
            startupState.LiveDataAccessMode,
            dashboardOptions.Value.UserId);

        return new DashboardRequestContext(
            selectedMode,
            queryUserId,
            effectiveUserLabel,
            isAuthenticated,
            canUseLiveData,
            canSignIn,
            canSignOut,
            startupState.LiveDataAccessMode,
            startupState.LiveSourceName,
            BuildLiveSourceDetail(selectedMode, startupState, isAuthenticated, canUseLiveData));
    }

    private static (string QueryUserId, string EffectiveUserLabel) ResolveUserIdentity(
        ClaimsPrincipal? user,
        DashboardOperatingMode selectedMode,
        LiveDataAccessMode liveDataAccessMode,
        string sampleUserId)
    {
        if (selectedMode == DashboardOperatingMode.Sample)
        {
            return (sampleUserId, $"Sample user ({sampleUserId})");
        }

        if (liveDataAccessMode == LiveDataAccessMode.Entra && user?.Identity?.IsAuthenticated == true)
        {
            var identifier = FirstNonEmptyClaim(user, "oid", ClaimTypes.NameIdentifier, "preferred_username", ClaimTypes.Upn, ClaimTypes.Email)
                ?? sampleUserId;
            var displayName = FirstNonEmptyClaim(user, "preferred_username", ClaimTypes.Email, ClaimTypes.Name, ClaimTypes.Upn, "oid")
                ?? identifier;
            return (identifier, displayName);
        }

        if (liveDataAccessMode == LiveDataAccessMode.Cli)
        {
            return ("workiq-cli-signed-in-user", "Work IQ CLI signed-in user");
        }

        return ("live-mode-corporate-user", "Corporate user sign-in required");
    }

    private static string BuildLiveSourceDetail(
        DashboardOperatingMode selectedMode,
        DashboardStartupState startupState,
        bool isAuthenticated,
        bool canUseLiveData)
    {
        if (!canUseLiveData)
        {
            return startupState.LiveDataAccessMode switch
            {
                LiveDataAccessMode.Cli =>
                    "CLI-backed live mode is limited to localhost on the machine that owns the signed-in Work IQ CLI session.",
                LiveDataAccessMode.Entra when selectedMode == DashboardOperatingMode.Live =>
                    "Live mode requires a local corporate sign-in before Work IQ can request access tokens.",
                _ => startupState.LiveModeMessage
            };
        }

        return startupState.LiveDataAccessMode switch
        {
            LiveDataAccessMode.Cli when selectedMode == DashboardOperatingMode.Live =>
                $"{startupState.LiveModeMessage} Live requests run under the Work IQ CLI session on this machine.",
            LiveDataAccessMode.Cli =>
                $"{startupState.LiveModeMessage} Sample mode stays isolated until Live is selected.",
            LiveDataAccessMode.Entra when isAuthenticated =>
                $"{startupState.LiveModeMessage} Live requests use the currently signed-in corporate user.",
            LiveDataAccessMode.Entra =>
                "Live mode requires a local corporate sign-in before Work IQ can request access tokens.",
            _ => startupState.LiveModeMessage
        };
    }

    private static bool IsLocalMachineRequest(HttpContext? httpContext)
    {
        var remoteIpAddress = httpContext?.Connection.RemoteIpAddress;
        return remoteIpAddress is not null && IPAddress.IsLoopback(remoteIpAddress);
    }

    private static bool IsLiveModeAllowed(
        LiveDataAccessMode liveDataAccessMode,
        bool isAuthenticated,
        bool isLocalMachineRequest) =>
        liveDataAccessMode switch
        {
            LiveDataAccessMode.Cli => isLocalMachineRequest,
            LiveDataAccessMode.Entra => isAuthenticated,
            _ => true
        };

    private static string? FirstNonEmptyClaim(ClaimsPrincipal user, params string[] claimTypes) =>
        claimTypes
            .Select(claimType => user.FindFirst(claimType)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
