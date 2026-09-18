using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.Billing.Infrastructure.Http;
using OrderPlatform.BuildingBlocks;
using OrderPlatform.Testing;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace OrderPlatform.Billing.Infrastructure.Tests;

/// <summary>
/// The billing HTTP adapter against the example provider contract documented in
/// <c>src/Modules/Billing/Billing.Infrastructure/Http/README.md</c>, with WireMock.Net standing in for the provider
/// (ADR-0014, ADR-0022). Every stub below is the executable copy of one row of that document.
/// </summary>
public sealed class BillingHttpGatewayTests : WireMockTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 18, 8, 55, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = new("0199a0d0-3a5f-7000-8000-00000000010a");
    private static readonly Guid AccountId = new("0199a0d0-3a5f-7000-8000-00000000010b");
    private static readonly Money Total = Money.InEur(120.00m);
    private const string InvoiceId = "INV-1001";
    private const string InvoicesPath = "/v1/invoices";
    private const string InvoicePath = $"{InvoicesPath}/{InvoiceId}";
    private const string OutcomePath = "/v1/idempotency-keys/*";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly List<ServiceProvider> providers = [];

    [Fact]
    public async Task Issuing_an_invoice_sends_our_idempotency_key_and_maps_the_providers_invoice()
    {
        Stub(Request.Create().WithPath(InvoicesPath).UsingPost(), Response.Create().WithStatusCode(201).WithBodyAsJson(InvoiceBody()));

        var invoice = (await Gateway().IssueInvoiceAsync(IssueRequest(), Token)).Value.ShouldNotBeNull();

        invoice.InvoiceId.ShouldBe(InvoiceId);
        invoice.OrderId.ShouldBe(OrderId);
        invoice.Total.ShouldBe(Total);
        invoice.IssuedAt.ShouldBe(IssuedAt);
        invoice.DueAt.ShouldBe(IssuedAt.AddDays(3));

        var request = Received().Single();
        request.Headers!["Idempotency-Key"].ShouldContain($"invoice:{OrderId}");
        // The provider counts in minor units; our Money is decimal (anti-corruption layer).
        request.Body.ShouldNotBeNull().ShouldContain("\"amountMinor\":12000");
    }

    [Theory]
    [InlineData("OPEN", 0, InvoicePaymentState.Unpaid, "0.00")]
    [InlineData("PART_PAID", 6000, InvoicePaymentState.PartiallyPaid, "60.00")]
    [InlineData("SETTLED", 12000, InvoicePaymentState.Paid, "120.00")]
    [InlineData("VOID", 0, InvoicePaymentState.Voided, "0.00")]
    [InlineData("CREDITED", 0, InvoicePaymentState.Refunded, "0.00")]
    [InlineData("IN_DISPUTE", 0, InvoicePaymentState.Unknown, "0.00")]
    [InlineData(null, 0, InvoicePaymentState.Unknown, "0.00")]
    public async Task Invoice_status_maps_the_provider_vocabulary_and_never_guesses(
        string? providerStatus, long paidMinor, InvoicePaymentState expected, string amountPaid)
    {
        Stub(Request.Create().WithPath(InvoicePath).UsingGet(),
            Response.Create().WithStatusCode(200).WithBodyAsJson(InvoiceBody(providerStatus, paidMinor)));

        var payment = (await Gateway().GetInvoiceStatusAsync(InvoiceId, Token)).Value.ShouldNotBeNull();

        payment.State.ShouldBe(expected);
        payment.AmountPaid.ShouldBe(Money.InEur(decimal.Parse(amountPaid, CultureInfo.InvariantCulture)));
        payment.Total.ShouldBe(Total);
        payment.AsOf.ShouldBe(Now);
    }

    [Theory]
    [InlineData(404, "INVOICE_NOT_FOUND", "billing-invoice-not-found", ErrorKind.NotFound)]
    [InlineData(400, "INVALID_REQUEST", "billing-provider-rejected", ErrorKind.Conflict)]
    [InlineData(401, "UNAUTHENTICATED", "billing-provider-rejected", ErrorKind.Conflict)]
    [InlineData(403, "FORBIDDEN", "billing-provider-rejected", ErrorKind.Conflict)]
    [InlineData(409, "INVOICE_ALREADY_SETTLED", "billing-provider-rejected", ErrorKind.Conflict)]
    [InlineData(422, "INVALID_AMOUNT", "billing-provider-rejected", ErrorKind.Conflict)]
    [InlineData(429, "RATE_LIMITED", "billing-provider-unavailable", ErrorKind.Integration)]
    [InlineData(500, "INTERNAL_ERROR", "billing-provider-unavailable", ErrorKind.Integration)]
    [InlineData(503, "MAINTENANCE", "billing-provider-unavailable", ErrorKind.Integration)]
    public async Task Every_provider_failure_becomes_a_Result_failure(int status, string providerCode, string expectedCode, ErrorKind kind)
    {
        Stub(Request.Create().WithPath(InvoicePath).UsingGet(),
            Response.Create().WithStatusCode(status).WithBodyAsJson(new { code = providerCode, message = "Reported by the provider." }));

        var error = (await Gateway().GetInvoiceStatusAsync(InvoiceId, Token)).Error.ShouldNotBeNull();

        error.Code.ShouldBe(expectedCode);
        error.Kind.ShouldBe(kind);
    }

    [Fact]
    public async Task The_outcome_query_reports_a_key_the_provider_has_no_record_of()
    {
        Stub(Request.Create().WithPath(OutcomePath).UsingGet(),
            Response.Create().WithStatusCode(404).WithBodyAsJson(new { code = "KEY_NOT_FOUND", message = "Unknown key." }));

        var error = (await Gateway().GetOutcomeAsync(BillingIdempotencyKey.Issue(OrderId), Token)).Error.ShouldNotBeNull();

        error.Code.ShouldBe("billing-outcome-unknown");
    }

    [Fact]
    public async Task A_timed_out_issue_asks_what_the_key_produced_and_does_not_issue_a_second_invoice()
    {
        // The provider answers too late for the per-attempt timeout, so the outcome of the POST is unknown. A second POST
        // would issue INV-2002; the adapter has to come back with what the key already produced (ADR-0017 §7).
        Server.Given(Request.Create().WithPath(InvoicesPath).UsingPost())
            .InScenario("issue").WillSetStateTo("issued")
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(3)).WithStatusCode(201).WithBodyAsJson(InvoiceBody()));
        Server.Given(Request.Create().WithPath(InvoicesPath).UsingPost())
            .InScenario("issue").WhenStateIs("issued")
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(InvoiceBody(invoiceId: "INV-2002")));
        Stub(Request.Create().WithPath(OutcomePath).UsingGet(), Response.Create().WithStatusCode(200).WithBodyAsJson(
            new { operation = "INVOICE_ISSUED", reference = InvoiceId, completedAt = IssuedAt }));
        Stub(Request.Create().WithPath(InvoicePath).UsingGet(), Response.Create().WithStatusCode(200).WithBodyAsJson(InvoiceBody()));

        var invoice = (await Gateway().IssueInvoiceAsync(IssueRequest(), Token)).Value.ShouldNotBeNull();

        invoice.InvoiceId.ShouldBe(InvoiceId);
        OutcomeQueries().ShouldBe(1);
        Requests("GET", InvoicePath).ShouldBe(1);
    }

    [Fact]
    public async Task A_timed_out_issue_the_provider_has_no_record_of_is_reported_as_unavailable()
    {
        Stub(Request.Create().WithPath(InvoicesPath).UsingPost(),
            Response.Create().WithDelay(TimeSpan.FromSeconds(3)).WithStatusCode(201).WithBodyAsJson(InvoiceBody()));
        Stub(Request.Create().WithPath(OutcomePath).UsingGet(), Response.Create().WithStatusCode(404));

        var error = (await Gateway().IssueInvoiceAsync(IssueRequest(), Token)).Error.ShouldNotBeNull();

        // Nothing was invoiced, so the caller may send the same key again on the Wolverine schedule.
        error.Code.ShouldBe("billing-provider-unavailable");
        OutcomeQueries().ShouldBe(1);
    }

    [Fact]
    public async Task An_open_circuit_fails_the_next_call_without_reaching_the_provider()
    {
        Stub(Request.Create().WithPath(InvoicesPath).UsingPost(), Response.Create().WithStatusCode(503));
        var gateway = Gateway(settings => settings[$"{BillingHttpOptions.SectionName}:CircuitBreakerMinimumCalls"] = "2");

        var failures = new List<Error?>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            failures.Add((await gateway.IssueInvoiceAsync(IssueRequest(), Token)).Error);
        }

        failures.ShouldAllBe(error => error != null && error.Code == "billing-provider-unavailable");
        // Two calls opened the circuit; the third never left the process.
        Requests("POST", InvoicesPath).ShouldBe(2);
    }

    [Fact]
    public async Task Voiding_and_refunding_send_the_key_of_their_own_step()
    {
        Stub(Request.Create().WithPath($"{InvoicePath}/void").UsingPost(),
            Response.Create().WithStatusCode(200).WithBodyAsJson(InvoiceBody("VOID")));
        Stub(Request.Create().WithPath($"{InvoicePath}/refunds").UsingPost(), Response.Create().WithStatusCode(201).WithBodyAsJson(
            new { refundId = "RFN-7", invoiceId = InvoiceId, amountMinor = 12000, currency = "EUR", refundedAt = Now }));
        var gateway = Gateway();

        var voided = await gateway.VoidInvoiceAsync(InvoiceId, BillingIdempotencyKey.Void(OrderId), Token);
        var refund = (await gateway.RefundInvoiceAsync(InvoiceId, Total, BillingIdempotencyKey.Refund(OrderId), Token)).Value.ShouldNotBeNull();

        voided.IsSuccess.ShouldBeTrue(voided.Error?.Message);
        refund.RefundId.ShouldBe("RFN-7");
        refund.Amount.ShouldBe(Total);
        Keys().ShouldBe([$"void:{OrderId}", $"refund:{OrderId}"]);
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var provider in providers)
        {
            await provider.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>The adapter as the module registers it for <c>Integrations:Billing:Mode=Http</c>, pointed at WireMock.</summary>
    private IBillingGateway Gateway(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{BillingHttpOptions.SectionName}:BaseAddress"] = Server.Url,
            // Comfortably above the first call through a fresh HttpClient (~0.5s here), well below the slow stubs below.
            [$"{BillingHttpOptions.SectionName}:AttemptTimeout"] = "00:00:01.500",
            [$"{BillingHttpOptions.SectionName}:RetryDelay"] = "00:00:00.010",
            // High enough that the failure mappings above never open the circuit; the circuit test lowers it.
            [$"{BillingHttpOptions.SectionName}:CircuitBreakerMinimumCalls"] = "100",
        };
        configure?.Invoke(settings);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddBillingHttpGateway(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        var provider = services.BuildServiceProvider();
        providers.Add(provider);
        return provider.GetRequiredService<IBillingGateway>();
    }

    private void Stub(IRequestBuilder request, IResponseBuilder response) => Server.Given(request).RespondWith(response);

    private IEnumerable<IRequestMessage> Received() => Server.LogEntries.Select(entry => entry.RequestMessage!);

    private int Requests(string method, string path) =>
        Received().Count(request => request.Method == method && request.Path == path);

    private int OutcomeQueries() =>
        Received().Count(request => request.Path!.StartsWith("/v1/idempotency-keys/", StringComparison.Ordinal));

    private IEnumerable<string> Keys() => Received().Select(request => request.Headers!["Idempotency-Key"].Single());

    private static IssueInvoiceRequest IssueRequest() => new(OrderId, AccountId, Total, BillingIdempotencyKey.Issue(OrderId));

    private static object InvoiceBody(string? status = "OPEN", long paidMinor = 0, string invoiceId = InvoiceId) => new
    {
        invoiceId,
        status,
        amountMinor = 12000L,
        paidMinor,
        currency = Money.Eur,
        issuedAt = IssuedAt,
        dueAt = IssuedAt.AddDays(3),
    };
}
