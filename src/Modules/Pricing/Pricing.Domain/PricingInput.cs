namespace OrderPlatform.Pricing.Domain;

/// <summary>
/// Everything the rule pipeline needs to price an order. Assembled by the Pricing application from price lists and the
/// customer's tax profile, so the rules stay pure and testable without infrastructure (ADR-0018).
/// </summary>
/// <param name="Lines">Order lines with the unit price that applies to this customer.</param>
/// <param name="TaxProfile">The customer's tax profile, read from <c>Customers.Contracts</c>.</param>
/// <param name="ShippingCharge">The shipping charge configured for this order, applied by <see cref="Rules.ShippingChargeRule"/>.</param>
/// <param name="PriceListVersion">Version of the price list the unit prices come from; recorded on the breakdown so
/// historic orders stay explainable.</param>
/// <param name="FlagDecisions">Feature flag decisions taken for this pricing run, evaluated once before the pipeline runs
/// and recorded on the breakdown (ADR-0019 rule 4).</param>
public sealed record PricingInput(
    IReadOnlyList<PricingLine> Lines,
    TaxProfile TaxProfile,
    ShippingCharge ShippingCharge,
    string PriceListVersion,
    IReadOnlyDictionary<string, bool> FlagDecisions);

/// <summary>One order line with its resolved unit price and the tax rate that applies to the product.</summary>
public sealed record PricingLine(string Sku, int Quantity, decimal UnitPrice, decimal TaxRate);

/// <summary>Shipping charge for the order, taxed at <paramref name="TaxRate"/> like a product line.</summary>
public sealed record ShippingCharge(decimal Amount, decimal TaxRate);

/// <summary>
/// The customer's tax position. <paramref name="ReverseCharge"/> is the B2B case where the customer accounts for the
/// VAT themselves, so we invoice without tax (README §2).
/// </summary>
public sealed record TaxProfile(string Country, bool ReverseCharge);
