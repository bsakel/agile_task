using WireMock.Server;
using Xunit;

namespace OrderPlatform.Testing;

/// <summary>
/// Base class for adapter tests (ADR-0014, ADR-0022): a WireMock.Net server per test class standing in for the external
/// provider. Adapter tests configure the typed <c>HttpClient</c> with <see cref="BaseAddress"/> and stub each provider
/// response: success, every error mapping, and a timeout followed by the outcome query.
/// </summary>
/// <example>
/// <code>
/// public sealed class BillingHttpAdapterTests : WireMockTestBase
/// {
///     [Fact]
///     public async Task Maps_a_declined_invoice_to_a_failure()
///     {
///         Server.Given(Request.Create().WithPath("/invoices").UsingPost())
///               .RespondWith(Response.Create().WithStatusCode(422).WithBodyAsJson(new { code = "DECLINED" }));
///         var adapter = new BillingHttpAdapter(new HttpClient { BaseAddress = BaseAddress });
///         ...
///     }
/// }
/// </code>
/// </example>
public abstract class WireMockTestBase : IAsyncLifetime
{
    protected WireMockServer Server { get; private set; } = null!;

    protected Uri BaseAddress => new(Server.Url!);

    public virtual ValueTask InitializeAsync()
    {
        Server = WireMockServer.Start();
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
