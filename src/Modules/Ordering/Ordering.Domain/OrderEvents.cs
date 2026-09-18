using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Messaging;

namespace OrderPlatform.Ordering.Domain;

/// <summary>One ordered product with the price that was locked at submission.</summary>
public sealed record OrderLine(string Sku, int Quantity, Money UnitPrice);

/// <summary>
/// The price the order was locked at, as the Ordering module stores it. The Pricing module owns the calculation and its
/// full breakdown (ADR-0018); the handler maps it onto this snapshot, so the Ordering domain stays independent of
/// Pricing's types (ADR-0003).
/// </summary>
public sealed record OrderPricing(Money Net, Money Tax, Money Total, string PriceListVersion, bool ReverseCharge);

/// <summary>Why an order was cancelled; recorded on the event so support and reporting never have to guess.</summary>
public enum OrderCancellationReason
{
    CustomerRequest = 1,
    InvoiceOverdue = 2,
    CustomerResponseTimeout = 3,
    SupportRequest = 4,
}

/// <summary>The order was accepted; the process starts by reserving inventory (ADR-0017 §3).</summary>
public sealed record OrderSubmitted(
    Guid OrderId,
    Guid AccountId,
    int ProcessVersion,
    IReadOnlyList<OrderLine> Lines,
    OrderPricing Pricing,
    Guid BillingAddressId,
    Guid ShippingAddressId,
    Guid ContactId,
    DateTimeOffset SubmittedAt) : IDomainEvent;

/// <summary>All lines were reserved in the external inventory system.</summary>
public sealed record InventoryReserved(Guid OrderId, Guid ReservationId, DateTimeOffset ReservedAt) : IDomainEvent;

/// <summary>The reservation failed; the customer is asked to reduce the order (all-or-nothing, README §2).</summary>
public sealed record InventoryUnavailable(Guid OrderId, IReadOnlyList<string> UnavailableSkus, DateTimeOffset ReportedAt) : IDomainEvent;

/// <summary>The order was cancelled; the compensation steps are the handler's follow-ups (ADR-0017 §5).</summary>
public sealed record OrderCancelled(
    Guid OrderId,
    OrderCancellationReason Reason,
    string? Details,
    DateTimeOffset CancelledAt) : IDomainEvent;

/// <summary>The external billing system issued the invoice; payment is due on <paramref name="DueAt"/> (ADR-0017 §2).</summary>
public sealed record InvoiceIssued(
    Guid OrderId,
    string InvoiceId,
    Money Amount,
    DateTimeOffset DueAt,
    DateTimeOffset IssuedAt) : IDomainEvent;

/// <summary>The invoice was paid in full.</summary>
public sealed record InvoicePaid(Guid OrderId, string InvoiceId, DateTimeOffset PaidAt) : IDomainEvent;

/// <summary>
/// Part of the invoice was paid. The order stays in <see cref="OrderStatus.AwaitingPayment"/>: only the due date decides
/// what happens to a part-paid invoice, and support handles it from there (ADR-0017 §4).
/// </summary>
public sealed record InvoicePartiallyPaid(
    Guid OrderId,
    string InvoiceId,
    Money AmountPaid,
    Money AmountOutstanding,
    DateTimeOffset ReportedAt) : IDomainEvent;

/// <summary>Why automation stopped and a support agent has to take over (ADR-0017 §2).</summary>
public enum OrderAttentionReason
{
    PartialPaymentAtDueDate = 1,

    /// <summary>Money arrived for an order that was already cancelled; support decides refund or reinstatement (ADR-0017 §6).</summary>
    LatePaymentAfterCancellation = 2,

    /// <summary>A compensation step exhausted its retries; automation never retries forever (ADR-0017 §5).</summary>
    CompensationFailed = 3,
}

/// <summary>Automation stopped; the order waits for a support agent (ADR-0017 §2).</summary>
public sealed record AttentionRequired(
    Guid OrderId,
    OrderAttentionReason Reason,
    string? Details,
    DateTimeOffset RaisedAt) : IDomainEvent;

/// <summary>The customer reduced the order after an inventory problem; the reduced order is repriced (ADR-0017 §3).</summary>
public sealed record OrderItemsReduced(
    Guid OrderId,
    IReadOnlyList<OrderLine> Lines,
    OrderPricing Pricing,
    DateTimeOffset ReducedAt) : IDomainEvent;

/// <summary>The fulfilment system could not complete the order; the customer must correct the information (ADR-0017 §2).</summary>
public sealed record FulfilmentFailed(Guid OrderId, string Reason, DateTimeOffset ReportedAt) : IDomainEvent;

/// <summary>
/// The customer corrected the fulfilment information. The new address and contact are stored in Customers; the order
/// only references their ids, so no personal data reaches the stream (ADR-0016).
/// </summary>
public sealed record FulfilmentInformationUpdated(
    Guid OrderId,
    Guid ShippingAddressId,
    Guid ContactId,
    DateTimeOffset UpdatedAt) : IDomainEvent;

/// <summary>The carrier took the shipment; from here only delivery follows (ADR-0017 §2).</summary>
public sealed record ShipmentDispatched(Guid OrderId, string TrackingReference, DateTimeOffset DispatchedAt) : IDomainEvent;

/// <summary>The carrier delivered the shipment; the order is complete.</summary>
public sealed record ShipmentDelivered(Guid OrderId, DateTimeOffset DeliveredAt) : IDomainEvent;

/// <summary>
/// Cancelling once fulfilment has started cannot finish in one step: the shipment request is cancelled, the invoice
/// refunded and the inventory released, and the order waits in <see cref="OrderStatus.Refunding"/> until billing
/// confirms. The reason for the cancellation is recorded here, not on a later event (ADR-0017 §5).
/// </summary>
public sealed record RefundRequested(
    Guid OrderId,
    OrderCancellationReason Reason,
    string? Details,
    DateTimeOffset RequestedAt) : IDomainEvent;

/// <summary>Billing confirmed the refund; the cancellation that <see cref="RefundRequested"/> started is complete.</summary>
public sealed record RefundCompleted(Guid OrderId, string RefundReference, DateTimeOffset CompletedAt) : IDomainEvent;

/// <summary>A support agent closed an order that automation had stopped on; the reason is mandatory (ADR-0017 §3).</summary>
public sealed record AttentionResolved(Guid OrderId, string Reason, DateTimeOffset ResolvedAt) : IDomainEvent;
