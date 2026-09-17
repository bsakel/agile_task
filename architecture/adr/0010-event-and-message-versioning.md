# ADR-0010: Event and message contract versioning

- **Status:** Accepted — rules 5 and 7 revised after spike S3 ([phase-0-results](../phase-0-results.md#s3--unknown-event-types-after-a-rollback))
- **Date:** 2026-09-17
- **Related:** ADR-0009 (expand/contract), ADR-0017 (order lifecycle), ADR-0019 (feature flags)

## Context

Marten events are stored forever, and Wolverine messages can sit in durable queues across a deployment or rollback.
A release may therefore meet events and messages written by a **newer or older** version of the code. This is the
messaging counterpart of ADR-0009.

Spike S3 established how Marten 9 behaves when a build reads a stream containing an event type it cannot load:

| Operation in the older build | Result |
|---|---|
| `FetchStreamAsync` (e.g. `GET /orders/{id}/history`) | Fails with `UnknownEventTypeException` |
| Live `AggregateStreamAsync` | Fails with `UnknownEventTypeException` |
| Load an inline snapshot | Works |
| `FetchForWriting` on an inline snapshot + append | Works, but the snapshot is re-saved **without** the data from the unknown event |

Marten's `SkipUnknownEvents` applies only to asynchronous projections; there is no option for live reads. Emitting a
new event type behind a flag is therefore **not enough**: once one such event is stored, every build that does not know
the type breaks on that stream.

## Decision

1. **Additive changes only** to an existing event or message type: new optional properties with sensible defaults.
2. **Tolerant readers**: unknown JSON properties are ignored; missing properties get defaults.
3. **Never rename or remove** a property or type that has been released. A breaking shape change creates a new type
   (`OrderSubmittedV2`) plus an **upcaster** that converts old events to the new shape on read.
4. **Explicit type names**: event and message type aliases are configured explicitly (`MapEventType<T>("order_submitted")`),
   so renaming a C# class never changes the stored type name.
5. **Register before emit.** A new event or message type ships in **two steps**:

   | Release | Contains | Flag |
   |---|---|---|
   | N+1 | The type, its alias registration, `Apply` methods and handlers — **no code path emits it** | — |
   | N+2 (or later) | The code that emits it, behind a release flag (ADR-0019), enabled after the rollout completes | off → on |

   Rolling back N+2 → N+1 is safe: N+1 can read the new type. Rolling back to N is safe only while the flag has never
   been enabled in that environment.
6. **Rollback floor.** When a release-flag for a new event type is first enabled in an environment, the release that
   **registered** the type (N+1) becomes that environment's **rollback floor**. The deployment pipeline records the
   floor and refuses to deploy an older version (ADR-0021).
7. **Snapshots after a rollback.** If a rollback to N+1 happened while the flag was on, the inline snapshots written in
   the meantime may miss data from the new events. After rolling forward again, the affected snapshot projections are
   **rebuilt** from their streams (runbook step). Events remain the source of truth; no data is lost.
8. Integration events in `*.Contracts` are the published language between modules and follow the same rules. The same
   register-before-emit sequence applies to **Wolverine message types**: an older build must have a handler for every
   message type that may already be in a durable queue. (How Wolverine treats an unknown message type in a durable queue
   was not part of S3 and is verified in the Phase 3 durability tests.)

## Consequences

- Positive: rollback and rolling deployments do not break stream loading or message processing; history remains
  readable; the rule is mechanical and checkable in review.
- Negative: a new event type takes two releases before it can be emitted; the rollback floor limits how far back an
  environment can go once new events exist; snapshot rebuilds after a rollback are a manual runbook step in v1.
- The two-release sequence is the same shape as expand/contract for schema (ADR-0009), which keeps one mental model for
  the team.
