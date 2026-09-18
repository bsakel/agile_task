using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Pricing.Contracts;

/// <summary>
/// Prices an order for one customer account (ADR-0018). A synchronous query between modules: the caller passes SKUs and
/// quantities, the Pricing module resolves the prices, the tax profile and the rules, and answers with the breakdown the
/// order is locked at (ADR-0003).
/// </summary>
public interface IPricingService
{
    /// <summary>
    /// The breakdown for the request, or an expected failure returned as a <see cref="Result"/>:
    /// <see cref="PricingErrors.UnknownProductCode"/> for a SKU without a price in the applicable price list, and the
    /// Customers failure when the account is not known.
    /// </summary>
    Task<Result<PriceBreakdown>> PriceAsync(PricingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What to price: the account whose price list and tax profile apply, and the requested lines.</summary>
public sealed record PricingRequest(Guid AccountId, IReadOnlyList<PricingRequestLine> Lines);

/// <summary>One requested product: a SKU of the external inventory system and how many of it (ADR-0018).</summary>
public sealed record PricingRequestLine(string Sku, int Quantity);

/// <summary>
/// The calculated price as other modules see it: the lines the rules produced, the totals, and what the calculation was
/// based on. Stored on the order event, so the invoice uses exactly these amounts and historic orders never change
/// (ADR-0018). Contracts carry no domain types, so this is the published shape of the domain's breakdown (ADR-0003).
/// </summary>
/// <param name="PriceListVersion">Every price list that took part, base list first (e.g. <c>base-2026-09+acme-2026-09</c>).</param>
/// <param name="FlagDecisions">The flag decisions this price was calculated with; later steps use these, not a fresh
/// evaluation (ADR-0019 rule 4).</param>
public sealed record PriceBreakdown(
    IReadOnlyList<PriceLine> Lines,
    Money Net,
    Money Tax,
    Money Total,
    bool ReverseCharge,
    string PriceListVersion,
    IReadOnlyDictionary<string, bool> FlagDecisions);

/// <summary>One line of the breakdown, already rounded. <paramref name="Sku"/> is set for product lines only.</summary>
public sealed record PriceLine(PriceLineKind Kind, string Description, Money Amount, string? Sku)
{
    /// <summary>
    /// The unit price the product line was priced at; <c>null</c> for discounts, charges and tax. Published because
    /// <paramref name="Amount"/> is the rounded line total: dividing it back by the quantity would not give the price
    /// the line was locked at (ADR-0018 rounding policy).
    /// </summary>
    public Money? UnitPrice { get; init; }
}

public enum PriceLineKind
{
    Line,
    Discount,
    Charge,
    Tax,
}

/// <summary>Stable error codes of the Pricing module (ADR-0020).</summary>
public static class PricingErrors
{
    public const string UnknownProductCode = "unknown-product";

    /// <summary>
    /// A SKU without a price cannot be ordered. This validates products without calling the external inventory system in
    /// the request path; submission turns it into <c>422</c> (ADR-0018, ADR-0020).
    /// </summary>
    public static Error UnknownProduct(string sku) =>
        Error.BusinessRule(UnknownProductCode, $"Product '{sku}' has no price in the applicable price list.");
}
