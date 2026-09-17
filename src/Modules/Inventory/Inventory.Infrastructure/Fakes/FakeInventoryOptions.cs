namespace OrderPlatform.Inventory.Infrastructure.Fakes;

/// <summary>Configuration of <see cref="FakeInventoryGateway"/>, bound from <c>Integrations:Inventory:Fake</c>.</summary>
public sealed class FakeInventoryOptions
{
    public const string SectionName = "Integrations:Inventory:Fake";

    /// <summary>Units in stock per SKU. A SKU that is not listed has <see cref="DefaultStock"/> units.</summary>
    public Dictionary<string, int> Stock { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SKUs the provider always reports as unavailable, whatever the stock says.</summary>
    public List<string> UnavailableSkus { get; } = [];

    /// <summary>Units available for a SKU that <see cref="Stock"/> does not list.</summary>
    public int DefaultStock { get; set; } = 100;

    /// <summary>
    /// Makes every reserve and release fail like a provider outage, returned as a <c>Result</c> failure. The outcome query
    /// keeps answering, so the unknown-outcome flow of ADR-0014 can still be exercised.
    /// </summary>
    public bool FailStateChangingCalls { get; set; }
}
