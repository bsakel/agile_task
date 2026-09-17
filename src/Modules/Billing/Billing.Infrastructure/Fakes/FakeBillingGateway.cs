using Microsoft.Extensions.Options;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Billing.Infrastructure.Fakes;

/// <summary>
/// In-memory billing provider for local development and tests, selected by <c>Integrations:Billing:Mode=Fake</c>
/// (ADR-0014). It keeps invoices in memory, honours our idempotency keys and can be told to report any payment state or
/// to be unavailable. It is not proof that the HTTP adapter works — the PR 3c WireMock.Net tests are.
/// </summary>
public sealed class FakeBillingGateway(IOptions<FakeBillingOptions> options, TimeProvider timeProvider) : IBillingGateway
{
    /// <summary>Payment is due 3 calendar days after issuing, in UTC (README §2, ADR-0017 §4).</summary>
    private static readonly TimeSpan PaymentTerm = TimeSpan.FromDays(3);

    private readonly Lock gate = new();
    private readonly Dictionary<string, Invoice> invoices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Refund> refunds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InvoicePaymentState> settlements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BillingOutcome> outcomes = new(StringComparer.Ordinal);
    private int sequence;

    /// <summary>The live options; tests change the reported payment state or the outage through them.</summary>
    public FakeBillingOptions Options { get; } = options.Value;

    public Task<Result<Invoice>> IssueInvoiceAsync(IssueInvoiceRequest request, CancellationToken cancellationToken) =>
        Call<Invoice>(nameof(IssueInvoiceAsync), () =>
        {
            // The key, not the order id, decides: a retried call must not produce a second invoice (ADR-0014).
            if (outcomes.TryGetValue(request.Key.Value, out var recorded))
            {
                return invoices[recorded.Reference];
            }

            var issuedAt = timeProvider.GetUtcNow();
            var invoice = new Invoice(NextId("INV"), request.OrderId, request.Total, issuedAt, issuedAt + PaymentTerm);
            invoices.Add(invoice.InvoiceId, invoice);
            Record(request.Key, BillingOperation.IssueInvoice, invoice.InvoiceId, issuedAt);
            return invoice;
        });

    public Task<Result> VoidInvoiceAsync(string invoiceId, BillingIdempotencyKey key, CancellationToken cancellationToken) =>
        Call(nameof(VoidInvoiceAsync), () =>
        {
            if (outcomes.ContainsKey(key.Value))
            {
                return Result.Success();
            }

            if (!invoices.ContainsKey(invoiceId))
            {
                return BillingErrors.InvoiceNotFound(invoiceId);
            }

            settlements[invoiceId] = InvoicePaymentState.Voided;
            Record(key, BillingOperation.VoidInvoice, invoiceId, timeProvider.GetUtcNow());
            return Result.Success();
        });

    public Task<Result<InvoicePayment>> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken) =>
        Call<InvoicePayment>(nameof(GetInvoiceStatusAsync), () =>
        {
            if (!invoices.TryGetValue(invoiceId, out var invoice))
            {
                return BillingErrors.InvoiceNotFound(invoiceId);
            }

            var state = settlements.TryGetValue(invoiceId, out var settled) ? settled : Options.PaymentState;
            var paid = state switch
            {
                InvoicePaymentState.Paid => invoice.Total.Amount,
                InvoicePaymentState.PartiallyPaid => decimal.Round(invoice.Total.Amount * Options.PartialPaymentShare, 2, MidpointRounding.ToEven),
                _ => decimal.Zero,
            };

            return new InvoicePayment(invoiceId, state, new Money(paid, invoice.Total.Currency), invoice.Total, timeProvider.GetUtcNow());
        });

    public Task<Result<Refund>> RefundInvoiceAsync(string invoiceId, Money amount, BillingIdempotencyKey key, CancellationToken cancellationToken) =>
        Call<Refund>(nameof(RefundInvoiceAsync), () =>
        {
            if (outcomes.TryGetValue(key.Value, out var recorded))
            {
                return refunds[recorded.Reference];
            }

            if (!invoices.ContainsKey(invoiceId))
            {
                return BillingErrors.InvoiceNotFound(invoiceId);
            }

            var refund = new Refund(NextId("RFN"), invoiceId, amount, timeProvider.GetUtcNow());
            refunds.Add(refund.RefundId, refund);
            settlements[invoiceId] = InvoicePaymentState.Refunded;
            Record(key, BillingOperation.RefundInvoice, refund.RefundId, refund.RefundedAt);
            return refund;
        });

    public Task<Result<BillingOutcome>> GetOutcomeAsync(BillingIdempotencyKey key, CancellationToken cancellationToken) =>
        Call<BillingOutcome>(nameof(GetOutcomeAsync), () =>
            outcomes.TryGetValue(key.Value, out var outcome) ? outcome : BillingErrors.OutcomeUnknown(key));

    private Task<Result<T>> Call<T>(string operation, Func<Result<T>> call)
    {
        if (Options.ProviderUnavailable)
        {
            return Task.FromResult<Result<T>>(Unavailable(operation));
        }

        lock (gate)
        {
            return Task.FromResult(call());
        }
    }

    private Task<Result> Call(string operation, Func<Result> call)
    {
        if (Options.ProviderUnavailable)
        {
            return Task.FromResult(Result.Failure(Unavailable(operation)));
        }

        lock (gate)
        {
            return Task.FromResult(call());
        }
    }

    private void Record(BillingIdempotencyKey key, BillingOperation operation, string reference, DateTimeOffset completedAt) =>
        outcomes.Add(key.Value, new BillingOutcome(key, operation, reference, completedAt));

    private string NextId(string prefix) => $"{prefix}-{++sequence:D6}";

    private static Error Unavailable(string operation) =>
        BillingErrors.ProviderUnavailable($"The fake billing provider is configured as unavailable ({operation}).");
}

/// <summary>
/// Controls the fake provider from configuration (<c>Integrations:Billing:Fake</c>) or from a test. The properties are
/// read on every call, so a test can change what the provider reports while an order is in flight.
/// </summary>
public sealed class FakeBillingOptions
{
    public const string SectionName = "Integrations:Billing:Fake";

    /// <summary>Payment state reported for invoices that were not voided or refunded.</summary>
    public InvoicePaymentState PaymentState { get; set; } = InvoicePaymentState.Unpaid;

    /// <summary>Share of the total that counts as received while <see cref="PaymentState"/> is partially paid.</summary>
    public decimal PartialPaymentShare { get; set; } = 0.5m;

    /// <summary>Simulates a provider outage: every call fails with <c>billing-provider-unavailable</c> (ADR-0014).</summary>
    public bool ProviderUnavailable { get; set; }
}
