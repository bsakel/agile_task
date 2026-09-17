using Npgsql;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>PR 1a: the Migrator builds the complete schema, is safe to run again, and reports its run as a trace (ADR-0008, ADR-0012).</summary>
public sealed class MigratorTests(PlatformFixture platform)
{
    [Fact]
    public async Task Migrator_creates_every_schema_records_its_run_and_can_run_again()
    {
        var connectionString = await platform.Containers.CreateDatabaseAsync("migrator");

        var first = await PlatformFixture.RunMigratorAsync(connectionString);
        first.ExitCode.ShouldBe(0, first.Output);
        var scriptsAfterFirstRun = await ScalarAsync<long>(connectionString, "select count(*) from platform.schemaversions");

        var second = await PlatformFixture.RunMigratorAsync(connectionString);
        second.ExitCode.ShouldBe(0, second.Output);

        var schemas = await ListAsync(connectionString,
            "select nspname from pg_namespace where nspname in ('billing','customers','inventory','ordering','platform','pricing','shipping','wolverine') order by 1");
        schemas.ShouldBe(["billing", "customers", "inventory", "ordering", "platform", "pricing", "shipping", "wolverine"]);

        (await ScalarAsync<long>(connectionString, "select count(*) from platform.schemaversions")).ShouldBe(scriptsAfterFirstRun);
        (await ScalarAsync<long>(connectionString, "select count(*) from platform.migrator_runs")).ShouldBe(2);
        (await ScalarAsync<bool>(connectionString, "select to_regclass('ordering.mt_doc_idempotencyrecord') is not null")).ShouldBeTrue();
    }

    [Fact]
    public async Task Migrator_exports_a_trace_with_its_steps()
    {
        using var receiver = new OtlpReceiver();
        var connectionString = await platform.Containers.CreateDatabaseAsync("traced");
        var environment = new Dictionary<string, string?>(receiver.Settings)
        {
            ["ConnectionStrings__orderplatform"] = connectionString,
            ["OTEL_SERVICE_NAME"] = "migrator",
        };

        var migrator = await PlatformProcess.RunAsync(PlatformFixture.MigratorAssembly, environment, TimeSpan.FromMinutes(2));

        migrator.ExitCode.ShouldBe(0, migrator.Output);
        (await receiver.WaitForTracesContainingAsync(
            TimeSpan.FromSeconds(10),
            "migrate database", "apply Marten schema", "apply Wolverine message storage", "verify Marten schema", "verify Wolverine message storage"))
            .ShouldBeTrue("the Migrator flushes its trace before exiting");
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ListAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
