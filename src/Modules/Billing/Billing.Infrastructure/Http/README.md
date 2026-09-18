Example HTTP contract of the external billing provider, which `BillingHttpGateway` is written against. It stands in for
the real provider's documentation until that is available (plan: next steps). The WireMock.Net stubs in
`tests/Billing.Infrastructure.Tests/BillingHttpGatewayTests.cs` are the executable copy of every response below, and the
anti-corruption mapping lives in `BillingProviderContract.cs` — the provider's vocabulary stops there (ADR-0014).

Base address, credentials and resilience settings: `Integrations:Billing:Http` (`BillingHttpOptions`).

## Conventions

- JSON, camelCase. Amounts are integer **minor units** next to a currency (`"amountMinor": 12000, "currency": "EUR"`);
  our `Money` is decimal, so the adapter converts in both directions.
- Timestamps are ISO 8601 with an offset.
- State-changing calls carry our own key in the `Idempotency-Key` header (`invoice:{orderId}`, ADR-0017 §7). Repeating a
  key returns the first call's result; the provider never performs the operation twice.
- Failures carry `{ "code": "INVOICE_NOT_FOUND", "message": "..." }` with the provider's own code.

## Endpoints

| Call | Request | Success |
|---|---|---|
| `POST /v1/invoices` | `{ orderReference, accountReference, amountMinor, currency }` + `Idempotency-Key` | `201` invoice |
| `POST /v1/invoices/{invoiceId}/void` | no body + `Idempotency-Key` | `200` invoice |
| `GET /v1/invoices/{invoiceId}` | — | `200` invoice |
| `POST /v1/invoices/{invoiceId}/refunds` | `{ amountMinor, currency }` + `Idempotency-Key` | `201` refund |
| `GET /v1/idempotency-keys/{key}` | — | `200` outcome |

- Invoice: `{ invoiceId, status, amountMinor, paidMinor, currency, issuedAt, dueAt }`. The provider sets `dueAt`
  (3 calendar days, README §2).
- Refund: `{ refundId, invoiceId, amountMinor, currency, refundedAt }`.
- Outcome: `{ key, operation, reference, completedAt }`, where `operation` is `INVOICE_ISSUED`, `INVOICE_VOIDED` or
  `REFUND_ISSUED` and `reference` is the invoice or refund id the key produced.

## Invoice status → `InvoicePaymentState`

| Provider `status` | Ours |
|---|---|
| `OPEN` | `Unpaid` |
| `PART_PAID` | `PartiallyPaid` |
| `SETTLED` | `Paid` |
| `VOID` | `Voided` |
| `CREDITED` | `Refunded` |
| anything else | `Unknown` — the lifecycle stops instead of guessing (ADR-0014) |

## Failures → `Result` failures

| Provider response | Our failure | What the caller does |
|---|---|---|
| `404` on an invoice call | `billing-invoice-not-found` (NotFound) | Gives up; the invoice id is wrong |
| `404` on the outcome query | `billing-outcome-unknown` (NotFound) | The call never took effect and may be sent again |
| `400`, `401`, `403`, `409`, `422` | `billing-provider-rejected` (Conflict), carrying the provider's code | Sending the same call again will not help |
| `408`, `429`, `5xx` | `billing-provider-unavailable` (Integration) | Scheduled Wolverine retry with the same key |
| Connection failure, per-attempt timeout, open circuit | `billing-provider-unavailable` | Same |

## Unknown outcomes

A state-changing call that times out or loses its connection may or may not have been applied. The adapter never sends it
again by itself: it asks `GET /v1/idempotency-keys/{key}` what the key produced (ADR-0014, ADR-0017 §7).

- Outcome found → the call succeeded; for an issue the adapter reads the named invoice and returns it, so no second
  invoice is created.
- Outcome unknown (`404`) or the query itself fails → `billing-provider-unavailable`, and the scheduled retry sends the
  call again with the same key.

## Resilience pipeline (ADR-0014)

Outermost to innermost, configured in `BillingHttpGatewayExtensions`:

| Strategy | Setting |
|---|---|
| Retry | At most **one**, and only for idempotent methods (`DisableForUnsafeHttpMethods`); unknown outcomes of `POST` are resolved by the outcome query instead |
| Circuit breaker | Opens after `CircuitBreakerFailureRatio` of at least `CircuitBreakerMinimumCalls` calls fail; while open, every call fails immediately |
| Timeout | One per attempt (`AttemptTimeout`) |

The pipeline runs on `TimeProvider.System`, not on the platform clock: timeouts and the breaker's window measure real
elapsed time, and tests that replace the platform clock with a `FakeTimeProvider` would otherwise freeze them.

Long-running recovery is not the adapter's job: it belongs to the Wolverine scheduled retries of the calling handler.
