using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace OrderPlatform.Composition.Schema;

/// <summary>Readiness: the database is reachable and has been migrated (ADR-0012).</summary>
internal sealed class MigratedSchemaHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await MigrationHistory.HasCompletedRunAsync(dataSource, cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database schema has not been migrated.");
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
