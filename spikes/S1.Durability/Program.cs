// Phase 0 spikes S1 (durable outbox + crash recovery), S2 (Dapper transaction outbox), S7 (local queue circuit breaker).
// Throwaway code: optimised for answering questions, not for style.
using Dapper;
using JasperFx;
using Marten;
using Npgsql;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Marten;
using Wolverine.Marten.Publishing;
using Wolverine.RDBMS;
using Wolverine.Runtime;

var cs = Environment.GetEnvironmentVariable("SPIKE_PG")
         ?? "Host=localhost;Port=55432;Database=spike;Username=postgres;Password=spike";

var builder = WebApplication.CreateBuilder(args);

var productionMode = Environment.GetEnvironmentVariable("SPIKE_AUTOCREATE") == "none";

builder.Services.AddMarten(o =>
    {
        o.Connection(cs);
        o.DatabaseSchemaName = "s1";
        if (productionMode) o.AutoCreateSchemaObjects = JasperFx.AutoCreate.None;
        if (Environment.GetEnvironmentVariable("SPIKE_REGISTER") == "1") { o.Schema.For<Requested>(); o.Schema.For<Processed>(); }
        if (Environment.GetEnvironmentVariable("SPIKE_INDEX") == "1")
            o.Schema.For<Processed>().Index(x => x.Kind, i => i.IsConcurrent = Environment.GetEnvironmentVariable("SPIKE_CONCURRENT") == "1");
    })
    .IntegrateWithWolverine(i =>
    {
        i.MessageStorageSchemaName = "wolverine";
        if (productionMode) i.AutoCreate = JasperFx.AutoCreate.None;
    });

builder.Services.AddSingleton(NpgsqlDataSource.Create(cs));

builder.Host.UseWolverine(opts =>
{
    opts.Durability.Mode = DurabilityMode.Solo;
    if (Environment.GetEnvironmentVariable("SPIKE_STATIC") == "1") opts.CodeGeneration.TypeLoadMode = JasperFx.CodeGeneration.TypeLoadMode.Static;
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    if (Environment.GetEnvironmentVariable("SPIKE_BREAKER") != "0")
    opts.LocalQueueFor<FlakyWork>()
        .CircuitBreaker(cb =>
        {
            cb.MinimumThreshold = 5;
            cb.FailurePercentageThreshold = 50;
            cb.PauseTime = TimeSpan.FromSeconds(15);
            cb.TrackingPeriod = TimeSpan.FromSeconds(30);
            cb.SamplingPeriod = TimeSpan.FromSeconds(1);
        });
    if (Environment.GetEnvironmentVariable("SPIKE_SCHEDULED_RETRY") == "1")
        opts.OnException<ProviderDownException>().ScheduleRetry(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40)).Then.MoveToErrorQueue();
    else
        opts.OnException<ProviderDownException>().RetryWithCooldown(TimeSpan.FromSeconds(1)).Then.MoveToErrorQueue();
});

var app = builder.Build();

// ---- S1: publish from endpoint (bus only) ----
app.MapPost("/s1/publish/{n:int}", async (int n, IMessageBus bus) =>
{
    for (var i = 0; i < n; i++) await bus.PublishAsync(new SlowWork(Guid.NewGuid()));
    return Results.Ok(new { published = n });
});

// ---- S1: publish from endpoint through Marten outbox, atomically with a document ----
app.MapPost("/s1/outbox/{n:int}", async (int n, IMessageBus bus, OutboxedSessionFactory factory) =>
{
    await using var session = factory.OpenSession(bus);
    for (var i = 0; i < n; i++)
    {
        var id = Guid.NewGuid();
        session.Store(new Requested { Id = id });
        await bus.PublishAsync(new SlowWork(id));
    }
    await session.SaveChangesAsync();
    return Results.Ok(new { published = n });
});

// ---- S1: scheduled message ----
app.MapPost("/s1/schedule/{seconds:int}", async (int seconds, IMessageBus bus) =>
{
    var id = Guid.NewGuid();
    await bus.ScheduleAsync(new ScheduledWork(id), TimeSpan.FromSeconds(seconds));
    return Results.Ok(new { id, dueInSeconds = seconds });
});

