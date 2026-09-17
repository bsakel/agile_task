# ADR-0012: Observability with OpenTelemetry from day one

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

An order crosses HTTP, handlers, the database, durable queues and external providers, often asynchronously.
Diagnosing that without correlated telemetry is guesswork, and retrofitting telemetry is expensive.

## Decision

- **OpenTelemetry** for traces, metrics and logs, configured once in `OrderPlatform.ServiceDefaults` and exported via **OTLP**.
- Instrumentation: ASP.NET Core, `HttpClient`, Npgsql, Wolverine (message send/receive/handle spans with propagated
  trace context), Marten, runtime metrics.
- Structured logging through `ILogger` with OpenTelemetry log export; no string-concatenated log messages.
- Business metrics via `System.Diagnostics.Metrics` (orders submitted, orders cancelled, orders per lifecycle state and time spent in it, orders in `RequiresAttention`, dead letters).
- Health checks: `/health/live` (process) and `/health/ready` (database reachable, migrations applied).
- Correlation: `OrderId` added as a span attribute and log scope on every order-related operation.
- Local: Aspire Dashboard (from AppHost or as a standalone container in compose). Production: any OTLP backend —
  the choice is deployment configuration, not code.

## Consequences

- Positive: vendor-neutral; end-to-end traces from the first slice; same experience locally and in production.
- Negative: telemetry volume and cost need sampling decisions in production.
