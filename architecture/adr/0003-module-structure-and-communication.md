# ADR-0003: Module structure and communication

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

Module isolation only holds if the physical project structure makes the wrong dependency hard and visible.

## Decision

Each module consists of four projects:

| Project | Contains | May reference |
|---|---|---|
| `X.Domain` | Aggregates, value objects, domain events, invariants. No I/O. | `BuildingBlocks` |
| `X.Application` | Command/query handlers, ports (interfaces) for persistence and external systems | `X.Domain`, `X.Contracts`, other modules' `*.Contracts`, `BuildingBlocks` |
| `X.Infrastructure` | Marten/Dapper persistence, adapters, module registration (`IModule`) | `X.Application`, `X.Domain` |
| `X.Contracts` | Public API of the module: query interfaces, DTOs, integration events | nothing (except `BuildingBlocks` primitives) |

Communication rules:
- **Synchronous query** needing an immediate answer (e.g. price a basket, check availability): call an interface from
  the other module's `Contracts`, implemented inside that module. Treat it as if it were a remote call — no leaking
  domain types, no transactions spanning modules.
- **State change in another module** (e.g. reserve stock after an order is submitted): publish an **integration event** or send
  a command message via Wolverine. The receiving module handles it in its own transaction.
- Domain events stay inside the module; integration events in `Contracts` are the published language.

## Alternatives considered

| Option | Why not |
|---|---|
| One project per module with folders | Cheaper, but references cannot be enforced by the compiler |
| Shared domain model | Couples modules; defeats extraction |

## Consequences

- Positive: the compiler and architecture tests catch boundary violations; a module's public surface is explicit.
- Negative: more projects (4 × 6 modules); mapping between domain types and contract DTOs.
