# ADR-0022: Testing strategy

- **Status:** Accepted — see the implementation notes from Phases 2 and 3 below
- **Date:** 2026-09-17
- **Related:** ADR-0009, ADR-0014, ADR-0015, ADR-0017, ADR-0018, ADR-0019, ADR-0021

## Context

The architecture makes specific promises — module boundaries, safe migrations, durable messaging, a correct order
lifecycle, deterministic pricing, flags that can be switched off, adapters that map provider errors correctly. Each
promise needs a test at the cheapest level that can actually prove it.

## Decision

### Tooling

- **xUnit v3** as test framework, **Shouldly** for assertions (FluentAssertions is avoided because of its commercial
  licence from v8), **NSubstitute** only where a hand-written fake is impractical.
- **Testcontainers** (PostgreSQL, and Keycloak where auth is under test), **WireMock.Net** for HTTP adapters,
  **ArchUnitNET** for architecture rules.
- Time-dependent code uses `TimeProvider`; tests use `FakeTimeProvider`.
- Test data builders per module (`OrderBuilder`, `PriceListBuilder`), no shared static fixtures between modules.

### Levels

| Level | Project(s) | Scope | Infrastructure | Must cover |
|---|---|---|---|---|
| Domain unit | `<Module>.Domain.Tests` | Aggregates, value objects, pricing rules | None | **Every Order state transition and every disallowed command** (table-driven); every pricing rule; rounding cases |
| Application | `<Module>.Application.Tests` | Handlers as plain classes | In-memory fakes of ports | Handler decisions, messages emitted, flag on/off paths |
| Adapter | `<Module>.Infrastructure.Tests` | HTTP adapters | WireMock.Net | Success, every error mapping, timeout followed by outcome query |
| Integration | `OrderPlatform.Api.IntegrationTests` | Endpoints → handlers → Marten/Dapper → messages | Testcontainers PostgreSQL, schema created **by running the Migrator**; the API runs with **`AutoCreate.None`** as in production | One test per endpoint; idempotency behaviour; account scoping (`404` for other accounts); message cascades awaited with Wolverine's tracked sessions; timer handlers invoked by sending the scheduled message; a missing document registration fails here (S4) |
| Durability | `OrderPlatform.Api.IntegrationTests` (separate category) | Two API processes in `DurabilityMode.Balanced` | Testcontainers PostgreSQL | Kill one process with queued, scheduled and retrying messages; the other completes them; message types unknown to an older build (ADR-0010 rule 8) |
| Architecture | `OrderPlatform.Architecture.Tests` | Assembly dependencies, handler and document registration | None / built host without infrastructure | ADR-0015 rules |
| Migration safety | `OrderPlatform.Migrations.Tests` | DbUp scripts and the generated Marten patch | None | ADR-0009 expand/contract and online-safety rules |
| Smoke | CI step + post-deployment | Running system | docker-compose / deployed environment | Health, submit and read an order (ADR-0021) |

### Conventions

- **Feature flags:** any test of flagged behaviour runs with the flag on and off (ADR-0019).
- **Durability** (crash and restart completes pending messages and timers) is proven once by the durability tests, not
  per feature. Phase 0 proved it in `Solo` mode; the durability tests (plan next step N14) prove it in the production `Balanced` mode.
- Tests use `WolverineFx.RuntimeCompilation` (dynamic code generation); the container smoke test covers the
  pre-generated static mode (ADR-0021).
- Integration tests share one PostgreSQL container per test run and isolate data by unique account ids, not by
  recreating the database.
- Tests do not depend on execution order or wall-clock time.

### Implementation (PR 1c)

- Projects: `OrderPlatform.Architecture.Tests`, `OrderPlatform.Migrations.Tests`, `OrderPlatform.Api.IntegrationTests`,
  **`OrderPlatform.AppHost.Tests`** (added: `Aspire.Hosting.Testing` proves that the AppHost runs the Migrator to
  completion before the Api is ready), and the shared library `OrderPlatform.Testing`. See `tests/README.md`.
- xUnit v3 4.x runs on Microsoft.Testing.Platform v2; `global.json` selects that runner for `dotnet test`.
- One PostgreSQL and one Keycloak container per run. The Migrator is run as a **process**, like the deployment job.
- Process isolation where it matters:
  - Startup guards run the Api as a process and assert its exit code.
  - Trace export runs the Api as a process against a stand-in OTLP receiver. OpenTelemetry listeners are process-wide,
    so an in-process test host exported the HTTP and database activity of other tests (seen while implementing).
- Everything else uses the in-process host (`WebApplicationFactory`) with `JasperFxEnvironment.AutoStartHost = true`,
  which `RunJasperFxCommands` needs to start under a test host.
- WireMock.Net is on a separate path from `Aspire.Hosting.Testing`: their Humanizer versions conflict (NU1608), so
  `OrderPlatform.AppHost.Tests` does not reference `OrderPlatform.Testing`.

### Not in scope for v1

- **No coverage percentage gate.** The "must cover" column above is the requirement, reviewed in pull requests.
- Load and performance tests, UI end-to-end tests, mutation testing — backlog.
- Rollback compatibility test (previous release's integration tests against the new schema) — backlog (ADR-0009).

## Implementation notes from Phases 2 and 3

- **Do not assert on an in-memory fake across hosts.** The Api hosts of an integration test run share one database and
  therefore one set of durable queues, while each host has its own fake adapter (ADR-0014). A message sent by one host
  may be handled by another, whose fake knows nothing about the call — so an assertion on the fake passes or fails
  depending on which node picked the message up. PR 2j's release assertion failed about half the time this way, in the
  full suite only. Assert on the **durable message log** instead (a handled envelope for that order), which every node
  shares; the handler throws when the provider refuses, so a handled message is proof the call succeeded.
- **Asynchronous steps are polled, never slept on.** The "must cover" table above expects message cascades to be awaited
  with Wolverine's tracked sessions; that works when a test sends the message, but not when the cascade starts inside an
  HTTP request the test made. Those tests poll instead: `WaitForStatusAsync` rebuilds the order from its stream until it
  reaches the expected state, with a timeout that fails with the state it actually saw.
- **A trace is not a test.** PR 2i's tracing test passed while the flow was broken: the spans exist even when the
  handler throws. Assert the resulting state, and use traces for the end-to-end path, not for correctness.
- **`FakeTimeProvider` does not belong in a resilience pipeline** — see the warning in ADR-0014.
- Known defect: `ContractNamesTests` writes `contract-names.approved.received.txt` (via `Path.ChangeExtension`) while
  its failure message names `contract-names.received.txt`. Cosmetic, but it sends the reader to a file that is not
  there; in the backlog.

## Consequences

- Positive: each architectural promise has an executable proof; fast feedback from the lower levels; integration tests
  also verify the migrations.
- Negative: container-based tests need Docker in CI and locally; table-driven lifecycle tests must be updated with every state change.
