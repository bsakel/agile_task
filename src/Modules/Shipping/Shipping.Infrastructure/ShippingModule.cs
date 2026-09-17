using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Shipping.Application;
using Wolverine;

namespace OrderPlatform.Shipping.Infrastructure;

public sealed class ShippingModule : IModule
{
    public string Name => "shipping";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("shipping", typeof(ShippingModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(ShippingApplication.Assembly);
}
