using System.Reflection;
using OrderPlatform.BuildingBlocks.Messaging;
using OrderPlatform.Ordering.Application;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// An inline snapshot ignores an event it has no <c>Apply</c> for, so a new lifecycle event would silently stop the read
/// model following the order — visible only as a stale status in production. The same failure mode as an unregistered
/// document (spike S4), so it is caught the same way: at build time (ADR-0006, ADR-0015).
/// </summary>
public sealed class ProjectionCoverageTests
{
    [Fact]
    public void OrderDetails_applies_every_stored_order_event()
    {
        var applied = typeof(OrderDetails)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Apply")
            .Select(method => method.GetParameters().Single().ParameterType)
            .ToHashSet();

        var missing = PlatformArchitecture.PlatformTypes
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IDomainEvent).IsAssignableFrom(type)
                && type.Namespace == typeof(OrderPlatform.Ordering.Domain.Order).Namespace)
            .Where(type => !applied.Contains(type))
            .Select(type => type.Name)
            .ToList();

        missing.ShouldBeEmpty($"add an Apply for each of these to {nameof(OrderDetails)}, or the read model stops following the order");
    }
}
