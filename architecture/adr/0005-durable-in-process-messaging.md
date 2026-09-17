# ADR-0005: Durable in-process messaging, broker deferred

- **Status:** Accepted — conditions verified by spikes S1 and S2 ([phase-0-results](../phase-0-results.md))
- **Date:** 2026-09-17

## Context

Messages between modules must not be lost if the process crashes. A broker (RabbitMQ) guarantees this but adds
infrastructure to run, secure and monitor. Since all modules run in one process and share one PostgreSQL server, the
database can provide the durability instead.

Decision criterion agreed up front: **in-process only if messages survive a process crash; otherwise RabbitMQ.**

## Decision

We will run messaging **in-process on durable local queues persisted in PostgreSQL** (Wolverine `wolverine` schema):
- **Transactional outbox**: messages published during a handler are stored in the same transaction as the Marten
  changes and dispatched only after commit.
- **Durable local queues**: every queued message is persisted before processing; after a crash, Wolverine's durability
  agent recovers and re-dispatches incomplete messages (on restart, or on another node).
- **Scheduled messages** (payment checks, due dates, customer response timeouts — ADR-0017) are persisted the same
  way and survive crashes and deployments; no separate scheduler service is needed.
- **Dead letters** are stored in the database for inspection and replay.
- Wolverine's default local queues are in-memory — durability is **explicitly enabled** in the host configuration and
  verified by an integration test.
- **Dapper-based modules** use the same PostgreSQL outbox by enlisting their Npgsql transaction. Because this relies on
  low-level Wolverine types, modules use a `RelationalOutbox` helper from `BuildingBlocks` instead (ADR-0007).
- Production runs with `DurabilityMode.Balanced` (several API instances). Spike S1 ran in `Solo` mode; recovery with two
  instances is verified by the durability tests (plan next step N14).

## Required companion rules

1. **At-least-once delivery** → every handler is idempotent (natural idempotency, or a processed-message check).
2. **HTTP requests are not durable** → `POST` endpoints that create state accept an `Idempotency-Key` header; retries
   with the same key return the original result.
3. **Calls to external providers** pass our own idempotency key (e.g. reservation or refund id) so retries never reserve or refund twice.

## Alternatives considered

| Option | Why not (now) |
|---|---|
| RabbitMQ from day one | Extra infrastructure with no benefit while there is a single deployable; still needs an outbox for DB/broker consistency |
| In-memory queues | Loses messages on crash — fails the criterion |

## Consequences

- Positive: no broker to operate; atomic state + message commit; switching to RabbitMQ later is transport configuration, not a handler rewrite.
- Negative: message throughput bounded by PostgreSQL; database becomes the single critical dependency.
- Revisit when: a second deployable needs to consume events, or message volume stresses the database.
- Evidence: with the process force-killed, 80 queued and 1 scheduled message were persisted and all were handled after
  restart (S1); a Dapper transaction commit/rollback included/excluded its message atomically (S2).
