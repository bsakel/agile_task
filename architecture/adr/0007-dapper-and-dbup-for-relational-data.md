# ADR-0007: Dapper and DbUp for relational data

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

Customers, Pricing, Inventory, Billing and Shipping hold relational, mostly simple data (customer reference data, price lists and tax rules, reservation records, invoice
and refund records, shipment requests). We want explicit, reviewable SQL and schema changes whose effect on a live database is obvious —
a prerequisite for the expand/contract policy (ADR-0009).

## Decision

- **Dapper** for data access in relational modules. SQL is written by hand, lives in the module's `Infrastructure`
  project and is covered by integration tests against a real PostgreSQL (Testcontainers).
- **DbUp** for schema migrations: plain SQL scripts embedded in each module's `Infrastructure` project, executed by the
  dedicated Migrator (ADR-0008). DbUp runs **without its own transaction** (required for `CREATE INDEX CONCURRENTLY`,
  spike S6); script rules are in ADR-0009.
- **Messages from Dapper modules** go through a `RelationalOutbox` helper in `BuildingBlocks`, which enlists the module's
  `NpgsqlTransaction` in the Wolverine PostgreSQL outbox and flushes after commit. Modules never touch Wolverine's
  low-level types (`MessageContext`, `DatabaseEnvelopeTransaction`) directly (spike S2).
- **Schema per module** (`customers`, `pricing`, `inventory`, `billing`, `shipping`); each module's DbUp journal table lives in its
  own schema. No module reads or writes another module's schema.
- **Schema ownership:** DbUp owns the relational schemas; Marten owns `ordering`; Wolverine owns `wolverine`.
  No tool touches another tool's schema.
- Database roles: v1 uses one API role without DDL rights and a separate Migrator role; per-module roles are the target (ADR-0016).

## Alternatives considered

| Option | Why not |
|---|---|
| EF Core + EF migrations | Generated SQL and migrations are less transparent; model-driven diffs make it easy to produce destructive changes unknowingly |
| Marten documents for everything | Relational data with reporting needs benefits from real tables and constraints |

## Consequences

- Positive: full control over SQL and locking behaviour; migrations are reviewable SQL; low runtime overhead.
- Negative: more hand-written mapping code; no change tracking; SQL typos only caught by integration tests.
