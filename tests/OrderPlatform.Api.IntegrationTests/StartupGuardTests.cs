using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// Startup guards, verified on the Api as a separate process so the exit code is checked like a container platform would
/// (PR 1a: empty database; PR 1b: fake integrations outside Development/Test).
/// </summary>
public sealed class StartupGuardTests(PlatformFixture platform)
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Api_against_an_unmigrated_database_fails_at_startup_with_a_clear_message()
    {
        var emptyDatabase = await platform.Containers.CreateDatabaseAsync("empty");

        await using var api = await PlatformProcess.RunAsync(PlatformFixture.ApiAssembly, Environment("Development", emptyDatabase), StartupTimeout);

        api.ExitCode.ShouldNotBe(0);
        api.Output.ShouldContain("The database schema has not been migrated. Run OrderPlatform.Migrator before starting the Api");
    }

    [Fact]
    public async Task Api_in_Production_with_a_fake_integration_fails_at_startup()
    {
        var environment = Environment("Production", platform.Containers.ConnectionString);
        environment["Integrations__Inventory__Mode"] = "Fake";

        await using var api = await PlatformProcess.RunAsync(PlatformFixture.ApiAssembly, environment, StartupTimeout);

        api.ExitCode.ShouldNotBe(0);
        api.Output.ShouldContain("Integrations [Inventory] are configured with Mode=Fake in the 'Production' environment");
    }

    [Theory]
    [InlineData("Production", "Http")]
    [InlineData("Test", "Fake")]
    public async Task Api_starts_when_the_integration_mode_is_allowed(string environmentName, string mode)
    {
        var port = FreeTcpPort();
        var environment = Environment(environmentName, platform.Containers.ConnectionString);
        environment["Integrations__Inventory__Mode"] = mode;
        environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";

        await using var api = PlatformProcess.Start(PlatformFixture.ApiAssembly, environment);
        var healthy = await api.WaitUntilHealthyAsync(new Uri($"http://127.0.0.1:{port}/health/ready"), StartupTimeout);

        healthy.ShouldBeTrue(api.Output);
    }

    private Dictionary<string, string?> Environment(string environmentName, string connectionString) => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = environmentName,
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{FreeTcpPort()}",
        ["ConnectionStrings__orderplatform"] = connectionString,
        ["Authentication__Schemes__Bearer__Authority"] = platform.Containers.Authority,
        ["Authentication__Schemes__Bearer__RequireHttpsMetadata"] = "false",
    };

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