// ---- S2: Dapper transaction enlisted in the Wolverine outbox ----
app.MapPost("/s2/{outcome}", async (string outcome, NpgsqlDataSource ds, IWolverineRuntime runtime) =>
{
    var id = Guid.NewGuid();
    await using var conn = await ds.OpenConnectionAsync();
    await conn.ExecuteAsync("create schema if not exists s2; create table if not exists s2.items(id uuid primary key)");
    await using var tx = await conn.BeginTransactionAsync();
    await conn.ExecuteAsync("insert into s2.items(id) values (@id)", new { id }, tx);

    var context = new MessageContext(runtime);
    var database = (IMessageDatabase)runtime.Storage;
    await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction(database, tx));
    await context.PublishAsync(new DapperWork(id));

    if (outcome == "commit")
    {
        await tx.CommitAsync();
        await context.FlushOutgoingMessagesAsync();
    }
    else
    {
        await tx.RollbackAsync();
    }
    return Results.Ok(new { id, outcome, storage = runtime.Storage.GetType().FullName });
});

// ---- S7: circuit breaker ----
app.MapPost("/s7/provider/{state}", (string state) => { FlakyProvider.Down = state == "down"; return Results.Ok(new { FlakyProvider.Down }); });
app.MapPost("/s7/publish/{n:int}", async (int n, IMessageBus bus) =>
{
    for (var i = 0; i < n; i++) await bus.PublishAsync(new FlakyWork(Guid.NewGuid()));
    return Results.Ok(new { published = n });
});

// ---- status ----
app.MapGet("/status", async (IQuerySession q, NpgsqlDataSource ds) =>
{
    var processed = (await q.Query<Processed>().ToListAsync()).GroupBy(p => p.Kind).ToDictionary(g => g.Key, g => g.Count());
    await using var conn = await ds.OpenConnectionAsync();
    var tables = (await conn.QueryAsync<string>(
        "select table_name from information_schema.tables where table_schema = 'wolverine' order by 1")).ToList();
    var incoming = tables.Contains("wolverine_incoming_envelopes")
        ? (await conn.QueryAsync("select status, count(*) as count from wolverine.wolverine_incoming_envelopes group by status")).ToList()
        : null;
    var dapperRows = await conn.ExecuteScalarAsync<string?>("select to_regclass('s2.items')::text") is null
        ? 0 : await conn.ExecuteScalarAsync<long>("select count(*) from s2.items");
    return Results.Ok(new { processed, wolverineTables = tables, incoming, dapperRows, FlakyProvider.Attempts });
});

return await app.RunJasperFxCommands(args);

public record SlowWork(Guid Id);
public record ScheduledWork(Guid Id);
public record DapperWork(Guid Id);
public record FlakyWork(Guid Id);

public class Requested { public Guid Id { get; set; } }
public class Processed { public Guid Id { get; set; } public string Kind { get; set; } = ""; }

public class ProviderDownException() : Exception("provider down");

public static class FlakyProvider
{
    public static volatile bool Down;
    public static int Attempts;
}

public static class SpikeHandler
{
    public static async Task Handle(SlowWork m, IDocumentSession session)
    {
        await Task.Delay(1500);
        session.Store(new Processed { Id = m.Id, Kind = "slow" });
    }

    public static void Handle(ScheduledWork m, IDocumentSession session) =>
        session.Store(new Processed { Id = m.Id, Kind = "scheduled" });

    public static void Handle(DapperWork m, IDocumentSession session) =>
        session.Store(new Processed { Id = m.Id, Kind = "dapper" });

    public static void Handle(FlakyWork m, IDocumentSession session)
    {
        Interlocked.Increment(ref FlakyProvider.Attempts);
        if (FlakyProvider.Down) throw new ProviderDownException();
        session.Store(new Processed { Id = m.Id, Kind = "flaky" });
    }
}
