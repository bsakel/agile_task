using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain;

/// <summary>
/// The order and its lifecycle process. The event stream is the single source of truth for business and process state:
/// handlers load the stream, let the aggregate decide, append the events and send the follow-ups in one transaction
/// (ADR-0006, ADR-0017 §1). The aggregate decides transitions only — pricing, inventory and billing rules stay in their
/// own modules.
/// </summary>
/// <remarks>
/// With PR 2e the aggregate covers the whole lifecycle: submission, the inventory outcomes, invoicing, payment,
/// fulfilment, shipping, refunding and the support actions on an order automation has stopped on.
/// </remarks>
public sealed class Order
{
    /// <summary>Process version recorded on submission; in-flight orders finish on the version they started with (ADR-0017 §8).</summary>
    public const int CurrentProcessVersion = 1;

    /// <summary>The states a customer may cancel from; from Processing on only support can (ADR-0017 §3).</summary>
    private static readonly OrderStatus[] CustomerCancellable =
    [
        OrderStatus.ValidatingInventory,
        OrderStatus.AwaitingCustomer,
        OrderStatus.Invoicing,
        OrderStatus.AwaitingPayment,
        OrderStatus.FulfilmentOnHold,
    ];

    /// <summary>Support cancels from everywhere the customer can, and from Processing as well (ADR-0017 §3, back office).</summary>
    private static readonly OrderStatus[] SupportCancellable = [.. CustomerCancellable, OrderStatus.Processing];

    public Guid Id { get; set; }

    public Guid AccountId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Guid? ReservationId { get; private set; }

    public string? InvoiceId { get; private set; }

    public DateTimeOffset? PaymentDueAt { get; private set; }

    /// <summary>Part of the invoice was paid. Only the due date acts on it, so it is state, not a transition (ADR-0017 §4).</summary>
    public bool IsPartiallyPaid { get; private set; }

    /// <summary>The carrier's reference, set once the shipment is dispatched.</summary>
    public string? TrackingReference { get; private set; }

