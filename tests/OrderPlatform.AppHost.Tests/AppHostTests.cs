using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace OrderPlatform.AppHost.Tests;

/// <summary>
/// PR 1a criterion: the AppHost starts the system and runs the Migrator to completion before the Api starts (ADR-0008, ADR-0013).
/// </summary>
public sealed class AppHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task AppHost_runs_the_Migrator_first_and_starts_a_ready_Api()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(Timeout);
        var ct = cancellation.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.OrderPlatform_AppHost>(ct);

        // Hermetic run: no persistent data volume, so a generated password never meets a database initialised with another one.
        foreach (var postgres in builder.Resources.OfType<PostgresServerResource>())
        {
            foreach (var volume in postgres.Annotations.OfType<ContainerMountAnnotation>().Where(mount => mount.Type == ContainerMountType.Volume).ToList())
            {
                postgres.Annotations.Remove(volume);
            }
        }

        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);

        var migrator = await app.ResourceNotifications.WaitForResourceAsync(
            "migrator", resource => resource.Snapshot.State?.Text == KnownResourceStates.Finished, ct);
        var api = await app.ResourceNotifications.WaitForResourceHealthyAsync("api", ct);

        migrator.Snapshot.ExitCode.ShouldBe(0);
        api.Snapshot.StartTimeStamp.ShouldNotBeNull();
        migrator.Snapshot.StopTimeStamp.ShouldNotBeNull();
        api.Snapshot.StartTimeStamp.Value.ShouldBeGreaterThanOrEqualTo(migrator.Snapshot.StopTimeStamp.Value);

        using var http = app.CreateHttpClient("api");
        using var ready = await http.GetAsync("/health/ready", ct);
        ready.IsSuccessStatusCode.ShouldBeTrue();
    }
}
