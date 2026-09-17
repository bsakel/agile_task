using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OrderPlatform.Testing;

/// <summary>Access tokens for the clients and users of the local realm (deploy/keycloak/orderplatform-realm.json).</summary>
public sealed class KeycloakTokens(Uri authority)
{
    public const string AcmeAccountId = "0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01";
    public const string GlobexAccountId = "0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c02";

    private static readonly HttpClient Http = new();

    /// <summary>Customer system integration of ACME (client credentials, all order scopes).</summary>
    public Task<string> AcmeErpAsync() => ClientCredentialsAsync("acme-erp", "acme-erp-dev-secret");

    /// <summary>Customer system integration of Globex (client credentials, all order scopes).</summary>
    public Task<string> GlobexProcurementAsync() => ClientCredentialsAsync("globex-procurement", "globex-procurement-dev-secret");

    /// <summary>ACME portal user with only the requested scopes, e.g. <c>orders:read</c>.</summary>
    public Task<string> PortalUserAsync(string scopes) => PasswordAsync("portal.user", "portal-user-dev", scopes);

    /// <summary>Support agent (role <c>support-agent</c>, no account).</summary>
    public Task<string> SupportAgentAsync(string scopes) => PasswordAsync("support.agent", "support-agent-dev", scopes);

    private Task<string> ClientCredentialsAsync(string clientId, string secret) => RequestAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = secret,
    });

    // The password grant is enabled for the portal client in the local realm only, for tests.
    private Task<string> PasswordAsync(string username, string password, string scopes) => RequestAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "password",
        ["client_id"] = "orderplatform-portal",
        ["username"] = username,
        ["password"] = password,
        ["scope"] = scopes,
    });

    private async Task<string> RequestAsync(Dictionary<string, string> form)
    {
        using var response = await Http.PostAsync(new Uri(authority, "protocol/openid-connect/token"), new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token?.AccessToken ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
