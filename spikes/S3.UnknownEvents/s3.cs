// Phase 0 spike S3: how does an OLDER build behave when a stream contains an event type written by a NEWER build?
// Run: dotnet run s3.cs
#:package Marten@9.37.0
#:property PublishAot=false
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Npgsql;

const string cs = "Host=localhost;Port=55432;Database=spike;Username=postgres;Password=spike";

await using (var c = new NpgsqlConnection(cs))
{
    await c.OpenAsync();
    await new NpgsqlCommand("drop schema if exists s3 cascade", c).ExecuteNonQueryAsync();
}

DocumentStore Build(bool newer) => DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "s3";
    o.AutoCreateSchemaObjects = AutoCreate.All;
    o.Events.MapEventType<OrderSubmitted>("order_submitted");
    o.Events.MapEventType<InventoryReserved>("inventory_reserved");
    if (newer) o.Events.MapEventType<OrderPriorityChanged>("order_priority_changed");
    o.Projections.Snapshot<OrderDetails>(SnapshotLifecycle.Inline);
});

var orderId = Guid.NewGuid();

// ---- newer build writes a stream including a new event type ----
await using (var newer = Build(newer: true))
await using (var s = newer.LightweightSession())
{
    s.Events.StartStream<OrderDetails>(orderId, new OrderSubmitted(orderId, 3), new InventoryReserved(orderId));
    s.Events.Append(orderId, new OrderPriorityChanged(orderId, "high"));
    await s.SaveChangesAsync();
    Console.WriteLine("newer build: wrote OrderSubmitted, InventoryReserved, OrderPriorityChanged");
}

// Simulate that the older build cannot load the CLR type (it does not exist in its assemblies).
await using (var c = new NpgsqlConnection(cs))
{
    await c.OpenAsync();
    var n = await new NpgsqlCommand(
        "update s3.mt_events set mt_dotnet_type = 'Spike.OrderPriorityChanged, Spike.NewerBuild' where type = 'order_priority_changed'", c)
        .ExecuteNonQueryAsync();
    Console.WriteLine($"rewrote dotnet type on {n} row(s) to an unloadable type");
}

// ---- older build (rolled back) reads the same stream ----
await using var older = Build(newer: false);

await Try("FetchStreamAsync", async () =>
{
    await using var q = older.QuerySession();
    var events = await q.Events.FetchStreamAsync(orderId);
    return string.Join(", ", events.Select(e => $"{e.EventTypeName}:{e.Data?.GetType().Name ?? "null"}"));
});

await Try("AggregateStreamAsync (live)", async () =>
{
    await using var q = older.QuerySession();
    var agg = await q.Events.AggregateStreamAsync<OrderDetails>(orderId);
    return $"version={agg?.Version} lines={agg?.Lines} reserved={agg?.Reserved}";
});

await Try("Load inline snapshot document", async () =>
{
    await using var q = older.QuerySession();
    var doc = await q.LoadAsync<OrderDetails>(orderId);
    return $"version={doc?.Version} reserved={doc?.Reserved}";
});

await Try("FetchForWriting + append known event", async () =>
{
    await using var s = older.LightweightSession();
    var stream = await s.Events.FetchForWriting<OrderDetails>(orderId);
    stream.AppendOne(new InventoryReserved(orderId));
    await s.SaveChangesAsync();
    return $"appended at version {stream.CurrentVersion}";
});

static async Task Try(string name, Func<Task<string>> action)
{
    try { Console.WriteLine($"[OK]   {name}: {await action()}"); }
    catch (Exception e) { Console.WriteLine($"[FAIL] {name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
}

public record OrderSubmitted(Guid OrderId, int Lines);
public record InventoryReserved(Guid OrderId);
public record OrderPriorityChanged(Guid OrderId, string Priority);

public class OrderDetails
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public int Lines { get; set; }
    public bool Reserved { get; set; }
    public string? Priority { get; set; }

    public void Apply(OrderSubmitted e) => Lines = e.Lines;
    public void Apply(InventoryReserved e) => Reserved = true;
    public void Apply(OrderPriorityChanged e) => Priority = e.Priority;
}
