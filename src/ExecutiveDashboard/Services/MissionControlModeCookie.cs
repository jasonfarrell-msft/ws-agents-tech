using ExecutiveDashboard.Models;
using Microsoft.AspNetCore.Http;

namespace ExecutiveDashboard.Services;

public static class MissionControlModeCookie
{
    public const string CookieName = "executive-dashboard-mode";

    public static DashboardOperatingMode Read(IRequestCookieCollection? cookies)
    {
        if (cookies is not null
            && cookies.TryGetValue(CookieName, out var value)
            && TryParse(value, out var parsed))
        {
            return parsed;
        }

        return DashboardOperatingMode.Sample;
    }

    public static void Write(HttpResponse response, DashboardOperatingMode mode, bool secure) =>
        response.Cookies.Append(
            CookieName,
            mode.ToString(),
            CreateCookieOptions(secure, DateTimeOffset.UtcNow.AddDays(30)));

    public static void Delete(HttpResponse response, bool secure) =>
        response.Cookies.Delete(CookieName, CreateCookieOptions(secure));

    public static bool TryParse(string? value, out DashboardOperatingMode mode)
    {
        if (Enum.TryParse<DashboardOperatingMode>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            mode = parsed;
            return true;
        }

        mode = DashboardOperatingMode.Sample;
        return false;
    }

    private static CookieOptions CreateCookieOptions(bool secure, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = secure,
            Path = "/",
            Expires = expires
        };
}
