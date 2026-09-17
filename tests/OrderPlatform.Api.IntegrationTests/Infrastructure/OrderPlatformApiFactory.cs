using System.Net.Http.Headers;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real Api host in-process, against the migrated test database and the Keycloak container. Runs as Development because
/// the diagnostics endpoint exists only there until Phase 2.
/// </summary>
public sealed class OrderPlatformApiFactory(PlatformContainers containers, IReadOnlyDictionary<string, string?> settings)
    : WebApplicationFactory<Program>
{
    static OrderPlatformApiFactory()
    {
        // The Api runs through JasperFx.RunJasperFxCommands, which only starts the host for WebApplicationFactory with this switch.
        JasperFxEnvironment.AutoStartHost = true;
    }

    public HttpClient CreateClient(string? accessToken)
    {
        var client = CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:orderplatform", containers.ConnectionString);
        builder.UseSetting("Authentication:Schemes:Bearer:Authority", containers.Authority);
        builder.UseSetting("Authentication:Schemes:Bearer:RequireHttpsMetadata", "false");

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
