using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain;

/// <summary>Failures the Order aggregate returns for commands it cannot accept, with stable error codes (ADR-0020).</summary>
public static class OrderErrors
{
    /// <summary>
    /// The order is past the point where the customer may cancel it. The endpoint turns this into a
    /// <c>409 Conflict</c> that names the current state and points at customer support (ADR-0017 §3).
    /// </summary>
    public static Error NotCancellable(OrderStatus status) =>
        Error.Conflict("order-not-cancellable", $"An order in state {status} can no longer be cancelled by the customer.");
}
