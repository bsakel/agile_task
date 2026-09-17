using OrderPlatform.Billing.Infrastructure;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Customers.Infrastructure;
using OrderPlatform.Inventory.Infrastructure;
using OrderPlatform.Ordering.Infrastructure;
using OrderPlatform.Pricing.Infrastructure;
using OrderPlatform.Shipping.Infrastructure;

namespace OrderPlatform.Composition;

/// <summary>The explicit list of modules hosted by the platform (ADR-0002).</summary>
public static class PlatformModules
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new OrderingModule(),
        new CustomersModule(),
        new PricingModule(),
        new InventoryModule(),
        new BillingModule(),
        new ShippingModule(),
    ];

    /// <summary>The modules' <c>FeatureFlags</c> registry classes (ADR-0019).</summary>
    public static IEnumerable<Type> FeatureFlagRegistries => All.Select(module => module.FeatureFlags).OfType<Type>();
}
