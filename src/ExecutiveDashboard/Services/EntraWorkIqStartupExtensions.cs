using ExecutiveDashboard.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ExecutiveDashboard.Services;

internal static class EntraWorkIqStartupExtensions
{
    public static void AddEntraWorkIqAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        WorkIqOptions workIqOptions)
    {
        var entraIdOptions = configuration.GetSection(EntraIdOptions.SectionName).Get<EntraIdOptions>() ?? new EntraIdOptions();
        if (!entraIdOptions.HasUsableConfiguration || !workIqOptions.HasUsableDirectConfiguration)
        {
            return;
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = $"{entraIdOptions.Instance.TrimEnd('/')}/{entraIdOptions.TenantId}/v2.0";
                options.ClientId = entraIdOptions.ClientId;
                options.ClientSecret = entraIdOptions.ClientSecret;
                options.CallbackPath = entraIdOptions.CallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                foreach (var scope in workIqOptions.EffectiveScopes)
                {
                    options.Scope.Add(scope);
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username"
                };
            });
    }
}
