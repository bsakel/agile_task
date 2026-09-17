using System.Reflection;

namespace OrderPlatform.Customers.Application;

/// <summary>Anchor for the Customers application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class CustomersApplication
{
    public static Assembly Assembly => typeof(CustomersApplication).Assembly;
}
