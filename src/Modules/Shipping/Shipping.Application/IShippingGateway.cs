using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Shipping.Application;

/// <summary>
/// Port to the external fulfilment/shipping system, expressed in our language (ADR-0014). Adapters are the
/// anti-corruption layer: provider errors and unknown statuses become <see cref="Result"/> failures, never exceptions
/// and never a guess. Calls are made from message handlers, never inside an HTTP request.
/// </summary>
public interface IShippingGateway
{
    /// <summary>
    /// Requests a shipment for an order. Repeating the call with the same <see cref="ShipmentRequest.IdempotencyKey"/>
    /// returns the shipment of the first call and does not request a second one (ADR-0014, ADR-0017 §7).
    /// </summary>
    Task<Result<Shipment>> RequestShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a shipment request that has not been dispatched, as compensation while an order is <c>Refunding</c>
    /// (ADR-0017 §5). <paramref name="idempotencyKey"/> is our own key for this cancellation, e.g.
    /// <c>cancel-shipment:{orderId}</c>; an unknown or already dispatched shipment is a <see cref="ShippingErrors"/> failure.
    /// </summary>
    Task<Result<Shipment>> CancelShipmentRequestAsync(string shipmentId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Current state of a shipment. The fulfilment step polls it on a schedule and on demand until dispatch, delivery
    /// or failure; provider push (webhooks) is added later and feeds the same commands (ADR-0014 pull before push).
    /// </summary>
    Task<Result<Shipment>> GetShipmentStatusAsync(string shipmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Outcome of an earlier state-changing call, looked up by our idempotency key. After a timeout the caller asks for
    /// the outcome before retrying, so an unknown outcome never causes a second shipment (ADR-0014). The failure code
    /// <c>shipment-request-unknown</c> means the call never took effect and is safe to retry.
    /// </summary>
    Task<Result<Shipment>> GetOutcomeAsync(string idempotencyKey, CancellationToken cancellationToken);
}
