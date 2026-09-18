using Marten;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Customers.Contracts;
using OrderPlatform.Ordering.Domain;
using OrderPlatform.Pricing.Contracts;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// The synchronous part of submission (ADR-0017 §3): check the account, price the order, start the stream. No external
/// system is called in the request path — inventory is reserved by the follow-up step of PR 2i (ADR-0014).
/// </summary>
public static class SubmitOrderHandler
{
    public static async Task<Result<SubmitOrderResponse>> Handle(
        SubmitOrder command,
        ICustomerDirectory customers,
        IPricingService pricing,
        IDocumentSession session,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var account = await customers.GetAccountAsync(command.AccountId, cancellationToken);
        if (!account.IsSuccess)
        {
            return account.Error;
        }

        if (!account.Value.IsActive)
        {
            return OrderErrors.AccountNotActive(account.Value.Status.ToString());
        }

        var priced = await pricing.PriceAsync(
            new PricingRequest(command.AccountId, [.. command.Lines.Select(line => new PricingRequestLine(line.Sku, line.Quantity))]),
            cancellationToken);
        if (!priced.IsSuccess)
        {
            return priced.Error;
        }

        var breakdown = priced.Value;
        var orderId = Guid.CreateVersion7();

        var decision = Order.Submit(
            orderId,
            command.AccountId,
            [.. LinesOf(command, breakdown)],
            ToOrderPricing(breakdown),
            account.Value.BillingAddressId,
            account.Value.ShippingAddressId,
            account.Value.PrimaryContactId,
            timeProvider.GetUtcNow());

        // Wolverine applies the Marten transaction around this handler (ADR-0005); PR 2i sends decision.FollowUps
        // through the outbox in the same transaction, which is why the order stays in ValidatingInventory here.
        session.Events.StartStream<Order>(orderId, [.. decision.Events]);

        return new SubmitOrderResponse(orderId, OrderStatus.ValidatingInventory, breakdown);
    }

    /// <summary>The ordered lines with the unit price Pricing resolved, so the order records what it was priced at.</summary>
    private static IEnumerable<OrderLine> LinesOf(SubmitOrder command, PriceBreakdown breakdown) =>
        command.Lines.Select(line => new OrderLine(line.Sku, line.Quantity, UnitPriceOf(breakdown, line.Sku)));

    private static Money UnitPriceOf(PriceBreakdown breakdown, string sku) =>
        breakdown.Lines.Single(line => line.Kind == PriceLineKind.Line && line.Sku == sku).UnitPrice!.Value;

    /// <summary>
    /// The Pricing breakdown as the Ordering domain stores it. Ordering keeps its own snapshot so the domain stays
    /// independent of Pricing's types (ADR-0003); the full breakdown travels on the response and, from PR 2g, the read model.
    /// </summary>
    private static OrderPricing ToOrderPricing(PriceBreakdown breakdown) =>
        new(breakdown.Net, breakdown.Tax, breakdown.Total, breakdown.PriceListVersion, breakdown.ReverseCharge);
}
