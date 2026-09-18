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
    string AccountId,
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
