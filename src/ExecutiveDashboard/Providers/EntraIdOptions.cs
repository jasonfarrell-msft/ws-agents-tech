namespace ExecutiveDashboard.Providers;

public sealed class EntraIdOptions
{
    public const string SectionName = "AzureAd";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string CallbackPath { get; set; } = "/signin-oidc";

    public bool HasAppRegistrationConfiguration =>
        Uri.TryCreate(Instance, UriKind.Absolute, out var instance)
        && instance.Scheme == Uri.UriSchemeHttps
        && Guid.TryParse(TenantId, out _)
        && Guid.TryParse(ClientId, out _)
        && !string.IsNullOrWhiteSpace(CallbackPath)
        && CallbackPath.StartsWith("/", StringComparison.Ordinal);

    public bool HasUsableConfiguration =>
        HasAppRegistrationConfiguration
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
