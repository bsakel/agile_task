namespace OrderPlatform.Inventory.Application.Integrations;

/// <summary>How much of a SKU one line needs.</summary>
public sealed record InventoryLine(string Sku, int Quantity);

/// <summary>An all-or-nothing reservation; <c>IdempotencyKey</c> is ours and identifies the reservation (ADR-0014).</summary>
public sealed record InventoryReservationRequest(string IdempotencyKey, IReadOnlyList<InventoryLine> Lines);

/// <summary>Releases the reservation made with <c>ReservationKey</c>; the release itself carries its own key.</summary>
public sealed record InventoryReleaseRequest(string IdempotencyKey, string ReservationKey);

/// <summary>What the provider did with a reservation request.</summary>
public sealed record InventoryReservation(string ReservationKey, InventoryReservationStatus Status, IReadOnlyList<string> UnavailableSkus)
{
    public static InventoryReservation Reserved(string reservationKey) => new(reservationKey, InventoryReservationStatus.Reserved, []);

    public static InventoryReservation Unavailable(string reservationKey, IReadOnlyList<string> unavailableSkus) =>
        new(reservationKey, InventoryReservationStatus.Unavailable, unavailableSkus);
}

public enum InventoryReservationStatus
{
    /// <summary>Every line is held for us until it is released or consumed.</summary>
    Reserved,

    /// <summary>Nothing was reserved because at least one line is not available (ADR-0017: the order waits for the customer).</summary>
    Unavailable,
}

/// <summary>The recorded outcome of an earlier call, found by its idempotency key.</summary>
public sealed record InventoryOutcome(string IdempotencyKey, InventoryOperation Operation, InventoryReservation? Reservation);

public enum InventoryOperation
{
    Reserve,
    Release,
}