    /// <summary>Submission decided outside the aggregate (account active, prices resolved); the process starts here.</summary>
    public static OrderDecision Submit(
        Guid orderId,
        Guid accountId,
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

    /// <summary>
    /// The invoice was paid in full; fulfilment takes over. Payment that lands on an order already cancelled — by the
    /// customer or by the overdue timer — is money we hold without an order, so automation stops and support decides
    /// between a refund and reinstatement (ADR-0017 §6).
    /// </summary>
    public OrderDecision PaymentReceived(DateTimeOffset now) => Status switch
    {
        OrderStatus.AwaitingPayment => OrderDecision.From(new InvoicePaid(Id, InvoiceId!, now), OrderFollowUp.RequestShipment),
        OrderStatus.Cancelled => OrderDecision.From(
            new AttentionRequired(
                Id,
                OrderAttentionReason.LatePaymentAfterCancellation,
                $"Invoice {InvoiceId} was paid after the order was cancelled.",
                now)),
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
    /// Fulfilment could not complete the order and asks the customer to correct the information. Reported after the
    /// carrier already took the shipment it is ignored and logged: carrier issues are out of scope (ADR-0017 §6).
    /// </summary>
    public OrderDecision FulfilmentAttemptFailed(string reason, DateTimeOffset now) => Status switch
    {
        OrderStatus.Processing => OrderDecision.From(new FulfilmentFailed(Id, reason, now)),
        _ => Ignore(nameof(FulfilmentAttemptFailed)),
    };

    /// <summary>The carrier took the shipment; only delivery follows.</summary>
    public OrderDecision DispatchReported(string trackingReference, DateTimeOffset now) => Status switch
    {
        OrderStatus.Processing => OrderDecision.From(new ShipmentDispatched(Id, trackingReference, now)),
        _ => Ignore(nameof(DispatchReported)),
    };

    /// <summary>The carrier delivered the shipment; the order is complete.</summary>
    public OrderDecision DeliveryReported(DateTimeOffset now) => Status switch
    {
        OrderStatus.Shipped => OrderDecision.From(new ShipmentDelivered(Id, now)),
        _ => Ignore(nameof(DeliveryReported)),
    };

    /// <summary>Billing confirmed the refund, which is the last compensation step of a late cancellation (ADR-0017 §5).</summary>
    public OrderDecision RefundConfirmed(string refundReference, DateTimeOffset now) => Status switch
    {
        OrderStatus.Refunding => OrderDecision.From(new RefundCompleted(Id, refundReference, now)),
        _ => Ignore(nameof(RefundConfirmed)),
    };

    /// <summary>
    /// A compensation step gave up after its retries. Automation never retries forever, so the order stops for a
    /// support agent instead of silently staying half-compensated (ADR-0017 §5). Only the states that are still
    /// waiting for compensation can act on it; anywhere else it is a stale report.
    /// </summary>
    public OrderDecision CompensationFailed(OrderFollowUp step, DateTimeOffset now) => Status switch
    {
        OrderStatus.Refunding or OrderStatus.Cancelled => OrderDecision.From(
            new AttentionRequired(
                Id,
                OrderAttentionReason.CompensationFailed,
                $"Compensation step {step} could not be completed.",
                now)),
        _ => Ignore(nameof(CompensationFailed)),
    };

    /// <summary>
    /// The CustomerResponseTimeout timer fired (ADR-0017 §4). Waiting for the customer ends in the same cancellation,
    /// and the same compensation, as if the customer had asked for it. Like every timer it is ignored once the order
    /// has moved on (ADR-0017 §1).
    /// </summary>
    public OrderDecision CustomerResponseTimedOut(DateTimeOffset now) => Status switch
    {
        OrderStatus.AwaitingCustomer or OrderStatus.FulfilmentOnHold =>
            Cancel(OrderCancellationReason.CustomerResponseTimeout, details: null, now),
        _ => Ignore(nameof(CustomerResponseTimedOut)),
    };

    /// <summary>
    /// The customer asked to cancel. Allowed up to and including FulfilmentOnHold; from Processing on, only support can
    /// cancel, which the endpoint reports as a conflict naming the current state (ADR-0017 §3).
    /// </summary>
    public Result<OrderDecision> CancelByCustomer(DateTimeOffset now) =>
        CustomerCancellable.Contains(Status)
            ? Cancel(OrderCancellationReason.CustomerRequest, details: null, now)
            : OrderErrors.NotCancellable(Status);

    /// <summary>
    /// A support agent cancelled the order. Support reaches one state further than the customer — Processing, where a
    /// shipment has been requested but not dispatched — and must say why, because it overrides the normal rule
    /// (ADR-0017 §3, back-office table).
    /// </summary>
    public Result<OrderDecision> CancelBySupport(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OrderErrors.SupportReasonRequired();
        }

        return SupportCancellable.Contains(Status)
            ? Cancel(OrderCancellationReason.SupportRequest, reason, now)
            : OrderErrors.NotCancellableBySupport(Status);
    }

    /// <summary>
    /// The customer removed what inventory could not supply. The handler reprices the reduced order before calling
    /// this, so the aggregate only records the new lines and asks inventory to try again (ADR-0017 §3, ADR-0018).
    /// </summary>
    public Result<OrderDecision> ReduceItems(IReadOnlyList<OrderLine> lines, OrderPricing pricing, DateTimeOffset now) =>
        Status is OrderStatus.AwaitingCustomer
            ? OrderDecision.From(new OrderItemsReduced(Id, lines, pricing, now), OrderFollowUp.ReserveInventory)
            : OrderErrors.ItemsNotReducible(Status);

    /// <summary>
    /// The customer corrected the address or contact that fulfilment rejected. Only the new ids reach the stream; the
    /// data itself stays in Customers (ADR-0016). Fulfilment is then asked again.
    /// </summary>
    public Result<OrderDecision> UpdateFulfilmentInformation(Guid shippingAddressId, Guid contactId, DateTimeOffset now) =>
        Status is OrderStatus.FulfilmentOnHold
            ? OrderDecision.From(
                new FulfilmentInformationUpdated(Id, shippingAddressId, contactId, now),
                OrderFollowUp.RequestShipment)
            : OrderErrors.FulfilmentInformationNotUpdatable(Status);

    /// <summary>
    /// Ask billing whether the invoice has been paid. The decision is only whether asking still makes sense; the answer
    /// comes back as a payment event. The daily timer sends the same command and its handler drops this failure instead
    /// of surfacing it, because a timer that fires late is not a caller error (ADR-0017 §4).
    /// </summary>
    public Result<OrderDecision> CheckInvoiceStatus() =>
        Status is OrderStatus.AwaitingPayment
            ? OrderDecision.FollowUpOnly(OrderFollowUp.CheckInvoiceStatus)
            : OrderErrors.InvoiceStatusNotCheckable(Status);

    /// <summary>
    /// A support agent dealt with an order automation had stopped on and closes it, stating why (ADR-0017 §3). Every
    /// reason for attention — a part payment at the due date, a payment after cancellation, a failed compensation —
    /// leaves an order that is not going to be fulfilled, and ADR-0017 §2 gives RequiresAttention no way back into the
    /// automated flow, so resolving it ends in Cancelled. Reinstatement would need its own transition (ADR-0017 §6).
    /// </summary>
    public Result<OrderDecision> ResolveAttention(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OrderErrors.SupportReasonRequired();
        }

        return Status is OrderStatus.RequiresAttention
            ? OrderDecision.From(new AttentionResolved(Id, reason, now))
            : OrderErrors.NotRequiringAttention(Status);
    }

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

