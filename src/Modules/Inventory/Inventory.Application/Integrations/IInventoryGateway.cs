using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Inventory.Application.Integrations;

/// <summary>
/// Port to the external inventory system, in our language (ADR-0014). Expected failures — a provider outage, a key the
/// provider does not know — are returned as <see cref="Result"/> failures, never thrown. Adapters live in
/// <c>Inventory.Infrastructure</c> and are selected by <c>Integrations:Inventory:Mode</c>.
/// </summary>
public interface IInventoryGateway
{
    /// <summary>
    /// Reserves every line or nothing. The request carries our own idempotency key: repeating a key returns the outcome of
    /// the first call and never reserves twice. Lines that are not available are a business outcome
    /// (<see cref="InventoryReservationStatus.Unavailable"/>), not a failure.
    /// </summary>
    Task<Result<InventoryReservation>> ReserveAsync(InventoryReservationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a reservation, identified by the key it was made with. The release call carries its own idempotency key, so
    /// a repeated release restores the stock only once.
    /// </summary>
    Task<Result> ReleaseAsync(InventoryReleaseRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The outcome the provider recorded for <paramref name="idempotencyKey"/>. After a timeout the caller asks for the
    /// outcome instead of retrying, so an unknown outcome never causes a second reservation (ADR-0014). A key the provider
    /// does not know is an <see cref="ErrorKind.NotFound"/> failure: the earlier call never arrived.
    /// </summary>
    Task<Result<InventoryOutcome>> GetOutcomeAsync(string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>Stable error codes every <see cref="IInventoryGateway"/> adapter returns (ADR-0020).</summary>
public static class InventoryGatewayErrors
{
    public static Error ProviderUnavailable(string message) => Error.Integration("inventory-provider-unavailable", message);

    public static Error UnknownReservation(string reservationKey) =>
        Error.NotFound("inventory-unknown-reservation", $"The inventory system does not know a reservation for key '{reservationKey}'.");

    public static Error UnknownKey(string idempotencyKey) =>
        Error.NotFound("inventory-unknown-key", $"The inventory system has no outcome recorded for key '{idempotencyKey}'.");
}
