namespace OrderPlatform.Shipping.Application;

/// <summary>
/// Where a shipment stands in the external fulfilment/shipping system, in our own vocabulary (ADR-0014). The states
/// map to the order lifecycle: dispatched → <c>Shipped</c>, delivered → <c>Delivered</c>, failed →
/// <c>FulfilmentOnHold</c>, and a request is cancelled while an order is <c>Refunding</c> (ADR-0017).
/// </summary>
public enum ShipmentState
{
    Requested,
    Dispatched,
    Delivered,
    Failed,
    Cancelled,
}

/// <summary>One order line to ship. Product ids are the SKUs of the external inventory system (README §2).</summary>
public sealed record ShipmentLine(string Sku, int Quantity);

/// <summary>
/// A request to ship an order. It carries address and contact <b>ids</b> only: personal data stays in the Customers
/// module and never travels with the order process (ADR-0016, ADR-0017 §3). <paramref name="IdempotencyKey"/> is our
/// own key for this call, e.g. <c>ship:{orderId}:{attempt}</c> (ADR-0017 §7).
/// </summary>
public sealed record ShipmentRequest(
    Guid OrderId, string IdempotencyKey, Guid ShippingAddressId, Guid ContactId, IReadOnlyList<ShipmentLine> Lines);

/// <summary>
/// A shipment as the provider reports it, mapped into our model. <paramref name="LastChangedAt"/> is UTC (ADR-0020).
/// </summary>
public sealed record Shipment(
    string ShipmentId, Guid OrderId, ShipmentState State, string? TrackingCode, string? FailureReason, DateTimeOffset LastChangedAt);
