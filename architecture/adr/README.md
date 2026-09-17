# Architecture Decision Records

Format: lightweight [MADR](https://adr.github.io/madr/). One decision per file. Accepted ADRs are not edited to change
the decision — they are **superseded** by a new ADR.

Statuses: `Proposed` (open for discussion) · `Accepted` · `Superseded by ADR-xxxx` · `Deprecated`.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-modular-monolith.md) | Modular monolith | Accepted |
| [0003](0003-module-structure-and-communication.md) | Module structure and communication | Accepted |
| [0004](0004-wolverine-for-messaging.md) | Wolverine for messaging and mediation | Accepted |
| [0005](0005-durable-in-process-messaging.md) | Durable in-process messaging, broker deferred | Accepted (pending spike S1/S2) |
| [0006](0006-marten-event-sourcing-for-ordering.md) | Marten event sourcing for Ordering | Accepted |
| [0007](0007-dapper-and-dbup-for-relational-data.md) | Dapper and DbUp for relational data | Accepted |
| [0008](0008-dedicated-migrator-forward-only.md) | Dedicated migrator, forward-only migrations | Accepted |
| [0009](0009-expand-contract-database-changes.md) | Expand/contract database changes | Accepted |
| [0010](0010-event-and-message-versioning.md) | Event and message contract versioning | Accepted |
| [0011](0011-minimal-apis-dispatch-messages.md) | Minimal APIs dispatch messages | Accepted |
| [0012](0012-observability-opentelemetry-day-one.md) | Observability with OpenTelemetry from day one | Accepted |
| [0013](0013-local-development-aspire-and-compose.md) | Local development with Aspire and docker-compose | Accepted |
| [0014](0014-external-integrations-ports-and-adapters.md) | External integrations via ports, adapters and resilience | Accepted |
| [0015](0015-architecture-tests-enforce-boundaries.md) | Architecture tests enforce boundaries | Accepted |
| [0016](0016-security-baseline.md) | Security baseline | Accepted |
| [0017](0017-order-lifecycle-process.md) | Order lifecycle as an event-sourced process | Accepted |
| [0018](0018-pricing-rule-pipeline.md) | Pricing as a rule pipeline | Accepted |
| [0019](0019-feature-flags.md) | Feature flags (v1 Microsoft.FeatureManagement, target OpenFeature) | Accepted |
| [0020](0020-api-conventions-and-versioning.md) | HTTP API conventions, versioning and idempotency | Accepted |
| [0021](0021-deployment-and-ci-cd.md) | Deployment and CI/CD | Accepted |
| [0022](0022-testing-strategy.md) | Testing strategy | Accepted |

Template for new records: [template.md](template.md).
