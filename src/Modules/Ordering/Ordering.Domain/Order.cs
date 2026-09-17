namespace OrderPlatform.Ordering.Domain;

/// <summary>
/// The order and its lifecycle process. The event stream is the single source of truth for business and process state:
/// handlers load the stream, let the aggregate decide, append the events and send the follow-ups in one transaction
/// (ADR-0006, ADR-0017 §1). The aggregate decides transitions only — pricing, inventory and billing rules stay in their
/// own modules.
/// </summary>
/// <remarks>
/// This PR covers submission and the inventory outcomes. Invoicing, payment and cancellation follow in PR 2d-2, the
/// fulfilment states in PR 2e.
/// </remarks>
public sealed class Order
{
    /// <summary>Process version recorded on submission; in-flight orders finish on the version they started with (ADR-0017 §8).</summary>
    public const int CurrentProcessVersion = 1;

    public Guid Id { get; set; }

    public string AccountId { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public Guid? ReservationId { get; private set; }

    /// <summary>Submission decided outside the aggregate (account active, prices resolved); the process starts here.</summary>
    public static OrderDecision Submit(
        Guid orderId,
        string accountId,
        IReadOnlyList<OrderLine> lines,
        OrderPricing pricing,
        Guid billingAddressId,
        Guid shippingAddressId,
        Guid contactId,
        DateTimeOffset now) =>
        OrderDecision.From(
            new OrderSubmitted(orderId, accountId, CurrentProcessVersion, lines, pricing, billingAddressId, shippingAddressId, contactId, now),
            OrderFollowUp.ReserveInventory);

    /// <summary>Inventory reserved all lines. Arriving after a cancellation, the reservation is released instead (ADR-0017 §6).</summary>
    public OrderDecision ReservationSucceeded(Guid reservationId, DateTimeOffset now) => Status switch
    {
        OrderStatus.ValidatingInventory => OrderDecision.From(
            new InventoryReserved(Id, reservationId, now),
            OrderFollowUp.IssueInvoice),
        OrderStatus.Cancelled => OrderDecision.FollowUpOnly(OrderFollowUp.ReleaseInventory),
        _ => Ignore(nameof(ReservationSucceeded)),
    };

    /// <summary>Inventory could not reserve every line; the customer is asked to reduce the order (all-or-nothing).</summary>
    public OrderDecision ReservationFailed(IReadOnlyList<string> unavailableSkus, DateTimeOffset now) => Status switch
    {
        OrderStatus.ValidatingInventory => OrderDecision.From(new InventoryUnavailable(Id, unavailableSkus, now)),
        _ => Ignore(nameof(ReservationFailed)),
    };

    public void Apply(OrderSubmitted @event)
    {
        Id = @event.OrderId;
        AccountId = @event.AccountId;
        Status = OrderStatus.ValidatingInventory;
    }

    public void Apply(InventoryReserved @event)
    {
        ReservationId = @event.ReservationId;
        Status = OrderStatus.Invoicing;
    }

    public void Apply(InventoryUnavailable _) => Status = OrderStatus.AwaitingCustomer;

    public void Apply(OrderCancelled _) => Status = OrderStatus.Cancelled;

    private OrderDecision Ignore(string trigger) =>
        OrderDecision.Ignore($"{trigger} does not apply to an order in state {Status}.");
}
