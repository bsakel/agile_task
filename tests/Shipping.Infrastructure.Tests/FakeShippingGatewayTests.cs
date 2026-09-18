using Microsoft.Extensions.Time.Testing;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Shipping.Application;
using OrderPlatform.Shipping.Infrastructure.Fakes;

namespace OrderPlatform.Shipping.Infrastructure.Tests;

/// <summary>
/// The fake fulfilment/shipping system behind <see cref="IShippingGateway"/> (PR 3b): the controllable outcomes, the
/// idempotency key that prevents a second shipment, and the failures the port returns instead of throwing (ADR-0014).
/// </summary>
public sealed class FakeShippingGatewayTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = new("2b0a1b6e-2b0a-4b6e-8b0a-1b6e2b0a1b6e");

    private readonly FakeTimeProvider clock = new(RequestedAt);
    private readonly FakeShippingOptions options = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(FakeShipmentOutcome.Dispatched, ShipmentState.Dispatched)]
    [InlineData(FakeShipmentOutcome.Delivered, ShipmentState.Delivered)]
    [InlineData(FakeShipmentOutcome.Failed, ShipmentState.Failed)]
    public async Task Status_of_a_requested_shipment_reports_the_configured_outcome(FakeShipmentOutcome outcome, ShipmentState expected)
    {
        options.Outcome = outcome;
        var gateway = Gateway();
        var shipment = await RequestAsync(gateway);
        clock.Advance(TimeSpan.FromHours(4));

        var status = Succeeded(await gateway.GetShipmentStatusAsync(shipment.ShipmentId, Cancellation));

        status.State.ShouldBe(expected);
        status.LastChangedAt.ShouldBe(RequestedAt.AddHours(4));
        status.FailureReason.ShouldBe(expected == ShipmentState.Failed ? options.FailureReason : null);
        (status.TrackingCode is null).ShouldBe(expected == ShipmentState.Failed);
    }

    [Fact]
    public async Task Repeating_the_idempotency_key_does_not_request_a_second_shipment()
    {
        var gateway = Gateway();
        var first = await RequestAsync(gateway);

        var repeated = Succeeded(await gateway.RequestShipmentAsync(Request(), Cancellation));

        repeated.ShipmentId.ShouldBe(first.ShipmentId);
        gateway.Shipments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_request_with_another_idempotency_key_is_a_second_shipment()
    {
        var gateway = Gateway();
        var first = await RequestAsync(gateway);

        var second = Succeeded(await gateway.RequestShipmentAsync(Request(key: "ship:order:2"), Cancellation));

        second.ShipmentId.ShouldNotBe(first.ShipmentId);
        gateway.Shipments.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cancelling_an_unknown_shipment_request_returns_a_failure()
    {
        var gateway = Gateway();

        var error = Failed(await gateway.CancelShipmentRequestAsync("SHP-999999", "cancel-shipment:order", Cancellation));

        error.Code.ShouldBe("shipment-unknown");
        error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task A_shipment_request_is_cancelled_while_it_is_not_dispatched()
    {
        var gateway = Gateway();
        var shipment = await RequestAsync(gateway);

        var cancelled = Succeeded(await gateway.CancelShipmentRequestAsync(shipment.ShipmentId, "cancel-shipment:order", Cancellation));
        var status = Succeeded(await gateway.GetShipmentStatusAsync(shipment.ShipmentId, Cancellation));

        cancelled.State.ShouldBe(ShipmentState.Cancelled);
        status.State.ShouldBe(ShipmentState.Cancelled);
    }

    [Fact]
    public async Task A_dispatched_shipment_can_no_longer_be_cancelled()
    {
        var gateway = Gateway();
        var shipment = await RequestAsync(gateway);
        Succeeded(await gateway.GetShipmentStatusAsync(shipment.ShipmentId, Cancellation));

        var error = Failed(await gateway.CancelShipmentRequestAsync(shipment.ShipmentId, "cancel-shipment:order", Cancellation));

        error.Code.ShouldBe("shipment-already-dispatched");
        error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task The_outcome_query_returns_the_shipment_of_an_earlier_call()
    {
        var gateway = Gateway();
        var shipment = await RequestAsync(gateway);

        var outcome = Succeeded(await gateway.GetOutcomeAsync("ship:order:1", Cancellation));

        outcome.ShipmentId.ShouldBe(shipment.ShipmentId);
    }

    [Fact]
    public async Task The_outcome_query_fails_for_a_key_the_provider_never_saw()
    {
        var gateway = Gateway();

        var error = Failed(await gateway.GetOutcomeAsync("ship:order:1", Cancellation));

        error.Code.ShouldBe("shipment-request-unknown");
    }

    private FakeShippingGateway Gateway() => new(options, clock);

    private static ShipmentRequest Request(string key = "ship:order:1") => new(
        OrderId, key, Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
        [new ShipmentLine("SKU-1", 2)]);

    private static async Task<Shipment> RequestAsync(FakeShippingGateway gateway, string key = "ship:order:1")
    {
        var requested = Succeeded(await gateway.RequestShipmentAsync(Request(key), Cancellation));

        requested.State.ShouldBe(ShipmentState.Requested);
        requested.LastChangedAt.ShouldBe(RequestedAt);
        return requested;
    }

    private static Shipment Succeeded(Result<Shipment> result)
    {
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value!;
    }

    private static Error Failed(Result<Shipment> result)
    {
        result.IsSuccess.ShouldBeFalse();
        return result.Error!;
    }
}
