# ADR-0006: Marten event sourcing for Ordering

- **Status:** Accepted
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

## Consequences

- Positive: complete audit trail (including who cancelled and why); new projections can be built retroactively from
  history; natural fit with the Wolverine outbox and scheduled messages.
- Negative: events are permanent, so schema evolution needs discipline (ADR-0010); team learning curve; projections must be rebuildable.
- Only Ordering is event sourced. Other modules are CRUD-shaped and use Dapper (ADR-0007).
