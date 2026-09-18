# Order Processing Platform

A **modular monolith** on **.NET 10** that processes B2B orders: submit an order, price it, reserve stock in the
external inventory system, read the order and its history, and cancel it with its compensation — with the full order
state machine, a durable outbox, migrations, telemetry, identity and CI in place from day one.

This repository is an **implementation-ready skeleton, not a finished product**. It delivers the structure, the
cross-cutting foundations and one fully worked vertical slice that the delivery team copies for the remaining
features. What is built and what is deliberately left as next steps is in [§ Scope](#scope) below.

> **New here? Read [`architecture/README.md`](architecture/README.md) first** — scope, business assumptions,
> C4 diagrams, modules and the order lifecycle. This file covers how to run the solution and where everything lives.

---

## Major technical decisions

Every non-obvious choice has an [ADR](architecture/adr/README.md) that can be challenged on its own. The load-bearing
ones:

| Decision | Why | ADR |
|---|---|---|
| **Modular monolith**, one deployable, module boundaries enforced by tests | Team-scale delivery now, each module extractable later | [0002](architecture/adr/0002-modular-monolith.md), [0015](architecture/adr/0015-architecture-tests-enforce-boundaries.md) |
| **Wolverine** for in-process messaging, with a **durable PostgreSQL outbox** and durable scheduled messages | No message loss across crashes (proven by spike S1); RabbitMQ only if that had failed | [0004](architecture/adr/0004-wolverine-for-messaging.md), [0005](architecture/adr/0005-durable-in-process-messaging.md) |
| **Marten event sourcing** for Ordering; **Dapper + DbUp** for the other modules; **no EF Core** | Order history and lifecycle are the domain; the rest is plain reference data | [0006](architecture/adr/0006-marten-event-sourcing-for-ordering.md), [0007](architecture/adr/0007-dapper-and-dbup-for-relational-data.md) |
| **Dedicated Migrator** runs to completion before the API; the API never creates schema | Same path locally and in deployment; no DDL rights for the API | [0008](architecture/adr/0008-dedicated-migrator-forward-only.md) |
| **Forward-only, expand/contract database changes** | Any release can be rolled back by redeploying the previous version | [0009](architecture/adr/0009-expand-contract-database-changes.md) |
| **Register event types before emitting them**, pinned aliases | An older build must still be able to read a newer stream (spike S3) | [0010](architecture/adr/0010-event-and-message-versioning.md) |
| **Minimal APIs that dispatch messages**; versioned, additive-only contract, problem details, idempotency keys | HTTP stays a translation layer; no business rule lives in an endpoint | [0011](architecture/adr/0011-minimal-apis-dispatch-messages.md), [0020](architecture/adr/0020-api-conventions-and-versioning.md) |
| **Aggregate as process manager** — the Order drives its own lifecycle, with durable timers | One owner of the rules, no separate saga state to keep in step | [0017](architecture/adr/0017-order-lifecycle-process.md) |
| **Ports and adapters for every external system**, fakes locally, guarded out of deployed environments | Testable without the real systems; anti-corruption at the edge | [0014](architecture/adr/0014-external-integrations-ports-and-adapters.md) |
| **Feature flags** separate deployment from release — Microsoft.FeatureManagement behind our own interface, OpenFeature as the documented target | Rollback without redeploy; the full OpenFeature design was judged too heavy for v1 | [0019](architecture/adr/0019-feature-flags.md) |
| **OpenTelemetry, Aspire, Keycloak and docker-compose from day one** | Operability is not a later phase | [0012](architecture/adr/0012-observability-opentelemetry-day-one.md), [0013](architecture/adr/0013-local-development-aspire-and-compose.md), [0016](architecture/adr/0016-security-baseline.md) |
| **A test level for every architectural promise**; build once, migrate, roll out, roll back | Rules are enforced by tests, not by wiki pages | [0021](architecture/adr/0021-deployment-and-ci-cd.md), [0022](architecture/adr/0022-testing-strategy.md) |

## Major assumptions

"Order processing" means different things for an airline, a web shop and a supplier. **This platform is designed for
B2B ordering** — that single assumption drives the lifecycle, the payment model and the cancellation rules.

**The full, authoritative list is [`architecture/README.md` § 2 — Domain scope and business assumptions](architecture/README.md#2-domain-scope-a-b2b-ordering-platform).**
The ones with the widest reach:

- **Customers are businesses** with an account and several users; a user only sees their own account's orders.
- **External systems are the source of truth** — inventory, billing and the CRM/ERP. The platform keeps local read
  copies behind anti-corruption layers, never a second master.
- **No instant answer required.** Validation, reservation and invoicing are asynchronous; the order status tells the
  customer where the order is.
- **Payment by invoice**, issued by an external billing system, due in **3 days**. No card payments. Single currency: **EUR**.
- **All-or-nothing reservation.** If stock is short the order waits for the customer, who may only *reduce* it; without
  an answer in **7 days** (configurable) it is cancelled.
- **Cancellation** is allowed through the API up to fulfilment; from processing onwards only a support agent can cancel.
- **Out of scope:** returns, partial shipments, multi-currency, adding items to an existing order, credit limits,
  CRM/ERP synchronisation, price list maintenance (seeded in v1) and the customer portal itself.

## Documentation map

| Document | What it is for |
|---|---|
| [`architecture/README.md`](architecture/README.md) | **Start here.** Scope, business assumptions, architectural style, C4 diagrams, modules, order lifecycle, how the NFRs are met |
| [`architecture/adr/`](architecture/adr/README.md) | 22 Architecture Decision Records — one decision per file, immutable once accepted |
| [`architecture/implementation-plan.md`](architecture/implementation-plan.md) | Phased plan, delivery workflow, acceptance criteria per PR, the [open questions](architecture/implementation-plan.md#open-questions) and the ordered [next steps N1–N16](architecture/implementation-plan.md#next-steps-for-the-delivery-team) |
| [`architecture/phase-0-results.md`](architecture/phase-0-results.md) | Evidence from the seven verification spikes and the design changes they forced |
| [`architecture/ai-usage.md`](architecture/ai-usage.md) | Where AI was used and where human judgement shaped or overrode it |
| [`tests/README.md`](tests/README.md) | Test levels, what each project proves, what it needs, shared test infrastructure |
| [`.github/pull_request_template.md`](.github/pull_request_template.md) | The PR format the delivery workflow expects |

## Scope

**Built.** Phases 0–4 of the [implementation plan](architecture/implementation-plan.md). An order can be submitted,
priced against seeded price lists, reserved through the outbox, read with its history and cancelled with its
compensation — all against the schema the Migrator builds, proven by tests in every PR. The complete state machine of
[ADR-0017](architecture/adr/0017-order-lifecycle-process.md) is implemented and covered by domain tests, including the
states no endpoint reaches yet.

**Not built.** Invoicing, payment checks, timers, fulfilment and the retry/dead-letter policy are specified but not
implemented — they are next steps [N1–N16](architecture/implementation-plan.md#next-steps-for-the-delivery-team).
Billing and shipping have ports with fakes, and billing additionally a real-shaped HTTP adapter; the other adapters are
next steps.

**One open design question** should be decided before next step N3: how an order leaves `RequiresAttention` —
[ADR-0017 § 2a](architecture/adr/0017-order-lifecycle-process.md).

---

## Running the solution

### Prerequisites

| | |
|---|---|
| .NET SDK | **10.0.401** or a later feature band — pinned in [`global.json`](global.json) |
| Docker | Docker Desktop or an equivalent engine. Required for **both** local options and for the integration tests |
| Ports | `8080` (Keycloak) must be free. Compose also uses `5432`, `8081` and `18888` |

Nothing else is needed: the Aspire CLI is not required, and package versions are centrally pinned in
[`Directory.Packages.props`](Directory.Packages.props).

### Option A — Aspire AppHost (the .NET inner loop)

Starts PostgreSQL and Keycloak as containers, runs the **Migrator to completion**, then the API — exactly the order used
in deployment ([ADR-0008](architecture/adr/0008-dedicated-migrator-forward-only.md)).

```bash
dotnet run --project src/AppHost/OrderPlatform.AppHost
```

**From Visual Studio:** set **OrderPlatform.AppHost** as the startup project (right-click it → *Set as Startup
Project*; the solution does not store one) and press F5. The dashboard opens in the browser automatically.

Aspire dashboard: `http://localhost:15080` — traces, metrics, logs and per-resource output. Started from the command
line it prints a one-time login URL with a token; Visual Studio opens it for you. API: `http://localhost:5080` ·
Keycloak: `http://localhost:8080`.

Ports and the dashboard endpoints come from
[`src/AppHost/OrderPlatform.AppHost/Properties/launchSettings.json`](src/AppHost/OrderPlatform.AppHost/Properties/launchSettings.json);
the Aspire CLI is not used, so `AspireUseCliBundle` stays at its default and ASPIRE010 is suppressed in the project file.

If the dashboard shows `postgres` unhealthy while `migrator` and `api` sit in **Waiting**, the `orderplatform-postgres`
data volume was initialised with a different password than the AppHost is using (PostgreSQL only applies
`POSTGRES_PASSWORD` to an empty data directory). Delete the volume once and start again — the Migrator rebuilds the
schema and the seed data:

```bash
docker volume rm orderplatform-postgres
```

### Option B — docker compose (no .NET toolchain)

Builds the API and Migrator images and runs the same stack with a standalone Aspire Dashboard as the OTLP receiver.
This is also what the CI smoke test runs.

```bash
docker compose -f deploy/docker-compose.yml up --build
```

API: `http://localhost:8081` · Keycloak: `http://localhost:8080` · Dashboard: `http://localhost:18888`.

Copy [`deploy/.env.example`](deploy/.env.example) to `deploy/.env` to override the development defaults. Every
credential in that file and in [`deploy/keycloak/orderplatform-realm.json`](deploy/keycloak/orderplatform-realm.json) is
**development-only** ([ADR-0016](architecture/adr/0016-security-baseline.md)).

In both options the external integrations run in `Fake` mode — a deployed environment refuses to start that way
([ADR-0014](architecture/adr/0014-external-integrations-ports-and-adapters.md)).

### Calling the API

Every endpoint is under `/v1`, needs a bearer token from the local Keycloak realm, and writes need an
`Idempotency-Key` header ([ADR-0020](architecture/adr/0020-api-conventions-and-versioning.md)). The OpenAPI document is
served at `/openapi/v1.json`.

The realm seeds two customer-system clients (`acme-erp`, `globex-procurement`), a portal user and a support agent.
Get a token for ACME:

```bash
curl -s http://localhost:8080/realms/orderplatform/protocol/openid-connect/token -d grant_type=client_credentials -d client_id=acme-erp -d client_secret=acme-erp-dev-secret
```

Submit an order against the seeded price lists (`SKU-1000`, `SKU-1001`, `SKU-2000`), here on the compose port:

```bash
curl -i -X POST http://localhost:8081/v1/orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" -d "{\"lines\":[{\"sku\":\"SKU-1000\",\"quantity\":2}]}"
```

Then `GET /v1/orders/{id}`, `GET /v1/orders/{id}/history` and `POST /v1/orders/{id}/cancel`.

[`.github/scripts/compose-smoke.sh`](.github/scripts/compose-smoke.sh) is a runnable end-to-end example of the same
sequence, including the checks CI makes.

### Building and testing

```bash
dotnet build OrderPlatform.slnx
```

Tests run on xUnit v3 with Microsoft.Testing.Platform (selected in [`global.json`](global.json)). There are nine test
projects, one per test level of [ADR-0022](architecture/adr/0022-testing-strategy.md).
**[`tests/README.md`](tests/README.md) is the guide to them** — what each project proves, what it needs, a table of
what every test class covers, the shared test infrastructure and the conventions. Only `OrderPlatform.Api.IntegrationTests` and `OrderPlatform.AppHost.Tests` need
Docker (and port `8080` free); every other level runs on a bare machine in seconds.

Everything:

```bash
dotnet test OrderPlatform.slnx
```

One project:

```bash
dotnet test --project tests/Ordering.Domain.Tests
```

One class or one test, with the xUnit query filter — `/assembly/namespace/class/method`, `*` for any part:

```bash
dotnet test --project tests/Pricing.Domain.Tests -- --filter-query "/*/*/PricingRuleTests/*"
```

What a project actually contains: test names are written as behaviour sentences, so the list reads as that level's
specification. Run the built test executable with xUnit's own switches:

```bash
./tests/Ordering.Domain.Tests/bin/Debug/net10.0/Ordering.Domain.Tests.exe -list tests
```

In Visual Studio the same projects appear in Test Explorer; the two Docker-backed ones take a minute or so to start
their containers before the first test reports.

Two things to expect locally:

- **A solution-wide run starts all nine projects at once.** On a loaded machine the wall-clock-sensitive levels — the
  billing adapter's 10-second attempt timeout, the Migrator's trace export — can exceed their timeouts and fail there
  while passing on their own. Re-run the project alone before investigating. CI runs the levels in sequence instead, in
  the order set out in [`pull-request.yml`](.github/workflows/pull-request.yml).
- **One test is skipped by design outside CI:** `RepositoryScriptTests.Generated_Marten_patch_is_expand_only` needs
  `MARTEN_PATCH_FILE`, which the release-compatibility job generates from the previous release.

### CI/CD

[`.github/workflows/pull-request.yml`](.github/workflows/pull-request.yml) — build with warnings as errors, vulnerable
package scan, unit, architecture, migration-safety, integration and AppHost tests, a **release compatibility** check
(the previous release still works against the new schema and contracts) and the **compose smoke test**.
[`.github/workflows/main.yml`](.github/workflows/main.yml) adds the image build, SBOM and image vulnerability scan; the
deployment stages are documented stubs until a hosting platform is chosen
([ADR-0021](architecture/adr/0021-deployment-and-ci-cd.md)).

---

## Repository layout

```
src/
  AppHost/         OrderPlatform.AppHost (Aspire orchestration), OrderPlatform.ServiceDefaults (OTel, health, resilience)
  Host/            OrderPlatform.Composition (module list, Marten/Wolverine config), OrderPlatform.Api (endpoints, auth)
  Tools/           OrderPlatform.Migrator (DbUp scripts, Marten schema, Wolverine storage, verification)
  BuildingBlocks/  technology-free primitives + infrastructure shared by modules
  Modules/         Ordering, Customers, Pricing, Inventory, Billing, Shipping
                   each: Domain | Application | Infrastructure | Contracts
                   (only Contracts may be referenced by another module)
tests/             one project per test level of ADR-0022, plus OrderPlatform.Testing (shared infrastructure)
deploy/            docker-compose.yml, .env.example, Keycloak realm
spikes/            Phase 0 throwaway experiments, kept as evidence
architecture/      README, ADRs, implementation plan, spike results, AI usage log
.github/           CI/CD workflows and scripts, PR template, branch protection definition
```

Module boundaries, layering, handler coverage and event registration are enforced by
`tests/OrderPlatform.Architecture.Tests` — an illegal reference fails the build, not the review
([ADR-0015](architecture/adr/0015-architecture-tests-enforce-boundaries.md)).

## Contributing

The delivery workflow — one concern per PR, a ~15-minute review budget, tests in the same PR, branch naming and how
deviations from the plan are recorded — is in
[`architecture/implementation-plan.md` § Delivery workflow](architecture/implementation-plan.md#delivery-workflow).
New decisions are added as ADRs from [`architecture/adr/template.md`](architecture/adr/template.md); accepted ADRs are
superseded, never edited away.
