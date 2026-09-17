namespace OrderPlatform.BuildingBlocks;

/// <summary>
/// Account scoping for handlers (ADR-0016): a caller may only act on resources of its own customer account. A resource of
/// another account is reported as not found, so its existence is not leaked (ADR-0020).
/// </summary>
public static class AccountAccess
{
    public const string ResourceNotFoundCode = "resource-not-found";

    public static Result EnsureOwnedBy(Guid callerAccountId, Guid ownerAccountId, string resource) =>
        callerAccountId != Guid.Empty && callerAccountId == ownerAccountId
            ? Result.Success()
            : Error.NotFound(ResourceNotFoundCode, $"The {resource} was not found.");
}
