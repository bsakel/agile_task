using Marten;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// Registration of one module into the host. The same registrations are used by the API and the Migrator,
/// so both know the complete schema (ADR-0003, ADR-0008).
/// </summary>
public interface IModule
{
    /// <summary>Short lower-case module name, e.g. <c>ordering</c>.</summary>
    string Name { get; }

    /// <summary>Relational schema and DbUp scripts owned by this module, or <c>null</c> when it has none.</summary>
    RelationalSchema? RelationalSchema => null;

    void AddServices(IHostApplicationBuilder builder);

    /// <summary>Registers document types, projections and event aliases explicitly (ADR-0008, ADR-0010).</summary>
    void ConfigureMarten(StoreOptions options)
    {
    }

    /// <summary>Includes the module's application assembly for handler discovery and adds its policies (ADR-0004).</summary>
    void ConfigureWolverine(WolverineOptions options);

    /// <summary>Maps the module's HTTP endpoints (ADR-0011).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
