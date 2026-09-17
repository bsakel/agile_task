using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Inventory.Application;
using Wolverine;

namespace OrderPlatform.Inventory.Infrastructure;

public sealed class InventoryModule : IModule
{
    public string Name => "inventory";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("inventory", typeof(InventoryModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(InventoryApplication.Assembly);
}
