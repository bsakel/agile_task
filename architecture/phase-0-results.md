# Phase 0 — Spike Results

- **Date:** 2026-09-17
- **Environment:** Windows 11, .NET SDK 10.0.401, Docker 29.6.1, PostgreSQL 17.11 (container)
- **Code and scripts:** [`spikes/`](../spikes) — throwaway code, kept as evidence. Re-runnable against a local
  `postgres:17-alpine` container on port 55432.

Every spike from the [implementation plan](implementation-plan.md#phase-0--spikes-and-verification-de-risk-before-committing)
was executed. Each result lists what was observed, what it means for the design, and where the ADRs or plan were changed.

## Summary

| # | Question | Result | Design impact |
|---|---|---|---|
| S1 | Durable outbox survives a process kill | ✅ Confirmed | Two unexpected findings on Wolverine 6 (code generation, handler discovery) |
| S2 | Dapper transaction enlisted in the outbox | ✅ Works | Needs a BuildingBlocks wrapper around internal-looking APIs |
| S3 | Older build reads a stream with an unknown event type | ❌ Fails | ADR-0010 rule changed to **register before emit** |
| S4 | Export Marten schema as SQL, run with no auto-create | ⚠️ Partially | Wolverine storage not exportable; unregistered document types silently missing; generated DDL not online-safe by default |
| S5 | Licences and maintenance | ✅ All permissive | Keycloak Aspire integration is preview-only; versions pinned |
| S6 | Online DDL behaviour on PostgreSQL 17 | ✅ Rules confirmed | Two rules added (volatile defaults, DbUp transaction mode) |
| S7 | Pause an integration queue while its provider is down | ❌ Unsafe | Listener circuit breaker dropped; scheduled retries with backoff instead |

## S1 — Durable outbox and crash recovery

**Setup** ([`S1.Durability`](../spikes/S1.Durability), [`scripts/s1-crash.ps1`](../spikes/S1.Durability/scripts/s1-crash.ps1)):
minimal API host with Marten + `IntegrateWithWolverine`, `UseDurableLocalQueues`, `AutoApplyTransactions`. 40 messages
published with `IMessageBus.PublishAsync` from an endpoint, 40 through `OutboxedSessionFactory` together with Marten
documents, 1 message scheduled 20 s ahead. Handler takes 1.5 s. Process **force-killed** 1.2 s later.

**Observed**

| Moment | Handled | Envelopes in PostgreSQL |
|---|---|---|
| Right after the kill | 0 | `Incoming = 80`, `Scheduled = 1` |
| Restart (after the scheduled time passed) | 80 + 1 | `Handled = 81` |

A message scheduled 5 s ahead while the process keeps running is also handled on time.

**Unexpected findings**

1. **Wolverine 6 no longer ships the runtime code compiler.** The host fails at startup with
   `no IAssemblyGenerator (Roslyn) is registered` unless `WolverineFx.RuntimeCompilation` is referenced, or handler code
   is pre-generated (`codegen write`) and `TypeLoadMode.Static` is used. Static mode was verified to work.
2. **Handler discovery is convention-based and silent.** A class named `SpikeHandlers` (plural) was not discovered
   (`Wolverine found no handlers`), and `PublishAsync` of a message without a handler **silently did nothing** — no
   exception, no stored envelope.
3. Tests ran in `DurabilityMode.Solo`. Recovery in the multi-node `Balanced` mode used in production was not measured.

**Changes:** ADR-0004 (code generation, explicit discovery, handler coverage test), ADR-0021 (pre-generated code in
images), ADR-0022 (durability test in `Balanced` mode), plan Phase 1.

## S2 — Dapper transaction in the Wolverine outbox

**Setup:** endpoint opens an Npgsql transaction, inserts a row with Dapper, enlists the transaction with
`MessageContext.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction((IMessageDatabase)runtime.Storage, tx))`, publishes
a message, then commits or rolls back.

**Observed:** commit → row stored and message handled; rollback → no row and no message. The message store behind
Marten integration is `Wolverine.Postgresql.PostgresqlMessageStore`, so Dapper modules and Marten share the same outbox.

**Implication:** the pattern works but relies on low-level types (`MessageContext`, `DatabaseEnvelopeTransaction`, a
cast of `runtime.Storage`). Modules must not repeat that code.

**Changes:** ADR-0005 (condition met → Accepted), ADR-0007 (`RelationalOutbox` helper in BuildingBlocks), plan Phase 1.

## S3 — Unknown event types after a rollback

**Setup** ([`S3.UnknownEvents/s3.cs`](../spikes/S3.UnknownEvents/s3.cs)): a "newer" store appends `OrderSubmitted`,
`InventoryReserved` and a new `OrderPriorityChanged` event to a stream with an inline snapshot projection. The stored
.NET type of the new event is then made unloadable to simulate an older build, which reads the stream.

**Observed (older build)**

| Operation | Result |
|---|---|
| `Events.FetchStreamAsync` | ❌ `UnknownEventTypeException` |
| `Events.AggregateStreamAsync` (live) | ❌ `UnknownEventTypeException` |
| Load inline snapshot document | ✅ |
| `FetchForWriting` + append a known event | ✅ (starts from the inline snapshot) |

Marten 9 has `SkipUnknownEvents`, but only for the asynchronous projection daemon — there is no option for live reads.
When the older build appends through the inline snapshot, it re-serialises the snapshot **without** the data from the
unknown event.

**Implication:** emitting a new event type behind a flag (ADR-0010 rule 5 as written) is **not sufficient**. Once one
such event exists in production, any build that does not know the type breaks on that stream (e.g.
`GET /orders/{id}/history`, live aggregation).

**Changes:** ADR-0010 rules 5 and 7 (register before emit, rollback floor, snapshot rebuild), ADR-0009 revert table.

## S4 — Marten schema export and production mode

**Setup** ([`scripts/s4.sh`](../spikes/S1.Durability/scripts/s4.sh)): `AutoCreate.None` for Marten and Wolverine, schema
generated with the JasperFx command line (`db-patch`, `db-dump`, `db-assert`, `resources setup`, `storage`).

**Observed**

1. Marten and Wolverine storage are **separate migration targets**; `db-patch` requires `-d <database>` per target.
2. `db-patch` produced SQL for Marten objects only. **Wolverine message storage could not be exported as SQL**
   (`db-dump` contains no Wolverine tables; `storage rebuild -f` threw). `resources setup` provisions it correctly.
3. **Document types that are not registered explicitly are not part of the schema.** The v1 patch and
   `resources setup` created no table for `Requested`/`Processed`; in production mode the first write returned **HTTP
   500**. Registering them (`Schema.For<T>()`) fixed it.
4. Marten's generated index DDL is `CREATE INDEX` (blocks writes, see S6) unless the index is configured with
   `IsConcurrent = true`, which produces and applies `CREATE INDEX CONCURRENTLY` correctly.
5. `db-patch` also writes a **drop file** containing `drop schema ... CASCADE`.
6. With `AutoCreate.None` and a missing schema, the host **fails fast at startup** with a clear message — the desired behaviour.

**Changes:** ADR-0008 (how the Migrator applies Marten and Wolverine schema, review artifact, drop files), ADR-0009
(concurrent Marten indexes), ADR-0015/0022 (registration checks by running tests in production mode), plan Phase 1.

## S5 — Licences and maintenance

| Package | Version (latest stable) | Licence | Last published |
|---|---|---|---|
| WolverineFx, WolverineFx.Marten, WolverineFx.Postgresql, WolverineFx.RuntimeCompilation | 6.38.0 | MIT | 2026-09-15 |
| Marten | 9.37.0 | MIT | 2026-09-15 |
| JasperFx, JasperFx.Events (transitive) | 2.72.0 | MIT | — |
| Weasel.Core, Weasel.Postgresql (transitive) | 9.32.0 | MIT | — |
| Npgsql | 10.0.3 | PostgreSQL | 2026-05-27 |
| Dapper | 2.1.86 | Apache-2.0 | 2026-09-12 |
| dbup-postgresql | 7.0.1 | MIT | 2026-02-23 |
| Microsoft.FeatureManagement.AspNetCore | 4.7.0 | MIT | 2026-08-27 |
| Microsoft.Extensions.Http.Resilience | 10.10.0 | MIT | 2026-09-09 |
| Aspire.Hosting.PostgreSQL | 13.5.4 | MIT | 2026-09-15 |
| Aspire.Hosting.Keycloak | **13.5.4-preview only** | MIT | 2026-09-15 |
| TngTech.ArchUnitNET (+ .xUnitV3) | 0.13.4 | Apache-2.0 | 2026-08-20 |
| WireMock.Net | 2.15.0 | Apache-2.0 | 2026-08-15 |
| xunit.v3 | 4.0.1 | Apache-2.0 | 2026-09-12 |
| Shouldly | 4.3.0 | BSD-3-Clause | 2026-07-18 |
| NSubstitute | 6.2.0 | BSD-3-Clause | 2026-08-11 |
| Testcontainers.PostgreSql, Testcontainers.Keycloak | 4.15.0 | MIT | 2026-09-06 |
| *MediatR (rejected)* | 14.2.0 | custom licence file (commercial) | — |
| *FluentAssertions (rejected)* | 8.11.0 | custom licence file (commercial) | — |

- The Critter Stack assemblies contain **optional integration hooks for CritterWatch** (a commercial monitoring
  product) but no licence-key checks; nothing commercial is required.
- Wolverine and Marten are one major version ahead of most published examples (Wolverine 6, Marten 9); examples found
  online must be checked against these versions.

**Changes:** ADR-0004 (versions and CritterWatch note), ADR-0013 (Keycloak as a plain container resource), plan
(pinned versions).

## S6 — Online DDL on PostgreSQL 17

**Setup** ([`S6.OnlineDdl/s6.sh`](../spikes/S6.OnlineDdl/s6.sh), [`dbup.cs`](../spikes/S6.OnlineDdl/dbup.cs)): table
with 2,000,000 rows. Timings include ≈ 0.4 s of `docker exec` overhead.

| Experiment | Observed |
|---|---|
| Reader holds a transaction 8 s; `ALTER TABLE` without `lock_timeout` | `ALTER` waited 7.5 s; a **plain `SELECT` queued behind it for 6.5 s** |
| Same with `SET lock_timeout = '2s'` | `ALTER` failed after 2 s; the `SELECT` ran immediately |
| `ADD COLUMN` nullable / constant default | No table rewrite |
| `ADD COLUMN ... DEFAULT gen_random_uuid()` (volatile) | **Table rewrite**, 5 s under exclusive lock |
| `VALIDATE CONSTRAINT` | Takes only `ShareUpdateExclusiveLock` (reads and writes continue) |
| `SET NOT NULL` after a validated `CHECK (col IS NOT NULL)` | No full scan needed |
| `CREATE INDEX` | Takes `ShareLock` (**blocks writes**) |
| `CREATE INDEX CONCURRENTLY` inside a transaction | Error `25001` |
| Batched backfill, 100k rows per batch | 2M rows in 20 short transactions, 56 s |
| DbUp default (no transaction configured) | Concurrent index script succeeds |
| DbUp `WithTransactionPerScript` / `WithTransaction` | Concurrent index script fails (`25001`) |

**Implications:** ADR-0009 rules hold. Additional rules needed: no volatile defaults on existing tables; DbUp runs
without its own transaction, so scripts must be idempotent and wrap multi-statement changes in explicit
`BEGIN/COMMIT`; a `lock_timeout` failure needs a retry in the Migrator; backfills take minutes and do not belong in the
Migrator.

**Changes:** ADR-0009, ADR-0008, plan Phase 1 migration tests.

## S7 — Circuit breaker on an integration queue

**Setup** ([`scripts/s2-s7.ps1`](../spikes/S1.Durability/scripts/s2-s7.ps1)): local durable queue for `FlakyWork` with
Wolverine's listener circuit breaker; the handler throws while a simulated provider is down.

**Observed**

| Configuration | Result |
|---|---|
| Breaker + short inline retries | Listener paused (attempts stayed flat), but **8 of 30 messages were dead-lettered** before the breaker tripped; 22 handled after recovery |
| **No breaker** + scheduled retries (5 s, 10 s, 20 s, 40 s) | **10/10 handled** after the provider recovered, 0 dead letters |
| **Breaker + scheduled retries** | Messages returned from `Scheduled` to `Incoming` and **stalled** — 0 handled 45 s after recovery; all handled only after a **process restart** |

**Implication:** the listener circuit breaker combined with durable local queues and scheduled retries stops
processing until a restart. This is the fallback foreseen in the plan: rely on scheduled retries with backoff. Fast
failure towards an unavailable provider stays in the HTTP resilience pipeline (Polly circuit breaker), which makes each
attempt cheap. The stall is worth reporting upstream; the design does not depend on it being fixed.

**Changes:** ADR-0014, plan Phase 3 acceptance criteria.

## Follow-up verifications (moved into later phases)

| Item | Where |
|---|---|
| Crash recovery in `DurabilityMode.Balanced` with two API instances | Phase 3 durability test |
| Pre-generated Wolverine code inside the container build | Phase 1 Dockerfile |
| Upstream issue for breaker + scheduled retry stall | Backlog |
