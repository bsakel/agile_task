using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Shipping.Application;

/// <summary>Failures every <see cref="IShippingGateway"/> adapter returns, with stable error codes (ADR-0020).</summary>
public static class ShippingErrors
{
    /// <summary>The provider does not know this shipment id.</summary>
    public static Error UnknownShipment(string shipmentId) =>
        Error.NotFound("shipment-unknown", $"The shipping system does not know shipment '{shipmentId}'.");

    /// <summary>No call is recorded for this key, so it never took effect and may be retried (ADR-0014).</summary>
    public static Error UnknownIdempotencyKey(string idempotencyKey) =>
        Error.NotFound("shipment-request-unknown", $"The shipping system has no call recorded for key '{idempotencyKey}'.");

    /// <summary>A shipment that already left the warehouse cannot be cancelled; returns are out of scope (README §2).</summary>
    public static Error AlreadyDispatched(string shipmentId) =>
        Error.Conflict("shipment-already-dispatched", $"Shipment '{shipmentId}' has already been dispatched and cannot be cancelled.");
}
