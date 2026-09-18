namespace OrderPlatform.Pricing.Domain;

/// <summary>
/// The fixed stages of the pricing pipeline. Stages always run in this order; within a stage, rules run in the order
/// they were registered (ADR-0018).
/// </summary>
public enum PricingStage
{
    Subtotal = 1,
    Discounts = 2,
    Charges = 3,
    Tax = 4,
}

/// <summary>
/// One pricing rule. A rule adds lines to the breakdown and never modifies lines added by earlier rules, so a new charge
/// is a new rule class plus its registration (ADR-0018).
/// </summary>
public interface IPricingRule
{
    PricingStage Stage { get; }

    void Apply(PricingContext context);
}

/// <summary>The input and the lines produced so far. Later rules read earlier lines; they cannot change them.</summary>
public sealed class PricingContext(PricingInput input)
{
    private readonly List<PriceBreakdownLine> lines = [];

    public PricingInput Input { get; } = input;

    public IReadOnlyList<PriceBreakdownLine> Lines => lines;

    public TaxTreatment TaxTreatment { get; private set; } = TaxTreatment.Standard;

    public void Add(PriceBreakdownLine line) => lines.Add(line);

    /// <summary>Records that this order is not taxed by us because the customer accounts for the VAT (reverse charge).</summary>
    public void UseReverseCharge() => TaxTreatment = TaxTreatment.ReverseCharge;

    /// <summary>
    /// A flag decision taken for this pricing run. A flag without a decision is off, because off is always the existing
    /// behaviour (ADR-0019).
    /// </summary>
    public bool IsEnabled(string flag) => Input.FlagDecisions.TryGetValue(flag, out var enabled) && enabled;
}

/// <summary>
/// The rules of pricing v1 in registration order. The module registers exactly this list, and a test pins the order, so
/// adding a rule is a visible decision rather than a side effect of dependency injection order (ADR-0018).
/// </summary>
public static class PricingRules
{
    public static IReadOnlyList<IPricingRule> V1 =>
    [
        new Rules.LineSubtotalRule(),
        new Rules.ShippingChargeRule(),
        new Rules.TaxRule(),
    ];
}

/// <summary>
/// Runs the registered rules stage by stage and returns the breakdown. The flag decisions and the price list version of
/// the input are carried over, so the stored breakdown explains itself later (ADR-0018, ADR-0019).
/// </summary>
public sealed class PricingPipeline(IEnumerable<IPricingRule> rules)
{
    /// <summary>The rules in execution order: by stage, then by registration order within the stage.</summary>
    public IReadOnlyList<IPricingRule> Rules { get; } = [.. rules.OrderBy(rule => rule.Stage)];

    public PriceBreakdown Price(PricingInput input)
    {
        var context = new PricingContext(input);

        foreach (var rule in Rules)
        {
            rule.Apply(context);
        }

        return new PriceBreakdown(context.Lines, context.TaxTreatment, input.PriceListVersion, input.FlagDecisions);
    }
}
