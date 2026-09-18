using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Pricing.Domain;

/// <summary>What each breakdown line represents. The stage that produced it is implied by the kind (ADR-0018).</summary>
public enum PriceBreakdownLineKind
{
    Line,
    Discount,
    Charge,
    Tax,
}

/// <summary>
/// One line of the price breakdown. Amounts are already rounded by the rule that added them; tax lines carry the rate
/// they were calculated for.
/// </summary>
public sealed record PriceBreakdownLine(PriceBreakdownLineKind Kind, string Description, Money Amount, decimal TaxRate)
{
    /// <summary>Set for product lines, so the invoice and support can trace an amount back to the ordered SKU.</summary>
    public string? Sku { get; init; }
}

/// <summary>How the order is taxed. Recorded so the invoice and the customer see why tax is zero.</summary>
public enum TaxTreatment
{
    Standard,
    ReverseCharge,
}

/// <summary>
/// The calculated price of an order: every line the rules produced plus the totals. Stored on the order event, so the
/// invoice uses exactly these amounts and historic orders never change (ADR-0018).
/// </summary>
public sealed record PriceBreakdown(
    IReadOnlyList<PriceBreakdownLine> Lines,
    TaxTreatment TaxTreatment,
    string PriceListVersion,
    IReadOnlyDictionary<string, bool> FlagDecisions)
{
    /// <summary>Sum of lines, discounts and charges, before tax.</summary>
    public Money Net => Sum(line => line.Kind != PriceBreakdownLineKind.Tax);

    /// <summary>Sum of the tax lines.</summary>
    public Money Tax => Sum(line => line.Kind == PriceBreakdownLineKind.Tax);

    /// <summary>The amount the customer is invoiced.</summary>
    public Money Total => Money.InEur(Net.Amount + Tax.Amount);

    private Money Sum(Func<PriceBreakdownLine, bool> predicate) =>
        Money.InEur(Lines.Where(predicate).Sum(line => line.Amount.Amount));
}

/// <summary>
/// The rounding policy agreed with the billing system: 2 decimals, halves away from zero, applied per line and per tax
/// rate — never on an intermediate sum (ADR-0018).
/// </summary>
public static class PricingRounding
{
    public const int Decimals = 2;

    public static decimal Round(decimal amount) => Math.Round(amount, Decimals, MidpointRounding.AwayFromZero);

    public static Money RoundToMoney(decimal amount) => Money.InEur(Round(amount));
}
