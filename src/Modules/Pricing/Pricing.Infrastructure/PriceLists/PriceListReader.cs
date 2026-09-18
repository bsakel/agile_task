using Dapper;
using Npgsql;
using OrderPlatform.Pricing.Application.PriceLists;
using OrderPlatform.Pricing.Domain;

namespace OrderPlatform.Pricing.Infrastructure.PriceLists;

/// <summary>
/// Reads the price lists of the <c>pricing</c> schema with Dapper and parameterised SQL (ADR-0007). The base list applies
/// to every account; a customer-specific list overrides it per SKU (ADR-0018).
/// </summary>
public sealed class PriceListReader(NpgsqlDataSource dataSource) : IPriceListReader
{
    // The lists that apply to this account, base list first and the customer's own list last (most specific).
    private const string SelectLists = """
        select version           as Version,
               shipping_charge   as ShippingCharge,
               shipping_tax_rate as ShippingTaxRate
        from pricing.price_lists
        where account_id is null or account_id = @accountId
        order by (account_id is not null)
        """;

    // distinct on keeps the first row per SKU, and the order puts the customer-specific price before the base price.
    // The SKUs go to PostgreSQL as one array parameter (= any), not as an expanded IN list.
    private const string SelectPrices = """
        select distinct on (item.sku)
               item.sku        as Sku,
               item.unit_price as UnitPrice,
               item.tax_rate   as TaxRate
        from pricing.price_list_items item
        join pricing.price_lists list on list.id = item.price_list_id
        where item.sku = any(@skus)
          and (list.account_id is null or list.account_id = @accountId)
        order by item.sku, (list.account_id is null)
        """;

    public async Task<ApplicablePrices> GetApplicablePricesAsync(
        Guid accountId, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var lists = (await connection.QueryAsync<PriceListRow>(
            new CommandDefinition(SelectLists, new { accountId }, cancellationToken: cancellationToken))).ToList();

        if (lists.Count == 0)
        {
            // No list is seeded for this installation: every SKU is an unknown product (ADR-0018).
            return new ApplicablePrices(string.Empty, new ShippingCharge(0m, 0m), new Dictionary<string, SkuPrice>(StringComparer.Ordinal));
        }

        var prices = new Dictionary<string, SkuPrice>(StringComparer.Ordinal);
        if (skus.Count > 0)
        {
            var rows = await connection.QueryAsync<SkuPriceRow>(
                new CommandDefinition(SelectPrices, new { accountId, skus }, cancellationToken: cancellationToken));

            foreach (var row in rows)
            {
                prices[row.Sku] = new SkuPrice(row.UnitPrice, row.TaxRate);
            }
        }

        var mostSpecific = lists[^1];

        return new ApplicablePrices(
            string.Join('+', lists.Select(list => list.Version)),
            new ShippingCharge(mostSpecific.ShippingCharge, mostSpecific.ShippingTaxRate),
            prices);
    }

    private sealed record PriceListRow(string Version, decimal ShippingCharge, decimal ShippingTaxRate);

    private sealed record SkuPriceRow(string Sku, decimal UnitPrice, decimal TaxRate);
}
