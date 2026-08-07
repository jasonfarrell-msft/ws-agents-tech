using ExecutiveDashboard.Models;

namespace ExecutiveDashboard.Services;

public static class SampleProfileCookie
{
    public const string CookieName = "executive-dashboard-sample-profile";

    public static SampleProfile Read(IRequestCookieCollection? cookies)
    {
        if (cookies is not null
            && cookies.TryGetValue(CookieName, out var value)
            && TryParse(value, out var profile))
        {
            return profile;
        }

        return SampleProfile.HealthyWeek;
    }

    public static void Write(HttpResponse response, SampleProfile profile, bool secure) =>
        response.Cookies.Append(
            CookieName,
            profile.ToString(),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = secure,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

    public static bool TryParse(string? value, out SampleProfile profile)
    {
        if (Enum.TryParse<SampleProfile>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            profile = parsed;
            return true;
        }

        profile = SampleProfile.HealthyWeek;
        return false;
    }
}
