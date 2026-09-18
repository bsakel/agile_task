using OrderPlatform.Pricing.Domain.Rules;
using OrderPlatform.Pricing.Domain.Tests.Builders;

namespace OrderPlatform.Pricing.Domain.Tests;

/// <summary>Stage order, the pinned v1 rule registration and what the pipeline carries over (ADR-0018).</summary>
public sealed class PricingPipelineTests
{
    [Fact]
    public void The_registered_v1_rules_are_pinned_in_order()
    {
        PricingRules.V1.Select(rule => rule.GetType()).ShouldBe(
            [typeof(LineSubtotalRule), typeof(ShippingChargeRule), typeof(TaxRule)]);
    }

    [Fact]
    public void Rules_run_stage_by_stage_whatever_order_they_were_registered_in()
    {
        var tax = new RecordingRule(PricingStage.Tax);
        var subtotal = new RecordingRule(PricingStage.Subtotal);
        var charge = new RecordingRule(PricingStage.Charges);
        var discount = new RecordingRule(PricingStage.Discounts);
        var executed = new List<PricingStage>();
        foreach (var rule in new[] { tax, subtotal, charge, discount })
        {
            rule.Executed = executed;
        }

        new PricingPipeline([tax, subtotal, charge, discount]).Price(new PricingInputBuilder().Build());

        executed.ShouldBe([PricingStage.Subtotal, PricingStage.Discounts, PricingStage.Charges, PricingStage.Tax]);
    }

    [Fact]
    public void Rules_of_the_same_stage_keep_their_registration_order()
    {
        var executed = new List<PricingStage>();
        var first = new RecordingRule(PricingStage.Charges) { Executed = executed, Description = "first" };
        var second = new RecordingRule(PricingStage.Charges) { Executed = executed, Description = "second" };

        var breakdown = new PricingPipeline([first, second]).Price(new PricingInputBuilder().Build());

        breakdown.Lines.Select(line => line.Description).ShouldBe(["first", "second"]);
    }

    [Fact]
    public void The_breakdown_records_the_price_list_version_and_the_flag_decisions()
    {
        var input = new PricingInputBuilder()
            .WithPriceListVersion("acme-2026-09")
            .WithFlag(PricingFeatureFlags.ShippingCharge, enabled: true)
            .Build();

        var breakdown = new PricingPipeline(PricingRules.V1).Price(input);

        breakdown.PriceListVersion.ShouldBe("acme-2026-09");
        breakdown.FlagDecisions[PricingFeatureFlags.ShippingCharge].ShouldBeTrue();
    }

    [Fact]
    public void A_flag_without_a_decision_is_off()
    {
        var input = new PricingInputBuilder().WithoutFlagDecisions().Build();

        new PricingContext(input).IsEnabled(PricingFeatureFlags.ShippingCharge).ShouldBeFalse();
    }

    [Fact]
    public void Totals_are_the_sum_of_the_lines_and_the_tax()
    {
        var input = new PricingInputBuilder()
            .WithLines(new PricingLine("SKU-1", 2, 10.00m, PricingInputBuilder.StandardRate))
            .WithFlag(PricingFeatureFlags.ShippingCharge, enabled: true)
            .WithShippingCharge(new ShippingCharge(12.50m, PricingInputBuilder.StandardRate))
            .Build();

        var breakdown = new PricingPipeline(PricingRules.V1).Price(input);

        breakdown.Net.Amount.ShouldBe(32.50m);
        breakdown.Tax.Amount.ShouldBe(6.83m);
        breakdown.Total.Amount.ShouldBe(39.33m);
        breakdown.Total.Currency.ShouldBe("EUR");
    }

    private sealed class RecordingRule(PricingStage stage) : IPricingRule
    {
        public PricingStage Stage => stage;

        public List<PricingStage> Executed { get; set; } = [];

        public string Description { get; init; } = "recorded";

        public void Apply(PricingContext context)
        {
            Executed.Add(stage);
            context.Add(new PriceBreakdownLine(PriceBreakdownLineKind.Charge, Description, BuildingBlocks.Money.InEur(0m), 0m));
        }
    }
}
