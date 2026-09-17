using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace OrderPlatform.Testing;

/// <summary>
/// A stand-in OTLP/HTTP collector that accepts exports and keeps the raw payloads, so tests can verify that a process
/// exports the expected spans through its real OpenTelemetry pipeline (ADR-0012). Span names and attribute values are
/// plain UTF-8 strings inside the protobuf payload, which is enough for "was it exported" assertions.
/// </summary>
public sealed class OtlpReceiver : IDisposable
{
    private readonly WireMockServer server = WireMockServer.Start();

    public OtlpReceiver() =>
        server.Given(Request.Create().UsingPost().WithPath("/v1/*"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/x-protobuf"));

    public string Endpoint => server.Url!;

    /// <summary>Environment/configuration entries that make a .NET process export to this receiver.</summary>
    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Endpoint,
        ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        ["OTEL_BSP_SCHEDULE_DELAY"] = "500",
    };

    /// <summary>All exported trace payloads so far, decoded leniently as text.</summary>
    public string TracesText => string.Join(
        "\n",
        server.LogEntries
            .Where(entry => entry.RequestMessage?.Path == "/v1/traces")
            .Select(entry => Encoding.UTF8.GetString(entry.RequestMessage!.BodyAsBytes ?? [])));

    /// <summary>Waits until every text appears in the exported traces.</summary>
    public async Task<bool> WaitForTracesContainingAsync(TimeSpan timeout, params string[] texts)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var exported = TracesText;
            if (texts.All(text => exported.Contains(text, StringComparison.Ordinal)))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    public void Dispose()
    {
        server.Stop();
        server.Dispose();
    }
}
