using System.Reflection;

namespace OrderPlatform.Billing.Application;

/// <summary>Anchor for the Billing application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class BillingApplication
{
    public static Assembly Assembly => typeof(BillingApplication).Assembly;
}
