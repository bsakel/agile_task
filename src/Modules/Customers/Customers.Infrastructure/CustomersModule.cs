using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderPlatform.BuildingBlocks.Infrastructure.Modules;
using OrderPlatform.Customers.Application;
using OrderPlatform.Customers.Contracts;
using OrderPlatform.Customers.Infrastructure.Accounts;
using Wolverine;

namespace OrderPlatform.Customers.Infrastructure;

public sealed class CustomersModule : IModule
{
    public string Name => "customers";

    // Relational module: Dapper + DbUp in its own schema (ADR-0007).
    public RelationalSchema? RelationalSchema => new("customers", typeof(CustomersModule).Assembly);

    public void AddServices(IHostApplicationBuilder builder) =>
        builder.Services.AddScoped<ICustomerDirectory, CustomerDirectory>();

    public void ConfigureWolverine(WolverineOptions options) =>
        options.Discovery.IncludeAssembly(CustomersApplication.Assembly);
}
