using OrderPlatform.Inventory.Contracts;
using OrderPlatform.Ordering.Domain;
using Wolverine;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// Translates the steps the aggregate decided into the owning modules' commands (ADR-0003, ADR-0017 §5). The aggregate
/// names a step in its own language; only this class knows which module performs it, and everything it sends goes
/// through the outbox in the handler's transaction, so a decision and its next step commit together (ADR-0005).
/// </summary>
internal static class OrderFollowUps
{
    /// <summary>
    /// Sends what each decided step means. <paramref name="reservationKey"/> is the reservation to give back: the one
    /// the order recorded, or — when the reservation itself arrived after the cancellation — the one that just
    /// completed, which the order never recorded (ADR-0017 §6).
    /// </summary>
    public static async Task SendAsync(
        IMessageContext messages,
        Guid orderId,
        OrderDecision decision,
        string? reservationKey = null,
        IReadOnlyList<ReservationLine>? lines = null)
    {
        foreach (var followUp in decision.FollowUps)
        {
            if (CommandFor(followUp, orderId, reservationKey, lines) is { } command)
            {
                await messages.SendAsync(command);
            }
        }
    }

    /// <summary>
    /// Voiding and refunding the invoice, cancelling a shipment request, issuing an invoice, requesting a shipment and
    /// checking the payment arrive with the billing and fulfilment steps (next steps N3, N4, N6 and N9). Until then the
    /// aggregate still decides them and the decision is on the stream — only the message is not sent yet.
    /// </summary>
    private static object? CommandFor(
        OrderFollowUp followUp,
        Guid orderId,
        string? reservationKey,
        IReadOnlyList<ReservationLine>? lines) => followUp switch
    {
        OrderFollowUp.ReserveInventory => new ReserveInventory(
            orderId,
            lines ?? throw new InvalidOperationException(
                $"Order {orderId} decided to reserve inventory, but no lines were given.")),
        OrderFollowUp.ReleaseInventory => new ReleaseInventory(
            orderId,
            reservationKey ?? throw new InvalidOperationException(
                $"Order {orderId} decided to release its reservation, but no reservation key is known.")),
        OrderFollowUp.IssueInvoice or OrderFollowUp.VoidInvoice or OrderFollowUp.RefundInvoice
            or OrderFollowUp.CancelShipmentRequest or OrderFollowUp.RequestShipment
            or OrderFollowUp.CheckInvoiceStatus => null,
        _ => throw new NotSupportedException($"{followUp} has no command in the Ordering module."),
    };
}
