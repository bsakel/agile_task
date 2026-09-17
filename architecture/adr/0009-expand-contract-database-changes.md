# ADR-0009: Expand/contract database changes

- **Status:** Accepted — rules confirmed and extended by spike S6 ([phase-0-results](../phase-0-results.md#s6--online-ddl-on-postgresql-17))
- **Date:** 2026-09-17
- **Related:** ADR-0008 (forward-only migrations), ADR-0010 (event and message versioning), ADR-0019 (feature flags)

## Context

Two goals:
1. **No downtime caused by database migrations.**
2. **Any release can be reverted immediately** by redeploying the previous application version — without restoring
   or rolling back the database.

Both goals require the same property: **application version N must work correctly against the schema of version N+1**.
This is needed not only for rollback, but also during every rolling deployment, when N and N+1 run side by side.

Problems this must address:

| Problem | Example |
|---|---|
| Breaking schema change | Dropping, renaming or retyping a column that the previous version still uses |
| New constraint old code violates | New `NOT NULL` column without default; new unique / foreign key / check constraint |
| Locking and table rewrites (downtime without any code incompatibility) | `CREATE INDEX` without `CONCURRENTLY` blocks writes; adding a validated FK scans the table under lock; changing column type rewrites the table; an `ALTER` waiting for a lock queues every subsequent query behind it |
| Large data backfills | A single `UPDATE` over millions of rows holds locks and bloats WAL |
| Data written by the new version that the old version cannot interpret | A new status value, new event type, JSON property rename |
| Lossy transformations | Splitting a free-text `address` into `street`, `number`, `post_code` cannot always be done reliably |

Expand/contract makes deployment rollback **possible**. It does not make every problem cheap to revert — that is the
role of feature flags (ADR-0019). The two are complementary.

## Decision

### Rule 1 — A deployment may only **expand** the schema

Allowed:
- Add a table, add a **nullable** column or a column with a **constant** default (no table rewrite).
- Add an index **concurrently**. Marten indexes are declared with `IsConcurrent = true`, which makes Marten generate and
  apply `CREATE INDEX CONCURRENTLY`.
- Add a constraint as `NOT VALID`; validate it in a later script (`VALIDATE` takes only `ShareUpdateExclusiveLock`).
- Make a column `NOT NULL` in two steps: add and validate `CHECK (col IS NOT NULL)`, then `SET NOT NULL` (no full scan
  under an exclusive lock).
- Add a temporary sync mechanism (trigger) for a transition.

Not allowed outside a contract script: `DROP`, `RENAME`, `ALTER COLUMN ... TYPE`, `SET NOT NULL` without a validated
check constraint, adding a validated constraint on an existing table, removing a sync mechanism.

Not allowed at all on existing tables: a column with a **volatile default** (e.g. `gen_random_uuid()`, `now()`), which
rewrites the whole table under an exclusive lock (5 s for 2M rows in S6). Add the column nullable and backfill instead.

### Rule 2 — Removal happens in a later **contract** deployment

A contract script may only remove an object when **all** of the following hold:
- **Code removal first:** the code that read or wrote the object was removed in an **earlier, separate deployment**
  that is live in production. Code removal and schema removal never ship together.
- **Stabilisation:** that code-removal deployment has been in production for **at least one full production release
  cycle** (default). A different period may be agreed per change and recorded in the contract PR.
- **Data:** any backfill has completed and been verified; no unresolved rows remain.

### Rule 3 — Migrations are forward-only and online-safe

- Every DDL script sets `lock_timeout` (e.g. `SET lock_timeout = '5s'`) so a blocked `ALTER` fails fast instead of
  stalling traffic — in S6 a plain `SELECT` waited 6.5 s behind an `ALTER` without it. The Migrator **retries** a
  script that failed on a lock timeout with backoff (default 5 attempts), then fails; the new application version is
  never deployed.
- **DbUp runs without its own transaction** (per-script transactions break `CREATE INDEX CONCURRENTLY`, S6). Therefore:
  - every script is **idempotent** (`IF NOT EXISTS`, guarded `DO` blocks), because a failed script is not journaled and runs again;
  - a script with more than one statement wraps them in an explicit `BEGIN; ... COMMIT;`;
  - a `CREATE INDEX CONCURRENTLY` statement is the **only** statement in its script.
- **Backfills do not run in the Migrator.** They take minutes on real data (2M rows ≈ 1 min in S6). A backfill is a
  batched, idempotent, resumable **background job** (Wolverine message processing one batch and scheduling the next),
  started after the rollout completes. Each batch is its own short transaction.

### Rule 4 — Rules extend beyond SQL

- Marten documents: add properties only; renaming or removing follows the same expand → remove code → contract sequence.
- Events and messages: ADR-0010.
- Enum-like values (e.g. order status): older versions must tolerate unknown values, or the new value is written only
  behind a feature flag that is enabled after the rollout completes (ADR-0019).

### Choosing how to revert

| What went wrong | Revert mechanism | Speed (v1) |
|---|---|---|
| New feature behaves badly | Disable its release flag (ADR-0019) | Config reload / restart — minutes |
| Integration with an external system misbehaves | Kill-switch flag | Config reload / restart — minutes |
| Build is broken (startup crash, package upgrade, bug outside any flag) | Redeploy previous version — safe because of this ADR | Rolling deployment — minutes |
| Migration script blocks or fails | `lock_timeout` aborts, Migrator retries then fails, deployment stops — nothing to revert | Immediate |
| Previous version cannot read new event types | Not revertible past the release that registered the type — prevented by ADR-0010 rule 5 | — |

### Sync mechanism for column transitions (v1: ordered deployments)

In v1, transitions are sequenced by **deployments**. The sync mechanism is **chosen per change**:

- **Database trigger** when the transformation is simple and expressible in SQL, or when data is written by something
  the application does not control. Fewer deployments; temporary logic lives in SQL.
- **Application dual-write** when the transformation logic is non-trivial or needs domain code. All logic in C# and
  unit-testable; more deployments.

The choice and its reasoning are recorded in the expand PR.

#### Worked example: split `address` into `street`, `number`, `post_code`

**Trigger sync — 2 deployments**

| Deployment | Schema | Application |
|---|---|---|
| D1 (expand) | Add nullable columns; two-way sync trigger; batched backfill after rollout | Reads and writes the new columns only |
| D2 (contract) | Drop trigger and `address` | Unchanged |

- D1 → D0 rollback: D0 writes `address`, trigger derives the new columns. ✔
- D2 → D1 rollback: D1 never uses `address`. ✔
- Rule 2's "code removal first" is satisfied by D1, because D1 already stops using `address`.

**Application dual-write — 4 deployments**

| Deployment | Schema | Application |
|---|---|---|
| D1 (expand) | Add nullable columns; batched backfill after D1 is fully rolled out | Writes **both**, reads old |
| D2 | — | Writes both, reads new (falls back to old when new is empty) |
| D3 (code removal) | — | Reads and writes new only |
| D4 (contract) | Drop `address` | — |

- D1 → D0: D0 writes old only; re-run the idempotent backfill before deploying D2 again. ✔
- D2 → D1, D3 → D2, D4 → D3: each previous version only uses columns that still exist and are populated. ✔

**Lossy transformations.** Combining parts → `address` is deterministic; parsing `address` → parts is not. The backfill
is best-effort, marks rows it could not parse (e.g. `address_parse_status`), and the contract step is blocked until
those rows are resolved.

### Target pattern (with OpenFeature, ADR-0019)

Once runtime, multi-state flags are available, the D1–D3 phases of dual-write collapse into **one deployment** whose
behaviour is driven by a migration flag: `Old → DualWrite → ReadNew → NewOnly`. Each step, and each step back, becomes
a flag change measured in seconds. Dual-write then becomes the default sync mechanism, and triggers the exception.
Code removal and contract remain separate deployments. Until then, this pattern is documentation only.

### Enforcement

- `OrderPlatform.Migrations.Tests` scans every DbUp script:
  - scripts under `expand/` fail on `DROP`, `RENAME`, `ALTER ... TYPE`, `SET NOT NULL` without a preceding validated
    `CHECK (... IS NOT NULL)`, `ADD ... NOT NULL` without `DEFAULT`, volatile defaults (`gen_random_uuid()`, `now()`,
    `clock_timestamp()`, `random()`, sequences), non-concurrent `CREATE INDEX` on existing tables, and missing `lock_timeout`;
  - multi-statement scripts without explicit `BEGIN/COMMIT`, and `CREATE INDEX CONCURRENTLY` combined with other statements, fail;
  - scripts under `contract/` must reference the expand script they complete, and that script must exist in an earlier release tag.
- The same rules are applied to the **generated Marten patch** published by CI (ADR-0008).
- Implementation notes (PR 1c):
  - Comments, string literals and dollar-quoted function bodies are ignored.
  - `SET` statements do not count as statements, so `SET lock_timeout` may precede a lone `CREATE INDEX CONCURRENTLY`.
  - Volatile defaults, missing defaults on `NOT NULL`, validated constraints and non-concurrent indexes are violations
    only for tables that already exist, i.e. not created in the same script.
  - A contract script names its expand script in a comment: `-- completes: expand/<file>.sql`. CI passes the latest
    release tag, and the test checks that the expand script is part of that release.
  - The generated patch may drop and recreate Marten's own `mt_*` functions.
  - Every rule has a sample script that must fail (`MigrationRuleTests`).
- Every expand PR creates linked "code removal" and "contract" backlog items so temporary structures are not forgotten.
- Later (backlog): a CI job runs the **previous release's integration tests against the new schema** — direct proof that rollback is safe.

## Alternatives considered

| Option | Why not |
|---|---|
| Down migrations for rollback | Down scripts are rarely tested, lose data written since deploy, and still cause downtime |
| Restore database backup on rollback | Loses all data written since the deployment; long recovery time |
| Blue/green with separate databases | Requires data replication between versions; far more infrastructure |
| Maintenance windows | Contradicts the no-downtime goal |
| Flag-driven migration phases from day one | Requires runtime multi-state flags (OpenFeature); too much machinery before any code exists — planned as target |

## Consequences

- Positive: zero-downtime rolling deployments; rollback by redeploying the previous image; migrations are small and low-risk.
- Negative: structural changes take several deployments; temporary duplication in schema and code; requires discipline and tooling.
- Lock behaviour was validated on PostgreSQL 17.11 (spike S6); re-check when upgrading PostgreSQL major versions.
