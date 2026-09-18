using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.Billing.Application;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.Billing.Infrastructure.Fakes;
using OrderPlatform.Billing.Infrastructure.Http;
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
        if (builder.Configuration.GetIntegrationMode(IntegrationName) is IntegrationMode.Fake)
        {
            builder.Services.Configure<FakeBillingOptions>(builder.Configuration.GetSection(FakeBillingOptions.SectionName));
            builder.Services.AddSingleton<IBillingGateway, FakeBillingGateway>();
        }
        else
        {
            builder.Services.AddBillingHttpGateway(builder.Configuration);
        }
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(BillingApplication.Assembly);
}
