using Marten;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Messaging;
using OrderPlatform.Ordering.Domain;
using Wolverine;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// The customer asks to cancel their order (ADR-0017 §3). Whether that is still allowed is the aggregate's decision,
/// and what has to be undone comes with it.
/// </summary>
public sealed record CancelOrder(Guid CallerAccountId, Guid OrderId) : ICommand;

/// <summary>What the customer gets back: the state the order is in now, and whether compensation is still running.</summary>
public sealed record CancelOrderResponse(Guid OrderId, OrderStatus Status);

public static class CancelOrderHandler
{
    public static async Task<Result<CancelOrderResponse>> Handle(
        CancelOrder command,
        IDocumentSession session,
        IMessageContext messages,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // Loaded through the read model first: an order of another account is not found, so cancelling cannot be used
        // to discover that someone else's order exists (ADR-0016).
        var owned = await OrderAccess.LoadAsync(session, command.CallerAccountId, command.OrderId, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var order = await OrderStream.LoadAsync(session, command.OrderId, cancellationToken);

        var decision = order.CancelByCustomer(timeProvider.GetUtcNow());
        if (!decision.IsSuccess)
        {
            return decision.Error;
        }

        await OrderStream.AppendAsync(session, command.OrderId, decision.Value, cancellationToken);
        await OrderFollowUps.SendAsync(messages, command.OrderId, decision.Value, order.ReservationKey);

        var cancelled = order;
        OrderStream.Apply(cancelled, decision.Value.Events);

        return new CancelOrderResponse(command.OrderId, cancelled.Status);
    }
}
