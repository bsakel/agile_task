using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain;

/// <summary>
/// The order and its lifecycle process. The event stream is the single source of truth for business and process state:
/// handlers load the stream, let the aggregate decide, append the events and send the follow-ups in one transaction
/// (ADR-0006, ADR-0017 §1). The aggregate decides transitions only — pricing, inventory and billing rules stay in their
/// own modules.
/// </summary>
/// <remarks>
/// This PR covers submission, the inventory outcomes, invoicing, payment and customer cancellation. The fulfilment
/// states follow in PR 2e, which also adds support cancellation and customer cancellation from FulfilmentOnHold.
/// </remarks>
public sealed class Order
{
    /// <summary>Process version recorded on submission; in-flight orders finish on the version they started with (ADR-0017 §8).</summary>
    public const int CurrentProcessVersion = 1;

    /// <summary>The states a customer may still cancel from in this PR; FulfilmentOnHold joins them in PR 2e (ADR-0017 §3).</summary>
    private static readonly OrderStatus[] CustomerCancellable =
    [
        OrderStatus.ValidatingInventory,
        OrderStatus.AwaitingCustomer,
        OrderStatus.Invoicing,
        OrderStatus.AwaitingPayment,
    ];

    public Guid Id { get; set; }

    public string AccountId { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public Guid? ReservationId { get; private set; }

    public string? InvoiceId { get; private set; }

    public DateTimeOffset? PaymentDueAt { get; private set; }

    /// <summary>Part of the invoice was paid. Only the due date acts on it, so it is state, not a transition (ADR-0017 §4).</summary>
    public bool IsPartiallyPaid { get; private set; }

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

    /// <summary>Billing issued the invoice; payment is due on the date billing set (ADR-0017 §2).</summary>
    public OrderDecision InvoicingSucceeded(string invoiceId, Money amount, DateTimeOffset dueAt, DateTimeOffset now) => Status switch
    {
        OrderStatus.Invoicing => OrderDecision.From(new InvoiceIssued(Id, invoiceId, amount, dueAt, now)),
        _ => Ignore(nameof(InvoicingSucceeded)),
    };

    /// <summary>The invoice was paid in full; fulfilment takes over.</summary>
    public OrderDecision PaymentReceived(DateTimeOffset now) => Status switch
    {
        OrderStatus.AwaitingPayment => OrderDecision.From(new InvoicePaid(Id, InvoiceId!, now)),
        _ => Ignore(nameof(PaymentReceived)),
    };

    /// <summary>Part of the invoice was paid; the order waits for the rest until the due date (ADR-0017 §4).</summary>
    public OrderDecision PartialPaymentReceived(Money amountPaid, Money amountOutstanding, DateTimeOffset now) => Status switch
    {
        OrderStatus.AwaitingPayment => OrderDecision.From(new InvoicePartiallyPaid(Id, InvoiceId!, amountPaid, amountOutstanding, now)),
        _ => Ignore(nameof(PartialPaymentReceived)),
    };

    /// <summary>
    /// The InvoiceOverdue timer fired (ADR-0017 §4). An unpaid invoice cancels the order with its compensation; a
    /// part-paid one stops automation, because only support can decide between a refund and chasing the remainder.
    /// The timer is never cancelled, so it is ignored once the order has moved on (ADR-0017 §1).
    /// </summary>
    public OrderDecision PaymentOverdue(DateTimeOffset now) => Status switch
    {
        OrderStatus.AwaitingPayment when IsPartiallyPaid => OrderDecision.From(
            new AttentionRequired(
                Id,
                OrderAttentionReason.PartialPaymentAtDueDate,
                $"Invoice {InvoiceId} was only partly paid on its due date.",
                now)),
        OrderStatus.AwaitingPayment => Cancel(OrderCancellationReason.InvoiceOverdue, details: null, now),
        _ => Ignore(nameof(PaymentOverdue)),
    };

    /// <summary>
    /// The customer asked to cancel. Allowed up to and including AwaitingPayment; from Processing on, only support can
    /// cancel, which the endpoint reports as a conflict naming the current state (ADR-0017 §3).
    /// </summary>
    public Result<OrderDecision> CancelByCustomer(DateTimeOffset now) =>
        CustomerCancellable.Contains(Status)
            ? Cancel(OrderCancellationReason.CustomerRequest, details: null, now)
            : OrderErrors.NotCancellable(Status);

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

    public void Apply(InvoiceIssued @event)
    {
        InvoiceId = @event.InvoiceId;
        PaymentDueAt = @event.DueAt;
        Status = OrderStatus.AwaitingPayment;
    }

    public void Apply(InvoicePaid _) => Status = OrderStatus.Processing;

    /// <summary>A partial payment records the shortfall; the state only changes at the due date (ADR-0017 §4).</summary>
    public void Apply(InvoicePartiallyPaid _) => IsPartiallyPaid = true;

    public void Apply(AttentionRequired _) => Status = OrderStatus.RequiresAttention;

    public void Apply(OrderCancelled _) => Status = OrderStatus.Cancelled;

    private OrderDecision Cancel(OrderCancellationReason reason, string? details, DateTimeOffset now) =>
        OrderDecision.From(new OrderCancelled(Id, reason, details, now), CompensationFor(Status));

    /// <summary>
    /// What leaving this state through cancellation has to undo (ADR-0017 §5). ValidatingInventory has nothing
    /// confirmed yet — a reservation that lands after the cancel is released by <see cref="ReservationSucceeded"/> —
    /// and AwaitingCustomer never reserved, because reservation is all-or-nothing.
    /// </summary>
    private static OrderFollowUp[] CompensationFor(OrderStatus status) => status switch
    {
        OrderStatus.Invoicing or OrderStatus.AwaitingPayment => [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
        _ => [],
    };

    private OrderDecision Ignore(string trigger) =>
        OrderDecision.Ignore($"{trigger} does not apply to an order in state {Status}.");
}
