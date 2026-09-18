using Microsoft.Extensions.Configuration;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Inventory.Application.Integrations;
using OrderPlatform.Inventory.Infrastructure.Fakes;

namespace OrderPlatform.Inventory.Infrastructure.Tests;

/// <summary>The fake inventory adapter: all-or-nothing reservation, idempotency keys, release and configured failures (ADR-0014).</summary>
public sealed class FakeInventoryGatewayTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reserve_succeeds_when_every_line_is_available()
    {
        var gateway = Gateway();

        var result = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 4), new("SKU-2", 10)]), Cancellation);

        var reservation = Succeeded(result);
        reservation.Status.ShouldBe(InventoryReservationStatus.Reserved);
        reservation.UnavailableSkus.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reserve_holds_nothing_when_a_single_line_is_short()
    {
        var gateway = Gateway();

        var result = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 4), new("SKU-2", 11)]), Cancellation);

        var attempt = Succeeded(result);
        attempt.Status.ShouldBe(InventoryReservationStatus.Unavailable);
        attempt.UnavailableSkus.ShouldBe(["SKU-2"]);

        // The available line was not taken: another key can still reserve all of it.
        var other = await gateway.ReserveAsync(new("key-2", [new("SKU-1", 10)]), Cancellation);
        Succeeded(other).Status.ShouldBe(InventoryReservationStatus.Reserved);
    }

    [Fact]
    public async Task A_configured_unavailable_sku_is_never_reserved()
    {
        var gateway = Gateway(options => options.UnavailableSkus.Add("sku-2"));

        var result = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 1), new("SKU-2", 1)]), Cancellation);

        var attempt = Succeeded(result);
        attempt.Status.ShouldBe(InventoryReservationStatus.Unavailable);
        attempt.UnavailableSkus.ShouldBe(["SKU-2"]);
    }

    [Fact]
    public async Task Repeating_an_idempotency_key_does_not_reserve_twice()
    {
        var gateway = Gateway();

        var first = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 5)]), Cancellation);
        var repeat = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 5)]), Cancellation);

        Succeeded(repeat).ShouldBe(Succeeded(first));

        // Only five of the ten units were taken, so a different key still gets the other five.
        var other = await gateway.ReserveAsync(new("key-2", [new("SKU-1", 5)]), Cancellation);
        Succeeded(other).Status.ShouldBe(InventoryReservationStatus.Reserved);
    }

    [Fact]
    public async Task Release_restores_the_stock()
    {
        var gateway = Gateway();
        await gateway.ReserveAsync(new("key-1", [new("SKU-1", 10)]), Cancellation);

        var release = await gateway.ReleaseAsync(new("release-1", "key-1"), Cancellation);

        release.IsSuccess.ShouldBeTrue();
        var again = await gateway.ReserveAsync(new("key-2", [new("SKU-1", 10)]), Cancellation);
        Succeeded(again).Status.ShouldBe(InventoryReservationStatus.Reserved);
    }

    [Fact]
    public async Task Releasing_a_reservation_the_provider_does_not_know_is_a_failure()
    {
        var release = await Gateway().ReleaseAsync(new("release-1", "never-reserved"), Cancellation);

        release.IsSuccess.ShouldBeFalse();
        release.Error.Code.ShouldBe("inventory-unknown-reservation");
    }

    [Fact]
    public async Task A_configured_failure_is_returned_as_a_Result_failure()
    {
        var gateway = Gateway(options => options.FailStateChangingCalls = true);

        var reserve = await gateway.ReserveAsync(new("key-1", [new("SKU-1", 1)]), Cancellation);
        var release = await gateway.ReleaseAsync(new("release-1", "key-1"), Cancellation);

        reserve.IsSuccess.ShouldBeFalse();
        reserve.Error.Kind.ShouldBe(ErrorKind.Integration);
        reserve.Error.Code.ShouldBe("inventory-provider-unavailable");
        release.IsSuccess.ShouldBeFalse();
        release.Error.Code.ShouldBe("inventory-provider-unavailable");
    }

    [Fact]
    public async Task The_outcome_of_a_key_can_be_queried_and_an_unknown_key_is_a_failure()
    {
        var gateway = Gateway();
        await gateway.ReserveAsync(new("key-1", [new("SKU-1", 1)]), Cancellation);

        var known = await gateway.GetOutcomeAsync("key-1", Cancellation);
        var unknown = await gateway.GetOutcomeAsync("key-2", Cancellation);

        var outcome = Succeeded(known);
        outcome.Operation.ShouldBe(InventoryOperation.Reserve);
        outcome.Reservation!.Status.ShouldBe(InventoryReservationStatus.Reserved);
        unknown.IsSuccess.ShouldBeFalse();
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public void The_fake_is_configured_from_the_integration_section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Integrations:Inventory:Fake:Stock:SKU-1"] = "7",
            ["Integrations:Inventory:Fake:UnavailableSkus:0"] = "SKU-9",
            ["Integrations:Inventory:Fake:FailStateChangingCalls"] = "true",
        }).Build();

        var options = configuration.GetSection(FakeInventoryOptions.SectionName).Get<FakeInventoryOptions>().ShouldNotBeNull();

        options.Stock["SKU-1"].ShouldBe(7);
        options.UnavailableSkus.ShouldBe(["SKU-9"]);
        options.FailStateChangingCalls.ShouldBeTrue();
    }

    /// <summary>Asserts that a call succeeded and returns its value.</summary>
    private static T Succeeded<T>(Result<T> result) where T : class
    {
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value!;
    }

    /// <summary>Ten units of two SKUs and nothing else in stock.</summary>
    private static FakeInventoryGateway Gateway(Action<FakeInventoryOptions>? configure = null)
    {
        var options = new FakeInventoryOptions { DefaultStock = 0 };
        options.Stock["SKU-1"] = 10;
        options.Stock["SKU-2"] = 10;
        configure?.Invoke(options);
        return new FakeInventoryGateway(options);
    }
}
