using System.Diagnostics;
using Microsoft.FeatureManagement;
using OrderPlatform.BuildingBlocks.FeatureFlags;

namespace OrderPlatform.Api.FeatureFlags;

/// <summary>
/// v1 implementation of <see cref="IFeatureFlags"/> on Microsoft.FeatureManagement (ADR-0019). Moving to OpenFeature
/// replaces this class only.
/// </summary>
internal sealed class FeatureManagementFeatureFlags(IVariantFeatureManager featureManager, FeatureFlagRegistry registry) : IFeatureFlags
{
    private const string ProviderName = "Microsoft.FeatureManagement";

    public async Task<bool> IsEnabledAsync(string flag, CancellationToken ct = default)
    {
        if (!registry.Contains(flag))
        {
            throw new InvalidOperationException(
                $"Feature flag '{flag}' is not registered. Declare it as a constant with [FeatureFlag] in the module's FeatureFlags class (ADR-0019).");
        }

        // A flag without configuration evaluates to off (FeatureManagementOptions.IgnoreMissingFeatures).
        var enabled = await featureManager.IsEnabledAsync(flag, ct);

        // OpenTelemetry semantic conventions for feature flags: one event per evaluation on the current span.
        Activity.Current?.AddEvent(new ActivityEvent("feature_flag.evaluation", tags: new ActivityTagsCollection
        {
            ["feature_flag.key"] = flag,
            ["feature_flag.provider.name"] = ProviderName,
            ["feature_flag.result.value"] = enabled,
        }));

        return enabled;
    }
}
