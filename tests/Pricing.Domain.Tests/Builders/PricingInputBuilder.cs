namespace OrderPlatform.Pricing.Domain.Tests.Builders;

/// <summary>Valid pricing input with one standard-rated line; every part is replaceable (tests/README conventions).</summary>
public sealed class PricingInputBuilder
{
    public const decimal StandardRate = 0.21m;
    public const decimal ReducedRate = 0.09m;

    private readonly List<PricingLine> lines = [new("SKU-1", 2, 10.00m, StandardRate)];
    private TaxProfile taxProfile = new("NL", ReverseCharge: false);
    private ShippingCharge shippingCharge = new(12.50m, StandardRate);
    private string priceListVersion = "base-2026-09";
    private Dictionary<string, bool> flagDecisions = [];

    public PricingInputBuilder WithLines(params PricingLine[] value)
    {
        lines.Clear();
        lines.AddRange(value);
        return this;
    }

    public PricingInputBuilder WithTaxProfile(TaxProfile value)
    {
        taxProfile = value;
        return this;
    }

    public PricingInputBuilder WithShippingCharge(ShippingCharge value)
    {
        shippingCharge = value;
        return this;
    }

    public PricingInputBuilder WithPriceListVersion(string value)
    {
        priceListVersion = value;
        return this;
    }

    public PricingInputBuilder WithFlag(string flag, bool enabled)
    {
        flagDecisions[flag] = enabled;
        return this;
    }

    public PricingInputBuilder WithoutFlagDecisions()
    {
        flagDecisions = [];
        return this;
    }

    public PricingInput Build() => new(lines, taxProfile, shippingCharge, priceListVersion, flagDecisions);
}
