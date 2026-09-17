using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.Billing.Infrastructure.Fakes;
using OrderPlatform.BuildingBlocks;

namespace OrderPlatform.Billing.Infrastructure.Tests;

/// <summary>
/// The fake billing provider selected by <c>Integrations:Billing:Mode=Fake</c> (ADR-0014). It has to behave like the
/// real provider in the ways the order lifecycle depends on: payment states, idempotency keys and outages.
/// </summary>
public sealed class FakeBillingGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 10, 30, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = new("0199a0d0-3a5f-7000-8000-00000000000a");
    private static readonly Money Total = Money.InEur(120.00m);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly FakeBillingOptions options = new();
    private readonly FakeBillingGateway gateway;

    public FakeBillingGatewayTests() => gateway = new FakeBillingGateway(Options.Create(options), new FakeTimeProvider(Now));

    [Fact]
    public async Task Issuing_an_invoice_returns_an_invoice_due_three_calendar_days_later()
    {
        var invoice = await IssueAsync();

        invoice.OrderId.ShouldBe(OrderId);
        invoice.Total.ShouldBe(Total);
        invoice.IssuedAt.ShouldBe(Now);
        invoice.DueAt.ShouldBe(Now.AddDays(3));
    }

    [Theory]
    [InlineData(InvoicePaymentState.Unpaid, "0.00")]
    [InlineData(InvoicePaymentState.PartiallyPaid, "60.00")]
    [InlineData(InvoicePaymentState.Paid, "120.00")]
    public async Task Invoice_status_reports_the_configured_payment_state(InvoicePaymentState state, string amountPaid)
    {
        var invoice = await IssueAsync();
        options.PaymentState = state;

        var payment = await StatusAsync(invoice);

        payment.State.ShouldBe(state);
        payment.AmountPaid.ShouldBe(Money.InEur(decimal.Parse(amountPaid, CultureInfo.InvariantCulture)));
        payment.Total.ShouldBe(Total);
    }

    [Fact]
    public async Task A_voided_invoice_reports_voided_whatever_the_configured_state_is()
    {
        var invoice = await IssueAsync();
        options.PaymentState = InvoicePaymentState.Paid;

        var voided = await gateway.VoidInvoiceAsync(invoice.InvoiceId, BillingIdempotencyKey.Void(OrderId), Token);

        voided.IsSuccess.ShouldBeTrue();
        (await StatusAsync(invoice)).State.ShouldBe(InvoicePaymentState.Voided);
    }

    [Fact]
    public async Task A_refunded_invoice_returns_a_refund_and_reports_refunded()
    {
        var invoice = await IssueAsync();
        options.PaymentState = InvoicePaymentState.Paid;

        var refund = (await RefundAsync(invoice.InvoiceId)).Value.ShouldNotBeNull();

        refund.InvoiceId.ShouldBe(invoice.InvoiceId);
        refund.Amount.ShouldBe(Total);
        (await StatusAsync(invoice)).State.ShouldBe(InvoicePaymentState.Refunded);
    }

    [Fact]
    public async Task Voiding_an_invoice_the_provider_does_not_know_is_a_failure() =>
        (await gateway.VoidInvoiceAsync("INV-999999", BillingIdempotencyKey.Void(OrderId), Token))
            .Error.ShouldNotBeNull().Code.ShouldBe("billing-invoice-not-found");

    [Fact]
    public async Task Repeating_the_idempotency_key_does_not_issue_a_second_invoice()
    {
        var first = await IssueAsync();
        var second = await IssueAsync();

        second.InvoiceId.ShouldBe(first.InvoiceId);
        second.IssuedAt.ShouldBe(first.IssuedAt);
    }

    [Fact]
    public async Task The_outcome_query_returns_the_result_of_an_earlier_call()
    {
        // What an adapter does after a timeout: ask what the key produced instead of issuing again (ADR-0014).
        var invoice = await IssueAsync();

        var outcome = (await gateway.GetOutcomeAsync(BillingIdempotencyKey.Issue(OrderId), Token)).Value.ShouldNotBeNull();

        outcome.Operation.ShouldBe(BillingOperation.IssueInvoice);
        outcome.Reference.ShouldBe(invoice.InvoiceId);
        outcome.CompletedAt.ShouldBe(invoice.IssuedAt);
    }

    [Fact]
    public async Task The_outcome_query_fails_when_the_provider_has_no_record_of_the_key() =>
        (await gateway.GetOutcomeAsync(BillingIdempotencyKey.Issue(OrderId), Token))
            .Error.ShouldNotBeNull().Code.ShouldBe("billing-outcome-unknown");

    [Fact]
    public async Task A_provider_outage_fails_every_call_until_it_is_over()
    {
        var invoice = await IssueAsync();
        options.ProviderUnavailable = true;

        Error?[] errors =
        [
            (await gateway.IssueInvoiceAsync(Request(), Token)).Error,
            (await gateway.GetInvoiceStatusAsync(invoice.InvoiceId, Token)).Error,
            (await gateway.VoidInvoiceAsync(invoice.InvoiceId, BillingIdempotencyKey.Void(OrderId), Token)).Error,
            (await RefundAsync(invoice.InvoiceId)).Error,
            (await gateway.GetOutcomeAsync(BillingIdempotencyKey.Issue(OrderId), Token)).Error,
        ];

        errors.ShouldAllBe(error => error != null && error.Code == "billing-provider-unavailable" && error.Kind == ErrorKind.Integration);

        options.ProviderUnavailable = false;
        (await StatusAsync(invoice)).State.ShouldBe(InvoicePaymentState.Unpaid);
    }

    private static IssueInvoiceRequest Request() =>
        new(OrderId, new Guid("0199a0d0-3a5f-7000-8000-00000000000b"), Total, BillingIdempotencyKey.Issue(OrderId));

    private async Task<Invoice> IssueAsync() =>
        (await gateway.IssueInvoiceAsync(Request(), Token)).Value.ShouldNotBeNull();

    private async Task<InvoicePayment> StatusAsync(Invoice invoice) =>
        (await gateway.GetInvoiceStatusAsync(invoice.InvoiceId, Token)).Value.ShouldNotBeNull();

    private Task<Result<Refund>> RefundAsync(string invoiceId) =>
        gateway.RefundInvoiceAsync(invoiceId, Total, BillingIdempotencyKey.Refund(OrderId), Token);
}
