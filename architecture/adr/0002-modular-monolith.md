# ADR-0002: Modular monolith

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

The platform has clear bounded contexts (Ordering, Customers, Pricing, Inventory, Billing, Shipping) but a single team, an
unproven domain model and no evidence yet of independent scaling needs per context. Boundaries drawn today will be
wrong in places and must be cheap to move.

## Decision

We will build a **modular monolith**: one deployable host, with each bounded context implemented as an isolated module
that owns its code, its database schema and its public contracts. Modules are designed so that any of them can be
extracted into a separate service later without rewriting its domain or application code.

Extraction readiness is ensured by:
- communication only through `*.Contracts` (sync queries) or messages (async) — ADR-0003;
- no shared tables or cross-schema queries — ADR-0007;
- messaging through Wolverine, whose transports can be swapped from local queues to a broker — ADR-0005.

## Alternatives considered

| Option | Why not |
|---|---|
| Microservices from day one | Distributed-systems cost (network failures, deployment orchestration, distributed tracing, data consistency) before we know the boundaries are right |
| Layered monolith without modules | Boundaries erode quickly; extraction later becomes a rewrite |

## Consequences

- Positive: simple deployment and debugging, in-process calls, one database server, cheap boundary refactoring.
- Negative: modules scale together; a bad deployment affects every module; discipline must be enforced by tests (ADR-0015).
- Triggers to revisit: a module with divergent scaling or release cadence, or a separate team owning it.
