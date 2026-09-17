using Marten;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Ordering.Application;
using Wolverine;

namespace OrderPlatform.Ordering.Infrastructure;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";

    // Event-sourced module: its schema is owned by Marten (ADR-0006), so it has no DbUp scripts.
    public RelationalSchema? RelationalSchema => null;

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureMarten(StoreOptions options)
    {
        // Every document type, projection and event alias is registered here explicitly (ADR-0008, ADR-0010).
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(OrderingApplication.Assembly);
}
