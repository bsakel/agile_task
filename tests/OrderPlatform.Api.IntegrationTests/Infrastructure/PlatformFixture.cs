using System.Collections.Concurrent;
using System.Reflection;
using OrderPlatform.Testing;

[assembly: AssemblyFixture(typeof(OrderPlatform.Api.IntegrationTests.Infrastructure.PlatformFixture))]

namespace OrderPlatform.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared for the whole test run: PostgreSQL and Keycloak containers, the schema built by running the real Migrator, and
/// Api hosts running with <c>AutoCreate.None</c> as in production (ADR-0008, ADR-0022).
/// </summary>
public sealed class PlatformFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, OrderPlatformApiFactory> factories = new();

    public PlatformContainers Containers { get; } = new();

    public KeycloakTokens Tokens => Containers.Tokens;

    public static string MigratorAssembly => BuildOutput("MigratorAssembly");

    public static string ApiAssembly => BuildOutput("ApiAssembly");

    /// <summary>The Api with default configuration (Development environment, no flags configured).</summary>
    public OrderPlatformApiFactory Api => ApiWith(new Dictionary<string, string?>());

    /// <summary>An Api host with additional configuration, e.g. <see cref="FeatureFlagStates.Settings"/>; cached per configuration.</summary>
    public OrderPlatformApiFactory ApiWith(IReadOnlyDictionary<string, string?> settings) =>
        factories.GetOrAdd(
            string.Join(";", settings.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}={entry.Value}")),
            _ => new OrderPlatformApiFactory(Containers, settings));

    public async ValueTask InitializeAsync()
    {
        await Containers.InitializeAsync();

        var migrator = await RunMigratorAsync(Containers.ConnectionString);
        migrator.ExitCode.ShouldBe(0, migrator.Output);
    }

    public static Task<PlatformProcess> RunMigratorAsync(string connectionString) =>
        PlatformProcess.RunAsync(
            MigratorAssembly,
            new Dictionary<string, string?> { ["ConnectionStrings__orderplatform"] = connectionString },
            TimeSpan.FromMinutes(2));

    public async ValueTask DisposeAsync()
    {
        foreach (var factory in factories.Values)
        {
            await factory.DisposeAsync();
        }

        await Containers.DisposeAsync();
    }

    private static string BuildOutput(string key) =>
        Path.GetFullPath(typeof(PlatformFixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!);
}
