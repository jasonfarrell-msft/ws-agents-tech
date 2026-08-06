using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace ExecutiveDashboard.Providers;

public sealed class HttpContextWorkIqAccessTokenProvider(IHttpContextAccessor httpContextAccessor) : IWorkIqAccessTokenProvider
{
    public async Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new WorkIqAuthenticationException("Live mode requires an active web request before Work IQ can request delegated data.");

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            throw new WorkIqAuthenticationException("Live mode requires a local corporate sign-in before Work IQ can request delegated data.");
        }

        var accessToken = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new WorkIqAuthenticationException("The local corporate sign-in did not yield a delegated Work IQ access token. Sign out and sign in again after confirming delegated consent.");
        }

        var expiresAt = await httpContext.GetTokenAsync("expires_at");
        if (!string.IsNullOrWhiteSpace(expiresAt)
            && DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiration)
            && expiration <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new WorkIqAuthenticationException("The local corporate sign-in for live mode has expired. Sign out and sign in again to refresh delegated Work IQ access.");
        }

        return accessToken;
    }
}
