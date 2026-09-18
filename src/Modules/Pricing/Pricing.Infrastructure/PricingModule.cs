using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Pricing.Application;
using OrderPlatform.Pricing.Application.PriceLists;
using OrderPlatform.Pricing.Contracts;
using OrderPlatform.Pricing.Domain;
using OrderPlatform.Pricing.Infrastructure.PriceLists;
using Wolverine;

namespace OrderPlatform.Pricing.Infrastructure;

public sealed class PricingModule : IModule
{
    public string Name => "pricing";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("pricing", typeof(PricingModule).Assembly);

    public Type? FeatureFlags => typeof(PricingFeatureFlags);

    public void AddServices(IHostApplicationBuilder builder)
    {
        // The rule pipeline of ADR-0018: the pinned v1 rule list, in registration order; the pipeline orders by stage.
        // Registered by type, not as instances, so Wolverine can build the pipeline in generated handler code instead of
        // locating it at runtime, which static code generation does not allow (ADR-0021).
        foreach (var rule in PricingRules.V1)
        {
            builder.Services.AddSingleton(typeof(IPricingRule), rule.GetType());
        }

        builder.Services.AddSingleton<PricingPipeline>();

        // The price data of the pricing schema and the query other modules use to price an order (ADR-0003, ADR-0018).
        builder.Services.AddScoped<IPriceListReader, PriceListReader>();
        builder.Services.AddScoped<IPricingService, PricingService>();
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(PricingApplication.Assembly);
}
