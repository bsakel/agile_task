using System.Net.Http.Headers;
using OrderPlatform.Api.IntegrationTests.Infrastructure;
using OrderPlatform.Testing;

namespace OrderPlatform.Api.IntegrationTests;

/// <summary>
/// PR 1a/1b: the Api exports traces of its requests, handlers and database activity through OTLP, without health probes
/// (ADR-0012), and records feature flag evaluations on the span (ADR-0019). Runs the Api as its own process: OpenTelemetry
/// listeners are process-wide, so an in-process test host also exports the activities of other tests.
/// </summary>
public sealed class ApiTracingTests(PlatformFixture platform)
{
    [Fact]
    public async Task Api_exports_request_handler_database_and_flag_spans_but_not_health_probes()
    {
        using var receiver = new OtlpReceiver();
        var baseAddress = new Uri($"http://127.0.0.1:{FreeTcpPort()}");
        var environment = new Dictionary<string, string?>(receiver.Settings)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = baseAddress.ToString(),
            ["ConnectionStrings__orderplatform"] = platform.Containers.ConnectionString,
            ["Authentication__Schemes__Bearer__Authority"] = platform.Containers.Authority,
            ["Authentication__Schemes__Bearer__RequireHttpsMetadata"] = "false",
            ["OTEL_SERVICE_NAME"] = "api",
        };

        await using var api = PlatformProcess.Start(PlatformFixture.ApiAssembly, environment);
        (await api.WaitUntilHealthyAsync(new Uri(baseAddress, "/health/ready"), TimeSpan.FromSeconds(90))).ShouldBeTrue(api.Output);

        using var client = new HttpClient { BaseAddress = baseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await platform.Tokens.AcmeErpAsync());
        using var response = await client.PostEchoAsync("""{"message":"traced"}""");
        response.EnsureSuccessStatusCode();

        (await receiver.WaitForTracesContainingAsync(
            TimeSpan.FromSeconds(20),
            "POST /v1/_diagnostics/echo",
            "OrderPlatform.Api.Diagnostics.EchoDiagnostics",
            "postgresql",
            "feature_flag.evaluation",
            "Diagnostics.EchoUppercase"))
            .ShouldBeTrue("expected spans were not exported");

        // The readiness probes above were answered but must not appear as spans.
        receiver.TracesText.ShouldNotContain("/health/ready", Case.Sensitive);
    }

    private static int FreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
