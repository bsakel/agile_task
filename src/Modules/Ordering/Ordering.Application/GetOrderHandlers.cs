using Marten;
using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Ordering.Application;

/// <summary>Reads the inline projection: strongly consistent, so an order can be read straight after it was submitted.</summary>
public static class GetOrderHandler
{
    public static async Task<Result<OrderView>> Handle(GetOrder query, IQuerySession session, CancellationToken cancellationToken)
    {
        var order = await OrderAccess.LoadAsync(session, query.CallerAccountId, query.OrderId, cancellationToken);
        if (!order.IsSuccess)
        {
            return order.Error;
        }

        var details = order.Value;

        return new OrderView(
            details.Id,
            details.Status,
            [.. details.Lines.Select(line => new OrderLineView(line.Sku, line.Quantity, line.UnitPrice))],
            new OrderPricingView(
                details.Pricing.Net,
                details.Pricing.Tax,
                details.Pricing.Total,
                details.Pricing.PriceListVersion,
                details.Pricing.ReverseCharge),
            details.InvoiceId,
            details.PaymentDueAt,
            details.SubmittedAt,
            details.UpdatedAt);
    }
}

/// <summary>
/// Reads the raw stream rather than a side table: the events are the audit trail (ADR-0006). The projection is loaded
/// first, because who may read the order is a fact of the order, not of the stream.
/// </summary>
public static class GetOrderHistoryHandler
{
    public static async Task<Result<OrderHistoryView>> Handle(
        GetOrderHistory query,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var order = await OrderAccess.LoadAsync(session, query.CallerAccountId, query.OrderId, cancellationToken);
        if (!order.IsSuccess)
        {
            return order.Error;
        }

        var stream = await session.Events.FetchStreamAsync(query.OrderId, token: cancellationToken);

        return new OrderHistoryView(
            query.OrderId,
            [.. stream.Select(stored => new OrderHistoryEntry(stored.Version, stored.EventTypeName, stored.Timestamp))]);
    }
}

/// <summary>
/// Loads an order the caller is allowed to see. An order that does not exist and an order of another account give the
/// same answer, so a response never reveals that someone else's order exists (ADR-0016, ADR-0020).
/// </summary>
internal static class OrderAccess
{
    public static async Task<Result<OrderDetails>> LoadAsync(
        IQuerySession session,
        Guid callerAccountId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await session.LoadAsync<OrderDetails>(orderId, cancellationToken);
        if (order is null)
        {
            return Error.NotFound(AccountAccess.ResourceNotFoundCode, "The order was not found.");
        }

        return AccountAccess.EnsureOwnedBy(callerAccountId, order.AccountId, "order") is { IsSuccess: false } denied
            ? denied.Error
            : order;
    }
}
