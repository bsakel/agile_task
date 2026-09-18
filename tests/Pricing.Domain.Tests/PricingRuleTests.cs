using OrderPlatform.Pricing.Domain.Rules;
using OrderPlatform.Pricing.Domain.Tests.Builders;

namespace OrderPlatform.Pricing.Domain.Tests;

/// <summary>The v1 rules: per-line rounding, the flagged shipping charge and tax per rate (ADR-0018, ADR-0019).</summary>
public sealed class PricingRuleTests
{
    [Theory]
    [InlineData(3, 0.105, 0.32)]   // 0.315 rounds away from zero
    [InlineData(1, 10.005, 10.01)]
    [InlineData(2, 10.00, 20.00)]
    [InlineData(3, 1.115, 3.35)]   // 3.345 rounds away from zero
    public void A_line_is_unit_price_times_quantity_rounded_per_line(int quantity, decimal unitPrice, decimal expected)
    {
        var context = Apply(new LineSubtotalRule(), new PricingInputBuilder()
            .WithLines(new PricingLine("SKU-1", quantity, unitPrice, PricingInputBuilder.StandardRate)));

        var line = context.Lines.ShouldHaveSingleItem();
        line.Kind.ShouldBe(PriceBreakdownLineKind.Line);
        line.Sku.ShouldBe("SKU-1");
        line.Amount.Amount.ShouldBe(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_shipping_charge_is_added_only_while_its_release_flag_is_on(bool enabled)
    {
        var context = Apply(new ShippingChargeRule(), new PricingInputBuilder()
            .WithShippingCharge(new ShippingCharge(12.50m, PricingInputBuilder.StandardRate))
            .WithFlag(PricingFeatureFlags.ShippingCharge, enabled));

        if (enabled)
        {
            var charge = context.Lines.ShouldHaveSingleItem();
            charge.Kind.ShouldBe(PriceBreakdownLineKind.Charge);
            charge.Amount.Amount.ShouldBe(12.50m);
        }
        else
        {
            context.Lines.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Tax_is_calculated_per_rate_on_the_sum_of_the_rounded_lines()
    {
        // Per line the 9% tax would be 0.09 twice (0.18); on the summed lines it is 0.19.
        var breakdown = Price(new PricingInputBuilder().WithLines(
            new PricingLine("SKU-1", 1, 1.05m, PricingInputBuilder.ReducedRate),
            new PricingLine("SKU-2", 1, 1.05m, PricingInputBuilder.ReducedRate)));

        var tax = breakdown.Lines.Where(line => line.Kind == PriceBreakdownLineKind.Tax).ShouldHaveSingleItem();
        tax.Amount.Amount.ShouldBe(0.19m);
        tax.TaxRate.ShouldBe(PricingInputBuilder.ReducedRate);
        tax.Description.ShouldBe("VAT 9%");
    }

    [Fact]
    public void Each_tax_rate_gets_its_own_line()
    {
        var breakdown = Price(new PricingInputBuilder().WithLines(
            new PricingLine("SKU-1", 2, 10.00m, PricingInputBuilder.StandardRate),
            new PricingLine("SKU-2", 2, 1.05m, PricingInputBuilder.ReducedRate)));

        breakdown.Lines
            .Where(line => line.Kind == PriceBreakdownLineKind.Tax)
            .Select(line => (line.TaxRate, line.Amount.Amount))
            .ShouldBe([(PricingInputBuilder.ReducedRate, 0.19m), (PricingInputBuilder.StandardRate, 4.20m)]);
    }

    [Fact]
    public void The_shipping_charge_is_taxed_with_the_lines_at_its_rate()
    {
        var breakdown = Price(new PricingInputBuilder()
            .WithLines(new PricingLine("SKU-1", 1, 100.00m, PricingInputBuilder.StandardRate))
            .WithShippingCharge(new ShippingCharge(10.00m, PricingInputBuilder.StandardRate))
            .WithFlag(PricingFeatureFlags.ShippingCharge, enabled: true));

        var tax = breakdown.Lines.Where(line => line.Kind == PriceBreakdownLineKind.Tax).ShouldHaveSingleItem();
        tax.Amount.Amount.ShouldBe(23.10m);
    }

    [Fact]
    public void A_reverse_charge_customer_is_invoiced_without_tax()
    {
        var breakdown = Price(new PricingInputBuilder().WithTaxProfile(new TaxProfile("DE", ReverseCharge: true)));

        breakdown.TaxTreatment.ShouldBe(TaxTreatment.ReverseCharge);
        breakdown.Lines.ShouldNotContain(line => line.Kind == PriceBreakdownLineKind.Tax);
        breakdown.Tax.Amount.ShouldBe(0m);
        breakdown.Total.ShouldBe(breakdown.Net);
    }

    [Fact]
    public void A_line_without_tax_does_not_produce_a_tax_line()
    {
        var breakdown = Price(new PricingInputBuilder()
            .WithLines(new PricingLine("SKU-1", 1, 50.00m, 0m)));

        breakdown.Lines.ShouldNotContain(line => line.Kind == PriceBreakdownLineKind.Tax);
    }

    private static PricingContext Apply(IPricingRule rule, PricingInputBuilder builder)
    {
        var context = new PricingContext(builder.Build());
        rule.Apply(context);
        return context;
    }

    private static PriceBreakdown Price(PricingInputBuilder builder) =>
        new PricingPipeline(PricingRules.V1).Price(builder.Build());
}
