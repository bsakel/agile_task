using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Pricing.Application;
using Wolverine;

namespace OrderPlatform.Pricing.Infrastructure;

public sealed class PricingModule : IModule
{
    public string Name => "pricing";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("pricing", typeof(PricingModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(PricingApplication.Assembly);
}
