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

**Not enforced here**
- Schema isolation between modules is **not** checked by scanning SQL text — that gives false confidence. It is enforced
  by database permissions (ADR-0016) and integration tests.
- Migration safety rules are enforced by `OrderPlatform.Migrations.Tests` (ADR-0009).

## Consequences

- Positive: violations fail CI with a clear message; rules are executable documentation.
- Negative: tests need updating when the module template legitimately changes.
