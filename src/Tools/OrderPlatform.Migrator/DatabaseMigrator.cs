using System.Diagnostics;
using System.Reflection;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Composition;
using OrderPlatform.Composition.Schema;
using Wolverine.Runtime;

namespace OrderPlatform.Migrator;

/// <summary>
/// Applies the schema in the order defined by ADR-0008: DbUp scripts, Marten, Wolverine storage, verification,
/// then records the run so the Api can start.
/// </summary>
internal sealed class DatabaseMigrator(
    NpgsqlDataSource dataSource,
    DbUpSchemaMigrator dbUp,
    IDocumentStore documentStore,
    IWolverineRuntime wolverine,
    ILogger<DatabaseMigrator> logger)
{
    private static readonly ActivitySource Activity = new("OrderPlatform.Migrator");

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var version = typeof(DatabaseMigrator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        using var run = Activity.StartActivity("migrate database");
        logger.LogInformation("Migrating database for application version {Version}", version);

        try
        {
            // 1. Relational schemas: the Migrator's own schema first, then every module that owns one.
            var schemas = new List<RelationalSchema> { new(PlatformDatabase.PlatformSchema, typeof(DatabaseMigrator).Assembly) };
            schemas.AddRange(PlatformModules.All.Select(module => module.RelationalSchema).OfType<RelationalSchema>());

            foreach (var schema in schemas)
            {
                if (!await dbUp.MigrateAsync(schema, cancellationToken))
                {
                    return false;
                }
            }

            // 2. Marten: event store, documents and projections registered by the modules.
            await Step("apply Marten schema", () =>
                documentStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate));

            // 3. Wolverine message storage (cannot be exported as SQL, spike S4).
            await Step("apply Wolverine message storage", () =>
                wolverine.Storage.Admin.MigrateAsync(AutoCreate.CreateOrUpdate));

            // 4. Verify that the database now matches the configuration.
            await Step("verify Marten schema", () =>
                documentStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync(cancellationToken));
            await Step("verify Wolverine message storage", () =>
                wolverine.Storage.Admin.AssertStorageProvisionedAsync(cancellationToken));

            // 5. Record the run; the Api refuses to start without it.
            await MigrationHistory.RecordRunAsync(dataSource, version, cancellationToken);

            logger.LogInformation("Database migration completed for application version {Version}", version);
            return true;
        }
        catch (Exception exception)
        {
            run?.SetStatus(ActivityStatusCode.Error, exception.Message);
            logger.LogError(exception, "Database migration failed");
            return false;
        }
    }

    private async Task Step(string name, Func<Task> action)
    {
        using var activity = Activity.StartActivity(name);
        var stopwatch = Stopwatch.StartNew();
        await action();
        logger.LogInformation("Completed step '{Step}' in {ElapsedMilliseconds} ms", name, stopwatch.ElapsedMilliseconds);
    }
}
