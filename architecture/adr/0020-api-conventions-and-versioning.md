# ADR-0020: HTTP API conventions, versioning and idempotency

- **Status:** Accepted
- **Date:** 2026-09-17
- **Related:** ADR-0009 (expand/contract), ADR-0010 (event and message versioning), ADR-0011 (minimal APIs), ADR-0016 (security), ADR-0019 (feature flags)

## Context

B2B customers integrate their own systems with our API and upgrade them on their own schedule. Our rollback rules cover
the database, events and messages, but the public HTTP API is a contract too: a deployment or a rollback must not break
integrated clients. Clients also retry over unreliable networks, so creating requests must be safe to repeat.

## Decision

### Versioning

- **URL path version**: `/v1/orders`, `/v1/backoffice/orders`. Implemented with route groups; no versioning library
  until a second version exists.
- **Within a version, changes are additive only**: new endpoints, new optional request fields, new response fields.
- **Clients must tolerate** unknown response fields and **unknown enum values** (e.g. new order statuses) — stated in
  the OpenAPI description and the integration guide.
- The API ignores unknown request fields.
- A breaking change creates `/v2` alongside `/v1`. `/v1` is deprecated with `Deprecation` and `Sunset` response headers
  and a minimum support period (default 6 months) communicated to customers.
- **Rollback safety:** new endpoints and new response fields are exposed behind a release flag (ADR-0019), enabled after
  the rollout is complete, so a rollback does not remove something clients have already started using.

### Request and response conventions

- JSON, `camelCase`, enums as strings.
- Timestamps in UTC, ISO 8601 (`2026-09-17T10:15:00Z`).
- Money: `{ "amount": "1234.50", "currency": "EUR" }` — amount as a **decimal string** to avoid floating-point loss in clients.
- Identifiers are opaque strings (UUIDs); clients must not parse them.
- Collections use cursor-based pagination: `?limit=50&cursor=...`, response `{ "items": [...], "nextCursor": "..." }`.
- Long-running operations return `202 Accepted` with a `Location` header to the order; clients read the status there.

### Errors

- All errors use **RFC 9457 problem details** (`application/problem+json`).
- Each business error has a **stable `type` URI and `errorCode`** (e.g. `order-not-cancellable`) that clients can rely
  on; the `title` and `detail` texts may change.
- Extensions: `traceId` (for support), `currentState` where relevant, and `errors` for field-level validation errors.

| Status | Used for |
|---|---|
| `400` | Malformed request, validation errors |
| `401` / `403` | Missing or invalid token / missing scope or role |
| `404` | Not found, **including orders of another account** (existence not leaked) |
| `409` | Command not allowed in the current order state; concurrent request with the same idempotency key |
| `422` | Business rule violation (e.g. unknown SKU); idempotency key reused with a different request body |
| `429` | Rate limit exceeded, with `Retry-After` |

### Idempotency

- `Idempotency-Key` header is **required** on `POST` endpoints that create or change state
  (`POST /v1/orders`, `/cancel`, `/items/reduce`, `/payment-status/refresh`, back-office commands) and optional on `PUT`.
- Keys are **scoped per account** (per agent for back-office).
- Records are stored as **Marten documents in the `ordering` schema** — in the same transaction as the order events they
  protect, so a crash can never store the order without the key or vice versa.
- A record holds: key, account, hash of the request body, response status and body, created timestamp.
- Behaviour:
  - Same key, same body, completed → return the stored response.
  - Same key, same body, still in progress → `409`.
  - Same key, **different body** → `422`.
- **Retention: 24 hours**, removed by a daily scheduled cleanup message.

### Documentation

- OpenAPI document per version generated with `Microsoft.AspNetCore.OpenApi`; an interactive UI is enabled in Development only.
- The OpenAPI document is published as a build artifact; a CI check flags **removed or changed** operations and fields
  compared with the previous release (breaking change detection).

## Alternatives considered

| Option | Why not |
|---|---|
| Header or media-type versioning | Less visible in logs, tooling and customer support conversations |
| No versioning, evolve in place | Every breaking change would break integrated customer systems |
| Idempotency records in a separate table via Dapper | Not atomic with Marten event appends in the Ordering module |
| Money as JSON number | Some clients parse as floating point and lose precision |

## Consequences

- Positive: customers can integrate safely; deployments and rollbacks do not break clients; retries are safe; errors are machine-readable.
- Negative: additive-only discipline within a version; idempotency adds a document write to every command; deprecation periods keep old versions alive.
