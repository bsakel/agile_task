using System.Globalization;

namespace OrderPlatform.Pricing.Domain.Rules;

/// <summary>
/// Stage 4: tax per tax rate. The rate is applied to the sum of the already rounded lines and charges at that rate and
/// the result is rounded once — not per line (ADR-0018). A reverse-charge customer accounts for the VAT themselves, so
/// no tax is added and the treatment is recorded on the breakdown (README §2).
/// </summary>
public sealed class TaxRule : IPricingRule
{
    public PricingStage Stage => PricingStage.Tax;

    public void Apply(PricingContext context)
    {
        if (context.Input.TaxProfile.ReverseCharge)
        {
            context.UseReverseCharge();
            return;
        }

        var taxable = context.Lines
            .Where(line => line.Kind != PriceBreakdownLineKind.Tax && line.TaxRate > 0m)
            .GroupBy(line => line.TaxRate)
            .OrderBy(group => group.Key);

        foreach (var rate in taxable)
        {
            var baseAmount = rate.Sum(line => line.Amount.Amount);

            context.Add(new PriceBreakdownLine(
                PriceBreakdownLineKind.Tax,
                $"VAT {(rate.Key * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%",
                PricingRounding.RoundToMoney(baseAmount * rate.Key),
                rate.Key));
        }
    }
}
