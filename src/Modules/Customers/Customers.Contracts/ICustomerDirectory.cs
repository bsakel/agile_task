using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Customers.Contracts;

/// <summary>
/// Read access to the customer account reference data other modules need (ADR-0003). The Customers module is the sole
/// owner of personal data, so this contract carries only ids and non-personal attributes (ADR-0016).
/// </summary>
public interface ICustomerDirectory
{
    /// <summary>
    /// The account, or an <see cref="CustomerErrors.UnknownAccountCode"/> failure when the local copy does not know it.
    /// Unknown accounts are an expected outcome and are returned, never thrown.
    /// </summary>
    Task<Result<CustomerAccount>> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>A customer account: its status, its tax profile and the ids of its default address and contact rows.</summary>
public sealed record CustomerAccount(
    Guid Id,
    CustomerAccountStatus Status,
    CustomerTaxProfile TaxProfile,
    Guid BillingAddressId,
    Guid ShippingAddressId,
    Guid PrimaryContactId)
{
    /// <summary>Only an active account may submit orders (README §2).</summary>
    public bool IsActive => Status == CustomerAccountStatus.Active;
}

/// <summary>Input for the tax stage of pricing: country, VAT registration and reverse-charge eligibility (ADR-0018).</summary>
public sealed record CustomerTaxProfile(string CountryCode, string? VatNumber, bool ReverseCharge);

public enum CustomerAccountStatus
{
    /// <summary>A status written by a newer version; older versions must tolerate it (ADR-0009 rule 4).</summary>
    Unknown = 0,
    Active,
    Suspended,
    Closed,
}

public static class CustomerErrors
{
    public const string UnknownAccountCode = "unknown-account";

    public static Error UnknownAccount(Guid accountId) =>
        Error.NotFound(UnknownAccountCode, $"Customer account '{accountId}' is not known.");
}
