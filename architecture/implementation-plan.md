# Implementation Plan

Status: **Agreed** — all ADRs accepted. **Phase 0 completed** ([results](phase-0-results.md)); plan and ADRs updated
with its findings. Next: Phase 1, PR 1a.

## Guiding principles

1. **Skeleton, not product.** Every pattern is shown once, end to end; breadth is covered by stubs and documentation.
2. **Decisions before code.** Every non-obvious choice has an ADR the team can challenge.
3. **Rules are enforced by tests, not by wiki pages** (module boundaries, migration safety).
4. **Day-one operability.** Telemetry, health checks, local environment and migrations exist before the first feature.

## Delivery workflow

Architecture and Phase 0 were committed directly to `main` as the design baseline. **From Phase 1 onwards all changes
are delivered through GitHub pull requests.**

| Rule | Detail |
|---|---|
| One PR per reviewable unit | A phase is split into several PRs when one PR would be too large to review; the split is recorded in this plan **before** the work starts (Phase 1: PRs 1a, 1b, 1c) |
| Branching | Each PR branches from the latest `main` after the previous PR is merged. Names: `phase-<n><letter>/<topic>` for implementation, `docs/<topic>` for plan or ADR-only changes |
| Manual review | Every PR is reviewed manually by the repository owner, who also merges it. The author (including AI assistance) never merges its own PR |
| Verification evidence | Until CI exists (PRs 1a and 1b), the PR description lists each acceptance criterion with the command run and its result. From PR 1c, the pull request pipeline must pass as well |
| PR description | Summary; ADRs implemented; acceptance criteria with evidence; deviations from the plan or ADRs; notes on AI usage (template in `.github/pull_request_template.md`) |
| Deviations | If implementation shows the plan or an ADR is wrong, the PR updates the plan/ADR (or adds a superseding ADR) in the same PR and calls it out in the description |
| Scope | A PR contains only what its plan section lists. Discovered follow-up work goes to the backlog section, not into the PR |

## Phases

### Phase 0 — Spikes and verification (de-risk before committing)

Short, throwaway experiments ([`spikes/`](../spikes)). **Status: completed** — full evidence in
[phase-0-results.md](phase-0-results.md).

| # | Spike | Question | Result | ADRs changed |
|---|---|---|---|---|
| S1 | Wolverine + Marten durable outbox | Do messages survive a process kill? | ✅ Yes (81/81 recovered). Found: runtime codegen removed in Wolverine 6; silent drop of messages without handler | 0004, 0005, 0015, 0021, 0022 |
| S2 | Outbox with Dapper/Npgsql transactions | Can a Dapper module enlist its transaction? | ✅ Yes, via low-level types → `RelationalOutbox` helper | 0005, 0007 |
| S3 | Marten unknown event types | Can an older build read a stream with a newer event type? | ❌ No → register before emit, rollback floor | 0009, 0010, 0021 |
| S4 | Marten schema in production mode | Can schema be exported as SQL and applied by the Migrator? | ⚠️ Marten yes (per database); Wolverine storage no; unregistered documents missing; non-concurrent indexes by default | 0008, 0009, 0013, 0015, 0022 |
| S5 | Licences and maintenance | Are all dependencies usable without commercial licences? | ✅ Yes; Keycloak Aspire integration preview-only | 0004, 0013 |
| S6 | Online DDL behaviour | Do the ADR-0009 assumptions hold on PostgreSQL 17? | ✅ Yes; added volatile-default and DbUp transaction rules | 0007, 0008, 0009 |
| S7 | Listener circuit breaker | Can an integration queue be paused during an outage? | ❌ Stalls with scheduled retries → fallback: scheduled backoff only | 0014 |

#### Pinned versions (from S5)

| Package | Version |
|---|---|
| WolverineFx, .Marten, .Postgresql, .RuntimeCompilation | 6.38.0 |
| Marten | 9.37.0 |
| Npgsql | 10.0.3 |
| Dapper | 2.1.86 |
| dbup-postgresql | 7.0.1 |
| Microsoft.FeatureManagement.AspNetCore | 4.7.0 |
| Microsoft.Extensions.Http.Resilience | 10.10.0 |
| Aspire (AppHost SDK, Aspire.Hosting.PostgreSQL) | 13.5.4 |
| xunit.v3 | 4.0.1 |
| Shouldly | 4.3.0 |
| NSubstitute | 6.2.0 |
| TngTech.ArchUnitNET.xUnitV3 | 0.13.4 |
| WireMock.Net | 2.15.0 |
| Testcontainers.PostgreSql, .Keycloak | 4.15.0 |

### Phase 1 — Foundation (no business logic)

