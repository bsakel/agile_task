using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Messaging;
using OrderPlatform.Ordering.Domain;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// Reads one order of the calling account (ADR-0017 §3). An order of another account is reported as not found, so its
/// existence is not leaked (ADR-0016).
/// </summary>
public sealed record GetOrder(Guid CallerAccountId, Guid OrderId) : ICommand;

/// <summary>The lifecycle of one order as the stream recorded it — the audit trail, not a side table (ADR-0006).</summary>
public sealed record GetOrderHistory(Guid CallerAccountId, Guid OrderId) : ICommand;

/// <summary>One ordered product with the price the order was locked at.</summary>
public sealed record OrderLineView(string Sku, int Quantity, Money UnitPrice);

/// <summary>What the order is priced at; the full calculation stays with Pricing (ADR-0003, ADR-0018).</summary>
public sealed record OrderPricingView(Money Net, Money Tax, Money Total, string PriceListVersion, bool ReverseCharge);

public sealed record OrderView(
    Guid OrderId,
    OrderStatus Status,
    IReadOnlyList<OrderLineView> Lines,
    OrderPricingView Pricing,
    string? InvoiceId,
    DateTimeOffset? PaymentDueAt,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One recorded step. <paramref name="Type"/> is the stored event alias, the name that lives forever in the stream
/// (ADR-0010 rule 4), so a client reads the contract rather than a C# class name.
/// </summary>
public sealed record OrderHistoryEntry(long Version, string Type, DateTimeOffset RecordedAt);

public sealed record OrderHistoryView(Guid OrderId, IReadOnlyList<OrderHistoryEntry> Entries);
