using OrderPlatform.BuildingBlocks.Messaging;
using OrderPlatform.Inventory.Application.Integrations;
using OrderPlatform.Inventory.Contracts;

namespace OrderPlatform.Inventory.Application;

/// <summary>
/// Reserves an order's lines in the external inventory system and reports the outcome back as an integration event
/// (ADR-0003, ADR-0014). The Inventory module performs this one step and knows nothing about the order lifecycle.
/// </summary>
public static class ReserveInventoryHandler
{
    public static async Task<IIntegrationEvent> Handle(
        ReserveInventory command,
        IInventoryGateway inventory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var request = new InventoryReservationRequest(
            KeyFor(command),
            [.. command.Lines.Select(line => new InventoryLine(line.Sku, line.Quantity))]);

        var result = await inventory.ReserveAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            // An unreachable provider is not a business outcome: the message is retried by Wolverine, and the scheduled
            // backoff and the RequiresAttention path after exhausted retries are next step N13 (ADR-0014).
            throw new InvalidOperationException(
                $"Reserving inventory for order {command.OrderId} failed: {result.Error.Code} — {result.Error.Message}");
        }

        var reservation = result.Value;
        var now = timeProvider.GetUtcNow();

        return reservation.Status == InventoryReservationStatus.Reserved
            ? new InventoryReserved(command.OrderId, reservation.ReservationKey, now)
            : new InventoryUnavailable(command.OrderId, reservation.UnavailableSkus, now);
    }

    /// <summary>Our own key for this call, so a retry never reserves twice (ADR-0014, ADR-0017 §7).</summary>
    private static string KeyFor(ReserveInventory command) => $"reserve:{command.OrderId}:{command.Attempt}";
}
