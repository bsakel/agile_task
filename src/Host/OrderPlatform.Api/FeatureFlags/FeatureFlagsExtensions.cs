using Microsoft.FeatureManagement;
using OrderPlatform.BuildingBlocks.FeatureFlags;

namespace OrderPlatform.Api.FeatureFlags;

internal static class FeatureFlagsExtensions
{
    /// <summary>
    /// Registers <see cref="IFeatureFlags"/>. Flags are read from the <c>FeatureManagement</c> configuration section
    /// (appsettings, environment variables); JSON configuration files reload on change, so a flag can be switched
    /// without a restart (ADR-0019).
    /// </summary>
    public static WebApplicationBuilder AddPlatformFeatureFlags(this WebApplicationBuilder builder, IEnumerable<Type> registries)
    {
        var registry = FeatureFlagRegistry.FromTypes(registries);

        builder.Services.AddFeatureManagement();
        builder.Services.Configure<FeatureManagementOptions>(options => options.IgnoreMissingFeatures = true);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<IFeatureFlags, FeatureManagementFeatureFlags>();
        builder.Services.AddHostedService<FeatureFlagRegistryReport>();

        return builder;
    }
}

/// <summary>Logs the registered flags at startup and warns about expired ones (flag debt, ADR-0019 rule 2).</summary>
internal sealed partial class FeatureFlagRegistryReport(
    FeatureFlagRegistry registry,
    TimeProvider timeProvider,
    ILogger<FeatureFlagRegistryReport> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        foreach (var flag in registry.Flags)
        {
            if (flag.Expires < today)
            {
                LogExpired(flag.Name, flag.Owner, flag.Expires.Value);
            }
            else
            {
                LogRegistered(flag.Name, flag.Type, flag.Owner, flag.Expires);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature flag {Flag} registered ({Type}, owner {Owner}, expires {Expires})")]
    private partial void LogRegistered(string flag, FeatureFlagType type, string owner, DateOnly? expires);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Feature flag {Flag} expired on {Expires}; owner {Owner} must remove it")]
    private partial void LogExpired(string flag, string owner, DateOnly expires);
}
