namespace OrderPlatform.Pricing.Domain.Rules;

/// <summary>Stage 1: unit price × quantity per order line, rounded per line (ADR-0018).</summary>
public sealed class LineSubtotalRule : IPricingRule
{
    public PricingStage Stage => PricingStage.Subtotal;

    public void Apply(PricingContext context)
    {
        foreach (var line in context.Input.Lines)
        {
            context.Add(new PriceBreakdownLine(
                PriceBreakdownLineKind.Line,
                $"{line.Sku} × {line.Quantity}",
                PricingRounding.RoundToMoney(line.UnitPrice * line.Quantity),
                line.TaxRate)
            {
                Sku = line.Sku,
            });
        }
    }
}
