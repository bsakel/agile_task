using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using OrderPlatform.Composition;
using OrderPlatform.Migrator;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Same module, Marten and Wolverine registrations as the Api, so the complete schema is known (ADR-0008).
builder.AddOrderPlatform(requireMigratedSchema: false);

builder.Services.AddSingleton<DbUpSchemaMigrator>();
builder.Services.AddSingleton<DatabaseMigrator>();

using var host = builder.Build();

// The host is deliberately not started: the Migrator must not run Wolverine listeners or durability agents.
// Resolving the tracer provider builds it without the hosted service; disposing the host flushes the telemetry.
_ = host.Services.GetService<TracerProvider>();

var succeeded = await host.Services.GetRequiredService<DatabaseMigrator>().RunAsync(CancellationToken.None);
return succeeded ? 0 : 1;
