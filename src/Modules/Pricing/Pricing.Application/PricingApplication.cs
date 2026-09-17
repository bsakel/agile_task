using System.Reflection;

namespace OrderPlatform.Pricing.Application;

/// <summary>Anchor for the Pricing application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class PricingApplication
{
    public static Assembly Assembly => typeof(PricingApplication).Assembly;
}
