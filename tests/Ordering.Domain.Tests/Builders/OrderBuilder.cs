using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain.Tests.Builders;

/// <summary>
/// Builds an order in a given state by replaying the events that lead there, which is how the handlers load it
/// (tests/README conventions). Every state of ADR-0017 §2 has an event path, so the transition table covers them all.
/// </summary>
public sealed class OrderBuilder
{
    public static readonly Guid OrderId = new("0f6b0d5e-0000-4000-8000-000000000001");
    public static readonly Guid AccountId = new("0f6b0d5e-0000-4000-8000-0000000000a1");
    public const string InvoiceId = "INV-2026-0001";
    public const string ReservationKey = "reserve:0f6b0d5e-0000-4000-8000-000000000001:1";
    public const string TrackingReference = "TRK-2026-0001";
    public static readonly DateTimeOffset At = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Invoices are due three calendar days after they are issued (ADR-0017 §2).</summary>
    public static readonly DateTimeOffset DueAt = At.AddDays(3);

    public static readonly Money InvoiceAmount = Money.InEur(24.20m);

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
                events.Add(Reserved());
                break;
            case OrderStatus.AwaitingPayment:
                events.AddRange([Reserved(), Issued()]);
                break;
            case OrderStatus.Processing:
                events.AddRange(Paid());
                break;
            case OrderStatus.FulfilmentOnHold:
                events.AddRange([.. Paid(), Held()]);
                break;
            case OrderStatus.Refunding:
                events.AddRange([.. Paid(), Held(), new RefundRequested(OrderId, OrderCancellationReason.CustomerRequest, null, At)]);
                break;
            case OrderStatus.Shipped:
                events.AddRange([.. Paid(), Dispatched()]);
                break;
            case OrderStatus.Delivered:
                events.AddRange([.. Paid(), Dispatched(), new ShipmentDelivered(OrderId, At)]);
                break;
            case OrderStatus.RequiresAttention:
                events.AddRange(
                [
                    Reserved(),
                    Issued(),
                    PartiallyPaid(),
                    new AttentionRequired(OrderId, OrderAttentionReason.PartialPaymentAtDueDate, null, At),
                ]);
                break;
            case OrderStatus.Cancelled:
                events.Add(new OrderCancelled(OrderId, OrderCancellationReason.CustomerRequest, null, At));
                break;
            default:
                throw new NotSupportedException($"No event path reaches {status}.");
        }

        return this;
    }

    /// <summary>Records a part payment on an order that already has an invoice; the state does not change (ADR-0017 §4).</summary>
    public OrderBuilder WithPartialPayment()
    {
        events.Add(PartiallyPaid());
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
                case InvoiceIssued e: order.Apply(e); break;
                case InvoicePaid e: order.Apply(e); break;
                case InvoicePartiallyPaid e: order.Apply(e); break;
                case AttentionRequired e: order.Apply(e); break;
                case AttentionResolved e: order.Apply(e); break;
                case OrderCancelled e: order.Apply(e); break;
                case OrderItemsReduced e: order.Apply(e); break;
                case FulfilmentFailed e: order.Apply(e); break;
                case FulfilmentInformationUpdated e: order.Apply(e); break;
                case ShipmentDispatched e: order.Apply(e); break;
                case ShipmentDelivered e: order.Apply(e); break;
                case RefundRequested e: order.Apply(e); break;
                case RefundCompleted e: order.Apply(e); break;
                default: throw new NotSupportedException($"{@event.GetType().Name} has no Apply.");
            }
        }
    }

    private static InventoryReserved Reserved() => new(OrderId, ReservationKey, At);

    /// <summary>The whole way to Processing: reserved, invoiced and paid in full.</summary>
    private static object[] Paid() => [Reserved(), Issued(), new InvoicePaid(OrderId, InvoiceId, At)];

    private static FulfilmentFailed Held() => new(OrderId, "The delivery address is incomplete.", At);

    private static ShipmentDispatched Dispatched() => new(OrderId, TrackingReference, At);

    private static InvoiceIssued Issued() => new(OrderId, InvoiceId, InvoiceAmount, DueAt, At);

    private static InvoicePartiallyPaid PartiallyPaid() =>
        new(OrderId, InvoiceId, Money.InEur(10.00m), Money.InEur(14.20m), At);
}
