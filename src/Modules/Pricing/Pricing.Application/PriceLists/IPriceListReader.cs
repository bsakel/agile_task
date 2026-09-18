using OrderPlatform.Pricing.Domain;

namespace OrderPlatform.Pricing.Application.PriceLists;

/// <summary>
/// Reads the price data the Pricing module owns in the <c>pricing</c> schema (ADR-0007). The reader resolves which list
/// applies: a customer-specific list overrides the base list per SKU (ADR-0018).
/// </summary>
public interface IPriceListReader
{
    /// <summary>The prices that apply to <paramref name="accountId"/> for the requested SKUs.</summary>
    Task<ApplicablePrices> GetApplicablePricesAsync(
        Guid accountId, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);
}

/// <summary>
/// What the applicable price lists say. A SKU without a price is simply absent from <paramref name="Prices"/>, which is
/// how the pricing service detects an unknown product.
/// </summary>
/// <param name="Version">Every list that took part, base list first, so the breakdown stays explainable after the lists
/// change (ADR-0018 price lock).</param>
/// <param name="Shipping">The shipping charge of the most specific applicable list.</param>
public sealed record ApplicablePrices(
    string Version, ShippingCharge Shipping, IReadOnlyDictionary<string, SkuPrice> Prices);

/// <summary>The unit price of one SKU and the tax rate of the product it stands for.</summary>
public sealed record SkuPrice(decimal UnitPrice, decimal TaxRate);
