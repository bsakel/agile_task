using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Customers.Contracts;
using OrderPlatform.Ordering.Application;
using OrderPlatform.Ordering.Domain;
using OrderPlatform.Pricing.Contracts;
using OrderPlatform.Testing;
using Wolverine;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2f: <c>POST /v1/orders</c> prices the order against the seeded price lists, starts the event stream and answers
/// with the state and the breakdown the order is locked at (ADR-0017 §3, ADR-0018, ADR-0020).
/// </summary>
public sealed class SubmitOrderTests(PlatformFixture platform)
{
    /// <summary>The seeded personal data of ACME: none of it may appear in a stored order event (ADR-0016).</summary>
    private static readonly string[] AcmePersonalData =
    [
        "ACME Industries B.V.",
        "Pat Buyer",
        "portal.user@acme.example",
        "+31 20 555 0101",
        "Keizersgracht 1",
        "Amsterdam",
        "1015 CJ",
    ];

    [Fact]
    public async Task Submitting_an_order_returns_201_with_its_state_and_price_breakdown()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-1000", 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await response.ReadOrderAsync();
        order.OrderId.ShouldNotBe(Guid.Empty);
        order.Status.ShouldBe(nameof(OrderStatus.ValidatingInventory));
        response.Headers.Location!.ToString().ShouldEndWith($"/v1/orders/{order.OrderId}");

        // ACME's own price list: 8.50 per unit, so 17.00 for the line, and the version names both lists (PR 2c).
        var line = order.Pricing.Lines.Where(line => line.Kind == PriceLineKind.Line).ShouldHaveSingleItem();
        line.Sku.ShouldBe("SKU-1000");
        line.UnitPrice!.Value.Amount.ShouldBe(8.50m);
        line.Amount.Amount.ShouldBe(17.00m);
        order.Pricing.PriceListVersion.ShouldBe("base-2026-09+acme-2026-09");
        order.Pricing.Total.Amount.ShouldBeGreaterThan(order.Pricing.Net.Amount);
    }

    [Fact]
    public async Task An_unknown_sku_returns_422_unknown_product()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-NOT-PRICED", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe(PricingErrors.UnknownProductCode);
    }

    /// <summary>
    /// Only an active account may order (README §2). The realm has no client for an inactive account, so the rule is
    /// proven on the command with an account this test owns; that its <see cref="ErrorKind.BusinessRule"/> becomes a
    /// <c>422</c> is proven by <see cref="An_unknown_sku_returns_422_unknown_product"/> and the problem details tests.
    /// </summary>
    [Fact]
    public async Task An_inactive_account_cannot_submit_an_order()
    {
        var accountId = await SuspendedAccountAsync();
        using var scope = platform.Api.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var result = await bus.InvokeAsync<Result<SubmitOrderResponse>>(
            new SubmitOrder(accountId, [new SubmitOrderLine("SKU-1000", 1)]),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("account-not-active");
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        result.Error.Message.ShouldContain(nameof(CustomerAccountStatus.Suspended));
    }

    [Fact]
    public async Task Repeating_a_submission_with_the_same_key_returns_the_original_order_and_starts_no_second_stream()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var key = EchoRequests.NewKey();
        var body = OrderRequests.OneLine("SKU-1000", 3);

        using var first = await client.PostOrderAsync(key, body);
        var streamsAfterFirst = await CountSubmittedOrdersAsync();
        using var repeat = await client.PostOrderAsync(key, body);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await repeat.ReadOrderAsync()).OrderId.ShouldBe((await first.ReadOrderAsync()).OrderId);
        (await CountSubmittedOrdersAsync()).ShouldBe(streamsAfterFirst);
    }

    /// <summary>
    /// ADR-0016: events carry ids, never personal data. Checked against the stored JSON, because that is what lands in
    /// the event store, projections and any future export.
    /// </summary>
    [Fact]
    public async Task A_stored_order_event_contains_no_personal_data()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostOrderAsync(OrderRequests.OneLine("SKU-1000", 1));
        var order = await response.ReadOrderAsync();

        var stored = await StoredEventAsync(order.OrderId);
        foreach (var personal in AcmePersonalData)
        {
            stored.ShouldNotContain(personal, Case.Insensitive);
        }

        // The ids that replace it are there, so the event is still complete (ADR-0017 §3).
        stored.ShouldContain(KeycloakTokens.AcmeAccountId);
        stored.ShouldContain("11111111-1111-4111-8111-11111111110c");
    }

    /// <summary>An account this test owns, so suspending it cannot affect the seeded accounts other tests use.</summary>
    private async Task<Guid> SuspendedAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            insert into customers.accounts (
                id, name, status, tax_country_code, tax_vat_number, tax_reverse_charge,
                billing_address_id, shipping_address_id, primary_contact_id)
            select @id, 'Suspended Test B.V.', 'suspended', tax_country_code, tax_vat_number, tax_reverse_charge,
                billing_address_id, shipping_address_id, primary_contact_id
            from customers.accounts where id = @template
            """,
            new NpgsqlParameter("id", accountId),
            new NpgsqlParameter("template", Guid.Parse(KeycloakTokens.AcmeAccountId)));

        return accountId;
    }

    private Task<long> CountSubmittedOrdersAsync() =>
        ScalarAsync<long>("select count(*) from ordering.mt_events where type = 'order_submitted'");

    private Task<string> StoredEventAsync(Guid orderId) => ScalarAsync<string>(
        "select data::text from ordering.mt_events where stream_id = @stream and type = 'order_submitted'",
        new NpgsqlParameter("stream", orderId));

    private async Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(platform.Containers.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(platform.Containers.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
