# ADR-0006: Marten event sourcing for Ordering

- **Status:** Accepted — see the implementation notes below
- **Date:** 2026-09-17

## Context

"Tracking the lifecycle of an order" is a core requirement. Orders move through many states driven by internal
decisions and external systems, and support staff need to know *what happened and when*, not only the current state.

## Decision

We will implement the Ordering module with **event sourcing on Marten** (PostgreSQL):
- The `Order` aggregate is rebuilt from its event stream (`OrderSubmitted`, `InventoryReserved`, `InvoiceIssued`,
  `InvoicePaid`, `OrderCancelled`, ...). Invariants (e.g. which states allow cancellation) are enforced in the
  aggregate, which also drives the lifecycle process (ADR-0017).
- Appends use optimistic concurrency on the stream version.
- **Read models** are Marten projections: `OrderDetails` (inline, strongly consistent for `GET /orders/{id}`) and later
  asynchronous projections (e.g. customer order history, operational dashboards).
- The lifecycle history endpoint reads the raw stream — the audit trail is the source of truth, not a side table.
- Marten documents are also available for non-event-sourced Ordering data where a document fits better than a table.

## Alternatives considered

| Option | Why not |
|---|---|
| State-based persistence + status history table | History becomes a secondary, easily inconsistent record |
| Dedicated event store (EventStoreDB/KurrentDB) | Another database to operate; Marten keeps everything in PostgreSQL with transactional outbox integration |

## Implementation (PRs 2f, 2g, 2i)

Marten 9 dispatches conventional `Apply` methods through a **compile-time source generator that must run in the assembly
declaring the type it generates for**. That collides with ADR-0003: `Ordering.Domain` is deliberately free of
frameworks, and the architecture tests enforce it. The consequences are worth knowing before the next aggregate:

- **Streams are folded by hand.** `AggregateStreamAsync<Order>` fails at runtime (`InvalidProjectionException`), so
  `OrderStream.LoadAsync` in `Ordering.Application` dispatches the stored events with an explicit `switch` and throws on
  one it does not know. **Every new lifecycle event needs a case there**, or loading an order that has one fails.
- **Marten-backed types live where Marten is referenced.** The `OrderDetails` read model is in `Ordering.Application`,
  not in the domain (no generator) and not in `Ordering.Infrastructure` (the query handlers may not depend on it).
- **An inline snapshot silently ignores an event it has no `Apply` for**, which would freeze the read model at a stale
  status with nothing failing. `ProjectionCoverageTests` therefore fails the build when a stored event has no `Apply` on
  `OrderDetails` — the same treatment spike S4 earned for unregistered documents.
- Neither problem was visible to the architecture tests or to a trace: PR 2i's tracing test passed while the flow was
  broken. Only a test that asserted the resulting **state** caught it.

ADR-0004 records the matching constraint for Wolverine's generated code.

## Consequences

- Positive: complete audit trail (including who cancelled and why); new projections can be built retroactively from
  history; natural fit with the Wolverine outbox and scheduled messages.
- Negative: events are permanent, so schema evolution needs discipline (ADR-0010); team learning curve; projections must be rebuildable.
- Only Ordering is event sourced. Other modules are CRUD-shaped and use Dapper (ADR-0007).
