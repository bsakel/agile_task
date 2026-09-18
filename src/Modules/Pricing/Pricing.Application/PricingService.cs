using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.FeatureFlags;
using OrderPlatform.Customers.Contracts;
using OrderPlatform.Pricing.Application.PriceLists;
using OrderPlatform.Pricing.Contracts;

namespace OrderPlatform.Pricing.Application;

/// <summary>
/// The Pricing module's answer to <see cref="IPricingService"/>: it combines the price lists, the customer's tax profile
/// from <c>Customers.Contracts</c> and the rule pipeline into one breakdown (ADR-0018). Neither the price lists nor the
/// tax profile need an external call in the request path, so pricing an order is a local query (ADR-0003).
/// </summary>
public sealed class PricingService(
    IPriceListReader priceLists,
    ICustomerDirectory customers,
    Domain.PricingPipeline pipeline,
    IFeatureFlags featureFlags) : IPricingService
{
    public async Task<Result<PriceBreakdown>> PriceAsync(PricingRequest request, CancellationToken cancellationToken = default)
    {
        var account = await customers.GetAccountAsync(request.AccountId, cancellationToken);
        if (!account.IsSuccess)
        {
            return account.Error;
        }

        var skus = request.Lines.Select(line => line.Sku).Distinct(StringComparer.Ordinal).ToList();
        var prices = await priceLists.GetApplicablePricesAsync(request.AccountId, skus, cancellationToken);

        var lines = new List<Domain.PricingLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (!prices.Prices.TryGetValue(line.Sku, out var price))
            {
                return PricingErrors.UnknownProduct(line.Sku);
            }

            lines.Add(new Domain.PricingLine(line.Sku, line.Quantity, price.UnitPrice, price.TaxRate));
        }

        // Evaluated once per pricing run and recorded on the breakdown; later steps use the recorded decision rather than
        // a fresh evaluation, so a flag change never splits a running order (ADR-0019 rule 4).
        var flagDecisions = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [Domain.PricingFeatureFlags.ShippingCharge] =
                await featureFlags.IsEnabledAsync(Domain.PricingFeatureFlags.ShippingCharge, cancellationToken),
        };

        var taxProfile = account.Value.TaxProfile;
        var breakdown = pipeline.Price(new Domain.PricingInput(
            lines,
            new Domain.TaxProfile(taxProfile.CountryCode, taxProfile.ReverseCharge),
            prices.Shipping,
            prices.Version,
            flagDecisions));

        return ToContract(breakdown, prices.Prices);
    }

    private static PriceBreakdown ToContract(
        Domain.PriceBreakdown breakdown, IReadOnlyDictionary<string, SkuPrice> prices) => new(
        [.. breakdown.Lines.Select(line => new PriceLine(ToKind(line.Kind), line.Description, line.Amount, line.Sku)
        {
            UnitPrice = UnitPriceOf(line, prices),
        })],
        breakdown.Net,
        breakdown.Tax,
        breakdown.Total,
        breakdown.TaxTreatment == Domain.TaxTreatment.ReverseCharge,
        breakdown.PriceListVersion,
        breakdown.FlagDecisions);

    /// <summary>The unit price behind a product line; other kinds have none.</summary>
    private static Money? UnitPriceOf(Domain.PriceBreakdownLine line, IReadOnlyDictionary<string, SkuPrice> prices) =>
        line.Sku is { } sku && prices.TryGetValue(sku, out var price) ? Money.InEur(price.UnitPrice) : null;

    private static PriceLineKind ToKind(Domain.PriceBreakdownLineKind kind) => kind switch
    {
        Domain.PriceBreakdownLineKind.Discount => PriceLineKind.Discount,
        Domain.PriceBreakdownLineKind.Charge => PriceLineKind.Charge,
        Domain.PriceBreakdownLineKind.Tax => PriceLineKind.Tax,
        _ => PriceLineKind.Line,
    };
}
