# ADR-0015: Architecture tests enforce boundaries

- **Status:** Accepted
- **Date:** 2026-09-17
- **Related:** ADR-0003 (module structure), ADR-0011 (minimal APIs), ADR-0016 (database roles), ADR-0019 (feature flags)

## Context

Module boundaries in a monolith erode one convenient reference at a time. Code review alone does not catch it reliably.

## Decision

`OrderPlatform.Architecture.Tests` uses **ArchUnitNET** (actively maintained, expressive rule API; maintenance status
re-checked in spike S5) and runs in every build. Rules:

**Module boundaries**
- A module may reference another module **only** through its `*.Contracts` assembly.
- `*.Contracts` depends on nothing except `BuildingBlocks` primitives, and exposes **no Marten, Wolverine, Dapper or
  Npgsql types**.

**Layering inside a module**
- `*.Domain` has no dependency on `Application`, `Infrastructure`, Marten, Wolverine, Dapper, Npgsql or ASP.NET Core.
- `*.Application` does not depend on `*.Infrastructure`.
- Handlers (`*.Application`) do not reference ASP.NET Core types — HTTP stays in endpoints (ADR-0011).

**Cross-cutting**
- Only the feature flag adapter references `Microsoft.FeatureManagement`; everything else uses `IFeatureFlags` (ADR-0019).
- Fake adapters live only in `*.Infrastructure` under a `Fakes` namespace (supports the production guard in ADR-0014).

**Messaging and persistence registration** (runtime checks against the built host, in the same test project)
- Every command and integration event type in `*.Contracts` and `*.Application` has **exactly one** Wolverine handler.
  Wolverine silently drops messages without a handler (spike S1).
- Handler classes are named `<Message>Handler` and live in `*.Application`.
- Every type stored through Marten in a module (`*.Infrastructure` document and projection types) is **registered
  explicitly** in that module's Marten configuration (spike S4).
- Every event type has an explicit alias (ADR-0010 rule 4).

### Implementation (PR 1c)

- Boundaries are checked twice: type dependencies with ArchUnitNET, and `ProjectReference` items in the module `.csproj`
  files. The compiler drops references that no code uses, so an illegal project reference would otherwise stay invisible
  until someone uses it.
- Commands and integration events are identified by marker interfaces in `BuildingBlocks` (`ICommand`,
  `IIntegrationEvent`, `IDomainEvent`). Every handled platform message must carry one of them, so coverage cannot be
  bypassed by leaving a message unmarked.
- **Revised:** a command has **exactly one** handler; an integration event has **at least one** — it is published language,
  and several modules may legitimately subscribe.
- **Revised:** handlers live in `*.Application`, except the two platform handlers outside the modules, which are listed
  explicitly in the test (`OrderPlatform.Api.Diagnostics`, the idempotency cleanup in `BuildingBlocks.Infrastructure`).
- Handler coverage is read from the Wolverine handler graph of the real Api host, built but not started and compiled the
  way `codegen write` does, so no database is needed.
- Document registration: every platform type passed as a generic argument to a Marten session or store member
  (`Insert<T>`, `LoadAsync<T>`, `Query<T>`, …; event store members excluded) must be a registered document type.
  Every `IDomainEvent` must be a registered event type.
- **Explicit aliases** are enforced by pinning names instead: `contract-names.approved.txt` lists every stored Wolverine
  message type name and Marten event type name. A rename changes the list and fails the build; a new name is a reviewed
  addition to the file. This also covers messages sitting in durable queues (ADR-0010 rule 8).

**Not enforced here**
- Schema isolation between modules is **not** checked by scanning SQL text — that gives false confidence. It is enforced
  by database permissions (ADR-0016) and integration tests.
- Migration safety rules are enforced by `OrderPlatform.Migrations.Tests` (ADR-0009).

## Consequences

- Positive: violations fail CI with a clear message; rules are executable documentation.
- Negative: tests need updating when the module template legitimately changes.
