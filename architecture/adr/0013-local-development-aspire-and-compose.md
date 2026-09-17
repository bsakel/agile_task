# ADR-0013: Local development with Aspire and docker-compose

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

Developers need a one-command local environment from day one. .NET Aspire gives the best inner loop for .NET
developers. docker-compose runs without the .NET toolchain, is used in CI, and is closer to container-based deployment.
Both can start PostgreSQL, so they can drift.

## Decision

Both are provided, with distinct roles:

| | Aspire AppHost | docker-compose |
|---|---|---|
| Audience | .NET developers, debugging | CI, non-.NET contributors, deployment-like smoke tests |
| Runs | Postgres and Keycloak containers, Migrator (wait for completion), Api (wait for Migrator and Keycloak) as projects | Postgres, Keycloak, Migrator image, Api image, standalone Aspire Dashboard image as OTLP receiver |
| Identity | Keycloak with the shared realm import | Keycloak with the same realm import |
| Telemetry | Aspire Dashboard built in | Standalone Aspire Dashboard container |

- **Keycloak realm** (`deploy/keycloak/orderplatform-realm.json`) is shared by both: two customer accounts each with a
  client-credentials client, one portal user, one support agent (ADR-0016). Credentials are development-only values.
- **Integrations** run in `Fake` mode locally (ADR-0014).
- **Keycloak in the AppHost is a plain container resource** (`AddContainer` with a pinned `quay.io/keycloak/keycloak`
  image tag and the realm import mounted), not `Aspire.Hosting.Keycloak`, which is only available as a preview package
  (spike S5). Revisit when a stable version ships.
- The AppHost runs the Migrator project before the API, as in deployment (ADR-0008); the API never auto-creates schema,
  even locally, so local runs exercise the same path as production.

Drift controls:
- Configuration uses the same environment variable names in both (connection strings, OTLP endpoint, identity authority).
- CI runs `docker compose up` and a smoke test (`/health/ready`, obtain a token, submit and read an order) (ADR-0021).
- Adding infrastructure requires updating both — checked in PR review.

## Alternatives considered

| Option | Why not |
|---|---|
| Aspire only | Requires .NET toolchain; less representative of deployment |
| Compose only | Loses the debugging and dashboard experience |
| Compose generated from AppHost | Generated file is harder to read and hand-tune; reconsider when mature |

## Consequences

- Positive: fast inner loop and a deployment-like environment, same observability in both.
- Negative: two definitions to maintain; Keycloak adds start-up time locally.
