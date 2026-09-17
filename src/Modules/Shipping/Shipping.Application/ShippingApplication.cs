using System.Reflection;

namespace OrderPlatform.Shipping.Application;

/// <summary>Anchor for the Shipping application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class ShippingApplication
{
    public static Assembly Assembly => typeof(ShippingApplication).Assembly;
}
