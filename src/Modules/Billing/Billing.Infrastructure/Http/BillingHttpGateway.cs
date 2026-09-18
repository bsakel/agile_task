using System.Net;
using System.Net.Http.Json;
using OrderPlatform.Billing.Application.Integration;
using OrderPlatform.BuildingBlocks;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderPlatform.Billing.Infrastructure.Http;

/// <summary>
/// The <see cref="IBillingGateway"/> adapter for the external billing provider, selected by
/// <c>Integrations:Billing:Mode=Http</c> (ADR-0014). The provider contract it is written against is documented in
/// <c>README.md</c> next to this file; its resilience pipeline is configured in <see cref="BillingHttpGatewayExtensions"/>.
/// Provider failures become <see cref="Result"/> failures with the error codes of <see cref="BillingErrors"/>, and a call
/// whose outcome is unknown is resolved by the outcome query rather than by sending it again (ADR-0017 §7).
/// </summary>
public sealed class BillingHttpGateway(HttpClient client, TimeProvider timeProvider) : IBillingGateway
{
    /// <summary>The header the provider deduplicates state-changing calls by.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public async Task<Result<Invoice>> IssueInvoiceAsync(IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var body = new IssueInvoiceBody(
            request.OrderId.ToString(), request.AccountId.ToString(), BillingProviderContract.ToMinorUnits(request.Total), request.Total.Currency);

        // No invoice id is in play yet, so a 404 here is a wrong base address, not a missing invoice.
        var call = await CallAsync<ProviderInvoice>(HttpMethod.Post, "v1/invoices", body, request.Key, null, cancellationToken);

        if (call.OutcomeUnknown)
        {
            return await RecoverInvoiceAsync(request, cancellationToken);
        }

        return call.Result.IsSuccess ? call.Result.Value.ToInvoice(request.OrderId) : call.Result.Error;
    }

    public async Task<Result> VoidInvoiceAsync(string invoiceId, BillingIdempotencyKey key, CancellationToken cancellationToken)
    {
        var call = await CallAsync<ProviderInvoice>(
            HttpMethod.Post, $"{InvoicePath(invoiceId)}/void", null, key, BillingErrors.InvoiceNotFound(invoiceId), cancellationToken);

        if (call.OutcomeUnknown)
        {
            // The void may already have been applied; the outcome query says so without voiding a second time.
            var outcome = await GetOutcomeAsync(key, cancellationToken);
            return outcome.IsSuccess ? Result.Success() : Result.Failure(Unresolved(key, outcome.Error));
        }

        return call.Result.IsSuccess ? Result.Success() : Result.Failure(call.Result.Error);
    }

    public async Task<Result<InvoicePayment>> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var call = await CallAsync<ProviderInvoice>(
            HttpMethod.Get, InvoicePath(invoiceId), null, null, BillingErrors.InvoiceNotFound(invoiceId), cancellationToken);

        return call.Result.IsSuccess ? call.Result.Value.ToPayment(timeProvider.GetUtcNow()) : call.Result.Error;
    }

    public async Task<Result<Refund>> RefundInvoiceAsync(string invoiceId, Money amount, BillingIdempotencyKey key, CancellationToken cancellationToken)
    {
        var body = new RefundBody(BillingProviderContract.ToMinorUnits(amount), amount.Currency);
        var call = await CallAsync<ProviderRefund>(
            HttpMethod.Post, $"{InvoicePath(invoiceId)}/refunds", body, key, BillingErrors.InvoiceNotFound(invoiceId), cancellationToken);

        if (call.OutcomeUnknown)
        {
            // The outcome record names the refund; its amount is the one we asked for, so no second call is needed.
            var outcome = await GetOutcomeAsync(key, cancellationToken);
            return outcome.IsSuccess
                ? new Refund(outcome.Value.Reference, invoiceId, amount, outcome.Value.CompletedAt)
                : Unresolved(key, outcome.Error);
        }

        return call.Result.IsSuccess ? call.Result.Value.ToRefund() : call.Result.Error;
    }

    public async Task<Result<BillingOutcome>> GetOutcomeAsync(BillingIdempotencyKey key, CancellationToken cancellationToken)
    {
        var path = $"v1/idempotency-keys/{Uri.EscapeDataString(key.Value)}";
        var call = await CallAsync<ProviderOutcome>(HttpMethod.Get, path, null, null, BillingErrors.OutcomeUnknown(key), cancellationToken);

        if (!call.Result.IsSuccess)
        {
            return call.Result.Error;
        }

        var outcome = call.Result.Value;
        return BillingProviderContract.ToOperation(outcome.Operation) is { } operation
            ? new BillingOutcome(key, operation, outcome.Reference, outcome.CompletedAt)
            : BillingErrors.ProviderRejected(outcome.Operation ?? "?", $"Unknown operation recorded for idempotency key '{key}'.");
    }

    /// <summary>
    /// The invoice an earlier issue call produced, read after an unknown outcome: the outcome query names it, the invoice
    /// call returns it. That way a timed-out issue never produces a second invoice (ADR-0014).
    /// </summary>
    private async Task<Result<Invoice>> RecoverInvoiceAsync(IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var outcome = await GetOutcomeAsync(request.Key, cancellationToken);
        if (!outcome.IsSuccess)
        {
            return Unresolved(request.Key, outcome.Error);
        }

        var invoiceId = outcome.Value.Reference;
        var call = await CallAsync<ProviderInvoice>(
            HttpMethod.Get, InvoicePath(invoiceId), null, null, BillingErrors.InvoiceNotFound(invoiceId), cancellationToken);

        return call.Result.IsSuccess ? call.Result.Value.ToInvoice(request.OrderId) : call.Result.Error;
    }

    /// <summary>
    /// One call to the provider. <paramref name="notFound"/> is what a <c>404</c> means for this call, and
    /// <paramref name="key"/> marks the call as state-changing: only those can leave an unknown outcome behind.
    /// </summary>
    private async Task<ProviderCall<T>> CallAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        BillingIdempotencyKey? key,
        Error? notFound,
        CancellationToken cancellationToken)
    {
        ProviderCall<T> Failed(Exception exception, bool outcomeUnknown) => new(
            BillingErrors.ProviderUnavailable($"{method} {path} did not complete: {exception.Message}"), outcomeUnknown);

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: BillingProviderContract.Json);
        }

        if (key is not null)
        {
            request.Headers.Add(IdempotencyKeyHeader, key.Value.Value);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ProviderCall<T>(await FailureAsync(response, path, notFound, cancellationToken), OutcomeUnknown: false);
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(BillingProviderContract.Json, cancellationToken);
            return new ProviderCall<T>(
                payload is not null
                    ? Result<T>.Success(payload)
                    : BillingErrors.ProviderUnavailable($"{method} {path} returned a body this version cannot read."),
                OutcomeUnknown: false);
        }
        catch (BrokenCircuitException exception)
        {
            // The open circuit short-circuited the call, so it never reached the provider: nothing to query (ADR-0014).
            return Failed(exception, outcomeUnknown: false);
        }
        catch (TimeoutRejectedException exception)
        {
            return Failed(exception, outcomeUnknown: key is not null);
        }
        catch (HttpRequestException exception)
        {
            return Failed(exception, outcomeUnknown: key is not null);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(exception, outcomeUnknown: key is not null);
        }
    }

    private static async Task<Error> FailureAsync(HttpResponseMessage response, string path, Error? notFound, CancellationToken cancellationToken)
    {
        var failure = await ReadFailureAsync(response, cancellationToken);
        var detail = $"{(int)response.StatusCode} from {path}: {failure?.Code ?? "no code"} {failure?.Message}".TrimEnd();

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound when notFound is not null => notFound,
            HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => BillingErrors.ProviderUnavailable(detail),
            >= HttpStatusCode.InternalServerError => BillingErrors.ProviderUnavailable(detail),
            // Everything else the provider refuses is a call that will be refused again in the same form.
            _ => BillingErrors.ProviderRejected(failure?.Code ?? response.StatusCode.ToString(), detail),
        };
    }

    private static async Task<ProviderFailure?> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProviderFailure>(BillingProviderContract.Json, cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            // A gateway or proxy answering instead of the provider; the status code alone decides the mapping.
            return null;
        }
    }

    /// <summary>
    /// The call did not complete and the provider has no outcome for the key: report it as unavailable, so the caller
    /// retries on the Wolverine schedule with the same key, which the provider deduplicates (ADR-0014).
    /// </summary>
    private static Error Unresolved(BillingIdempotencyKey key, Error outcome) => outcome.Kind == ErrorKind.Conflict
        ? outcome
        : BillingErrors.ProviderUnavailable(
            $"The call for idempotency key '{key}' did not complete and the provider has no outcome for it ({outcome.Code}); it may be sent again.");

    private static string InvoicePath(string invoiceId) => $"v1/invoices/{Uri.EscapeDataString(invoiceId)}";

    /// <summary>The result of one provider call, and whether it may have taken effect without us knowing.</summary>
    private readonly record struct ProviderCall<T>(Result<T> Result, bool OutcomeUnknown);
}
