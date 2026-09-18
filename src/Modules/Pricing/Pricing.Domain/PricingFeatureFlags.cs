using OrderPlatform.BuildingBlocks.FeatureFlags;

namespace OrderPlatform.Pricing.Domain;

/// <summary>The Pricing module's flag registry: every flag the module evaluates is a constant here (ADR-0019 rule 1).</summary>
public static class PricingFeatureFlags
{
    /// <summary>
    /// When on, <see cref="Rules.ShippingChargeRule"/> adds the shipping charge. Off (the default) prices orders without
    /// it — the reference example of a release flag whose decision is recorded on the order.
    /// </summary>
    [FeatureFlag(FeatureFlagType.Release, Owner = "team-pricing", Expires = "2026-12-31")]
    public const string ShippingCharge = "Pricing.ShippingCharge";
}
