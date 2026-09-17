using System.Net.Http.Json;
using System.Text;

namespace OrderPlatform.Api.IntegrationTests.Infrastructure;

/// <summary>Requests to the development-only diagnostics endpoint (removed with it in Phase 2).</summary>
internal static class EchoRequests
{
    public const string Path = "/v1/_diagnostics/echo";

    public static Task<HttpResponseMessage> PostEchoAsync(this HttpClient client, string? idempotencyKey, string json)
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

    public static Task<HttpResponseMessage> PostEchoAsync(this HttpClient client, string json) =>
        client.PostEchoAsync(NewKey(), json);

    public static string NewKey() => "test-" + Guid.NewGuid().ToString("N");

    public static async Task<ProblemResponse> ReadProblemAsync(this HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return (await response.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken))!;
    }
}

internal sealed record ProblemResponse(string? Type, string? Title, int? Status, string? Detail, string? ErrorCode, string? TraceId);

internal sealed record EchoResult(Guid EchoId, Guid AccountId, string Message, DateTimeOffset ProcessedAt, Dictionary<string, bool> FeatureFlags);
