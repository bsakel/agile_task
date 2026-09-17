using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrderPlatform.BuildingBlocks.Infrastructure.Integrations;

/// <summary>Which adapter a module uses for an external system: <c>Integrations:&lt;System&gt;:Mode</c> (ADR-0014).</summary>
public enum IntegrationMode
{
    /// <summary>In-memory fake for local development and tests only.</summary>
    Fake,

    /// <summary>The real HTTP adapter.</summary>
    Http,
}

public static class IntegrationModes
{
    public const string SectionName = "Integrations";

    /// <summary>The environment used by automated tests; fakes are allowed there and in Development.</summary>
    public const string TestEnvironment = "Test";

    /// <summary>Reads the mode of one external system, e.g. <c>Inventory</c>. A missing mode means <c>Http</c>.</summary>
    public static IntegrationMode GetIntegrationMode(this IConfiguration configuration, string system) =>
        Parse(system, configuration.GetSection(SectionName).GetSection(system)["Mode"]);

    /// <summary>
    /// Fails host startup when any integration runs in <c>Fake</c> mode outside Development and Test, so fakes can never
    /// reach a deployed environment (ADR-0014).
    /// </summary>
    public static IServiceCollection AddIntegrationModeGuard(this IServiceCollection services) =>
        services.AddHostedService<IntegrationModeGuard>();

    internal static IntegrationMode Parse(string system, string? value) =>
        value is null ? IntegrationMode.Http
        : Enum.TryParse<IntegrationMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode
        : throw new InvalidOperationException(
            $"Invalid value '{value}' for {SectionName}:{system}:Mode. Allowed values: {string.Join(", ", Enum.GetNames<IntegrationMode>())}.");
}

internal sealed class IntegrationModeGuard(IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Parsing every configured mode also rejects typos in any environment.
        var fakes = configuration.GetSection(IntegrationModes.SectionName).GetChildren()
            .Where(system => IntegrationModes.Parse(system.Key, system["Mode"]) == IntegrationMode.Fake)
            .Select(system => system.Key)
            .ToList();

        if (fakes.Count > 0 && !environment.IsDevelopment() && !environment.IsEnvironment(IntegrationModes.TestEnvironment))
        {
            throw new InvalidOperationException(
                $"Integrations [{string.Join(", ", fakes)}] are configured with Mode=Fake in the '{environment.EnvironmentName}' environment. " +
                "Fake adapters are only allowed in Development and Test; set Integrations:<System>:Mode=Http " +
                "(architecture/adr/0014-external-integrations-ports-and-adapters.md).");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
