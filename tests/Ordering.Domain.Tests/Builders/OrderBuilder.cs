using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain.Tests.Builders;

/// <summary>
/// Builds an order in a given state by replaying the events that lead there, which is how the handlers load it
/// (tests/README conventions). States that no PR can reach yet (fulfilment and refunding, PR 2e) are rejected.
/// </summary>
public sealed class OrderBuilder
{
    public static readonly Guid OrderId = new("0f6b0d5e-0000-4000-8000-000000000001");
    public const string AccountId = "acme";
    public static readonly DateTimeOffset At = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly List<object> events = [Submitted()];

    public static OrderSubmitted Submitted() => new(
        OrderId,
        AccountId,
        Order.CurrentProcessVersion,
        [new OrderLine("SKU-1", 2, Money.InEur(10.00m))],
        new OrderPricing(Money.InEur(20.00m), Money.InEur(4.20m), Money.InEur(24.20m), "base-2026-09", ReverseCharge: false),
        BillingAddressId: new Guid("0f6b0d5e-0000-4000-8000-0000000000b1"),
        ShippingAddressId: new Guid("0f6b0d5e-0000-4000-8000-0000000000b2"),
        ContactId: new Guid("0f6b0d5e-0000-4000-8000-0000000000c1"),
        At);

    public OrderBuilder InState(OrderStatus status)
    {
        switch (status)
        {
            case OrderStatus.ValidatingInventory:
                break;
            case OrderStatus.AwaitingCustomer:
                events.Add(new InventoryUnavailable(OrderId, ["SKU-1"], At));
                break;
            case OrderStatus.Invoicing:
                events.Add(new InventoryReserved(OrderId, Guid.NewGuid(), At));
                break;
            case OrderStatus.Cancelled:
                events.Add(new OrderCancelled(OrderId, OrderCancellationReason.CustomerRequest, null, At));
                break;
            default:
                throw new NotSupportedException($"No event path reaches {status} yet; it arrives with PR 2d-2 or 2e.");
        }

        return this;
    }

    public Order Build()
    {
        var order = new Order();
        Apply(order, events);
        return order;
    }

    /// <summary>Applies decided events the way Marten replays a stream, so tests assert the state the handler would see.</summary>
    public static void Apply(Order order, IEnumerable<object> stream)
    {
        foreach (var @event in stream)
        {
            switch (@event)
            {
                case OrderSubmitted e: order.Apply(e); break;
                case InventoryReserved e: order.Apply(e); break;
                case InventoryUnavailable e: order.Apply(e); break;
                case OrderCancelled e: order.Apply(e); break;
                default: throw new NotSupportedException($"{@event.GetType().Name} has no Apply in this PR.");
            }
        }
    }
}
