using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Integrations;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Shipping.Application;
using OrderPlatform.Shipping.Infrastructure.Fakes;
using Wolverine;

namespace OrderPlatform.Shipping.Infrastructure;

public sealed class ShippingModule : IModule
{
    public string Name => "shipping";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("shipping", typeof(ShippingModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
        // Adapter selection by Integrations:Shipping:Mode (ADR-0014). The HTTP adapter arrives with the fulfilment
        // step (next steps N9-N11), so Http registers no gateway: the host starts as before and the first consumer
        // fails with a missing IShippingGateway registration instead of silently talking to a fake.
        if (builder.Configuration.GetIntegrationMode("Shipping") != IntegrationMode.Fake)
        {
            return;
        }

        var options = new FakeShippingOptions();
        builder.Configuration.GetSection($"{IntegrationModes.SectionName}:Shipping:Fake").Bind(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IShippingGateway, FakeShippingGateway>();
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(ShippingApplication.Assembly);
}
