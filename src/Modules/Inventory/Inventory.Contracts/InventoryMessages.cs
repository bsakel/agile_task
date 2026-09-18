using OrderPlatform.BuildingBlocks.Messaging;

namespace OrderPlatform.Inventory.Contracts;

/// <summary>One line to reserve: a SKU of the external inventory system and how many of it.</summary>
public sealed record ReservationLine(string Sku, int Quantity);

/// <summary>
/// Reserve every line of an order, or nothing (README §2). Sent by the Ordering process through the outbox, so the
/// external call happens in a message handler and never in the HTTP request (ADR-0014, ADR-0017 §3).
/// </summary>
/// <param name="Attempt">
/// Which reservation this is for the order. It is part of the idempotency key, so reserving again after the customer
/// reduced the order (next step N7) is a new call rather than a replay of the first one (ADR-0017 §7).
/// </param>
public sealed record ReserveInventory(Guid OrderId, IReadOnlyList<ReservationLine> Lines, int Attempt = 1) : ICommand;

/// <summary>
/// Every line is held. <paramref name="ReservationKey"/> is the handle the reservation is released with; it is the
/// idempotency key the reservation was made under (ADR-0014, ADR-0017 §7).
/// </summary>
public sealed record InventoryReserved(Guid OrderId, string ReservationKey, DateTimeOffset ReservedAt) : IIntegrationEvent;

/// <summary>Nothing was reserved, because at least one line is not available (all-or-nothing).</summary>
public sealed record InventoryUnavailable(
    Guid OrderId,
    IReadOnlyList<string> UnavailableSkus,
    DateTimeOffset ReportedAt) : IIntegrationEvent;
