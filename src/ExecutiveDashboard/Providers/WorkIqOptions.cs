namespace ExecutiveDashboard.Providers;

public sealed class WorkIqOptions
{
    public const string SectionName = "WorkIQ";
    public const string DefaultEndpoint = "https://workiq.svc.cloud.microsoft/rest";
    public const string AutoProvider = "Auto";
    public const string SampleProvider = "Sample";
    public const string CliProvider = "Cli";
    public const string DirectProvider = "Direct";
    public const string EntraProvider = "Entra";

    public WorkIqProviderMode Mode { get; set; } = WorkIqProviderMode.Auto;

    public bool Enabled { get; set; }

    public string Provider { get; set; } = AutoProvider;

    public string? TenantId { get; set; }

    public string Endpoint { get; set; } = DefaultEndpoint;

    public string[] Scopes { get; set; } = [];

    public string TimeZone { get; set; } = "UTC";

    public string[] EffectiveScopes =>
        Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public bool WantsCliMode =>
        Mode == WorkIqProviderMode.Cli
        || (Enabled && string.Equals(Provider, CliProvider, StringComparison.OrdinalIgnoreCase));

    public bool WantsDirectMode =>
        Mode == WorkIqProviderMode.Entra
        || (Enabled
            && (string.Equals(Provider, DirectProvider, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Provider, EntraProvider, StringComparison.OrdinalIgnoreCase)));

    public bool IsLegacyCliMode =>
        Enabled && string.Equals(Provider, CliProvider, StringComparison.OrdinalIgnoreCase);

    public bool IsLegacyDirectMode =>
        Enabled
        && (string.Equals(Provider, DirectProvider, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Provider, EntraProvider, StringComparison.OrdinalIgnoreCase));

    public bool HasUsableDirectConfiguration =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && EffectiveScopes.Length > 0
        && !string.IsNullOrWhiteSpace(TimeZone);

    public bool HasUsableConfiguration =>
        WantsCliMode
        || Enabled && HasUsableDirectConfiguration;

    public bool CanAttemptLiveQuery =>
        WantsCliMode
        || Mode != WorkIqProviderMode.Sample
        && Enabled
        && HasUsableDirectConfiguration;
}
