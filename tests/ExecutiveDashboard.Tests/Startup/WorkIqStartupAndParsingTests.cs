using ExecutiveDashboard.Models;
using ExecutiveDashboard.Providers;
using ExecutiveDashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExecutiveDashboard.Tests.Startup;

public sealed class WorkIqStartupAndParsingTests
{
    [Fact]
    public async Task Startup_UsesSampleProviderWhenWorkIqConfigurationIsMissing()
    {
        var (app, startupState) = BuildApp(new Dictionary<string, string?>());
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IMeetingDataProvider>();

            var dataSet = await provider.GetMeetingsAsync(CreateQuery());

            Assert.True(dataSet.IsSampleData);
            Assert.Equal(AvailabilityState.Available, dataSet.Availability);
            Assert.Equal("Deterministic sample provider", dataSet.SourceName);
            Assert.Equal(4, dataSet.Meetings.Count);
            Assert.Contains("Sample data only", dataSet.Message);
        }
    }

    [Fact]
    public async Task Startup_UsesLocalCliProviderWhenWorkIqCliIsEnabledWithoutAzureCredentials()
    {
        var (app, startupState) = BuildApp(new Dictionary<string, string?>
        {
            ["WorkIQ:Mode"] = "Cli",
            ["WorkIQ:Cli:Enabled"] = "true",
            ["WorkIQ:Cli:ExecutablePath"] = "workiq-cli",
            ["WorkIQ:Cli:TimeoutSeconds"] = "15"
        });
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Cli, startupState.LiveDataAccessMode);
            Assert.Contains("CLI session owns corporate sign-in and delegated token acquisition", startupState.LiveModeMessage);

            using var scope = app.Services.CreateScope();
            Assert.IsType<WorkIqCliMeetingDataClient>(scope.ServiceProvider.GetRequiredService<IWorkIqMeetingDataClient>());
            Assert.IsType<WorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
            Assert.IsType<SwitchableMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<IMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_UsesCliProviderWhenWorkIqModeIsCliWithoutLegacyEnabledFlag()
    {
        var (app, startupState) = BuildApp(new Dictionary<string, string?>
        {
            ["WorkIQ:Mode"] = "Cli",
            ["WorkIQ:Cli:Enabled"] = "false"
        });
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Cli, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            Assert.IsType<WorkIqCliMeetingDataClient>(scope.ServiceProvider.GetRequiredService<IWorkIqMeetingDataClient>());
            Assert.IsType<WorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
            Assert.IsType<SwitchableMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<IMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_PrefersCliProviderWhenLegacyProviderExplicitlySelectsCli()
    {
        var configuration = CreateCompleteWorkIqConfiguration();
        configuration["WorkIQ:Provider"] = "Cli";
        configuration["WorkIQ:Cli:Enabled"] = "true";
        configuration["WorkIQ:Cli:ExecutablePath"] = "workiq-cli";
        configuration["WorkIQ:Cli:TimeoutSeconds"] = "15";

        var (app, startupState) = BuildApp(configuration);
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Cli, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.True(options.Enabled);
            Assert.True(options.HasUsableConfiguration);
            Assert.True(options.CanAttemptLiveQuery);
            Assert.IsType<WorkIqCliMeetingDataClient>(scope.ServiceProvider.GetRequiredService<IWorkIqMeetingDataClient>());
            Assert.IsType<WorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_UsesLegacyCliProviderOverrideWhenCheckedInAppSettingsStaySample()
    {
        var (app, startupState) = BuildAppWithProductionConfiguration(new Dictionary<string, string?>
        {
            ["WorkIQ:Provider"] = "Cli"
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Cli, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            Assert.IsType<WorkIqCliMeetingDataClient>(scope.ServiceProvider.GetRequiredService<IWorkIqMeetingDataClient>());
            Assert.IsType<WorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_KeepsSampleModeWhenOperatorExplicitlyOverridesLegacyCliSelectionWithSample()
    {
        var (app, startupState) = BuildAppWithProductionConfiguration(new Dictionary<string, string?>
        {
            ["WorkIQ:Provider"] = "Cli",
            ["WorkIQ:Mode"] = "Sample"
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IMeetingDataProvider>();
            var dataSet = await provider.GetMeetingsAsync(CreateQuery());

            Assert.True(dataSet.IsSampleData);
            Assert.Equal("Deterministic sample provider", dataSet.SourceName);
        }
    }

    [Fact]
    public async Task Startup_FallsBackToCliAndNormalizesOptionsWhenLegacyDirectSelectionCannotUseEntra()
    {
        var (app, startupState) = BuildAppWithProductionConfiguration(new Dictionary<string, string?>
        {
            ["WorkIQ:Provider"] = "Direct",
            ["WorkIQ:Cli:Enabled"] = "true"
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Cli, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.Equal(WorkIqProviderMode.Cli, options.Mode);
            Assert.Equal(WorkIqOptions.CliProvider, options.Provider);
            Assert.IsType<WorkIqCliMeetingDataClient>(scope.ServiceProvider.GetRequiredService<IWorkIqMeetingDataClient>());
        }
    }

    [Fact]
    public async Task Startup_HigherPrecedenceAutoModeClearsLowerLegacyProviderPin()
    {
        var (app, startupState) = BuildApp(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkIQ:Provider"] = "Cli"
            });
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkIQ:Mode"] = "Auto"
            });
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.Equal(WorkIqProviderMode.Auto, options.Mode);
            Assert.Equal(WorkIqOptions.AutoProvider, options.Provider);
        }
    }

    [Fact]
    public async Task Startup_HigherPrecedenceAutoProviderClearsCheckedInSampleMode()
    {
        var (app, startupState) = BuildAppWithProductionConfiguration(new Dictionary<string, string?>
        {
            ["WorkIQ:Provider"] = "Auto"
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.Equal(WorkIqProviderMode.Auto, options.Mode);
            Assert.Equal(WorkIqOptions.AutoProvider, options.Provider);
        }
    }

    [Fact]
    public async Task Startup_HigherPrecedenceAutoProviderAllowsAutoSelectionToUseEntra()
    {
        var (app, startupState) = BuildAppWithProductionConfiguration(new Dictionary<string, string?>
        {
            ["WorkIQ:Provider"] = "Auto",
            ["WorkIQ:Enabled"] = "true",
            ["AzureAd:TenantId"] = "e90bd921-0e00-4e6f-b87c-713670ee27bf",
            ["AzureAd:ClientId"] = "8c079820-fb0a-48c5-bd80-19f73201a665",
            ["AzureAd:ClientSecret"] = "unit-test-client-credential"
        });

        await using (app)
        {
            Assert.True(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Entra, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.True(options.Enabled);
            Assert.Equal(WorkIqProviderMode.Entra, options.Mode);
            Assert.Equal(WorkIqOptions.AutoProvider, options.Provider);
            Assert.True(options.HasUsableConfiguration);
            Assert.True(options.CanAttemptLiveQuery);
        }
    }

    [Fact]
    public async Task Startup_UsesSampleProviderWhenWorkIqIsEnabledWithoutSingleTenantEntraConfiguration()
    {
        var (app, startupState) = BuildApp(new Dictionary<string, string?>
        {
            ["WorkIQ:Enabled"] = "true",
            ["WorkIQ:Provider"] = "Direct",
            ["WorkIQ:Scopes:0"] = "api://workiq-resource/WorkIQAgent.Ask",
            ["WorkIQ:TimeZone"] = "UTC"
        });
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.Equal(WorkIqProviderMode.Auto, options.Mode);
            Assert.Equal(WorkIqOptions.AutoProvider, options.Provider);
            Assert.False(options.Enabled);
            Assert.False(options.HasUsableConfiguration);
            Assert.False(options.CanAttemptLiveQuery);
            Assert.IsType<UnavailableWorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_NormalizesUnavailableExplicitCliSelectionBackToAuto()
    {
        var (app, startupState) = BuildApp(new Dictionary<string, string?>
        {
            ["WorkIQ:Mode"] = "Cli",
            ["WorkIQ:Cli:TimeoutSeconds"] = "0"
        });

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<WorkIqOptions>>().Value;

            Assert.Equal(WorkIqProviderMode.Auto, options.Mode);
            Assert.Equal(WorkIqOptions.AutoProvider, options.Provider);
            Assert.False(options.Enabled);
            Assert.False(options.HasUsableConfiguration);
            Assert.False(options.CanAttemptLiveQuery);
            Assert.IsType<UnavailableWorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_KeepsDashboardOnSampleProviderWhenClientCredentialIsUnavailable()
    {
        var configuration = CreateCompleteWorkIqConfiguration();
        configuration.Remove("AzureAd:ClientSecret");
        var (app, startupState) = BuildApp(configuration);
        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            Assert.IsType<UnavailableWorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_UsesDelegatedEntraProviderWhenLocalAppRegistrationIsConfigured()
    {
        var (app, startupState) = BuildApp(CreateCompleteWorkIqConfiguration());
        await using (app)
        {
            Assert.True(startupState.EntraSignInEnabled);
            Assert.True(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.Entra, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            Assert.IsType<WorkIqMeetingDataProvider>(scope.ServiceProvider.GetRequiredService<ILiveMeetingDataProvider>());
        }
    }

    [Fact]
    public async Task Startup_DevelopmentSettingsStaySampleOnlyByDefault()
    {
        var appSettingsPath = GetAppSettingsPath("appsettings.Development.json");
        var (app, startupState) = BuildApp(configuration => configuration.AddJsonFile(appSettingsPath, optional: false));

        await using (app)
        {
            Assert.False(startupState.EntraSignInEnabled);
            Assert.False(startupState.WorkIqIntegrationEnabled);
            Assert.False(startupState.DashboardRequiresAuthentication);
            Assert.Equal(LiveDataAccessMode.None, startupState.LiveDataAccessMode);

            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IMeetingDataProvider>();
            var dataSet = await provider.GetMeetingsAsync(CreateQuery());

            Assert.True(dataSet.IsSampleData);
            Assert.Equal("Deterministic sample provider", dataSet.SourceName);
        }
    }

    [Fact]
    public async Task WorkIqProvider_ReturnsUnavailableWhenTokenAcquisitionOrAuthorizationFails()
    {
        var provider = new WorkIqMeetingDataProvider(
            new StubWorkIqChatClient(WorkIqMeetingDataResult.AuthorizationFailed("token unavailable")),
            Options.Create(new WorkIqOptions
            {
                Enabled = true,
                Scopes = ["api://workiq-resource/WorkIQAgent.Ask"],
                TimeZone = "UTC"
            }),
            Options.Create(new WorkIqCliOptions()),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-05T18:00:00Z")),
            NullLogger<WorkIqMeetingDataProvider>.Instance);

        var dataSet = await provider.GetMeetingsAsync(CreateQuery());

        Assert.False(dataSet.IsSampleData);
        Assert.Equal("Work IQ", dataSet.SourceName);
        Assert.Equal(AvailabilityState.Unavailable, dataSet.Availability);
    }

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void StrictParsingRejectsMalformedWorkIqPayloads(string json)
    {
        var result = WorkIqMeetingResponseParser.ParseStrictJson(json);

        Assert.Equal(WorkIqMeetingDataResultStatus.Malformed, result.Status);
    }

    public static IEnumerable<object[]> MalformedPayloads()
    {
        yield return new object[]
        {
            """{"meetingId":"meeting-1","subject":"Weekly review","startsAtUtc":"2026-08-05T12:00:00Z"}"""
        };

        yield return new object[]
        {
            """{"meetingId":"meeting-1","subject":"Weekly review","startsAtUtc":"2026-08-05T12:00:00Z","durationMinutes":30,"unexpected":"value"}"""
        };

        yield return new object[]
        {
            """{"meetingId":"meeting-1","subject":"Weekly review","startsAtUtc":"2026-08-05T12:00:00Z","durationMinutes":"thirty"}"""
        };
    }

    private static (WebApplication App, DashboardStartupState StartupState) BuildApp(Dictionary<string, string?> configuration)
        => BuildApp(configurationBuilder => configurationBuilder.AddInMemoryCollection(configuration));

    private static (WebApplication App, DashboardStartupState StartupState) BuildAppWithProductionConfiguration(Dictionary<string, string?> overrides) =>
        BuildApp(configuration =>
        {
            configuration.AddJsonFile(GetAppSettingsPath("appsettings.json"), optional: false);
            configuration.AddJsonFile(GetAppSettingsPath("appsettings.Development.json"), optional: true);
            configuration.AddInMemoryCollection(overrides);
        });

    private static (WebApplication App, DashboardStartupState StartupState) BuildApp(Action<ConfigurationManager> configureConfiguration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Configuration.Sources.Clear();
        configureConfiguration(builder.Configuration);
        var startupState = builder.AddExecutiveDashboardServices();

        return (builder.Build(), startupState);
    }

    private static Dictionary<string, string?> CreateCompleteWorkIqConfiguration() =>
        new()
        {
            ["WorkIQ:Provider"] = "Direct",
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "e90bd921-0e00-4e6f-b87c-713670ee27bf",
            ["AzureAd:ClientId"] = "8c079820-fb0a-48c5-bd80-19f73201a665",
            ["AzureAd:ClientSecret"] = "unit-test-client-credential",
            ["AzureAd:CallbackPath"] = "/signin-oidc",
            ["WorkIQ:Enabled"] = "true",
            ["WorkIQ:Scopes:0"] = "api://workiq-resource/WorkIQAgent.Ask",
            ["WorkIQ:TimeZone"] = "UTC"
        };

    private static string GetAppSettingsPath(string fileName) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            $"../../../../../src/ExecutiveDashboard/{fileName}"));

    private static MeetingQuery CreateQuery() =>
        new(
            DateTimeOffset.Parse("2026-07-29T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            "sample-executive");

    private sealed class StubWorkIqChatClient(WorkIqMeetingDataResult result) : IWorkIqChatClient
    {
        public Task<WorkIqMeetingDataResult> GetMeetingDataAsync(MeetingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
