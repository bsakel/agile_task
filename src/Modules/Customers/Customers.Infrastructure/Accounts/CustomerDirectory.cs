using Dapper;
using Npgsql;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Customers.Contracts;

namespace OrderPlatform.Customers.Infrastructure.Accounts;

/// <summary>
/// Reads the local copy in the <c>customers</c> schema with Dapper and parameterised SQL (ADR-0007, ADR-0016).
/// </summary>
public sealed class CustomerDirectory(NpgsqlDataSource dataSource) : ICustomerDirectory
{
    private const string SelectAccount = """
        select id                  as Id,
               status              as Status,
               tax_country_code    as CountryCode,
               tax_vat_number      as VatNumber,
               tax_reverse_charge  as ReverseCharge,
               billing_address_id  as BillingAddressId,
               shipping_address_id as ShippingAddressId,
               primary_contact_id  as PrimaryContactId
        from customers.accounts
        where id = @accountId
        """;

    public async Task<Result<CustomerAccount>> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(
            new CommandDefinition(SelectAccount, new { accountId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return CustomerErrors.UnknownAccount(accountId);
        }

        return new CustomerAccount(
            row.Id,
            Enum.TryParse<CustomerAccountStatus>(row.Status, ignoreCase: true, out var status) ? status : CustomerAccountStatus.Unknown,
            new CustomerTaxProfile(row.CountryCode, row.VatNumber, row.ReverseCharge),
            row.BillingAddressId,
            row.ShippingAddressId,
            row.PrimaryContactId);
    }

    private sealed record AccountRow(
        Guid Id,
        string Status,
        string CountryCode,
        string? VatNumber,
        bool ReverseCharge,
        Guid BillingAddressId,
        Guid ShippingAddressId,
        Guid PrimaryContactId);
}
