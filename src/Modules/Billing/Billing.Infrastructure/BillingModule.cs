using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.Billing.Application;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.Billing.Infrastructure.Fakes;
using OrderPlatform.BuildingBlocks.Infrastructure.Integrations;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using Wolverine;

namespace OrderPlatform.Billing.Infrastructure;

public sealed class BillingModule : IModule
{
    public string Name => "billing";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("billing", typeof(BillingModule).Assembly);

    /// <summary>The billing system name in <c>Integrations:&lt;System&gt;:Mode</c> (ADR-0014).</summary>
    public const string IntegrationName = "Billing";

    public void AddServices(IHostApplicationBuilder builder)
    {
        // The HTTP adapter arrives with PR 3c; until then Http mode registers no gateway, so the host still starts
        // (the ADR-0014 guard keeps the fake out of deployed environments) and only a caller would fail to resolve it.
        if (builder.Configuration.GetIntegrationMode(IntegrationName) is IntegrationMode.Fake)
        {
            builder.Services.Configure<FakeBillingOptions>(builder.Configuration.GetSection(FakeBillingOptions.SectionName));
            builder.Services.AddSingleton<IBillingGateway, FakeBillingGateway>();
        }
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(BillingApplication.Assembly);
}
