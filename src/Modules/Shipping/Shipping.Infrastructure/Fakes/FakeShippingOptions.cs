namespace OrderPlatform.Shipping.Infrastructure.Fakes;

/// <summary>What the fake shipping system reports when a requested shipment is polled.</summary>
public enum FakeShipmentOutcome
{
    Dispatched,
    Delivered,
    Failed,
}

/// <summary>
/// Controls the fake shipping system. Bound from <c>Integrations:Shipping:Fake</c> so a local environment can drive a
/// dispatched, delivered or failed shipment without code changes; tests set the properties directly (ADR-0014).
/// </summary>
public sealed class FakeShippingOptions
{
    /// <summary>Outcome reported for a requested shipment when its status is polled.</summary>
    public FakeShipmentOutcome Outcome { get; set; } = FakeShipmentOutcome.Dispatched;

    /// <summary>Reason reported with a <see cref="FakeShipmentOutcome.Failed"/> outcome.</summary>
    public string FailureReason { get; set; } = "carrier-rejected-the-shipment";
}
