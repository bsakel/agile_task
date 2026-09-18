# ADR-0004: Wolverine for messaging and mediation

- **Status:** Accepted — see the implementation notes below
- **Date:** 2026-09-17

## Context

We need in-process command dispatch (HTTP → handler), asynchronous integration events between modules, retries,
scheduled messages and a transactional outbox. MediatR, the common choice for in-process mediation, moved to a
commercial licence in 2025 and provides no durability, outbox or transport story.

## Decision

We will use **Wolverine** as the message bus and mediator:
- `IMessageBus.InvokeAsync` for request/response commands from endpoints;
- `PublishAsync` / `SendAsync` for integration events and asynchronous commands;
- Wolverine's Marten integration for the transactional outbox and saga persistence;
- error handling policies (retry, schedule retry, dead letter) configured per message type.

Handlers are plain classes with no framework base types, which keeps the application layer portable.

### Version and operational rules (from Phase 0, see [phase-0-results](../phase-0-results.md#s1--durable-outbox-and-crash-recovery))

- **Versions:** Wolverine 6.x and Marten 9.x (pinned in `Directory.Packages.props`). Online examples written for older
  major versions must be verified against these.
- **Code generation:** Wolverine 6 does not include the runtime compiler.
  - Development and tests reference `WolverineFx.RuntimeCompilation` (`TypeLoadMode.Dynamic`).
  - Container images use **pre-generated handler code** (`codegen write` during the image build) with
    `TypeLoadMode.Static`, so production needs no Roslyn at runtime (ADR-0021).
  - Because Wolverine is configured in the shared composition project, `ApplicationAssembly` is set explicitly to the
    host's entry assembly; otherwise Wolverine looks for pre-generated code in the composition assembly and silently
    falls back to a runtime scan (found while implementing PR 1a).
- **Explicit handler discovery:** each module registers its own assembly for handler discovery in its `IModule`, and
  handler classes follow one naming rule: `<Message>Handler` (singular). Discovery must never depend on accidental naming.
- **No silent drops:** publishing a message without a handler does nothing in Wolverine. A test asserts that **every
  command and integration event type** declared in `*.Contracts` and `*.Application` has a handler (ADR-0015).
- **Optional commercial tooling:** Wolverine and Marten contain hooks for CritterWatch (a commercial monitoring
  product). It is not used; nothing commercial is required.

## Alternatives considered

| Option | Why not |
|---|---|
| MediatR | Commercial licence; mediation only — outbox, retries and transports must be built or added separately |
| MassTransit | Strong, but its latest major versions also moved to a commercial licence; heavier broker-first model |
| Hand-rolled dispatcher | Cheap for mediation, but durability, retries and outbox are exactly the hard parts |

## Implementation (PR 2f)

Wolverine generates handler code **into the host assembly** and, since version 6, refuses service location in it by
default. Generated code therefore has to be able to construct a handler's dependencies itself, which constrains how
modules register their services:

- **A service a handler depends on must be `public`.** The Dapper adapters were `internal`; generated code in the host
  assembly cannot see them. They are public now — the module boundary is enforced by the architecture tests, not by CLR
  visibility.
- **Register by type, not as a pre-built instance.** The pricing rules were registered as instances, which generated
  code cannot construct; they are registered by type, keeping the order ADR-0018 pins.
- **A genuinely opaque registration is opted in explicitly.** Aspire registers `NpgsqlDataSource` through a factory, so
  the composition root calls `CodeGeneration.AlwaysUseServiceLocationFor<NpgsqlDataSource>()` — the escape hatch
  Wolverine documents for exactly this case, and the only one in the solution.

The failure mode is a startup exception (`InvalidServiceLocationException`) naming the handler, not a compile error, so
it appears the first time the handler graph is built — in the architecture tests or at startup. ADR-0006 records the
matching constraint for Marten's generated code.

## Consequences

- Positive: one library covers mediation, outbox, durable local queues, sagas and future broker transports; tight Marten integration.
- Negative: smaller community than MediatR/MassTransit; convention-based handler discovery can surprise newcomers
  (mitigated by explicit discovery, the naming rule and the handler coverage test); a code generation step in the image build.
- Spike S5 confirmed MIT licences for Wolverine, Marten and their transitive JasperFx/Weasel packages.
