using System.Net;
using OrderPlatform.Api.IntegrationTests.Infrastructure;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>PR 1b criterion: a repeated key returns the stored response; the same key with a different body returns 422 (ADR-0020).</summary>
public sealed class IdempotencyTests(PlatformFixture platform)
{
    [Fact]
    public async Task Repeating_a_request_with_the_same_key_returns_the_stored_response()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var key = EchoRequests.NewKey();

        using var first = await client.PostEchoAsync(key, """{"message":"once"}""");
        using var repeat = await client.PostEchoAsync(key, """{"message":"once"}""");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        repeat.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await repeat.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_key_with_a_different_body_returns_422()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var key = EchoRequests.NewKey();

        using var first = await client.PostEchoAsync(key, """{"message":"once"}""");
        using var different = await client.PostEchoAsync(key, """{"message":"twice"}""");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        different.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await different.ReadProblemAsync()).ErrorCode.ShouldBe("idempotency-key-reused");
    }

    [Fact]
    public async Task Repeat_while_the_first_request_is_in_progress_returns_409()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var key = EchoRequests.NewKey();
        const string slow = """{"message":"slow","simulate":"Slow"}""";

        var first = client.PostEchoAsync(key, slow);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        using var repeat = await client.PostEchoAsync(key, slow);
        using var firstResponse = await first;

        repeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await repeat.ReadProblemAsync()).ErrorCode.ShouldBe("idempotency-key-in-progress");
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Keys_are_scoped_per_account()
    {
        var key = EchoRequests.NewKey();
        using var acme = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        using var globex = platform.Api.CreateClient(await platform.Tokens.GlobexProcurementAsync());

        using var acmeResponse = await acme.PostEchoAsync(key, """{"message":"same"}""");
        using var globexResponse = await globex.PostEchoAsync(key, """{"message":"same"}""");

        globexResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        globexResponse.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await globexResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotBe(await acmeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_key_returns_400()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostEchoAsync(idempotencyKey: null, """{"message":"x"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("idempotency-key-required");
    }

    [Fact]
    public async Task Failed_request_releases_the_key_so_a_retry_executes_again()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());
        var key = EchoRequests.NewKey();

        using var failed = await client.PostEchoAsync(key, """{"message":"x","simulate":"Exception"}""");
        using var retried = await client.PostEchoAsync(key, """{"message":"x","simulate":"Exception"}""");

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retried.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }
}
