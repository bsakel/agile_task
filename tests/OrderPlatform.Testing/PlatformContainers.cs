using Npgsql;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderPlatform.Testing;

/// <summary>
/// One PostgreSQL and one Keycloak container per test run, with the same image versions as docker-compose and the
/// local realm import (ADR-0013, ADR-0022). Tests isolate data by unique keys and accounts, not by recreating databases.
/// </summary>
public sealed class PlatformContainers : IAsyncLifetime
{
    public const string PostgresImage = "postgres:17-alpine";
    public const string KeycloakImage = "quay.io/keycloak/keycloak:26.7.4";
    public const string Realm = "orderplatform";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase("orderplatform")
        .Build();

    private readonly KeycloakContainer keycloak = new KeycloakBuilder(KeycloakImage)
        .WithRealm(RepositoryPaths.KeycloakRealm)
        .Build();

    /// <summary>Connection string of the shared, migrated database.</summary>
    public string ConnectionString => postgres.GetConnectionString();

    /// <summary>The OpenID Connect authority (issuer) of the local realm.</summary>
    public string Authority => new Uri(new Uri(keycloak.GetBaseAddress()), $"realms/{Realm}").ToString();

    public KeycloakTokens Tokens => new(new Uri(Authority + "/"));

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(postgres.StartAsync(), keycloak.StartAsync());

    /// <summary>Creates an empty database on the shared server, e.g. to run the Migrator or the Api against it.</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"create database \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await postgres.DisposeAsync();
        await keycloak.DisposeAsync();
    }
}
