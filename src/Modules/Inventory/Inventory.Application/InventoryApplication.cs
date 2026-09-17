using System.Reflection;

namespace OrderPlatform.Inventory.Application;

/// <summary>Anchor for the Inventory application assembly, used for explicit Wolverine handler discovery (ADR-0004).</summary>
public static class InventoryApplication
{
    public static Assembly Assembly => typeof(InventoryApplication).Assembly;
}
