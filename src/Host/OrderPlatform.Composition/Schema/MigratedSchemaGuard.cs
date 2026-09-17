using Microsoft.Extensions.Hosting;
using Npgsql;

namespace OrderPlatform.Composition.Schema;

/// <summary>
/// Stops the Api at startup when the database has not been migrated, instead of failing later with
/// missing-table errors deep inside Wolverine or Marten (ADR-0008).
/// </summary>
internal sealed class MigratedSchemaGuard(NpgsqlDataSource dataSource) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await MigrationHistory.HasCompletedRunAsync(dataSource, cancellationToken))
        {
            throw new InvalidOperationException(
                "The database schema has not been migrated. Run OrderPlatform.Migrator before starting the Api " +
                "(architecture/adr/0008-dedicated-migrator-forward-only.md).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
