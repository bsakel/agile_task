using Microsoft.Extensions.DependencyInjection;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Pricing.Contracts;
using OrderPlatform.Testing;

// The flag registry lives in Pricing.Domain; Contracts has its own PriceBreakdown, so only the registry is imported.
using PricingFeatureFlags = OrderPlatform.Pricing.Domain.PricingFeatureFlags;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2c: after the Migrator applied the <c>pricing</c> scripts, <see cref="IPricingService"/> combines the seeded price
/// lists, the customer's tax profile from <c>Customers.Contracts</c> and the rule pipeline (ADR-0018, ADR-0019).
/// </summary>
public sealed class PricingServiceTests(PlatformFixture platform)
{
    private const string BaseVersion = "base-2026-09";
    private const string AcmeVersion = "acme-2026-09";

    /// <summary>The account, the unit price that applies to it for <c>SKU-1000</c> and the lists behind that price.</summary>
    public static TheoryData<string, decimal, string> SeededPrices => new()
    {
        { KeycloakTokens.AcmeAccountId, 8.50m, $"{BaseVersion}+{AcmeVersion}" },
        { KeycloakTokens.GlobexAccountId, 10.00m, BaseVersion },
    };

    [Theory]
    [MemberData(nameof(SeededPrices))]
    public async Task A_customer_specific_price_overrides_the_base_price(string accountId, decimal expectedUnitPrice, string expectedVersion)
    {
        var breakdown = await PriceAsync(platform.Api, accountId, new PricingRequestLine("SKU-1000", 2));

        var line = breakdown.Lines.Where(line => line.Kind == PriceLineKind.Line).ShouldHaveSingleItem();
        line.Sku.ShouldBe("SKU-1000");
        line.Amount.Amount.ShouldBe(expectedUnitPrice * 2);
        breakdown.PriceListVersion.ShouldBe(expectedVersion);
    }

    [Fact]
    public async Task A_sku_without_a_price_fails_with_unknown_product()
    {
        using var scope = platform.Api.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var result = await pricing.PriceAsync(
            new PricingRequest(Guid.Parse(KeycloakTokens.AcmeAccountId), [new PricingRequestLine("SKU-NOT-PRICED", 1)]),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(PricingErrors.UnknownProductCode);
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    /// <summary>
    /// ADR-0019 rule 3 and 4: the flagged charge is proven in both states, and the decision that produced this price is
    /// recorded on the breakdown together with the price list version, so later steps do not re-evaluate it.
    /// </summary>
    [Theory]
    [MemberData(nameof(FeatureFlagStates.OnAndOff), MemberType = typeof(FeatureFlagStates))]
    public async Task The_breakdown_records_the_price_list_version_and_the_shipping_charge_flag_decision(bool enabled)
    {
        var api = platform.ApiWith(FeatureFlagStates.Settings((PricingFeatureFlags.ShippingCharge, enabled)));

        var breakdown = await PriceAsync(api, KeycloakTokens.AcmeAccountId, new PricingRequestLine("SKU-1000", 2));

        breakdown.PriceListVersion.ShouldBe($"{BaseVersion}+{AcmeVersion}");
        breakdown.FlagDecisions[PricingFeatureFlags.ShippingCharge].ShouldBe(enabled);

        // ACME's own list carries a 9.95 shipping charge; with the flag off the order is priced without it.
        var charges = breakdown.Lines.Where(line => line.Kind == PriceLineKind.Charge).ToList();
        charges.Select(charge => charge.Amount.Amount).ShouldBe(enabled ? [9.95m] : []);
        // 17.00 for the two lines, plus 9.95 shipping while the flag is on; VAT is 21% of the rounded net.
        breakdown.Total.Amount.ShouldBe(enabled ? 32.61m : 20.57m);
    }

    private static async Task<PriceBreakdown> PriceAsync(OrderPlatformApiFactory api, string accountId, params PricingRequestLine[] lines)
    {
        using var scope = api.Services.CreateScope();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var result = await pricing.PriceAsync(
            new PricingRequest(Guid.Parse(accountId), lines), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value!;
    }
}
