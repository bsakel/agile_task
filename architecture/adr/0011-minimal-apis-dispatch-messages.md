# ADR-0011: Minimal APIs dispatch messages

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

Wolverine can expose handlers directly as HTTP endpoints (Wolverine.Http). This removes a layer but mixes HTTP concerns
(routing, status codes, auth, headers) with application logic.

## Decision

We will use **ASP.NET Core minimal APIs** as a thin HTTP adapter:
- Endpoints are grouped per module (`MapOrderingEndpoints`) and registered through the module's `IModule`.
- An endpoint binds and validates the request shape, applies authorization, maps to a command/query, calls
  `IMessageBus.InvokeAsync`, and maps the `Result` to an HTTP response (`201`, `404`, `409`, problem details).
- Handlers know nothing about HTTP and can be invoked equally from messages, tests or future transports.
- OpenAPI documents are generated from the endpoints.
- Versioning, error codes, idempotency and payload conventions are defined in ADR-0020.

## Alternatives considered

| Option | Why not |
|---|---|
| Wolverine.Http endpoints | Fewer lines, but handlers become HTTP-aware; harder to explain the seam |
| MVC controllers | More ceremony, no benefit for this API |

## Consequences

- Positive: clear separation of transport and application logic; standard ASP.NET Core knowledge applies.
- Negative: small amount of mapping code per endpoint.
