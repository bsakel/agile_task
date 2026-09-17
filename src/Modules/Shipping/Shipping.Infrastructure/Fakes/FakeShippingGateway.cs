using OrderPlatform.BuildingBlocks;
using OrderPlatform.Shipping.Application;

namespace OrderPlatform.Shipping.Infrastructure.Fakes;

/// <summary>
/// In-memory fulfilment/shipping system for local development and tests (ADR-0014). Registered only when
/// <c>Integrations:Shipping:Mode = Fake</c>; the startup guard keeps fakes out of deployed environments. A requested
/// shipment reports the outcome configured in <see cref="FakeShippingOptions"/> the first time its status is polled.
/// </summary>
public sealed class FakeShippingGateway(FakeShippingOptions options, TimeProvider timeProvider) : IShippingGateway
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> shipmentIdsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Shipment> shipments = new(StringComparer.Ordinal);
    private int sequence;

    /// <summary>Every shipment the fake accepted, so tests and local diagnostics can see what was requested.</summary>
    public IReadOnlyCollection<Shipment> Shipments
    {
        get { lock (gate) { return [.. shipments.Values]; } }
    }

    public Task<Result<Shipment>> RequestShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Request(request));

    public Task<Result<Shipment>> CancelShipmentRequestAsync(string shipmentId, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Cancel(shipmentId, idempotencyKey));

    public Task<Result<Shipment>> GetShipmentStatusAsync(string shipmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Status(shipmentId));

    public Task<Result<Shipment>> GetOutcomeAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Outcome(idempotencyKey));

    private Result<Shipment> Request(ShipmentRequest request)
    {
        lock (gate)
        {
            // The same key must never produce a second shipment, however often the message is delivered (ADR-0014).
            if (shipmentIdsByKey.TryGetValue(request.IdempotencyKey, out var existing))
            {
                return shipments[existing];
            }

            var shipment = new Shipment(
                $"SHP-{++sequence:D6}", request.OrderId, ShipmentState.Requested, null, null, timeProvider.GetUtcNow());

            shipmentIdsByKey[request.IdempotencyKey] = shipment.ShipmentId;
            shipments[shipment.ShipmentId] = shipment;
            return shipment;
        }
    }

    private Result<Shipment> Cancel(string shipmentId, string idempotencyKey)
    {
        lock (gate)
        {
            if (shipmentIdsByKey.TryGetValue(idempotencyKey, out var cancelled))
            {
                return shipments[cancelled];
            }

            if (!shipments.TryGetValue(shipmentId, out var known))
            {
                return ShippingErrors.UnknownShipment(shipmentId);
            }

            if (known.State is ShipmentState.Dispatched or ShipmentState.Delivered)
            {
                return ShippingErrors.AlreadyDispatched(shipmentId);
            }

            shipmentIdsByKey[idempotencyKey] = shipmentId;
            return Record(known with { State = ShipmentState.Cancelled, LastChangedAt = timeProvider.GetUtcNow() });
        }
    }

    private Result<Shipment> Status(string shipmentId)
    {
        lock (gate)
        {
            if (!shipments.TryGetValue(shipmentId, out var known))
            {
                return ShippingErrors.UnknownShipment(shipmentId);
            }

            return known.State == ShipmentState.Requested ? Record(Report(known)) : known;
        }
    }

    private Result<Shipment> Outcome(string idempotencyKey)
    {
        lock (gate)
        {
            return shipmentIdsByKey.TryGetValue(idempotencyKey, out var shipmentId)
                ? shipments[shipmentId]
                : ShippingErrors.UnknownIdempotencyKey(idempotencyKey);
        }
    }

    /// <summary>The configured outcome as the provider would report it the first time the shipment is polled.</summary>
    private Shipment Report(Shipment shipment)
    {
        var now = timeProvider.GetUtcNow();
        return options.Outcome switch
        {
            FakeShipmentOutcome.Delivered => shipment with { State = ShipmentState.Delivered, TrackingCode = Tracking(shipment), LastChangedAt = now },
            FakeShipmentOutcome.Failed => shipment with { State = ShipmentState.Failed, FailureReason = options.FailureReason, LastChangedAt = now },
            _ => shipment with { State = ShipmentState.Dispatched, TrackingCode = Tracking(shipment), LastChangedAt = now },
        };
    }

    private Shipment Record(Shipment shipment) => shipments[shipment.ShipmentId] = shipment;

    private static string Tracking(Shipment shipment) => $"TRK-{shipment.ShipmentId}";
}
