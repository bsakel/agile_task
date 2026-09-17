using Npgsql;

namespace OrderPlatform.Composition.Schema;

/// <summary>Reads and writes <c>platform.migrator_runs</c>, the record that the Migrator completed successfully.</summary>
public static class MigrationHistory
{
    public static async Task<bool> HasCompletedRunAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var tableExists = dataSource.CreateCommand(
            $"select to_regclass('{PlatformDatabase.PlatformSchema}.migrator_runs') is not null");
        if (await tableExists.ExecuteScalarAsync(cancellationToken) is not true)
        {
            return false;
        }

        await using var anyRun = dataSource.CreateCommand(
            $"select exists (select 1 from {PlatformDatabase.PlatformSchema}.migrator_runs)");
        return await anyRun.ExecuteScalarAsync(cancellationToken) is true;
    }

    public static async Task RecordRunAsync(NpgsqlDataSource dataSource, string applicationVersion, CancellationToken cancellationToken)
    {
        await using var insert = dataSource.CreateCommand(
            $"insert into {PlatformDatabase.PlatformSchema}.migrator_runs (application_version) values ($1)");
        insert.Parameters.Add(new NpgsqlParameter { Value = applicationVersion });
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
