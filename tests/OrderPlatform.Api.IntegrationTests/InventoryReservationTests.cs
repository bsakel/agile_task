using System.Net.Http.Headers;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Ordering.Application;
using OrderPlatform.Ordering.Domain;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2i: submitting an order sends <c>ReserveInventory</c> through the outbox, the Inventory module reserves in the
/// external system, and the outcome moves the order on (ADR-0005, ADR-0014, ADR-0017 §3). The steps are asynchronous,
/// so the tests poll for the state instead of asserting on a clock (tests/README conventions).
/// </summary>
public sealed class InventoryReservationTests(PlatformFixture platform)
{
    private const string UnavailableSku = "SKU-2000";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Available_stock_reserves_the_order_and_moves_it_to_invoicing()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-1000", 2));
        var order = await response.ReadOrderAsync();

        var reserved = await WaitForStatusAsync(platform.Api, order.OrderId, OrderStatus.Invoicing);
        reserved.ReservationKey.ShouldBe($"reserve:{order.OrderId}:1");
    }

    /// <summary>
    /// The fake is configured to report one SKU as unavailable, which is how ADR-0014 says an outcome is provoked
    /// without the external system: all-or-nothing, so the order goes back to the customer (README §2).
    /// </summary>
    [Fact]
    public async Task A_line_the_provider_cannot_supply_sends_the_order_back_to_the_customer()
    {
        var api = platform.ApiWith(new Dictionary<string, string?>
        {
            ["Integrations:Inventory:Fake:UnavailableSkus:0"] = UnavailableSku,
        });
        using var client = api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostOrderAsync(OrderRequests.OneLine(UnavailableSku, 1));
        var order = await response.ReadOrderAsync();

        var unavailable = await WaitForStatusAsync(api, order.OrderId, OrderStatus.AwaitingCustomer);
        unavailable.ReservationKey.ShouldBeNull();
    }

    /// <summary>
    /// The plan's phase criterion: one submission is traceable end to end — the request, the submit handler, the
    /// database, then the inventory handler and the Ordering handler that applies its outcome (ADR-0012).
    /// </summary>
    [Fact]
    public async Task One_submission_is_traced_from_the_request_through_the_outbox_to_the_order_update()
    {
        using var receiver = new OtlpReceiver();
        var baseAddress = new Uri($"http://127.0.0.1:{FreeTcpPort()}");
        var environment = new Dictionary<string, string?>(receiver.Settings)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = baseAddress.ToString(),
            ["ConnectionStrings__orderplatform"] = platform.Containers.ConnectionString,
            ["Authentication__Schemes__Bearer__Authority"] = platform.Containers.Authority,
            ["Authentication__Schemes__Bearer__RequireHttpsMetadata"] = "false",
            ["OTEL_SERVICE_NAME"] = "api",
        };

        await using var api = PlatformProcess.Start(PlatformFixture.ApiAssembly, environment);
        (await api.WaitUntilHealthyAsync(new Uri(baseAddress, "/health/ready"), TimeSpan.FromSeconds(90))).ShouldBeTrue(api.Output);

        using var client = new HttpClient { BaseAddress = baseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await platform.Tokens.AcmeErpAsync());
        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-1000", 1));
        response.EnsureSuccessStatusCode();

        (await receiver.WaitForTracesContainingAsync(
            Timeout,
            "POST /v1/orders",
            "OrderPlatform.Ordering.Application.SubmitOrder",
            "postgresql",
            "OrderPlatform.Inventory.Contracts.ReserveInventory",
            "OrderPlatform.Inventory.Contracts.InventoryReserved"))
            .ShouldBeTrue($"expected spans were not exported; received: {receiver.TracesText}");
    }

    /// <summary>Rebuilds the order from its stream, the way a handler sees it, until it reaches the expected state.</summary>
    private static async Task<Order> WaitForStatusAsync(OrderPlatformApiFactory api, Guid orderId, OrderStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        Order? order = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = api.Services.CreateScope();
            await using var session = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession();

            order = await OrderStream.LoadAsync(session, orderId, TestContext.Current.CancellationToken);
            if (order?.Status == expected)
            {
                return order;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Order {orderId} was {order?.Status.ToString() ?? "not found"} after {Timeout}, expected {expected}.");
    }

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