Phase 1 is delivered as **three pull requests**, reviewed manually and merged in order (see
[Delivery workflow](#delivery-workflow)). Each PR branches from `main` after the previous one is merged. CI only exists
from PR 1c, so PRs 1a and 1b carry their verification evidence in the PR description, and PR 1c adds automated tests
for the acceptance criteria of 1a and 1b retroactively.

#### PR 1a — Solution and runtime foundation

Branch: `phase-1a/runtime-foundation`

Deliverables:
- Solution, `Directory.Build.props` (nullable, warnings as errors, analyzers), `Directory.Packages.props` (central
  versions, pinned as above).
- `OrderPlatform.ServiceDefaults`: OpenTelemetry (traces, metrics, logs, OTLP exporter), health checks (`/health/live`,
  `/health/ready`), standard HTTP resilience.
- `OrderPlatform.BuildingBlocks`: technology-free `Result` and error types (referenced by Domain and Contracts).
- `OrderPlatform.BuildingBlocks.Infrastructure`: `IModule` abstraction and the `RelationalOutbox` helper enlisting an
  `NpgsqlTransaction` in the Wolverine outbox (ADR-0007). Split from BuildingBlocks so Domain and Contracts projects
  never depend on Marten, Wolverine or ASP.NET Core (ADR-0015).
- `OrderPlatform.Composition`: the module list and the Marten/Wolverine configuration shared by the Api and the
  Migrator (ADR-0008), the clock (`TimeProvider`), and the migrated-schema startup guard and readiness check.
- `OrderPlatform.Api`: host, Wolverine configured with durable local queues + Marten integration, module registration via `IModule`.
  - Wolverine: `WolverineFx.RuntimeCompilation` for development/tests, `TypeLoadMode.Static` when `codegen` output is
    present; explicit handler discovery per module assembly; `<Message>Handler` naming (ADR-0004).
  - Marten and Wolverine `AutoCreate.None` in every environment; every document type, projection and event alias
    registered explicitly by its module (ADR-0008, ADR-0010).
- Module shells for all **six** modules (Ordering, Customers, Pricing, Inventory, Billing, Shipping — four projects
  each, empty `IModule` registration, empty DbUp script folder).
- `OrderPlatform.Migrator` (ADR-0008): DbUp runner without DbUp transactions (per-module script folders, journal per
  schema, retry on `lock_timeout`), Marten schema apply, Wolverine storage setup, final schema assert, run record in
  `platform.migrator_runs`; exits non-zero on failure.
- `OrderPlatform.AppHost`: Postgres, Keycloak as a plain container with realm import (ADR-0013), Migrator (wait for
  completion), Api (wait for Migrator and Keycloak).
- `deploy/docker-compose.yml`: Postgres, Keycloak, Migrator, Api, standalone Aspire Dashboard as OTLP receiver;
  `deploy/keycloak/orderplatform-realm.json` with two accounts, a portal user and a support agent (used from PR 1b).
- Dockerfiles for Api and Migrator images; the Api image build runs Wolverine `codegen write` and starts in static mode (ADR-0021).

Acceptance criteria (verified manually, evidence in the PR):
- `dotnet build` succeeds with warnings as errors.
- `docker compose up` and `dotnet run --project src/AppHost/...` both start the system, run the Migrator first and show
  traces in the dashboard (the Migrator run with its steps, and the Api's database activity). Health probe requests are
  intentionally excluded from traces.
- Starting the Api against an empty database (Migrator not run) fails at startup with a clear message.
- The Api container starts in static code generation mode.

#### PR 1b — API conventions, security and feature flags

Branch: `phase-1b/api-security`

Deliverables:
- API conventions (ADR-0020): `/v1` route groups, problem details with stable error codes and `traceId`, JSON
  conventions (decimal-string money, UTC timestamps), OpenAPI document, idempotency filter + Marten idempotency
  documents + daily cleanup message.
- Security (ADR-0016): JWT bearer auth against Keycloak, scope policies, `account_id` access helper, `support-agent`
  policy, rate limiter partitioned by account.
- Feature flags (ADR-0019): `IFeatureFlags` in `BuildingBlocks`, Microsoft.FeatureManagement adapter in the Api host
  (records evaluations on the current span), per-module `FeatureFlags` registry convention, flag configuration in
  appsettings with reload on change.
- Integration mode configuration and the **startup guard** that rejects `Fake` adapters outside Development/Test (ADR-0014).
- A **development-only diagnostics endpoint** (`POST /v1/_diagnostics/echo`) that exercises authentication, account
  scoping, idempotency, problem details and a feature flag, so the conventions can be verified before any business
  endpoint exists. It is not mapped outside Development and is removed once Phase 2 endpoints cover the same behaviour.

Acceptance criteria (verified manually, evidence in the PR):
- A token obtained from local Keycloak calls the diagnostics endpoint; a request without a token gets `401`; a token
  without the required scope gets `403`.
- Repeating a request with the same `Idempotency-Key` returns the stored response; the same key with a different body returns `422`.
- Errors are returned as problem details with `errorCode` and `traceId`.
- A missing flag evaluates to off.
- Starting the Api with `ASPNETCORE_ENVIRONMENT=Production` and a `Fake` integration fails at startup.

#### PR 1c — Tests and CI

Branch: `phase-1c/tests-ci`

Deliverables:
- Test projects and shared test infrastructure per ADR-0022 (xUnit v3, Shouldly, Testcontainers fixture that runs the
  Migrator and starts the Api with `AutoCreate.None`, WireMock.Net base class, flag on/off helper, test data builder convention).
- `OrderPlatform.Architecture.Tests` with the ArchUnitNET rules from ADR-0015, plus the handler coverage and
  document/event registration checks (fails the build on violation).
- `OrderPlatform.Migrations.Tests` with the expand/contract and online-safety script rules from ADR-0009 (incl.
  volatile defaults, `BEGIN/COMMIT`, single-statement concurrent indexes).
- **Integration tests covering the acceptance criteria of PRs 1a and 1b** (empty-database startup failure, `401`/`403`,
  idempotency, problem details, fake adapter guard, missing flag).
- CI pipeline (`.github/workflows`) implementing the pull request pipeline and the image build stage of ADR-0021
  (incl. `codegen test`, the Marten patch artifact and the compose smoke test); deployment stages, including the
  rollback floor check, stubbed until a hosting platform is chosen.
- Branch protection on `main` requiring the pull request pipeline to pass (in addition to manual review).

Acceptance criteria:
- A deliberately illegal cross-module reference fails the architecture tests.
- A migration script containing `DROP COLUMN` outside the contract rules fails the migration tests; so does a volatile default.
- A message type without a handler, and a Marten document type without explicit registration, each fail a test.
- No application project references `Microsoft.FeatureManagement` directly (architecture test).
- Every acceptance criterion of PRs 1a and 1b is covered by an automated test.
- The pull request pipeline runs green on the PR itself, including the compose smoke test.

### Phase 2 — Reference vertical slice: Ordering

Deliverables:
- `Order` aggregate (event sourced) with the **complete state machine** from ADR-0017: all states, all transitions,
  cancellation rules per state, ignore-and-log for events that do not apply. Only the handlers below are wired in this phase.
- Customers: `customers` schema with seeded accounts (matching the Keycloak realm), addresses and tax profiles;
  `ICustomerDirectory` in `Customers.Contracts` served from the local copy.
- `POST /v1/orders` (with `Idempotency-Key` header) → `SubmitOrder` command → customer account via
  `Customers.Contracts` → pricing via `Pricing.Contracts` → start stream with `OrderSubmitted` (status
  `ValidatingInventory`, address/contact ids only) → `ReserveInventory` sent through the outbox.
- Inventory: `ReserveInventory` handler behind `IInventoryGateway` with a fake adapter (in-memory stock, configurable
  failures) → `InventoryReserved` | `InventoryUnavailable` → order moves to `Invoicing` | `AwaitingCustomer`.
- `GET /orders/{id}` → read model projection (Marten inline projection `OrderDetails`); `GET /orders/{id}/history` → stream events.
- `POST /orders/{id}/cancel` → `CancelOrder` → aggregate enforces the per-state rule → compensation (release
  reservation) → `OrderCancelled`.
- Pricing: seeded base and customer-specific price lists; staged rule pipeline with `LineSubtotalRule`,
  `ShippingChargeRule`, `TaxRule` (incl. reverse charge) and the rounding policy of ADR-0018 (data from `pricing` schema via Dapper).
  `ShippingChargeRule` is gated by the example release flag `Pricing.ShippingCharge`; the flag decision is recorded in the
  price breakdown on `OrderSubmitted` (reference example for ADR-0019).
- Remove the development-only diagnostics endpoint from PR 1b; its integration tests move to the real Ordering endpoints.
- Tests: aggregate unit tests covering **every transition and every disallowed command**, pricing rule tests (with the
  shipping charge flag on and off), one integration test per endpoint using Testcontainers.

Acceptance criteria:
- An order can be submitted, reserved (fake), read and cancelled end to end; the trace shows HTTP → handler → Postgres
  → outbox → inventory handler → order update.
- With the fake inventory configured as unavailable, the order ends in `AwaitingCustomer`.
- Submitting an unknown SKU returns `422` with error code `unknown-product`.
- Reading an order of another account returns `404`.
- `OrderSubmitted` contains no personal data (asserted in a test).
- Cancelling an order in `Processing` returns `409 Conflict` with problem details pointing to customer support.
- Repeating a `POST /orders` with the same idempotency key returns the original result without a second order.

### Phase 3 — Invoicing, fulfilment and timers (skeleton depth)

Deliverables:
- Billing module: `IssueInvoice`, `VoidInvoice`, `CheckInvoiceStatus`, `RefundInvoice` handlers behind `IBillingGateway`
  with a fake adapter (controllable paid / partially paid / unpaid status).
- Durable timers: daily `CheckInvoiceStatus`, `InvoiceOverdue` at +3 days, `CustomerResponseTimeout` at +7 days
  (configurable, shortened in tests via `TimeProvider`).
- `POST /orders/{id}/payment-status/refresh` sending the same `CheckInvoiceStatus` command as the timer.
- `POST /orders/{id}/items/reduce` and `PUT /orders/{id}/fulfilment-information`.
- Shipping module: `RequestShipment`, `CancelShipmentRequest` behind `IShippingGateway` with a fake adapter; events
  `ShipmentDispatched`, `ShipmentDelivered`, `FulfilmentFailed`.
- Back-office endpoints `POST /backoffice/orders/{id}/cancel` (support-agent policy, mandatory reason) and
  `POST /backoffice/orders/{id}/resolve`.
- One real-shaped HTTP adapter example (typed `HttpClient` + resilience pipeline + ACL mapping + idempotency key +
  outcome query after timeout), for billing.
- One local queue per integration (bounded parallelism) with **scheduled** retries and exponential backoff, then dead
  letter; exhausted compensation moves the order to `RequiresAttention`. No listener circuit breaker (ADR-0014).
- Durability test category: two API processes in `DurabilityMode.Balanced` (ADR-0022).

Acceptance criteria:
- Unpaid invoice past its due date cancels the order and releases inventory; partially paid goes to `RequiresAttention`.
- Payment received after cancellation moves the order to `RequiresAttention`.
- With the fake billing provider down, messages are rescheduled (not dead-lettered) and complete after it recovers,
  **without a restart**.
- Killing one of two API processes while a timer, a retry and a compensation are pending: the other process completes
  them (durability in `Balanced` mode).
- A durable message whose type the running build does not know is not lost silently (behaviour documented in ADR-0010 rule 8).

### Phase 4 — Hardening and handover documentation

- Security: review authorization policies per endpoint, the two v1 database roles, secret handling guidance.
- Runbook notes: migrations (incl. `lock_timeout` retries and backfill jobs), rollback decision table (flag vs redeploy,
  ADR-0009), rollback floor and snapshot rebuild after a rollback (ADR-0010), dead-letter replay (`storage replay`),
  flag change procedure, handling orders in `RequiresAttention`.
- Backlog of follow-up work for the delivery team (see below).
- Final pass on ADR statuses and `ai-usage.md`.

## Timebox allocation (assessment: 4–6 h)

| Phase | Assessment depth | Estimate |
|---|---|---|
| Architecture docs + ADRs | Full | 1.5 h |
| Phase 0 | All seven spikes executed (more than planned — S3, S4 and S7 changed the design) | 1 h (actual) |
| Phase 1 | Full, as three PRs (1a runtime, 1b API/security, 1c tests/CI); CI deployment stages stubbed | 2 h (+ review time) |
| Phase 2 | Full state machine + submit / reserve / get / cancel slice | 1.5 h |
| Phase 3 | Billing and shipping ports, timer registration and fakes only; no real adapters | 0.5 h |
| Phase 4 | Documentation only | 0.25 h |

Phase 0 took longer than budgeted; the overrun is absorbed by keeping Phase 3 at ports, fakes and timer registration
only. Beyond the timebox, a delivery team would complete Phase 3 in full and Phase 4 before the first production release.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Critter Stack (Wolverine/Marten) unfamiliar to the team | Slower delivery, misuse | Reference slice + ADR rationale; pairing; escape hatch — handlers are plain classes |
| Two schema owners (Marten, DbUp) plus Wolverine storage | Migration conflicts, missing tables | Single Migrator applying all three in order with a final assert; no runtime auto-create anywhere; explicit document registration tested (ADR-0008) |
| Wolverine 6 / Marten 9 are newer than most published examples | Wrong API usage copied from the web | Pinned versions; spike code and reference slice as the canonical examples |
| Wolverine behaviour changes between minor versions (e.g. breaker stall seen in S7) | Silent processing stalls | Durability and outage tests in CI; upgrades only through pull requests running them |
| New event types block rollback | Rollback impossible after emitting | Register before emit, rollback floor enforced by the pipeline (ADR-0010) |
| Expand/contract discipline erodes | Unrollbackable release | Automated script checks, code-removal and contract tickets created with every expand |
| Flag debt accumulates | Dead code paths, untested combinations | Registry with owner and expiry, both-states tests, release flags removed after stabilisation |
| v1 flag changes need reload/restart | Slower revert during incidents | Accepted for v1; OpenFeature migration triggers defined in ADR-0019 |
| At-least-once delivery | Duplicate side effects | Idempotent handlers, idempotency keys towards external providers |
| B2B assumptions (README §2) turn out wrong | Lifecycle redesign | Assumptions stated centrally; lifecycle isolated in one aggregate; ADR-0017 superseded if they change |
| Order aggregate grows into a god class | Hard to change, rules leak from other modules | Aggregate decides transitions only; pricing, inventory, billing rules stay in their modules; transition tests per state |
| `RequiresAttention` orders pile up | Customer impact, manual workload | Metric and alert on count and age; runbook; support tooling in backlog |
| External systems unavailable for long periods | Orders stuck in intermediate states | Retries with backoff, dead letters, state-age metrics per lifecycle state |
| Aspire and compose drift | "Works on my machine" | Compose checked in CI (`docker compose config` + smoke test) |
| Local copies of customer data and price lists go stale | Wrong tax or price on an order | v1 seeded data only; synchronisation designed before go-live; price breakdown records the price list version |
| PRs 1a and 1b merged before CI exists | Regressions not caught automatically | Manual verification evidence per acceptance criterion in each PR; PR 1c adds automated tests for all 1a/1b criteria before Phase 2 starts |
| Hosting platform not chosen | Deployment stages cannot be completed | Runtime requirements listed in ADR-0021; decision needed before the first non-local environment |
| Breaking API change slips into `/v1` | Integrated customer systems break | OpenAPI breaking change check in CI; release flags for new API surface (ADR-0020) |

## Open questions

None blocking. Business assumptions are recorded in [README §2](README.md#2-domain-scope-a-b2b-ordering-platform) and
should be validated with the business before Phase 3.

Resolved: expand/contract sync mechanism is chosen per change in v1 (ADR-0009); enforcement test is part of Phase 1;
feature flags start with Microsoft.FeatureManagement (ADR-0019); domain fixed as B2B with invoice payment and the
lifecycle in ADR-0017; integration retries, architecture test tooling, security, pricing data and rounding decided
(ADR-0014 to 0018); Customers module added; API conventions, deployment and testing strategy recorded (ADR-0020 to 0022);
Phase 0 findings incorporated (see [phase-0-results](phase-0-results.md)).

## Follow-up backlog for the delivery team (out of assessment scope)

- Real adapters for inventory, billing and fulfilment/shipping systems.
- Billing webhook (`POST /webhooks/billing`, signature validation) feeding `CheckInvoiceStatus`; shipping event webhooks.
- Support tooling for `RequiresAttention` orders (queue, late-payment refund or reinstatement).
- Returns after shipment, partial shipments, multi-currency, credit limits and configurable payment terms (out of scope per README §2).
- Customer-facing order list projection and search.
- RabbitMQ / Azure Service Bus transport when a second deployable appears.
- Move feature flags to OpenFeature (`flagd` locally, managed provider in production) when an ADR-0019 trigger is hit;
  then adopt flag-driven migration phases (ADR-0009 target pattern).
- CI job running the previous release's integration tests against the new schema (rollback proof, ADR-0009).
- CRM/ERP synchronisation for the Customers module; price list import API or ERP synchronisation for Pricing.
- Personal data erasure workflow (anonymise Customers data and address snapshots); crypto-shredding if references prove insufficient.
- Per-module database roles (ADR-0016 target).
- Hosting platform decision, production infrastructure as code (managed Postgres, container platform, secrets store)
  and the deployment stages of the pipeline (ADR-0021).
- Load testing and capacity baseline.
- Report the listener circuit breaker + scheduled retry stall to the Wolverine project with the S7 reproduction; revisit ADR-0014 when fixed.
- Reduce trace noise from Wolverine's background polling (one database span per second per instance, seen in PR 1a):
  filter or sample those spans in ServiceDefaults once real traffic makes the noise matter (ADR-0012).
- Automate snapshot projection rebuilds after a rollback past a new event type (manual runbook step in v1, ADR-0010).
