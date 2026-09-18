using OrderPlatform.BuildingBlocks.Messaging;
using OrderPlatform.Ordering.Domain;
using OrderPlatform.Pricing.Contracts;

namespace OrderPlatform.Ordering.Application;

/// <summary>
/// Command behind <c>POST /v1/orders</c> (ADR-0017 §3). The caller's account comes from the token, never from the body,
/// so an order can only be submitted for the account that is calling (ADR-0016).
/// </summary>
public sealed record SubmitOrder(Guid AccountId, IReadOnlyList<SubmitOrderLine> Lines) : ICommand;

/// <summary>One requested product: a SKU of the external inventory system and how many of it.</summary>
public sealed record SubmitOrderLine(string Sku, int Quantity);

/// <summary>
/// What the customer gets back: the order to poll, the state it starts in and the price it is locked at. The order
/// stays in <see cref="OrderStatus.ValidatingInventory"/> until the reservation step of PR 2i moves it on.
/// </summary>
public sealed record SubmitOrderResponse(Guid OrderId, OrderStatus Status, PriceBreakdown Pricing);
