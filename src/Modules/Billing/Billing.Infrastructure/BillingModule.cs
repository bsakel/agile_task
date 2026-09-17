using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Billing.Application;
using Wolverine;

namespace OrderPlatform.Billing.Infrastructure;

public sealed class BillingModule : IModule
{
    public string Name => "billing";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("billing", typeof(BillingModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(BillingApplication.Assembly);
}
