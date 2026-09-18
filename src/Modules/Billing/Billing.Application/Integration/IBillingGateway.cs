using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Billing.Application.Integration;

/// <summary>
/// Port to the external billing system, in our language (ADR-0014). Adapters live in <c>Billing.Infrastructure</c>,
/// translate provider errors into <see cref="Result"/> failures and map an unknown provider status to
/// <see cref="InvoicePaymentState.Unknown"/> rather than guessing. Every state-changing call carries our own
/// <see cref="BillingIdempotencyKey"/>, so a retry after an unknown outcome cannot issue, void or refund twice; after a
/// timeout the caller asks <see cref="GetOutcomeAsync"/> what the key produced before retrying (ADR-0017 §7). Calls are
/// made from message handlers, never inside an HTTP request.
/// </summary>
public interface IBillingGateway
{
    /// <summary>Issues the invoice for an order. The provider sets the due date (3 calendar days, README §2).</summary>
    Task<Result<Invoice>> IssueInvoiceAsync(IssueInvoiceRequest request, CancellationToken cancellationToken);

    /// <summary>Voids an unpaid invoice when its order is cancelled (ADR-0017 §5).</summary>
    Task<Result> VoidInvoiceAsync(string invoiceId, BillingIdempotencyKey key, CancellationToken cancellationToken);

    /// <summary>Reads what the provider has been paid so far; a partial payment counts as not paid (README §2).</summary>
    Task<Result<InvoicePayment>> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>Refunds a paid invoice as part of the <c>Refunding</c> compensation (ADR-0017 §5).</summary>
    Task<Result<Refund>> RefundInvoiceAsync(string invoiceId, Money amount, BillingIdempotencyKey key, CancellationToken cancellationToken);

    /// <summary>
    /// What an earlier call with <paramref name="key"/> produced. Fails with <c>billing-outcome-unknown</c> when the
    /// provider has no record of the key, which means the call did not take effect and may be sent again.
    /// </summary>
    Task<Result<BillingOutcome>> GetOutcomeAsync(BillingIdempotencyKey key, CancellationToken cancellationToken);
}

/// <summary>
/// Our idempotency key for one billing step of one order, e.g. <c>invoice:{orderId}</c> (ADR-0017 §7). The same step
/// retried for the same order produces the same key, so the provider recognises the duplicate.
/// </summary>
public readonly record struct BillingIdempotencyKey(string Value)
{
    public static BillingIdempotencyKey Issue(Guid orderId) => new($"invoice:{orderId}");

    public static BillingIdempotencyKey Void(Guid orderId) => new($"void:{orderId}");

    public static BillingIdempotencyKey Refund(Guid orderId) => new($"refund:{orderId}");

    public override string ToString() => Value;
}

/// <summary>Failures every billing adapter returns, with stable error codes (ADR-0020).</summary>
public static class BillingErrors
{
    /// <summary>Provider down or call timed out; the caller retries on the Wolverine schedule (ADR-0014).</summary>
    public static Error ProviderUnavailable(string detail) => Error.Integration("billing-provider-unavailable", detail);

    /// <summary>The provider refused the call, e.g. a settled invoice that cannot be voided; sending it again will not help.</summary>
    public static Error ProviderRejected(string providerCode, string detail) =>
        Error.Conflict("billing-provider-rejected", $"The billing system rejected the call ({providerCode}): {detail}");

    public static Error InvoiceNotFound(string invoiceId) =>
        Error.NotFound("billing-invoice-not-found", $"The billing system does not know invoice '{invoiceId}'.");

    public static Error OutcomeUnknown(BillingIdempotencyKey key) =>
        Error.NotFound("billing-outcome-unknown", $"The billing system has no call recorded for idempotency key '{key}'.");
}
