using System.Net;
using System.Net.Http.Json;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.BuildingBlocks;
using Npgsql;
using OrderPlatform.Inventory.Application.Integrations;
using OrderPlatform.Inventory.Contracts;
using OrderPlatform.Ordering.Application;
using OrderPlatform.Ordering.Domain;
using Wolverine;
using InventoryReservedMessage = OrderPlatform.Inventory.Contracts.InventoryReserved;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2j: cancelling an order undoes what its state had done (ADR-0017 §5), a state past fulfilment is refused with the
/// state and a support hint (§3), and a reservation that completes after the cancel is released (§6).
/// </summary>
public sealed class CancelOrderTests(PlatformFixture platform)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_reserved_order_is_cancelled_and_its_stock_released()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var order = await SubmitAsync(client);
        // Cancel only once the reservation exists, so there is stock to give back (ADR-0017 §5).
        await WaitForStatusAsync(platform.Api, order.OrderId, OrderStatus.Invoicing);

        using var response = await CancelAsync(client, order.OrderId);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ReadCancelAsync(response)).Status.ShouldBe(nameof(OrderStatus.Cancelled));
        await WaitForStatusAsync(platform.Api, order.OrderId, OrderStatus.Cancelled);
        await WaitUntilReleasedAsync(order.OrderId);
    }

    /// <summary>
    /// The stream is seeded straight into <see cref="OrderStatus.Processing"/>: the events that reach it come with the
    /// invoicing and payment steps of next steps N3 and N4, but the rule they gate exists now.
    /// </summary>
    [Fact]
    public async Task An_order_in_processing_is_refused_with_its_state_and_a_support_hint()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var order = await SubmitAsync(client);
        await WaitForStatusAsync(platform.Api, order.OrderId, OrderStatus.Invoicing);
        await SeedProcessingAsync(platform.Api, order.OrderId);

        using var response = await CancelAsync(client, order.OrderId);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (await response.Content.ReadFromJsonAsync<CancelProblem>(TestContext.Current.CancellationToken))!;
        problem.ErrorCode.ShouldBe("order-not-cancellable");
        problem.CurrentState.ShouldBe(nameof(OrderStatus.Processing));
        problem.Detail.ShouldNotBeNull().ShouldContain("support");
    }

    /// <summary>ADR-0017 §6: the reservation lost the race with the cancellation, so it is given straight back.</summary>
    [Fact]
    public async Task A_reservation_that_completes_after_the_cancel_releases_the_stock()
    {
        // The order is cancelled while it waits for the customer, so nothing is reserved yet; the reservation below
        // then reaches an order that is already cancelled, which is the race of §6.
        var api = platform.ApiWith(new Dictionary<string, string?>
        {
            ["Integrations:Inventory:Fake:UnavailableSkus:0"] = "SKU-1001",
        });
        using var client = api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var order = await SubmitAsync(client, "SKU-1001");
        await WaitForStatusAsync(api, order.OrderId, OrderStatus.AwaitingCustomer);

        using var cancelled = await CancelAsync(client, order.OrderId);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await WaitForStatusAsync(api, order.OrderId, OrderStatus.Cancelled);

        var lateKey = $"reserve:{order.OrderId}:late";
        await ReserveAsync(api, lateKey);
        await PublishReservedAsync(api, order.OrderId, lateKey);

        await WaitUntilReleasedAsync(order.OrderId);
        (await LoadAsync(api, order.OrderId)).Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task An_order_of_another_account_is_not_found()
    {
        using var acme = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        using var globex = platform.Api.CreateClient(await platform.Tokens.GlobexProcurementAsync());
        var order = await SubmitAsync(acme);

        using var response = await CancelAsync(globex, order.OrderId);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("resource-not-found");
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid orderId) =>
        client.PostActionAsync($"/v1/orders/{orderId}/cancel");

    private static async Task<CancelResponse> ReadCancelAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CancelResponse>(TestContext.Current.CancellationToken))!;

    private static async Task<SubmittedOrder> SubmitAsync(HttpClient client, string sku = "SKU-1000")
    {
        using var response = await client.PostOrderAsync(OrderRequests.OneLine(sku, 2));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.ReadOrderAsync();
    }

    /// <summary>Puts the order in Processing by appending the events that lead there, as the handlers of N3/N4 will.</summary>
    private static async Task SeedProcessingAsync(OrderPlatformApiFactory api, Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        await using var session = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession();

        var now = DateTimeOffset.UtcNow;
        session.Events.Append(
            orderId,
            new InvoiceIssued(orderId, "INV-TEST-0001", Money.InEur(20.57m), now.AddDays(3), now),
            new InvoicePaid(orderId, "INV-TEST-0001", now));

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Holds stock under a key of our own, so the late reservation below has something to give back.</summary>
    private static async Task ReserveAsync(OrderPlatformApiFactory api, string reservationKey)
    {
        using var scope = api.Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryGateway>();

        var result = await inventory.ReserveAsync(
            new InventoryReservationRequest(reservationKey, [new InventoryLine("SKU-1000", 1)]),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
    }

    /// <summary>Delivers the outcome the Inventory module would have published, without racing the real one.</summary>
    private static async Task PublishReservedAsync(OrderPlatformApiFactory api, Guid orderId, string reservationKey)
    {
        using var scope = api.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new InventoryReservedMessage(orderId, reservationKey, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Waits until the release of this order's reservation has been handled. The handler throws when the external
    /// system refuses, so a handled message means the stock really came back; that the gateway restores it is proven
    /// by the Inventory unit tests (PR 2h).
    /// </summary>
    /// <remarks>
    /// Observed in the durable message log rather than in the fake: the hosts of a test run share one database and so
    /// one set of durable queues, while each host has its own in-memory fake. Which node handles the release is
    /// therefore not decidable from the test, but the log is common to all of them (ADR-0005).
    /// </remarks>
    private async Task WaitUntilReleasedAsync(Guid orderId)
    {
        const string Sql = """
            select count(*)
            from wolverine.wolverine_incoming_envelopes
            where message_type = @type and status = 'Handled' and encode(body, 'escape') like @order
            """;

        var deadline = DateTimeOffset.UtcNow + Timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(platform.Containers.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(Sql, connection);
            command.Parameters.AddWithValue("type", typeof(ReleaseInventory).FullName!);
            command.Parameters.AddWithValue("order", $"%{orderId}%");

            if ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"The reservation of order {orderId} was not released within {Timeout}.");
    }

    private static async Task<Order> WaitForStatusAsync(OrderPlatformApiFactory api, Guid orderId, OrderStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        Order? order = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            order = await LoadAsync(api, orderId);
            if (order.Status == expected)
            {
                return order;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Order {orderId} was {order?.Status.ToString() ?? "not found"}, expected {expected}.");
    }

    private static async Task<Order> LoadAsync(OrderPlatformApiFactory api, Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        await using var session = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession();

        return await OrderStream.LoadAsync(session, orderId, TestContext.Current.CancellationToken);
    }

    private sealed record CancelResponse(Guid OrderId, string Status);

    private sealed record CancelProblem(string? Detail, string? ErrorCode, string? CurrentState);
}