    /// <summary>A reduced order starts the inventory check again with its new lines and price.</summary>
    public void Apply(OrderItemsReduced _) => Status = OrderStatus.ValidatingInventory;

    public void Apply(FulfilmentFailed _) => Status = OrderStatus.FulfilmentOnHold;

    public void Apply(FulfilmentInformationUpdated _) => Status = OrderStatus.Processing;

    public void Apply(ShipmentDispatched @event)
    {
        TrackingReference = @event.TrackingReference;
        Status = OrderStatus.Shipped;
    }

    public void Apply(ShipmentDelivered _) => Status = OrderStatus.Delivered;

    /// <summary>The order is being unwound but not cancelled yet; it waits for billing to confirm the refund.</summary>
    public void Apply(RefundRequested _) => Status = OrderStatus.Refunding;

    public void Apply(RefundCompleted _) => Status = OrderStatus.Cancelled;

    /// <summary>Support closed the order by hand; it leaves the lifecycle the same way an automated cancellation does.</summary>
    public void Apply(AttentionResolved _) => Status = OrderStatus.Cancelled;

    /// <summary>
    /// Cancelling from a state that has nothing to refund reaches Cancelled in one step. Once fulfilment has started
    /// the money has to come back first, so the order goes to Refunding and <see cref="RefundConfirmed"/> closes it;
    /// the reason is recorded on <see cref="RefundRequested"/> for that path (ADR-0017 §5).
    /// </summary>
    private OrderDecision Cancel(OrderCancellationReason reason, string? details, DateTimeOffset now) =>
        RequiresRefund(Status)
            ? OrderDecision.From(new RefundRequested(Id, reason, details, now), CompensationFor(Status))
            : OrderDecision.From(new OrderCancelled(Id, reason, details, now), CompensationFor(Status));

    private static bool RequiresRefund(OrderStatus status) =>
        status is OrderStatus.Processing or OrderStatus.FulfilmentOnHold;

    /// <summary>
    /// What leaving this state through cancellation has to undo (ADR-0017 §5). ValidatingInventory has nothing
    /// confirmed yet — a reservation that lands after the cancel is released by <see cref="ReservationSucceeded"/> —
    /// and AwaitingCustomer never reserved, because reservation is all-or-nothing.
    /// </summary>
    private static OrderFollowUp[] CompensationFor(OrderStatus status) => status switch
    {
        OrderStatus.Invoicing or OrderStatus.AwaitingPayment => [OrderFollowUp.VoidInvoice, OrderFollowUp.ReleaseInventory],
        OrderStatus.Processing or OrderStatus.FulfilmentOnHold =>
            [OrderFollowUp.CancelShipmentRequest, OrderFollowUp.RefundInvoice, OrderFollowUp.ReleaseInventory],
        _ => [],
    };

    private OrderDecision Ignore(string trigger) =>
        OrderDecision.Ignore($"{trigger} does not apply to an order in state {Status}.");
}
