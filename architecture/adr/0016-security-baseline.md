# ADR-0016: Security baseline

- **Status:** Accepted
- **Date:** 2026-09-17
- **Related:** ADR-0013 (local development), ADR-0017 (order lifecycle), ADR-0020 (API conventions)

## Context

The platform handles B2B customer data (including personal data of customer contacts), invoices and refunds. It is
called both by customer systems and by portal users, and by internal support agents. The skeleton must set secure
defaults so features inherit them.

## Decision

### Authentication

- JWT bearer tokens from an external **OpenID Connect** identity provider; the platform stores no passwords.
- **Customer system integrations:** OAuth2 **client credentials**; each client is registered for exactly one customer
  account and its tokens carry the `account_id` claim.
- **Portal users:** authorization code flow with PKCE (portal itself out of scope); tokens carry `account_id`. Order
  scopes are **optional** client scopes for users: the portal requests only the scopes a screen needs (least privilege).
  Customer system clients get their scopes by default.
- **Support agents:** authenticated through the same provider, with a `support-agent` role; no `account_id` restriction.
- **Local development:** a **Keycloak** container in both Aspire and docker-compose with a pre-configured realm:
  one customer client per test account (two accounts), one portal user, one support agent (ADR-0013).

### Authorization

- Policy-based, deny by default. Scopes such as `orders:read`, `orders:write`, `orders:cancel`.
- **Account scoping:** handlers check that the order belongs to the caller's `account_id`. An order of another account
  returns `404`, not `403`, so existence is not leaked (ADR-0020).
- Back-office endpoints require the `support-agent` policy; support actions record the agent identity and a mandatory
  reason on the order stream.
- Implementation (PR 1b): a fallback policy requires an authenticated caller on every endpoint (health probes and the
  OpenAPI document opt out explicitly). Policies: one per scope (named like the scope), `customer-account` (token has
  `account_id`) and `support-agent` (`roles` contains `support-agent`). Customer endpoints combine `customer-account`
  with a scope policy. Handlers receive the caller's account id in the command and use `AccountAccess.EnsureOwnedBy`,
  which returns a not-found error. Token claims are kept as issued (`sub`, `scope`, `roles`, `account_id`).

### Abuse protection

- ASP.NET Core **rate limiting partitioned by `account_id`**, with a stricter limit on `POST /v1/orders`. Limits are
  configuration; exceeding them returns `429` with `Retry-After`.
- Partitions (revised in PR 1b): `account:<account_id>` for customer callers, `agent:<sub>` for authenticated callers
  without an account (support agents), and the **remote address** for unauthenticated requests — an unauthenticated
  request has no verified client id, so the original "client id" wording could not be implemented. Behind a proxy or
  ingress, forwarded headers must be configured so the remote address is the client's (Phase 4 hardening).

### Personal data

- Personal data (contact names, emails, phone numbers, addresses) lives **only in the Customers module**, in the
  `customers` schema, where it can be updated or erased.
- **Events and messages carry references** (`contactId`, `billingAddressId`, `shippingAddressId`), never personal data,
  because Marten events are permanent (ADR-0006).
- Where an order must preserve the address it shipped to, the Customers module keeps an immutable **address snapshot**
  referenced by id; erasure anonymises the snapshot rather than breaking the reference.
- Alternative kept on record: crypto-shredding (per-customer encryption keys for personal data in events), if
  references prove insufficient.
- No personal data or secrets in logs or trace attributes.

### Data access

- **v1:** two database roles — an **API role** with data read/write on all application schemas and **no DDL rights**,
  and a **Migrator role** with DDL rights (ADR-0008).
- **Target:** one role per module, restricted to its own schema, introduced when a module is extracted or cross-schema
  access needs hard enforcement (ADR-0015 relies on this rather than SQL text scanning).
- Parameterised SQL only (Dapper parameters, never string concatenation).

### Other baseline controls

- **Transport:** HTTPS only, HSTS in production.
- **Input:** request validation at the endpoint (ADR-0020).
- **Secrets:** never in source or images; user-secrets / Aspire parameters locally, the platform secret store in deployed environments (ADR-0021).
- **Payments:** payment by invoice through the external billing system; no card or bank credentials are stored or processed.
- **Webhooks (later):** signature verification and replay protection.
- **Supply chain:** central package management, dependency vulnerability scanning and container image scanning in CI (ADR-0021).

## Consequences

- Positive: secure defaults from the first endpoint; realistic auth flows locally; erasure requests do not conflict with event sourcing.
- Negative: Keycloak adds a container and realm configuration to maintain; references to personal data require a lookup
  when displaying order details; v1 roles do not isolate modules at the database level.
