using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.BuildingBlocks.Infrastructure.Json;
using OrderPlatform.Pricing.Contracts;

namespace OrderPlatform.Api.IntegrationTests.Infrastructure;

/// <summary>Requests to the Ordering endpoints (ADR-0017 §3).</summary>
internal static class OrderRequests
{
    public const string Path = "/v1/orders";

    /// <summary>Reads responses the way a client following the API's JSON conventions would (ADR-0020).</summary>
    private static readonly JsonSerializerOptions ClientOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new MoneyJsonConverter(), new UtcDateTimeOffsetJsonConverter() },
    };

    public static Task<HttpResponseMessage> PostOrderAsync(this HttpClient client, string? idempotencyKey, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> PostOrderAsync(this HttpClient client, string json) =>
        client.PostOrderAsync(EchoRequests.NewKey(), json);

    /// <summary>One line of the given SKU and quantity, as the endpoint expects it.</summary>
    public static string OneLine(string sku, int quantity) =>
        $$"""{"lines":[{"sku":"{{sku}}","quantity":{{quantity}}}]}""";

    public static async Task<SubmittedOrder> ReadOrderAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<SubmittedOrder>(ClientOptions, TestContext.Current.CancellationToken))!;

    /// <summary>The order as <c>GET /v1/orders/{id}</c> returns it.</summary>
    public static async Task<OrderResponse> ReadViewAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<OrderResponse>(ClientOptions, TestContext.Current.CancellationToken))!;
}

internal sealed record OrderResponse(
    Guid OrderId,
    string Status,
    IReadOnlyList<OrderLineResponse> Lines,
    OrderPricingResponse Pricing,
    string? InvoiceId,
    string? TrackingReference,
    DateTimeOffset? PaymentDueAt,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

internal sealed record OrderLineResponse(string Sku, int Quantity, Money UnitPrice);

internal sealed record OrderPricingResponse(Money Net, Money Tax, Money Total, string PriceListVersion, bool ReverseCharge);

/// <summary>The submission response as the customer receives it (<c>SubmitOrderResponse</c> over the wire).</summary>
internal sealed record SubmittedOrder(Guid OrderId, string Status, PriceBreakdown Pricing);
