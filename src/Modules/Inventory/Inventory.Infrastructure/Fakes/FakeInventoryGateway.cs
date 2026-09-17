using OrderPlatform.BuildingBlocks;
using OrderPlatform.Inventory.Application.Integrations;

namespace OrderPlatform.Inventory.Infrastructure.Fakes;

/// <summary>
/// In-memory stand-in for the external inventory system, for local development and tests only; the startup guard keeps
/// fakes out of deployed environments (ADR-0014). It records the outcome of every idempotency key, so a repeated key
/// reserves or releases once, and it answers the outcome query for keys it has seen.
/// </summary>
public sealed class FakeInventoryGateway(FakeInventoryOptions options) : IInventoryGateway
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> stock = new(options.Stock, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InventoryOutcome> outcomes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> held = new(StringComparer.Ordinal);

    public Task<Result<InventoryReservation>> ReserveAsync(InventoryReservationRequest request, CancellationToken cancellationToken)
    {
        if (options.FailStateChangingCalls)
        {
            return Task.FromResult(Result<InventoryReservation>.Failure(Outage()));
        }

        lock (gate)
        {
            if (outcomes.TryGetValue(request.IdempotencyKey, out var recorded) && recorded.Reservation is { } first)
            {
                return Task.FromResult(Result<InventoryReservation>.Success(first));
            }

            var required = request.Lines
                .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(lines => lines.Key, lines => lines.Sum(line => line.Quantity), StringComparer.OrdinalIgnoreCase);

            var unavailable = required
                .Where(line => options.UnavailableSkus.Contains(line.Key, StringComparer.OrdinalIgnoreCase) || Available(line.Key) < line.Value)
                .Select(line => line.Key)
                .Order(StringComparer.Ordinal)
                .ToList();

            // All or nothing: a single unavailable line reserves none of them.
            var reservation = unavailable.Count > 0
                ? InventoryReservation.Unavailable(request.IdempotencyKey, unavailable)
                : Hold(request.IdempotencyKey, required);

            outcomes[request.IdempotencyKey] = new InventoryOutcome(request.IdempotencyKey, InventoryOperation.Reserve, reservation);
            return Task.FromResult(Result<InventoryReservation>.Success(reservation));
        }
    }

    public Task<Result> ReleaseAsync(InventoryReleaseRequest request, CancellationToken cancellationToken)
    {
        if (options.FailStateChangingCalls)
        {
            return Task.FromResult(Result.Failure(Outage()));
        }

        lock (gate)
        {
            if (outcomes.ContainsKey(request.IdempotencyKey))
            {
                return Task.FromResult(Result.Success());
            }

            if (!held.Remove(request.ReservationKey, out var lines))
            {
                return Task.FromResult(Result.Failure(InventoryGatewayErrors.UnknownReservation(request.ReservationKey)));
            }

            foreach (var (sku, quantity) in lines)
            {
                stock[sku] = Available(sku) + quantity;
            }

            outcomes[request.IdempotencyKey] = new InventoryOutcome(request.IdempotencyKey, InventoryOperation.Release, null);
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result<InventoryOutcome>> GetOutcomeAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(outcomes.TryGetValue(idempotencyKey, out var outcome)
                ? Result<InventoryOutcome>.Success(outcome)
                : Result<InventoryOutcome>.Failure(InventoryGatewayErrors.UnknownKey(idempotencyKey)));
        }
    }

    private InventoryReservation Hold(string reservationKey, Dictionary<string, int> required)
    {
        foreach (var (sku, quantity) in required)
        {
            stock[sku] = Available(sku) - quantity;
        }

        held[reservationKey] = required;
        return InventoryReservation.Reserved(reservationKey);
    }

    private int Available(string sku) => stock.TryGetValue(sku, out var units) ? units : options.DefaultStock;

    private static Error Outage() => InventoryGatewayErrors.ProviderUnavailable(
        $"The fake inventory system is configured with {nameof(FakeInventoryOptions.FailStateChangingCalls)}=true.");
}
