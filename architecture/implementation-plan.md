# Implementation Plan

Status: **Agreed** — all ADRs accepted. **Phase 0 completed** ([results](phase-0-results.md)); plan and ADRs updated
with its findings. **Phase 1 completed** (PRs 1a, 1b, 1c merged). Phases 2–4 re-planned as small PRs limited to the
assessment scope; the remaining work is split into [next steps](#next-steps-for-the-delivery-team). Next: Phase 2.

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
| One concern per PR | Each PR delivers exactly one row of a phase table below. No opportunistic refactoring, documentation sweeps or backlog items; plan or ADR edits only where they concern that PR (see Deviations). The split is recorded in this plan **before** the work starts |
| Size budget | A PR is reviewable in about **15 minutes**: at most ~400 changed lines of hand-written code **including tests** and ~15 files. Seed data, approved-names files and generated files do not count. If implementation exceeds the budget, the author stops and proposes a further split (docs PR or plan update in the PR) instead of finishing a large PR. Sizes in the tables: **S** ≈ up to 200 lines, **M** ≈ 200–400 |
| Tests in the same PR | Every acceptance criterion of a PR is proven by an automated test added in that PR; the pull request pipeline passes, so `main` stays green after every merge. Manual evidence alone is not accepted (PRs 1a and 1b were the exception before CI existed) |
| Domain before wiring | Code without infrastructure (domain logic, ports, fake adapters) lands in its own PR with unit tests before the PR that wires it into messaging or endpoints |
| Message with its handler | A command or integration event type lands in the same PR as its handler, because the architecture tests fail on unhandled message types (ADR-0015). Event types are registered with their aliases in the PR that introduces them (ADR-0010) |
| Dependency order | Each PR lists its dependencies and branches from the latest `main` after they are merged. PRs without a dependency on each other may be open at the same time |
| Branching | Names: `phase-<n><letter>/<topic>` for implementation (e.g. `phase-2a/pricing-pipeline`), `docs/<topic>` for plan or ADR-only changes |
| Manual review | Every PR is reviewed manually by the repository owner, who also merges it. The author (including AI assistance) never merges its own PR |
| PR description | Summary; ADRs implemented; **how to review** (files in suggested reading order); acceptance criteria with the tests that prove them; deviations from the plan or ADRs; notes on AI usage (template in `.github/pull_request_template.md`) |
| Phase criteria | Each phase-level acceptance criterion names the PR that proves it |
| Deviations | If implementation shows the plan or an ADR is wrong, the PR updates the plan/ADR (or adds a superseding ADR) in the same PR and calls it out in the description |
| Scope | A PR contains only what its plan row lists. Discovered follow-up work goes to the backlog section, not into the PR |

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
  endpoint exists. It is not mapped outside Development and is removed once Ordering endpoints cover the same behaviour (next step N2).

Implementation notes (added in PR 1b):
- Keycloak realm: the portal client's order scopes became optional client scopes, so a user token with only
  `orders:read` demonstrates the `403` (ADR-0016).
- `IModule.MapEndpoints` receives the `/v1` route group; `IModule.FeatureFlags` exposes the module's flag registry.
- The idempotency claim and the stored response use their own transactions in PR 1b; the Ordering handlers
  write the completion atomically with the order events (ADR-0020, next step N1).
- docker-compose runs the Api as `Development` (fake integrations and the diagnostics endpoint are allowed locally).

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

Implementation notes (added in PR 1c):
- `codegen test` replaced: it fails in Wolverine 6.38 regardless of our code. Generated code is proven by the image build
  and the static-mode check in the compose smoke test (ADR-0021).
- Added `OrderPlatform.AppHost.Tests` (Aspire.Hosting.Testing) for the PR 1a AppHost criterion (ADR-0022).
- Message marker interfaces (`ICommand`, `IIntegrationEvent`, `IDomainEvent`) in `BuildingBlocks`, and pinned stored
  names in `contract-names.approved.txt`, make handler coverage and alias stability checkable (ADR-0015).
- The release compatibility job also runs the previous Api against the new schema: a first step towards the backlog item
  "previous release's integration tests against the new schema".
- **Branch protection is not available** for this private repository on the GitHub Free plan (branch protection and
  rulesets both return `403 Upgrade to GitHub Pro or make this repository public`). The definition is ready in
  `.github/branch-protection-main.json`: the three pull request jobs are required checks, a pull request is required, and
  0 approvals because the owner reviews and merges their own PRs. The owner applies it after upgrading or making the
  repository public:
  `gh api -X PUT repos/bsakel/agile_task/branches/main/protection --input .github/branch-protection-main.json`.
  Until then, merging only green PRs is a manual rule.

Acceptance criteria:
- A deliberately illegal cross-module reference fails the architecture tests.
- A migration script containing `DROP COLUMN` outside the contract rules fails the migration tests; so does a volatile default.
- A message type without a handler, and a Marten document type without explicit registration, each fail a test.
- No application project references `Microsoft.FeatureManagement` directly (architecture test).
- Every acceptance criterion of PRs 1a and 1b is covered by an automated test.
- The pull request pipeline runs green on the PR itself, including the compose smoke test.

### Scope of Phases 2–4

After Phase 1, the remaining plan was re-cut into small PRs (see [Delivery workflow](#delivery-workflow)) and limited to
what the assessment needs: **one working example per functional requirement** of the brief (create, retrieve, cancel,
lifecycle tracking, inventory validation, pricing, integration with inventory, payment and shipping), plus the complete
Order state machine and one real-shaped HTTP adapter. Everything else stays specified in the ADRs and is split into
ordered PRs under [Next steps for the delivery team](#next-steps-for-the-delivery-team).

Dependency order (PRs on the same line can be open at the same time):

```
2a, 2b, 2d, 2h, 3a, 3b      no dependencies
2c (2a, 2b)   2e (2d)   3c (3a)
2f (2c, 2d)
2g (2f)   2i (2f, 2h)
2j (2g, 2i)
4a (all above)
```

### Phase 2 — Reference vertical slice: Ordering

Ten PRs. The slice ends when an order is reserved and waits in `Invoicing` (invoicing is a next step), or is cancelled.

| PR | Branch | Scope | Acceptance criteria (automated tests) | Size | Depends on |
|---|---|---|---|---|---|
| 2a | `phase-2a/pricing-pipeline` | Pricing domain only (ADR-0018): `IPricingRule`, fixed stages, `PriceBreakdown`, `LineSubtotalRule`, `ShippingChargeRule` gated by the release flag `Pricing.ShippingCharge` (registered in the Pricing flag registry), `TaxRule` incl. reverse charge, rounding policy. New `Pricing.Domain.Tests` | Stages run in fixed order and the registered rule order is asserted; per-line rounding and per-rate tax rounding cases; reverse charge yields zero tax; shipping charge present with the flag on and absent with it off | M | — |
| 2b | `phase-2b/customers-reference-data` | Customers module: `customers` schema expand script, seeded accounts matching the Keycloak realm (status, tax profile, billing and shipping address, contact), `ICustomerDirectory` in `Customers.Contracts`, Dapper implementation | After the Migrator runs, a seeded account is read with tax profile and address/contact ids; unknown account returns a failure; migration safety tests pass on the new scripts | M | — |
| 2c | `phase-2c/price-lists` | `pricing` schema with seeded base and customer-specific price lists, Dapper price list reader, `IPricingService` in `Pricing.Contracts` combining price lists, the customer tax profile (via `Customers.Contracts`) and the pipeline | Customer-specific price overrides the base price; a SKU without a price fails with `unknown-product`; the breakdown records the price list version and the shipping charge flag decision; integration tests with the flag on and off | M | 2a, 2b |
| 2d | `phase-2d/order-aggregate-pre-fulfilment` | `Order` aggregate part 1 (ADR-0017): all states and lifecycle events with explicit aliases; transitions out of `ValidatingInventory`, `AwaitingCustomer`, `Invoicing`, `AwaitingPayment`; customer cancellation rule for **every** state; ignore-and-log for events that do not apply; cancel while a reservation is in flight. New `Ordering.Domain.Tests` | Table-driven tests: every transition out of these four states, every disallowed command in every state for customer cancellation, ignored events leave state unchanged; `InventoryReserved` after cancellation asks for a release | M | — |
| 2e | `phase-2e/order-aggregate-fulfilment` | `Order` aggregate part 2: transitions out of `Processing`, `FulfilmentOnHold`, `Refunding`, `Shipped`, `Cancelled` (late payment) and `RequiresAttention`; support cancellation with mandatory reason; the remaining races of ADR-0017 §6 | Table-driven tests for every remaining transition and disallowed command; support cancel allowed up to and including `Processing`; payment after cancellation → `RequiresAttention`; `FulfilmentFailed` after dispatch ignored | M | 2d |
| 2f | `phase-2f/submit-order` | `POST /v1/orders` (`Idempotency-Key`) → `SubmitOrder` handler: active account via `Customers.Contracts`, price via `Pricing.Contracts`, start the stream with `OrderSubmitted` (process version, breakdown, address/contact ids). Order stays in `ValidatingInventory` until 2i | `201 Created` with status and price breakdown; unknown SKU → `422 unknown-product`; inactive account → `422`; repeating the request with the same key returns the original response and no second stream; `OrderSubmitted` contains no personal data (serialised event checked against the seeded names, emails and addresses) | M | 2c, 2d |
| 2g | `phase-2g/read-order` | `OrderDetails` inline projection (registered explicitly), `GET /v1/orders/{id}`, `GET /v1/orders/{id}/history` (raw stream) | Submitted order is returned with state, items and breakdown; history lists `OrderSubmitted`; another account's order returns `404` on both endpoints | S | 2f |
| 2h | `phase-2h/inventory-port` | `IInventoryGateway` in `Inventory.Application` (all-or-nothing reserve and release with idempotency keys, outcome query); fake adapter in `Inventory.Infrastructure` (in-memory stock, configurable unavailable SKUs and failures) selected by `Integrations:Inventory:Mode`. No messages | Unit tests: reserve succeeds only when every line is available; repeated key does not reserve twice; release restores stock; configured failure is returned as a `Result` failure | S | — |
| 2i | `phase-2i/inventory-reservation` | `ReserveInventory` command and handler (Inventory), `InventoryReserved` / `InventoryUnavailable` integration events and their Ordering handlers; `SubmitOrder` sends `ReserveInventory` through the outbox in the same transaction | Available stock → order in `Invoicing`; fake configured unavailable → `AwaitingCustomer`; the trace of one submission shows HTTP → handler → Postgres → outbox → inventory handler → order update | M | 2f, 2h |
| 2j | `phase-2j/cancel-order` | `POST /v1/orders/{id}/cancel` (`Idempotency-Key`) → `CancelOrder`; `ReleaseInventory` command and handler; release when a reservation completes after cancellation | Reserved order is cancelled and its stock released; `409` with `currentState` and a support hint for an order in `Processing` (stream seeded in the test); `InventoryReserved` arriving after the cancel releases the stock; another account's order returns `404` | M | 2g, 2i |

Phase acceptance criteria:
- An order can be submitted, reserved (fake), read and cancelled end to end; the trace shows HTTP → handler → Postgres
  → outbox → inventory handler → order update (2i, 2j).
- With the fake inventory configured as unavailable, the order ends in `AwaitingCustomer` (2i).
- Submitting an unknown SKU returns `422` with error code `unknown-product` (2f).
- Reading an order of another account returns `404` (2g).
- `OrderSubmitted` contains no personal data (2f).
- Cancelling an order in `Processing` returns `409 Conflict` with problem details pointing to customer support (2j).
- Repeating a `POST /orders` with the same idempotency key returns the original result without a second order (2f).
  Crash atomicity of the idempotency record is next step N1.
- Every state transition and every disallowed command of ADR-0017 is covered by a domain test (2d, 2e).

### Phase 3 — Integration ports and a reference adapter

Three PRs. Payment and shipping integration is shown through ports and fakes, and one real-shaped HTTP adapter shows the
full ADR-0014 pattern. Handlers, timers, retry queues and durability tests are next steps N3–N14.

| PR | Branch | Scope | Acceptance criteria (automated tests) | Size | Depends on |
|---|---|---|---|---|---|
| 3a | `phase-3a/billing-port` | `IBillingGateway` in `Billing.Application` (issue, void, invoice status, refund; idempotency keys; outcome query); fake adapter with controllable paid / partially paid / unpaid status and provider outage, selected by `Integrations:Billing:Mode`. No messages. New `Billing.Infrastructure.Tests` | Unit tests for each fake status and outage; repeated key does not issue twice; outcome query returns the result of an earlier call | M (estimated S) | — |
| 3b | `phase-3b/shipping-port` | `IShippingGateway` in `Shipping.Application` (request shipment, cancel request, shipment status; idempotency keys); fake adapter with controllable dispatched / delivered / failed outcomes, selected by `Integrations:Shipping:Mode`. No messages | Unit tests for each fake outcome; repeated key does not request twice; cancel of an unknown request is a failure | S | — |
| 3c | `phase-3c/billing-http-adapter` | Billing HTTP adapter against a documented example provider contract: typed `HttpClient`, resilience pipeline (timeout per attempt, at most one retry for idempotent calls, circuit breaker), provider DTOs and anti-corruption mapping (unknown status → explicit unknown), idempotency key header, outcome query after timeout; registered for `Mode = Http` (until then `Http` registers no gateway). Extends `Billing.Infrastructure.Tests` | WireMock.Net tests: success; every provider error mapped to a `Result` failure; unknown status mapped to unknown; timeout followed by outcome query that finds the invoice and does not issue a second one; open circuit fails fast | M | 3a |

Phase acceptance criteria:
- Billing and shipping each have a port, a fake selected by integration mode, and unit tests (3a, 3b).
- The billing HTTP adapter proves success, error mapping and timeout followed by an outcome query against WireMock.Net (3c).

### Phase 4 — Handover documentation

| PR | Branch | Scope | Acceptance criteria | Size | Depends on |
|---|---|---|---|---|---|
| 4a | `docs/handover` | Final pass on ADR statuses and implementation notes, README and plan status, next steps and backlog, `ai-usage.md` for PRs 2a–3c | Every ADR and plan section matches what was delivered; each next step and backlog item has an owner-facing description; `ai-usage.md` covers every merged PR | S | 2a–3c |

## Next steps for the delivery team

Specified in the ADRs but not built in the assessment. The split follows the same [Delivery workflow](#delivery-workflow)
rules; `N` ids are stable references for PR names (`next-n3/invoicing`). The unordered
[follow-up backlog](#follow-up-backlog-for-the-delivery-team-out-of-assessment-scope) comes after these.

| Id | Scope | Acceptance criteria (automated tests) | Size | Depends on |
|---|---|---|---|---|
| N1 | Atomic idempotency completion: state-changing Ordering handlers store the completed idempotency record in the same Marten session as the order events; the endpoint filter does not overwrite it (ADR-0020) | A simulated crash after the handler commits and before the filter stores the response: a retry after the lease returns the original result and creates no second order | S–M | 2j |
| N2 | Remove the development-only diagnostics endpoint from PR 1b; move its tests to the Ordering endpoints | `/v1/_diagnostics/echo` returns `404` in Development; authentication, idempotency, problem details and flag tests run against Ordering endpoints | S | N1 |
| N3 | Invoicing step: `IssueInvoice` command and handler (Billing), `InvoiceIssued` and its Ordering handler; `InventoryReserved` triggers invoicing | A reserved order reaches `AwaitingPayment`; the invoice uses the latest recorded breakdown | M | 2e, 2i, 3a |
| N4 | Payment check: `CheckInvoiceStatus` handler; daily durable timer scheduled on `InvoiceIssued` and re-scheduled after each check; `InvoicePaid` / `InvoicePartiallyPaid` and their Ordering handlers | Timer scheduled at issue time; paid → `Processing`; no change re-schedules the check; the timer handler is invoked by sending the scheduled message | M | N3 |
| N5 | `POST /v1/orders/{id}/payment-status/refresh` sending the same `CheckInvoiceStatus` command as the timer | Refresh of an order in `AwaitingPayment` triggers the check; `409` in other states | S | N4 |
| N6 | Overdue invoices: `InvoiceOverdue` timer at issue time + 3 days (UTC); `VoidInvoice` command and handler; cancellation from `Invoicing` / `AwaitingPayment` voids the invoice | Unpaid invoice past its due date cancels the order, voids the invoice and releases inventory; partially paid → `RequiresAttention`; payment received after cancellation → `RequiresAttention` | M | N4, 2j |
| N7 | `POST /v1/orders/{id}/items/reduce`: reprice, `OrderItemsReduced`, reserve again | `AwaitingCustomer` → `ValidatingInventory` with a new breakdown; `409` in other states | M | 2i |
| N8 | `CustomerResponseTimeout` on entering `AwaitingCustomer` (+7 days, configurable, shortened in tests) | Timeout cancels the order; a timeout after the state moved on is ignored | S | N7 |
| N9 | Fulfilment step: `RequestShipment` on `InvoicePaid`; `ShipmentDispatched`, `ShipmentDelivered`, `FulfilmentFailed` and their Ordering handlers | `Processing` → `Shipped` → `Delivered`; a failure → `FulfilmentOnHold` | M | N4, 3b |
| N10 | `PUT /v1/orders/{id}/fulfilment-information`: new address/contact stored in Customers, the order references the new ids | `FulfilmentOnHold` → `Processing`; events still contain no personal data | M | N9, 2b |
| N11 | Refunding compensation: `CancelShipmentRequest`, `RefundInvoice`, release inventory, `RefundCompleted`; customer response timeout on `FulfilmentOnHold` | Cancel from `FulfilmentOnHold` → `Refunding` → `Cancelled`; the timeout does the same | M | N6, N8, N9 |
| N12 | Back-office endpoints: `POST /v1/backoffice/orders/{id}/cancel` (mandatory reason), `POST /v1/backoffice/orders/{id}/resolve` | Support-agent policy enforced; cancel from `Processing` → `Refunding` with the reason on the event; resolve only in `RequiresAttention` | M | N11 |
| N13 | One local queue per integration (bounded parallelism) with scheduled exponential backoff, then dead letter; exhausted compensation → `RequiresAttention`. No listener circuit breaker (ADR-0014) | With the fake billing provider down, messages are rescheduled (not dead-lettered) and complete after it recovers **without a restart**; exhausted compensation moves the order to `RequiresAttention` | M | N3 |
| N14 | Durability test category: two Api processes in `DurabilityMode.Balanced` (ADR-0022) | Killing one process while a timer, a retry and a compensation are pending: the other completes them; a durable message whose type the running build does not know is not lost silently (result recorded in ADR-0010 rule 8) | M | N6, N11, N13 |
| N15 | Security hardening: test that every endpoint has an explicit authorization policy; the two v1 database roles and secret handling guidance (ADR-0016) | A test fails for an endpoint mapped without a policy | S | N12 |
| N16 | Runbook: migrations (incl. `lock_timeout` retries and backfill jobs), rollback decision table (flag vs redeploy, ADR-0009), rollback floor and snapshot rebuild after a rollback (ADR-0010), dead-letter replay (`storage replay`), flag change procedure, handling orders in `RequiresAttention` | Docs only; reviewed against ADR-0009, 0010, 0014, 0019 | S | N14 |

## Timebox allocation (assessment: 4–6 h)

| Phase | Assessment depth | Estimate |
|---|---|---|
| Architecture docs + ADRs | Full | 1.5 h |
| Phase 0 | All seven spikes executed (more than planned — S3, S4 and S7 changed the design) | 1 h (actual) |
| Phase 1 | Full, as three PRs (1a runtime, 1b API/security, 1c tests/CI); CI deployment stages stubbed | 2 h (+ review time) |
| Phase 2 | Full state machine + submit / reserve / get / cancel slice, 10 PRs | 1.5 h |
| Phase 3 | Billing and shipping ports with fakes, one billing HTTP adapter, 3 PRs | 0.5 h |
| Phase 4 | Handover documentation, 1 PR | 0.25 h |

Phase 0 took longer than budgeted, and Phase 1 PRs proved too large to review. Phases 2–4 are therefore limited to one
working example per functional requirement, delivered as 14 small PRs (about 15 minutes of review each, on top of the
estimates). Beyond the timebox, a delivery team works through the next steps before the first production release.

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
| The skeleton stops before invoicing: timers, retry queues and `Balanced`-mode durability are specified but not built | Delivery team meets these patterns without a worked example | ADR-0005, 0014 and 0017 specify them; spikes S1 and S7 proved the underlying behaviour; next steps N4, N13 and N14 come with the tests that prove them |
| Large PRs cannot be reviewed properly (seen in Phase 1) | Defects and unintended design changes merged unnoticed | Size budget, one concern per PR and tests in the same PR (Delivery workflow) |

## Open questions

None blocking. Business assumptions are recorded in [README §2](README.md#2-domain-scope-a-b2b-ordering-platform) and
should be validated with the business before the invoicing and fulfilment next steps (N3 onwards).

Resolved: expand/contract sync mechanism is chosen per change in v1 (ADR-0009); enforcement test is part of Phase 1;
feature flags start with Microsoft.FeatureManagement (ADR-0019); domain fixed as B2B with invoice payment and the
lifecycle in ADR-0017; integration retries, architecture test tooling, security, pricing data and rounding decided
(ADR-0014 to 0018); Customers module added; API conventions, deployment and testing strategy recorded (ADR-0020 to 0022);
Phase 0 findings incorporated (see [phase-0-results](phase-0-results.md)).

## Follow-up backlog for the delivery team (out of assessment scope)

- Real adapters for inventory, billing and fulfilment/shipping systems (billing: complete the PR 3c reference adapter against the real provider contract).
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
- Interactive OpenAPI UI in Development (ADR-0020) and `401`/`403`/`429` responses in the OpenAPI document (PR 1b).
- AppHost: pin a development Postgres password parameter. The generated password lives in user secrets; if they are
  reset, the persistent data volume no longer accepts it and the Migrator waits forever (seen while verifying PR 1b).
- Forwarded headers behind the ingress so anonymous rate limiting partitions by the real client address (ADR-0016).
- Report the `codegen test` compilation failure (static handler registry compiled without the handler files) to the
  Wolverine project; add `codegen test` to CI once fixed (PR 1c, ADR-0021).
- Tag the first release (`v0.1.0` or similar) so release compatibility compares against a release rather than the merge
  base with main, and contract scripts can be checked against release tags (PR 1c, ADR-0009).
- Apply branch protection on `main` once the plan or visibility allows it (`.github/branch-protection-main.json`, PR 1c).
