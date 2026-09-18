# ADR-0021: Deployment and CI/CD

- **Status:** Accepted
- **Date:** 2026-09-17
- **Related:** ADR-0008 (migrator), ADR-0009 (expand/contract), ADR-0012 (observability), ADR-0013 (local development), ADR-0016 (security), ADR-0019 (feature flags), ADR-0022 (testing)

## Context

Deployment is an explicit non-functional requirement. The architecture already assumes a dedicated migrator that runs
before the API, rolling deployments, and rollback by redeploying the previous version. This ADR defines how builds
become running software, without committing to a specific hosting platform before one is chosen.

## Decision

### Artifacts

- Two container images: **`orderplatform-api`** and **`orderplatform-migrator`**, built from the same commit.
- The API image contains **pre-generated Wolverine handler code**: the image build runs `codegen write` and the API
  starts with `TypeLoadMode.Static`, so no runtime compilation happens in production (ADR-0004). Generated code is never
  committed, so it cannot go out of date; CI proves that it builds and loads by building the image and starting it in the
  compose smoke test. **Revised in PR 1c:** `codegen test` is not used: in Wolverine 6.38 it compiles the static handler
  registry apart from the handler files it references and always fails (CS0234), while `codegen write` plus a build works.
- Images are **built once and promoted** across environments by digest; tagged with the git commit SHA. No
  environment-specific builds, no configuration baked into images.
- The OpenAPI document and an SBOM are published alongside the images.

### Pipelines

**Pull request pipeline** (must pass before merge):

1. Restore and build (warnings as errors)
2. Unit tests (domain, application, pricing)
3. Architecture tests and migration safety tests (ADR-0015, ADR-0009)
4. Adapter tests (WireMock.Net) and integration tests (Testcontainers PostgreSQL) (ADR-0022)
5. OpenAPI breaking change check against the last release (ADR-0020); Marten schema patch generated against the last
   release's schema, published as an artifact and checked by the migration safety rules (ADR-0008, ADR-0009)
6. Dependency vulnerability scan
7. `docker compose` smoke test: start the stack, wait for `/health/ready`, submit and read an order

**Main branch pipeline** (after merge):

1. Steps of the pull request pipeline
2. Build and scan container images; publish SBOM
3. Deploy to **Test/Staging** (sequence below), run smoke tests
4. **Manual approval** gate
5. Deploy the **same image digests** to **Production** (sequence below), run smoke tests

The reference implementation is a GitHub Actions workflow; the stages are tool-agnostic.

#### Implementation (PR 1c)

- `.github/workflows/pull-request.yml` runs three parallel jobs:
  - **build and test**: build, vulnerable packages, module unit tests, architecture, migration, integration and AppHost tests;
  - **release compatibility** (`.github/scripts/release-compatibility.sh`);
  - **compose smoke test** (`.github/scripts/compose-smoke.sh`).
- **Previous release** = the latest `v*` tag. Until the first release, the merge base with `main` (on `main` itself, the
  previous commit). Against it, the release compatibility job:
  1. migrates an empty database with the previous Migrator;
  2. publishes the Marten patch of the new commit as an artifact and checks it with the migration rules;
  3. migrates forward with the new Migrator;
  4. **starts the previous Api on the new schema** (a first automated rollback check);
  5. compares both OpenAPI documents with `oasdiff` (fails on breaking changes; skipped while the previous release has
     no document).
- `.github/workflows/main.yml` reuses the pull request pipeline, then builds both images tagged with the commit SHA,
  produces SBOMs (Syft) and scans the images (Trivy, fails on fixable HIGH/CRITICAL).
- Stubbed until a hosting platform is chosen: registry push, the rollback floor check and the deployment sequence
  (placeholders in the `staging` and `production` jobs). Manual approval is the `production` environment's required
  reviewers.
- Tool images are pinned by digest (oasdiff, Trivy, Syft).

### Deployment sequence (every environment)

0. **Check the rollback floor**: the pipeline refuses to deploy a version older than the environment's recorded rollback
   floor (ADR-0010 rule 6). Enabling a release flag for a new event type records the floor.
1. **Run the Migrator as a one-off job** with the Migrator database role. Failure stops the deployment; nothing else
   changes (ADR-0009 rule 3).
2. **Rolling update of the API** with no reduction in capacity (new instances start before old ones stop).
   Instances receive traffic only when `/health/ready` succeeds.
3. **Smoke tests** against the environment.
4. **On failure after step 2: automatic rollback** by redeploying the previous API image. The schema stays as it is —
   this is safe because of expand/contract (ADR-0009) and register-before-emit (ADR-0010).

### Runtime requirements

The hosting platform is not chosen yet. It must support: one-off jobs, rolling updates with readiness and liveness
probes, secrets injection, horizontal scaling and OTLP export. Kubernetes, Azure Container Apps and Amazon ECS all
qualify. Infrastructure as code for the chosen platform is backlog.

- **API instances are stateless** and scale horizontally. Wolverine coordinates durable message recovery and scheduled
  messages across instances.
- **Graceful shutdown:** on `SIGTERM`, stop accepting requests and new messages, finish in-flight work within a
  configured drain timeout; unfinished durable messages are recovered by another instance.
- **Configuration:** environment variables per environment; secrets from the platform's secret store (ADR-0016).
  Feature flag configuration is mounted as a file with reload on change (ADR-0019).
- **PostgreSQL:** managed service with automated backups and point-in-time recovery. Restoring a backup is **disaster
  recovery, not a rollback mechanism**.
- **Telemetry:** OTLP endpoint configured per environment (ADR-0012).

### Environments

| Environment | Purpose | How it runs |
|---|---|---|
| Local | Development | Aspire AppHost or docker-compose (ADR-0013) |
| CI | Automated verification | Testcontainers and docker-compose |
| Test/Staging | Pre-production verification, same deployment sequence | Chosen hosting platform |
| Production | Customers | Chosen hosting platform |

Aspire is used for local orchestration only; production deployment does not depend on the AppHost.

## Alternatives considered

| Option | Why not |
|---|---|
| Migrations on API startup | Races between replicas; hides failures (ADR-0008) |
| Rebuild images per environment | What is tested is not what is deployed |
| Deploy with the Aspire publisher | Ties production to the local development tool; the platform is not chosen yet |
| Blue/green switch | More infrastructure; rolling updates plus expand/contract already give safe rollback |

## Consequences

- Positive: every environment deploys the same way; rollback is automatic and cheap; the platform choice remains open.
- Negative: the pipeline runs container-based tests and is slower than unit tests alone; a platform decision is still needed before production.
