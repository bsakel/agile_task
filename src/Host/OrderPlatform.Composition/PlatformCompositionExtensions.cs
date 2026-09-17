using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration;
using Marten;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Messaging;
using OrderPlatform.Composition.Schema;
using Wolverine;
using Wolverine.Marten;

namespace OrderPlatform.Composition;

public static class PlatformCompositionExtensions
{
    /// <summary>
    /// Registers all modules, Marten and Wolverine. Used by the Api and the Migrator so both see the same schema (ADR-0008).
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="requireMigratedSchema">
    /// <c>true</c> for the Api: startup fails with an actionable message when the Migrator has not run.
    /// </param>
    public static IHostApplicationBuilder AddOrderPlatform(this IHostApplicationBuilder builder, bool requireMigratedSchema)
    {
        builder.AddNpgsqlDataSource(PlatformDatabase.ConnectionName);

        // Single clock for all time-dependent code; tests replace it with FakeTimeProvider (ADR-0022).
        builder.Services.AddSingleton(TimeProvider.System);

        if (requireMigratedSchema)
        {
            // Registered before Wolverine so it starts first.
            builder.Services.AddHostedService<MigratedSchemaGuard>();
            builder.Services.AddHealthChecks()
                .AddCheck<MigratedSchemaHealthCheck>("database-schema", tags: [ServiceDefaultsExtensions.ReadinessTag]);
        }

        foreach (var module in PlatformModules.All)
        {
            module.AddServices(builder);
        }

        builder.Services
            .AddMarten(options =>
            {
                options.DatabaseSchemaName = PlatformDatabase.MartenSchema;

                // The Api never changes the schema, in any environment; the Migrator applies it (ADR-0008).
                options.AutoCreateSchemaObjects = AutoCreate.None;

                foreach (var module in PlatformModules.All)
                {
                    module.ConfigureMarten(options);
                }
            })
            .UseNpgsqlDataSource()
            .IntegrateWithWolverine(integration =>
            {
                integration.MessageStorageSchemaName = PlatformDatabase.WolverineSchema;
                integration.AutoCreate = AutoCreate.None;
            });

        builder.UseWolverine(options =>
        {
            // Wolverine defaults to the assembly calling UseWolverine (this one); generated code lives in the host (Api).
            options.ApplicationAssembly = Assembly.GetEntryAssembly() ?? typeof(PlatformCompositionExtensions).Assembly;

            options.Durability.Mode = DurabilityMode.Balanced;
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();

            // Container images run pre-generated handler code; development and tests compile at runtime (ADR-0004, ADR-0021).
            options.CodeGeneration.TypeLoadMode = string.Equals(
                builder.Configuration["Wolverine:TypeLoadMode"], nameof(TypeLoadMode.Static), StringComparison.OrdinalIgnoreCase)
                ? TypeLoadMode.Static
                : TypeLoadMode.Dynamic;

            foreach (var module in PlatformModules.All)
            {
                module.ConfigureWolverine(options);
            }
        });

        builder.Services.AddRelationalOutbox();

        return builder;
    }

    public static IEndpointRouteBuilder MapOrderPlatformModules(this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in PlatformModules.All)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
