# ADR-0014: External integrations via ports, adapters and resilience

- **Status:** Accepted — queue-level circuit breaker removed after spike S7 ([phase-0-results](../phase-0-results.md#s7--circuit-breaker-on-an-integration-queue))
- **Date:** 2026-09-17
- **Related:** ADR-0005 (durable messaging), ADR-0017 (order lifecycle), ADR-0022 (testing strategy)

## Context

Inventory, billing, fulfilment/shipping and the customer CRM/ERP are external systems with their own models, failure
modes and latency. Their vocabulary must not leak into our domain, and their outages must not cascade into ours.

## Decision

### Ports and anti-corruption layers

- Each module's `Application` project defines a **port** (e.g. `IBillingGateway`) in our language.
- `Infrastructure` provides **adapters** acting as an **anti-corruption layer**: typed `HttpClient`, provider DTOs,
  mapping to/from our models, error translation to `Result` failures. Unknown provider statuses map to an explicit
  "unknown" result, never to a guessed one.
- External calls are made from **message handlers**, never inside an HTTP request (exception: none in v1 — reference
  data needed synchronously is read from local copies, see Customers and Pricing).

### Retries: one owner per time scale

Retrying at two layers multiplies attempts against a struggling system. Responsibilities are split:

| Layer | Owns | Configuration |
|---|---|---|
| HTTP resilience pipeline (`Microsoft.Extensions.Http.Resilience`) | Per-call protection | Timeout per attempt, **at most one** quick retry for transient failures (connection reset, 503) on idempotent calls, **circuit breaker** so calls to a failing provider fail immediately |
| Wolverine error policies | Long-running recovery | **Scheduled** retries with exponential backoff (e.g. 1 min → 5 min → 30 min → 2 h), then dead letter; the Order process then moves to `RequiresAttention` (ADR-0017) |

- Retries for integration failures are always **scheduled** retries (the message goes back to durable storage until its
  retry time), never inline retries, so a provider outage does not dead-letter messages within seconds (S7: inline
  retries dead-lettered 8 of 30 messages).
- **No Wolverine listener circuit breaker** on durable local queues. In S7 the combination of listener circuit breaker,
  durable local queue and scheduled retries **stalled processing until a process restart**. During an outage, the HTTP
  circuit breaker makes each attempt cheap and the scheduled backoff limits the load on the provider. The stall is
  reported upstream; this decision is revisited if it is fixed.
- Each integration uses its **own local queue** (e.g. `billing-integration`) with a bounded parallelism, so one
  failing provider does not consume the workers of other modules.

### Idempotency and unknown outcomes

- **Idempotency keys** on all state-changing calls (reservation id, invoice id, refund id, shipment request id).
- After a timeout, the adapter **queries the outcome** for that key before retrying, so an unknown outcome never causes
  a duplicate reservation, invoice or refund.

### Pull before push

Where an external system can notify us (billing payment events, shipping events), v1 polls on a schedule and on demand.
Push integration (webhooks) is added later and feeds the **same command** (ADR-0017). Inbound webhooks, when added, are
signature-verified, stored raw, and converted into messages.

### Adapter selection and the production guard

- Each integration has a mode in configuration: `Integrations:<System>:Mode = Fake | Http`.
- **Fake adapters** exist for local development and tests.
- **The host fails to start if any integration is in `Fake` mode outside the `Development` or `Test` environments.**

### Verifying adapters

- **Adapter tests with WireMock.Net** exercise each HTTP adapter against recorded or documented provider responses:
  success, each error mapping, timeouts, and the outcome query after timeout.
- Fakes are used only for module and integration tests; they are kept simple and are not the proof that an adapter works.

## Consequences

- Positive: providers swappable; domain testable without network; failures contained; no retry storms; fakes cannot
  leak into production.
- Negative: mapping code per provider; recorded responses must be refreshed when providers change their APIs.
