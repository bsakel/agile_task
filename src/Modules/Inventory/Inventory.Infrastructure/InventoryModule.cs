using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Integrations;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Inventory.Application;
using OrderPlatform.Inventory.Application.Integrations;
using OrderPlatform.Inventory.Infrastructure.Fakes;
using Wolverine;

namespace OrderPlatform.Inventory.Infrastructure;

public sealed class InventoryModule : IModule
{
    /// <summary>The external system this module talks to: <c>Integrations:Inventory:Mode</c> (ADR-0014).</summary>
    public const string IntegrationSystem = "Inventory";

    public string Name => "inventory";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("inventory", typeof(InventoryModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder)
    {
        // The HTTP adapter is a next step; until it exists, Mode=Http registers no gateway rather than failing startup, so a
        // deployed host still starts and only a caller of the port would notice. Fakes stay an explicit opt-in (ADR-0014).
        if (builder.Configuration.GetIntegrationMode(IntegrationSystem) == IntegrationMode.Fake)
        {
            var options = builder.Configuration.GetSection(FakeInventoryOptions.SectionName).Get<FakeInventoryOptions>()
                ?? new FakeInventoryOptions();

            builder.Services.AddSingleton<IInventoryGateway>(_ => new FakeInventoryGateway(options));
        }
    }

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(InventoryApplication.Assembly);
}
