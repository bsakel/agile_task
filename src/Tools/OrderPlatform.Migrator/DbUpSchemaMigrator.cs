using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Composition;

namespace OrderPlatform.Migrator;

/// <summary>
/// Runs the DbUp scripts of one schema following ADR-0009: no DbUp transaction (scripts are idempotent and use explicit
/// BEGIN/COMMIT where needed), expand and contract scripts ordered by number, retry when a script hits lock_timeout.
/// </summary>
internal sealed class DbUpSchemaMigrator(NpgsqlDataSource dataSource, IConfiguration configuration, ILogger<DbUpSchemaMigrator> logger)
{
    // NpgsqlDataSource.ConnectionString omits the password, so DbUp gets the configured connection string.
    private readonly string _connectionString = configuration.GetConnectionString(PlatformDatabase.ConnectionName)
        ?? throw new InvalidOperationException($"Connection string '{PlatformDatabase.ConnectionName}' is not configured.");

    private const string LockNotAvailable = "55P03";
    private const int MaxAttempts = 5;
    private const string ScriptPrefix = "migrations/";

    public async Task<bool> MigrateAsync(RelationalSchema schema, CancellationToken cancellationToken)
    {
        var scripts = LoadScripts(schema);
        logger.LogInformation("Schema {Schema}: {ScriptCount} script(s) found", schema.SchemaName, scripts.Count);

        await using (var createSchema = dataSource.CreateCommand($"create schema if not exists \"{schema.SchemaName}\""))
        {
            await createSchema.ExecuteNonQueryAsync(cancellationToken);
        }

        if (scripts.Count == 0)
        {
            return true;
        }

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(_connectionString)
            .WithScripts(scripts)
            .JournalToPostgresqlTable(schema.SchemaName, "schemaversions")
            // CREATE INDEX CONCURRENTLY cannot run inside a transaction (spike S6).
            .WithoutTransaction()
            .LogTo(logger)
            .Build();

        for (var attempt = 1; ; attempt++)
        {
            var result = upgrader.PerformUpgrade();
            if (result.Successful)
            {
                return true;
            }

            if (IsLockTimeout(result.Error) && attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    "Schema {Schema}: script {Script} hit lock_timeout (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}",
                    schema.SchemaName, result.ErrorScript?.Name, attempt, MaxAttempts, delay);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            logger.LogError(result.Error, "Schema {Schema}: script {Script} failed", schema.SchemaName, result.ErrorScript?.Name);
            return false;
        }
    }

    /// <summary>
    /// Loads <c>migrations/expand/*.sql</c> and <c>migrations/contract/*.sql</c>, ordered by file name across both folders,
    /// so a contract script runs after the expand scripts it depends on.
    /// </summary>
    private static List<SqlScript> LoadScripts(RelationalSchema schema)
    {
        var assembly = schema.ScriptsAssembly;

        return assembly.GetManifestResourceNames()
            .Select(resource => (Resource: resource, Name: resource.Replace('\\', '/')))
            .Where(script => script.Name.StartsWith(ScriptPrefix, StringComparison.Ordinal)
                             && script.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(script => Path.GetFileName(script.Name), StringComparer.Ordinal)
            .Select(script =>
            {
                using var stream = assembly.GetManifestResourceStream(script.Resource)!;
                using var reader = new StreamReader(stream);
                return new SqlScript(script.Name[ScriptPrefix.Length..], reader.ReadToEnd());
            })
            .ToList();
    }

    private static bool IsLockTimeout(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: LockNotAvailable })
            {
                return true;
            }
        }

        return false;
    }
}
