using System.Security.Claims;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Security;

/// <summary>OAuth2 scopes of the platform API (ADR-0016). Each scope is also the name of the policy requiring it.</summary>
public static class PlatformScopes
{
    public const string OrdersRead = "orders:read";
    public const string OrdersWrite = "orders:write";
    public const string OrdersCancel = "orders:cancel";

    public static IReadOnlyList<string> All { get; } = [OrdersRead, OrdersWrite, OrdersCancel];
}

/// <summary>Authorization policy names (ADR-0016). Scope policies use the scope name, see <see cref="PlatformScopes"/>.</summary>
public static class PlatformPolicies
{
    /// <summary>The caller acts for one customer account: the token carries an <c>account_id</c> claim.</summary>
    public const string CustomerAccount = "customer-account";

    /// <summary>Back-office actions on any account.</summary>
    public const string SupportAgent = "support-agent";
}

/// <summary>Claim names used by the platform, as issued by the identity provider.</summary>
public static class PlatformClaims
{
    public const string AccountId = "account_id";
    public const string Scope = "scope";
    public const string Roles = "roles";
    public const string Subject = "sub";
    public const string AuthorizedParty = "azp";

    public const string SupportAgentRole = "support-agent";
}

/// <summary>Reads the caller's identity from the validated token.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The customer account the caller acts for, or <c>null</c> (e.g. support agents).</summary>
    public static Guid? GetAccountId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(PlatformClaims.AccountId), out var accountId) && accountId != Guid.Empty
            ? accountId
            : null;

    /// <summary>The account id of a caller that passed the <see cref="PlatformPolicies.CustomerAccount"/> policy.</summary>
    public static Guid GetRequiredAccountId(this ClaimsPrincipal principal) =>
        principal.GetAccountId()
        ?? throw new InvalidOperationException(
            $"The caller has no '{PlatformClaims.AccountId}' claim; require the '{PlatformPolicies.CustomerAccount}' policy on this endpoint.");

    public static bool HasScope(this ClaimsPrincipal principal, string scope) =>
        principal.FindAll(PlatformClaims.Scope)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    public static bool IsSupportAgent(this ClaimsPrincipal principal) =>
        principal.HasClaim(PlatformClaims.Roles, PlatformClaims.SupportAgentRole);

    /// <summary>
    /// The partition a caller's requests are counted and keyed under: <c>account:&lt;id&gt;</c> for customer callers,
    /// <c>agent:&lt;sub&gt;</c> for support agents and other callers without an account, or <c>null</c> when anonymous.
    /// Used for idempotency keys and rate limiting (ADR-0016, ADR-0020).
    /// </summary>
    public static string? GetCallerPartition(this ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.GetAccountId() is { } accountId
            ? $"account:{accountId}"
            : $"agent:{principal.FindFirstValue(PlatformClaims.Subject) ?? principal.FindFirstValue(PlatformClaims.AuthorizedParty)}";
    }
}
