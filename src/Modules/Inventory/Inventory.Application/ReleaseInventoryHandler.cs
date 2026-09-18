using OrderPlatform.Inventory.Application.Integrations;
using OrderPlatform.Inventory.Contracts;

namespace OrderPlatform.Inventory.Application;

/// <summary>
/// Gives a reservation back to the external inventory system (ADR-0017 §5). The release carries its own idempotency
/// key, so a repeated release restores the stock once (ADR-0014).
/// </summary>
public static class ReleaseInventoryHandler
{
    public static async Task Handle(
        ReleaseInventory command,
        IInventoryGateway inventory,
        CancellationToken cancellationToken)
    {
        var result = await inventory.ReleaseAsync(
            new InventoryReleaseRequest(KeyFor(command), command.ReservationKey),
            cancellationToken);

        if (!result.IsSuccess)
        {
            // Releasing is compensation: it must not be lost, so the message is retried rather than swallowed. Scheduled
            // backoff and RequiresAttention after exhausted retries are next step N13 (ADR-0014, ADR-0017 §5).
            throw new InvalidOperationException(
                $"Releasing the inventory of order {command.OrderId} failed: {result.Error.Code} — {result.Error.Message}");
        }
    }

    private static string KeyFor(ReleaseInventory command) => $"release:{command.OrderId}";
}
