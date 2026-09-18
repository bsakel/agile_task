using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Domain;

/// <summary>Failures the Ordering module returns for commands it cannot accept, with stable error codes (ADR-0020).</summary>
public static class OrderErrors
{
    /// <summary>
    /// The order is past the point where the customer may cancel it. The endpoint turns this into a
    /// <c>409 Conflict</c> that names the current state and points at customer support (ADR-0017 §3).
    /// </summary>
    public static Error NotCancellable(OrderStatus status) =>
        Error.Conflict("order-not-cancellable", $"An order in state {status} can no longer be cancelled by the customer.");

    /// <summary>
    /// Only an active customer account may submit orders (README §2). A suspended or closed account is a business rule
    /// failure, not a missing resource, so submission answers <c>422</c> (ADR-0020).
    /// </summary>
    public static Error AccountNotActive(string status) =>
        Error.BusinessRule("account-not-active", $"The customer account is {status} and cannot submit orders.");
}
