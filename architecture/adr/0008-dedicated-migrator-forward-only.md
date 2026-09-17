# ADR-0008: Dedicated migrator, forward-only migrations

- **Status:** Accepted — revised after spike S4 ([phase-0-results](../phase-0-results.md#s4--marten-schema-export-and-production-mode))
- **Date:** 2026-09-17

## Context

Running migrations on application startup causes races between replicas, couples app start-up time to DDL, and hides
failures. Three tools manage schemas (DbUp, Marten, Wolverine).

Spike S4 showed that the three behave differently:
- DbUp applies hand-written SQL scripts.
- Marten can generate a reviewable SQL patch per database (`db-patch -d <database>`) and apply its schema itself, but
  **only for document types and projections registered explicitly**; the generated patch also comes with a
  destructive "drop" file.
- Wolverine's message storage **cannot be exported as SQL**; it is provisioned through its own resource setup.

## Decision

### One component changes the schema

- A separate console application, **`OrderPlatform.Migrator`**, is the only component that changes the database schema.
  It runs to completion **before** the new API version is deployed (Aspire: `WaitForCompletion`; compose:
  `depends_on: condition: service_completed_successfully`; production: a pre-deployment job).
- The Migrator composes the **same module registrations** as the API (Marten document types, projections, Wolverine
  configuration), so it knows the complete schema, but it starts no HTTP endpoints and processes no messages.

### Order of application

1. **DbUp scripts** per module (relational schemas). A script failing on `lock_timeout` is retried with backoff
   (default 5 attempts), then the Migrator fails (ADR-0009).
2. **Marten schema**: applied programmatically from the registered configuration (equivalent of `db-apply`).
3. **Wolverine message storage**: applied programmatically through Wolverine's resource setup (equivalent of `resources setup`).
4. **Verification**: an assert step (equivalent of `db-assert`) confirms the database matches the configuration; any
   difference fails the Migrator.
5. **Run record**: the Migrator writes a row to `platform.migrator_runs` (its own DbUp-managed schema). The Api checks for
   it at startup and fails with an actionable message if it is missing; the `/health/ready` check uses the same record.
   Without it, a missing schema only surfaced as a missing-table error inside Wolverine's startup.

### Review of generated schema changes

- CI generates the Marten patch (`db-patch`) against a database built from the **previous release** and publishes it as
  a build artifact. The patch is reviewed in the pull request and checked by the migration safety tests (ADR-0009).
- Generated **drop files are never applied**. Removing Marten objects is a hand-written contract script (ADR-0009).
- Wolverine's storage schema is owned by the library; changes arrive only with a Wolverine version upgrade, which is
  reviewed through its release notes and tested in CI before merging.

### Runtime configuration

- **Migrations are forward-only.** There are no down scripts. Rollback of a release means redeploying the previous
  application version against the newer schema — which is only safe because of ADR-0009.
- The API runs with `AutoCreate.None` for Marten **and** Wolverine in **every** environment, including local development
  (the AppHost and compose run the Migrator first), and with a database role that has no DDL rights. Local runs thereby
  exercise the production schema path; the only exception is unit-level Marten tests that create throwaway schemas. With a missing or outdated schema the API **fails at startup** (verified in S4).
- **Every Marten document type and projection is registered explicitly** in its module's registration. Unregistered
  types are not created by the Migrator and fail at runtime (S4). Integration tests catch omissions by running the API
  with `AutoCreate.None` against a Migrator-built schema (ADR-0022).
- The Migrator uses a privileged role; the API does not.

## Consequences

- Positive: one visible, auditable migration step per deployment; no replica races; the API cannot accidentally change
  schema; generated Marten DDL is reviewed before it reaches production.
- Negative: one more deployable; Wolverine storage DDL is not reviewable as SQL; explicit registration of every document
  type is a discipline enforced by tests.
