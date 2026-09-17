using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Billing.Application.Integration;

/// <summary>
/// Invoice request for one order, priced from the latest recorded breakdown (ADR-0017 §2). Carries ids only — the
/// provider resolves the account's billing address and contact from its own customer record (ADR-0016).
/// </summary>
public sealed record IssueInvoiceRequest(Guid OrderId, Guid AccountId, Money Total, BillingIdempotencyKey Key);

/// <summary>An invoice as the provider issued it. <c>DueAt</c> drives the overdue timer (ADR-0017 §4).</summary>
public sealed record Invoice(string InvoiceId, Guid OrderId, Money Total, DateTimeOffset IssuedAt, DateTimeOffset DueAt);

/// <summary>How much of an invoice the provider has been paid (README §2: a partial payment counts as not paid).</summary>
public sealed record InvoicePayment(string InvoiceId, InvoicePaymentState State, Money AmountPaid, Money Total, DateTimeOffset AsOf);

/// <summary>
/// Payment states the order lifecycle reacts to (ADR-0017 §4). <c>Unknown</c> is what an adapter reports for a provider
/// status this version does not know, so the process stops instead of guessing (ADR-0014).
/// </summary>
public enum InvoicePaymentState
{
    Unpaid,
    PartiallyPaid,
    Paid,
    Voided,
    Refunded,
    Unknown,
}

/// <summary>A refund the provider accepted for an invoice.</summary>
public sealed record Refund(string RefundId, string InvoiceId, Money Amount, DateTimeOffset RefundedAt);

/// <summary>What an earlier call with an idempotency key produced, as recorded by the provider (ADR-0014).</summary>
/// <param name="Reference">The invoice id, or the refund id for <see cref="BillingOperation.RefundInvoice"/>.</param>
public sealed record BillingOutcome(BillingIdempotencyKey Key, BillingOperation Operation, string Reference, DateTimeOffset CompletedAt);

public enum BillingOperation
{
    IssueInvoice,
    VoidInvoice,
    RefundInvoice,
}
