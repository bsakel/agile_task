using Marten;
using OrderPlatform.Ordering.Domain;
using Wolverine;
using Domain = OrderPlatform.Ordering.Domain;

// Each outcome exists twice by design and the two must not be confused: the Inventory contract is the message reporting
// what the external system did, the Ordering domain event is what the order records on its stream (ADR-0003, ADR-0010).
using InventoryReservedMessage = OrderPlatform.Inventory.Contracts.InventoryReserved;
using InventoryUnavailableMessage = OrderPlatform.Inventory.Contracts.InventoryUnavailable;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// The Ordering side of the reservation step: load the stream, let the aggregate decide, append what it decided
/// (ADR-0017 §1). An outcome that no longer applies — a reservation that arrives after the order was cancelled, a
/// duplicate delivery — is ignored by the aggregate and simply appends nothing.
/// </summary>
public static class InventoryReservedHandler
{
    public static Task Handle(
        InventoryReservedMessage @event,
        IDocumentSession session,
        IMessageContext messages,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        OrderStream.DecideAsync(
            session,
            messages,
            @event.OrderId,
            order => order.ReservationSucceeded(@event.ReservationKey, timeProvider.GetUtcNow()),
            cancellationToken,
            // The reservation that just completed is the one to give back when it lost the race with a cancellation;
            // the order itself never recorded it (ADR-0017 §6).
            @event.ReservationKey);
}

/// <summary>The reservation failed; the aggregate asks the customer to reduce the order (all-or-nothing, README §2).</summary>
public static class InventoryUnavailableHandler
{
    public static Task Handle(
        InventoryUnavailableMessage @event,
        IDocumentSession session,
        IMessageContext messages,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        OrderStream.DecideAsync(
            session,
            messages,
            @event.OrderId,
            order => order.ReservationFailed(@event.UnavailableSkus, timeProvider.GetUtcNow()),
            cancellationToken);
}

/// <summary>
/// Load, decide, append — the one shape every Ordering handler reacting to an event follows (ADR-0017 §1). Appending
/// is optimistic on the stream version (ADR-0006): a handler that loses the race retries against the new state and is
/// ignored if the trigger no longer applies.
/// </summary>
public static class OrderStream
{
    public static async Task DecideAsync(
        IDocumentSession session,
        IMessageContext messages,
        Guid orderId,
        Func<Order, OrderDecision> decide,
        CancellationToken cancellationToken,
        string? reservationKey = null)
    {
        var order = await LoadAsync(session, orderId, cancellationToken);

        var decision = decide(order);

        await AppendAsync(session, orderId, decision, cancellationToken);
        await OrderFollowUps.SendAsync(messages, orderId, decision, reservationKey ?? order.ReservationKey);
    }

    /// <summary>
    /// Appends what the aggregate decided, optimistically on the stream version (ADR-0006): a handler that loses the
    /// race retries against the new state, where the trigger is ignored if it no longer applies. An ignored decision
    /// has no events and appends nothing.
    /// </summary>
    public static Task AppendAsync(
        IDocumentSession session,
        Guid orderId,
        OrderDecision decision,
        CancellationToken cancellationToken) =>
        decision.Events.Count > 0
            ? session.Events.AppendOptimistic(orderId, [.. decision.Events])
            : Task.CompletedTask;

    /// <summary>
    /// Rebuilds the order from its stream, the single source of truth for its state (ADR-0006). The events are
    /// dispatched explicitly rather than through Marten's aggregation:
    /// that path needs Marten's source generator to run in the assembly that declares the aggregate, and the Ordering
    /// domain is deliberately free of Marten (ADR-0003, enforced by the architecture tests). A stored event with no case
    /// here is a registration someone forgot, so it fails loudly instead of being folded away silently (ADR-0010).
    /// </summary>
    public static async Task<Order> LoadAsync(IQuerySession session, Guid orderId, CancellationToken cancellationToken)
    {
        var stream = await session.Events.FetchStreamAsync(orderId, token: cancellationToken);
        if (stream.Count == 0)
        {
            throw new InvalidOperationException($"Order {orderId} has no event stream.");
        }

        var order = new Order();
        Apply(order, stream.Select(stored => stored.Data), orderId);

        return order;
    }

    /// <summary>Applies decided events to a loaded order, so a handler can answer with the state it just produced.</summary>
    public static void Apply(Order order, IEnumerable<object> events) => Apply(order, events, order.Id);

    private static void Apply(Order order, IEnumerable<object> events, Guid orderId)
    {
        foreach (var @event in events)
        {
            switch (@event)
            {
                case OrderSubmitted e: order.Apply(e); break;
                case Domain.InventoryReserved e: order.Apply(e); break;
                case Domain.InventoryUnavailable e: order.Apply(e); break;
                case InvoiceIssued e: order.Apply(e); break;
                case InvoicePaid e: order.Apply(e); break;
                case InvoicePartiallyPaid e: order.Apply(e); break;
                case AttentionRequired e: order.Apply(e); break;
                case OrderCancelled e: order.Apply(e); break;
                case OrderItemsReduced e: order.Apply(e); break;
                case FulfilmentFailed e: order.Apply(e); break;
                case FulfilmentInformationUpdated e: order.Apply(e); break;
                case ShipmentDispatched e: order.Apply(e); break;
                case ShipmentDelivered e: order.Apply(e); break;
                case RefundRequested e: order.Apply(e); break;
                case RefundCompleted e: order.Apply(e); break;
                case AttentionResolved e: order.Apply(e); break;
                default: throw new NotSupportedException(
                    $"Order {orderId} has an event of type {@event.GetType().FullName} that the aggregate cannot apply.");
            }
        }
    }
}
