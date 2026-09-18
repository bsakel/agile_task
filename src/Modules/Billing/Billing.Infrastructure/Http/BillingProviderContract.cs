using System.Text.Json;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Billing.Infrastructure.Http;

/// <summary>
/// The example provider's wire format and its translation into our models — the anti-corruption layer of ADR-0014. The
/// contract is documented in <c>README.md</c> next to this file; the provider's vocabulary goes no further than here.
/// </summary>
internal static class BillingProviderContract
{
    /// <summary>The provider speaks camelCase JSON and adds fields without notice, so unknown ones are ignored.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A status this version does not know becomes <see cref="InvoicePaymentState.Unknown"/>, never a guess.</summary>
    public static InvoicePaymentState ToPaymentState(string? status) => status switch
    {
        "OPEN" => InvoicePaymentState.Unpaid,
        "PART_PAID" => InvoicePaymentState.PartiallyPaid,
        "SETTLED" => InvoicePaymentState.Paid,
        "VOID" => InvoicePaymentState.Voided,
        "CREDITED" => InvoicePaymentState.Refunded,
        _ => InvoicePaymentState.Unknown,
    };

    /// <summary>An operation this version does not know has no safe interpretation, so it is reported as null.</summary>
    public static BillingOperation? ToOperation(string? operation) => operation switch
    {
        "INVOICE_ISSUED" => BillingOperation.IssueInvoice,
        "INVOICE_VOIDED" => BillingOperation.VoidInvoice,
        "REFUND_ISSUED" => BillingOperation.RefundInvoice,
        _ => null,
    };

    /// <summary>The provider counts in minor units; our <see cref="Money"/> is decimal.</summary>
    public static long ToMinorUnits(Money money) => (long)decimal.Round(money.Amount * 100m, MidpointRounding.ToEven);

    public static Money ToMoney(long minorUnits, string currency) => new(minorUnits / 100m, currency);
}

internal sealed record ProviderInvoice(
    string InvoiceId,
    string? Status,
    long AmountMinor,
    long PaidMinor,
    string Currency,
    DateTimeOffset IssuedAt,
    DateTimeOffset DueAt)
{
    public Invoice ToInvoice(Guid orderId) =>
        new(InvoiceId, orderId, BillingProviderContract.ToMoney(AmountMinor, Currency), IssuedAt, DueAt);

    public InvoicePayment ToPayment(DateTimeOffset asOf) => new(
        InvoiceId,
        BillingProviderContract.ToPaymentState(Status),
        BillingProviderContract.ToMoney(PaidMinor, Currency),
        BillingProviderContract.ToMoney(AmountMinor, Currency),
        asOf);
}

internal sealed record ProviderRefund(string RefundId, string InvoiceId, long AmountMinor, string Currency, DateTimeOffset RefundedAt)
{
    public Refund ToRefund() => new(RefundId, InvoiceId, BillingProviderContract.ToMoney(AmountMinor, Currency), RefundedAt);
}

internal sealed record ProviderOutcome(string? Operation, string Reference, DateTimeOffset CompletedAt);

/// <summary>The provider's failure body; both fields are absent on a bare gateway error page.</summary>
internal sealed record ProviderFailure(string? Code, string? Message);

internal sealed record IssueInvoiceBody(string OrderReference, string AccountReference, long AmountMinor, string Currency);

internal sealed record RefundBody(long AmountMinor, string Currency);
