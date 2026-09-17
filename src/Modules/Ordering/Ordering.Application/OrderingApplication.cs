using System.Reflection;

namespace OrderPlatform.Ordering.Application;

/// <summary>Anchor for the Ordering application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class OrderingApplication
{
    public static Assembly Assembly => typeof(OrderingApplication).Assembly;
}
