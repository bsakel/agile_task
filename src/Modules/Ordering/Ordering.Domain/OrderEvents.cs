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
public sealed record InventoryReserved(Guid OrderId, string ReservationKey, DateTimeOffset ReservedAt) : IDomainEvent;

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
}

/// <summary>Automation stopped; the order waits for a support agent (ADR-0017 §2).</summary>
public sealed record AttentionRequired(
    Guid OrderId,
    OrderAttentionReason Reason,
    string? Details,
    DateTimeOffset RaisedAt) : IDomainEvent;
