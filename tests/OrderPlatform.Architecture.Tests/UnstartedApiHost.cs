using JasperFx.CodeGeneration;
using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace OrderPlatform.Architecture.Tests;

/// <summary>
/// The Api host built but not started: no database, no listeners. Handler chains are compiled the same way
/// <c>codegen write</c> does during the image build, so registrations can be inspected without infrastructure.
/// </summary>
public sealed class UnstartedApiHost : IDisposable
{
    private readonly Factory factory = new();

    public UnstartedApiHost()
    {
        foreach (var collection in Services.GetServices<ICodeFileCollection>())
        {
            _ = collection.BuildFiles();
        }
    }

    public IServiceProvider Services => factory.Services;

    public HandlerGraph Handlers => ((WolverineRuntime)Services.GetRequiredService<IWolverineRuntime>()).Handlers;

    public IDocumentStore DocumentStore => Services.GetRequiredService<IDocumentStore>();

    public void Dispose() => factory.Dispose();

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:orderplatform", "Host=not-used;Database=not-used");
        }

        protected override IHost CreateHost(IHostBuilder builder) => builder.Build();
    }
}
