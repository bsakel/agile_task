using System.Net;
using System.Net.Http.Json;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>PR 1b criterion: a Keycloak token calls the endpoint, no token gets 401, a token without the scope gets 403 (ADR-0016).</summary>
public sealed class AuthenticationTests(PlatformFixture platform)
{
    [Fact]
    public async Task Token_from_local_Keycloak_calls_the_diagnostics_endpoint()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.AcmeErpAsync());

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<EchoResult>(TestContext.Current.CancellationToken);
        echo!.AccountId.ShouldBe(Guid.Parse(KeycloakTokens.AcmeAccountId));
        echo.Message.ShouldBe("hello");
    }

    [Fact]
    public async Task Request_without_a_token_gets_401_problem_details()
    {
        using var client = platform.Api.CreateClient(accessToken: null);

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldStartWith("Bearer");
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task Request_with_an_invalid_token_gets_401()
    {
        using var client = platform.Api.CreateClient("not.a.token");

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_without_the_required_scope_gets_403()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.PortalUserAsync("orders:read"));

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).ErrorCode.ShouldBe("forbidden");
    }

    [Fact]
    public async Task Portal_user_with_the_required_scope_is_allowed()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.PortalUserAsync("orders:read orders:write"));

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Caller_without_a_customer_account_gets_403_on_customer_endpoints()
    {
        using var client = platform.Api.CreateClient(await platform.Tokens.SupportAgentAsync("orders:write"));

        using var response = await client.PostEchoAsync("""{"message":"hello"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
