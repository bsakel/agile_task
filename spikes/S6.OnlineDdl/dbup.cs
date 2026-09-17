// Phase 0 spike S6 (part 2): DbUp transaction modes vs CREATE INDEX CONCURRENTLY, journal per schema.
// Run: dotnet run dbup.cs
#:package dbup-postgresql@7.0.1
#:property PublishAot=false
using DbUp;
using DbUp.Engine;
using DbUp.Builder;

const string cs = "Host=localhost;Port=55432;Database=spike;Username=postgres;Password=spike";

var scripts = new[]
{
    new SqlScript("0001_expand_create_table.sql", "set lock_timeout = '5s'; create table if not exists s6dbup.items(id bigint primary key, name text);"),
    new SqlScript("0002_expand_index_concurrently.sql", "create index concurrently if not exists ix_items_name on s6dbup.items(name);"),
};

Run("default (no transaction configured)", b => b);
Run("WithTransactionPerScript", b => b.WithTransactionPerScript());
Run("WithTransaction (single)", b => b.WithTransaction());

void Run(string mode, Func<UpgradeEngineBuilder, UpgradeEngineBuilder> configure)
{
    using (var c = new Npgsql.NpgsqlConnection(cs))
    {
        c.Open();
        new Npgsql.NpgsqlCommand("drop schema if exists s6dbup cascade; create schema s6dbup;", c).ExecuteNonQuery();
    }
    var builder = DeployChanges.To.PostgresqlDatabase(cs)
        .WithScripts(scripts)
        .JournalToPostgresqlTable("s6dbup", "schemaversions")
        .LogToNowhere();
    var result = configure(builder).Build().PerformUpgrade();
    Console.WriteLine($"{mode,-40} success={result.Successful} applied={result.Scripts.Count()} error={result.Error?.Message.Split('\n')[0]}");
}
