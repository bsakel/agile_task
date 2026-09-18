namespace OrderPlatform.Pricing.Domain.Rules;

/// <summary>
/// Stage 3: the shipping charge, gated by the release flag <see cref="PricingFeatureFlags.ShippingCharge"/>. With the
/// flag off the order is priced without the charge, which is the behaviour before the flag was introduced (ADR-0019).
/// </summary>
public sealed class ShippingChargeRule : IPricingRule
{
    public PricingStage Stage => PricingStage.Charges;

    public void Apply(PricingContext context)
    {
        if (!context.IsEnabled(PricingFeatureFlags.ShippingCharge))
        {
            return;
        }

        var charge = context.Input.ShippingCharge;
        if (charge.Amount == 0m)
        {
            return;
        }

        context.Add(new PriceBreakdownLine(
            PriceBreakdownLineKind.Charge,
            "Shipping",
            PricingRounding.RoundToMoney(charge.Amount),
            charge.TaxRate));
    }
}
