using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Pricing.Application;
using OrderPlatform.Pricing.Domain;
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
        foreach (var rule in PricingRules.V1)
        {
            builder.Services.AddSingleton<IPricingRule>(rule);
        }

        builder.Services.AddSingleton<PricingPipeline>();
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(PricingApplication.Assembly);
}
