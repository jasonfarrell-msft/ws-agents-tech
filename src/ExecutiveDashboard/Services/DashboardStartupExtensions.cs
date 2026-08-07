using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;

namespace ExecutiveDashboard.Services;

public sealed record DashboardStartupState(
    bool EntraSignInEnabled,
    bool WorkIqIntegrationEnabled,
    bool DashboardRequiresAuthentication,
    LiveDataAccessMode LiveDataAccessMode,
    string LiveSourceName,
    string LiveModeMessage);

public static class DashboardStartupExtensions
{
    public static DashboardStartupState AddExecutiveDashboardServices(this WebApplicationBuilder builder)
    {
        var workIqOptions = builder.Configuration.GetSection(WorkIqOptions.SectionName).Get<WorkIqOptions>() ?? new WorkIqOptions();
        var cliOptions = builder.Configuration.GetSection(WorkIqCliOptions.SectionName).Get<WorkIqCliOptions>() ?? new WorkIqCliOptions();
        var entraIdOptions = builder.Configuration.GetSection(EntraIdOptions.SectionName).Get<EntraIdOptions>() ?? new EntraIdOptions();
        var configuredSelection = ResolveConfiguredSelection(builder.Configuration, workIqOptions);
        var explicitSelection = ToExplicitSelection(configuredSelection);
        var liveConfiguration = ResolveLiveConfiguration(workIqOptions, explicitSelection, cliOptions, entraIdOptions);
        var startupState = new DashboardStartupState(
            liveConfiguration.LiveDataAccessMode == LiveDataAccessMode.Entra,
            liveConfiguration.LiveDataAccessMode != LiveDataAccessMode.None,
            false,
            liveConfiguration.LiveDataAccessMode,
            liveConfiguration.LiveSourceName,
            liveConfiguration.LiveModeMessage);

        builder.Services.AddRazorPages();
        builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));
        builder.Services.Configure<EntraIdOptions>(builder.Configuration.GetSection(EntraIdOptions.SectionName));
        builder.Services.Configure<WorkIqOptions>(builder.Configuration.GetSection(WorkIqOptions.SectionName));
        builder.Services.PostConfigure<WorkIqOptions>(options => ApplyResolvedSelection(options, configuredSelection, liveConfiguration.LiveDataAccessMode));
        builder.Services.Configure<WorkIqCliOptions>(builder.Configuration.GetSection(WorkIqCliOptions.SectionName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<SampleMeetingDataProvider>();
        builder.Services.AddSingleton(startupState);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IDashboardRequestContextAccessor, DashboardRequestContextAccessor>();

        switch (liveConfiguration.LiveDataAccessMode)
        {
            case LiveDataAccessMode.Cli:
                builder.Services.AddSingleton<IWorkIqCliRunner, SystemWorkIqCliRunner>();
                builder.Services.AddScoped<IWorkIqMeetingDataClient, WorkIqCliMeetingDataClient>();
                builder.Services.AddScoped<ILiveMeetingDataProvider, WorkIqMeetingDataProvider>();
                break;
            case LiveDataAccessMode.Entra:
                builder.Services.AddScoped<IWorkIqAccessTokenProvider, HttpContextWorkIqAccessTokenProvider>();
                builder.Services.AddHttpClient<IWorkIqChatClient, WorkIqChatClient>();
                builder.Services.AddScoped<IWorkIqMeetingDataClient>(sp => sp.GetRequiredService<IWorkIqChatClient>());
                builder.Services.AddScoped<ILiveMeetingDataProvider, WorkIqMeetingDataProvider>();
                builder.Services.AddEntraWorkIqAuthentication(builder.Configuration, workIqOptions);
                break;
            default:
                builder.Services.AddScoped<ILiveMeetingDataProvider>(sp =>
                    new UnavailableWorkIqMeetingDataProvider(
                        sp.GetRequiredService<TimeProvider>(),
                        liveConfiguration.LiveModeMessage));
                break;
        }

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMeetingMetricsService, MeetingMetricsService>();
        builder.Services.AddScoped<IMeetingDataProvider, SwitchableMeetingDataProvider>();
        builder.Services.AddScoped<IDashboardService, DashboardService>();

        return startupState;
    }

    public static void UseExecutiveDashboard(this WebApplication app, DashboardStartupState startupState)
    {
        if (startupState.EntraSignInEnabled)
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapStaticAssets();
        app.MapRazorPages()
           .WithStaticAssets();
    }

    private static (LiveDataAccessMode LiveDataAccessMode, string LiveSourceName, string LiveModeMessage) ResolveLiveConfiguration(
        WorkIqOptions workIqOptions,
        WorkIqExplicitSelection explicitSelection,
        WorkIqCliOptions cliOptions,
        EntraIdOptions entraIdOptions)
    {
        if (explicitSelection == WorkIqExplicitSelection.Sample)
        {
            return (
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Configuration is pinned to sample mode. Change WorkIQ:Mode to Cli or Entra to enable live requests.");
        }

        var explicitCliSelection = explicitSelection == WorkIqExplicitSelection.Cli;
        var explicitDirectSelection = explicitSelection == WorkIqExplicitSelection.Entra;
        var cliUsable = (explicitCliSelection || cliOptions.Enabled)
            && cliOptions.HasUsableConfiguration;
        var directConfigured = explicitDirectSelection
            || explicitSelection == WorkIqExplicitSelection.None && workIqOptions.Enabled && !explicitCliSelection;
        var directUsable = directConfigured && workIqOptions.HasUsableDirectConfiguration && entraIdOptions.HasUsableConfiguration;

        if (explicitCliSelection)
        {
            if (cliUsable)
            {
                return CreateCliLiveConfiguration(directConfigured: false, entraIdOptions.HasUsableConfiguration);
            }

            return (
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Configuration explicitly selects the Work IQ CLI. Configure WorkIQ:Cli or change WorkIQ:Mode/Provider to Entra before delegated live access can be used.");
        }

        if (directUsable)
        {
            return (
                LiveDataAccessMode.Entra,
                "Work IQ delegated live access",
                "Live mode uses the local corporate sign-in and delegated Work IQ consent captured by this Razor Pages app.");
        }

        if (cliUsable)
        {
            return CreateCliLiveConfiguration(directConfigured, entraIdOptions.HasUsableConfiguration);
        }

        if (directConfigured)
        {
            return (
                LiveDataAccessMode.None,
                "Mission Control live mode unavailable",
                "Live mode needs either the Work IQ CLI or a local AzureAd app registration with delegated Work IQ consent. No approved live source is configured.");
        }

        return (
            LiveDataAccessMode.None,
            "Mission Control live mode unavailable",
            "Live mode is not configured. Install and sign in to the Work IQ CLI, or configure AzureAd plus delegated Work IQ consent for this local app.");
    }

    private static (LiveDataAccessMode LiveDataAccessMode, string LiveSourceName, string LiveModeMessage) CreateCliLiveConfiguration(
        bool directConfigured,
        bool entraConfigured)
    {
        var message = directConfigured && !entraConfigured
            ? "AzureAd app registration or delegated consent is unavailable, so live mode falls back to the installed Work IQ CLI."
            : "Live mode uses the installed Work IQ CLI; the CLI session owns corporate sign-in and delegated token acquisition.";
        return (LiveDataAccessMode.Cli, "Work IQ CLI", message);
    }

    private static WorkIqConfiguredSelection? ResolveConfiguredSelection(
        IConfiguration configuration,
        WorkIqOptions workIqOptions)
    {
        var modeCandidate = BuildModeCandidate(configuration, workIqOptions);
        var providerCandidate = BuildProviderCandidate(configuration, workIqOptions);
        if (!modeCandidate.HasValue)
        {
            return providerCandidate?.Selection;
        }

        if (!providerCandidate.HasValue)
        {
            return modeCandidate.Value.Selection;
        }

        var modeSelection = modeCandidate.Value;
        var providerSelection = providerCandidate.Value;
        if (modeSelection.SourceIndex > providerSelection.SourceIndex)
        {
            return modeSelection.Selection;
        }

        if (providerSelection.SourceIndex > modeSelection.SourceIndex)
        {
            return providerSelection.Selection;
        }

        if (modeSelection.Selection == providerSelection.Selection)
        {
            return modeSelection.Selection;
        }

        if (modeSelection.Selection == WorkIqConfiguredSelection.Auto)
        {
            return providerSelection.Selection;
        }

        if (providerSelection.Selection == WorkIqConfiguredSelection.Auto)
        {
            return modeSelection.Selection;
        }

        if (modeSelection.Selection == WorkIqConfiguredSelection.Sample
            || providerSelection.Selection == WorkIqConfiguredSelection.Sample)
        {
            return WorkIqConfiguredSelection.Sample;
        }

        return modeSelection.Selection;
    }

    private static void ApplyResolvedSelection(
        WorkIqOptions options,
        WorkIqConfiguredSelection? configuredSelection,
        LiveDataAccessMode liveDataAccessMode)
    {
        switch (liveDataAccessMode)
        {
            case LiveDataAccessMode.Cli:
                options.Enabled = true;
                options.Mode = WorkIqProviderMode.Cli;
                options.Provider = configuredSelection == WorkIqConfiguredSelection.Auto
                    ? WorkIqOptions.AutoProvider
                    : WorkIqOptions.CliProvider;
                return;
            case LiveDataAccessMode.Entra:
                options.Enabled = true;
                options.Mode = WorkIqProviderMode.Entra;
                options.Provider = configuredSelection == WorkIqConfiguredSelection.Auto
                    ? WorkIqOptions.AutoProvider
                    : string.Equals(options.Provider, WorkIqOptions.DirectProvider, StringComparison.OrdinalIgnoreCase)
                        ? WorkIqOptions.DirectProvider
                        : WorkIqOptions.EntraProvider;
                return;
        }

        if (configuredSelection == WorkIqConfiguredSelection.Sample)
        {
            options.Enabled = false;
            options.Mode = WorkIqProviderMode.Sample;
            options.Provider = WorkIqOptions.SampleProvider;
        }
        else
        {
            options.Enabled = false;
            options.Mode = WorkIqProviderMode.Auto;
            options.Provider = WorkIqOptions.AutoProvider;
        }
    }

    private static WorkIqSelectionCandidate? BuildModeCandidate(
        IConfiguration configuration,
        WorkIqOptions workIqOptions)
    {
        var sourceIndex = GetSettingSourceIndex(configuration, $"{WorkIqOptions.SectionName}:Mode");
        if (!sourceIndex.HasValue)
        {
            return null;
        }

        return workIqOptions.Mode switch
        {
            WorkIqProviderMode.Auto => new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Auto, sourceIndex.Value),
            WorkIqProviderMode.Sample => new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Sample, sourceIndex.Value),
            WorkIqProviderMode.Cli => new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Cli, sourceIndex.Value),
            WorkIqProviderMode.Entra => new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Entra, sourceIndex.Value),
            _ => null
        };
    }

    private static WorkIqSelectionCandidate? BuildProviderCandidate(
        IConfiguration configuration,
        WorkIqOptions workIqOptions)
    {
        var sourceIndex = GetSettingSourceIndex(configuration, $"{WorkIqOptions.SectionName}:Provider");
        if (!sourceIndex.HasValue)
        {
            return null;
        }

        if (string.Equals(workIqOptions.Provider, WorkIqOptions.AutoProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Auto, sourceIndex.Value);
        }

        if (string.Equals(workIqOptions.Provider, WorkIqOptions.SampleProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Sample, sourceIndex.Value);
        }

        if (string.Equals(workIqOptions.Provider, WorkIqOptions.CliProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Cli, sourceIndex.Value);
        }

        if (string.Equals(workIqOptions.Provider, WorkIqOptions.DirectProvider, StringComparison.OrdinalIgnoreCase)
            || string.Equals(workIqOptions.Provider, WorkIqOptions.EntraProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkIqSelectionCandidate(WorkIqConfiguredSelection.Entra, sourceIndex.Value);
        }

        return null;
    }

    private static WorkIqExplicitSelection ToExplicitSelection(WorkIqConfiguredSelection? selection) => selection switch
    {
        WorkIqConfiguredSelection.Sample => WorkIqExplicitSelection.Sample,
        WorkIqConfiguredSelection.Cli => WorkIqExplicitSelection.Cli,
        WorkIqConfiguredSelection.Entra => WorkIqExplicitSelection.Entra,
        _ => WorkIqExplicitSelection.None
    };

    private static int? GetSettingSourceIndex(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return null;
        }

        var providers = root.Providers.ToArray();
        for (var providerIndex = providers.Length - 1; providerIndex >= 0; providerIndex--)
        {
            if (providers[providerIndex].TryGet(key, out _))
            {
                return providerIndex;
            }
        }

        return null;
    }

    private enum WorkIqExplicitSelection
    {
        None = 0,
        Sample = 1,
        Cli = 2,
        Entra = 3
    }

    private enum WorkIqConfiguredSelection
    {
        Auto = 0,
        Sample = 1,
        Cli = 2,
        Entra = 3
    }

    private readonly record struct WorkIqSelectionCandidate(
        WorkIqConfiguredSelection Selection,
        int SourceIndex);
}
