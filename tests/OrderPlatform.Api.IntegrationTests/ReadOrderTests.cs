using System.Net;
using System.Net.Http.Json;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Ordering.Domain;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2g: an order is read back from the inline projection straight after it was submitted, its history comes from the
/// raw stream, and an order of another account is not found rather than forbidden (ADR-0006, ADR-0016, ADR-0020).
/// </summary>
public sealed class ReadOrderTests(PlatformFixture platform)
{
    [Fact]
    public async Task A_submitted_order_is_returned_with_its_state_items_and_breakdown()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var submitted = await SubmitAsync(client);

        using var response = await client.GetAsync($"/v1/orders/{submitted.OrderId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await OrderRequests.ReadViewAsync(response);
        order.OrderId.ShouldBe(submitted.OrderId);
        // Read straight after the write: the inline projection is already up to date, whatever the reservation did next.
        order.Status.ShouldBeOneOf(
            nameof(OrderStatus.ValidatingInventory),
            nameof(OrderStatus.Invoicing),
            nameof(OrderStatus.AwaitingCustomer));

        var line = order.Lines.ShouldHaveSingleItem();
        line.Sku.ShouldBe("SKU-1000");
        line.Quantity.ShouldBe(2);
        line.UnitPrice.Amount.ShouldBe(8.50m);
        order.Pricing.PriceListVersion.ShouldBe("base-2026-09+acme-2026-09");
        order.Pricing.Total.Amount.ShouldBe(submitted.Pricing.Total.Amount);
    }

    [Fact]
    public async Task The_history_lists_the_recorded_events_under_their_stored_names()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var submitted = await SubmitAsync(client);

        using var response = await client.GetAsync(
            $"/v1/orders/{submitted.OrderId}/history", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = (await response.Content.ReadFromJsonAsync<OrderHistoryResponse>(TestContext.Current.CancellationToken))!;
        history.OrderId.ShouldBe(submitted.OrderId);

        var first = history.Entries.First();
        first.Type.ShouldBe("order_submitted");
        first.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/history")]
    public async Task An_order_of_another_account_is_not_found(string suffix)
    {
        using var acme = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        using var globex = platform.Api.CreateClient(await platform.Tokens.GlobexProcurementAsync());
        var submitted = await SubmitAsync(acme);

        using var response = await globex.GetAsync(
            $"/v1/orders/{submitted.OrderId}{suffix}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("resource-not-found");
    }

    [Fact]
    public async Task An_order_that_does_not_exist_is_not_found()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.GetAsync($"/v1/orders/{Guid.CreateVersion7()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("resource-not-found");
    }

    private static async Task<SubmittedOrder> SubmitAsync(HttpClient client)
    {
        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-1000", 2));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadOrderAsync();
    }
}

internal sealed record OrderHistoryResponse(Guid OrderId, IReadOnlyList<OrderHistoryEntryResponse> Entries);

internal sealed record OrderHistoryEntryResponse(long Version, string Type, DateTimeOffset RecordedAt);
