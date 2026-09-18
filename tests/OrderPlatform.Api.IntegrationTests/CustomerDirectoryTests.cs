using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Customers.Contracts;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 2b: after the Migrator applied the <c>customers</c> scripts, the seeded accounts of the local Keycloak realm are
/// readable through <see cref="ICustomerDirectory"/>, and an unknown account is a returned failure (ADR-0007, ADR-0016).
/// </summary>
public sealed class CustomerDirectoryTests(PlatformFixture platform)
{
    public static TheoryData<string, string, bool> SeededAccounts => new()
    {
        { KeycloakTokens.AcmeAccountId, "NL", false },
        { KeycloakTokens.GlobexAccountId, "BE", true },
    };

    [Theory]
    [MemberData(nameof(SeededAccounts))]
    public async Task Seeded_account_is_read_with_its_tax_profile_and_address_and_contact_ids(
        string accountId, string expectedCountry, bool expectedReverseCharge)
    {
        using var scope = platform.Api.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<ICustomerDirectory>();

        var result = await directory.GetAccountAsync(Guid.Parse(accountId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        var account = result.Value!;
        account.IsActive.ShouldBeTrue();
        account.TaxProfile.CountryCode.ShouldBe(expectedCountry);
        account.TaxProfile.ReverseCharge.ShouldBe(expectedReverseCharge);
        account.TaxProfile.VatNumber.ShouldStartWith(expectedCountry);
        account.ShippingAddressId.ShouldNotBe(account.BillingAddressId);

        // The ids point at rows of this account; the personal data behind them stays in the customers schema.
        (await CountAsync(
            "select (select count(*) from customers.addresses where id in (@billing, @shipping) and account_id = @account)"
            + " + (select count(*) from customers.contacts where id = @contact and account_id = @account)",
            new NpgsqlParameter("billing", account.BillingAddressId),
            new NpgsqlParameter("shipping", account.ShippingAddressId),
            new NpgsqlParameter("contact", account.PrimaryContactId),
            new NpgsqlParameter("account", account.Id)))
            .ShouldBe(3);
    }

    [Fact]
    public async Task Unknown_account_returns_a_failure_instead_of_throwing()
    {
        using var scope = platform.Api.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<ICustomerDirectory>();

        var result = await directory.GetAccountAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(CustomerErrors.UnknownAccountCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    private async Task<long> CountAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(platform.Containers.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
