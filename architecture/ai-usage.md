# AI Usage Log

The assessment expects AI assistance. This log records where AI was used and where human judgement shaped or
overrode the output, so every artifact can be explained and defended.

Tool: Claude (Claude Code).

| Step | AI contribution | Human judgement / decision |
|---|---|---|
| Brief analysis | Extracted and summarised the assessment brief; proposed a modular monolith with five modules and a single reference slice | Confirmed approach |
| Mediator choice | Proposed avoiding MediatR (commercial licence) | Proposed Marten; AI clarified Marten is a store, not a mediator, and suggested Wolverine alongside it |
| Persistence | Suggested EF Core initially | Rejected EF Core; chose Dapper + DbUp. AI flagged the overlap with Marten schema management → single Migrator, schema ownership rule |
| Local environment | Proposed Aspire | Required docker-compose as well from day one. AI flagged drift risk and proposed distinct roles |
| Database change policy | — | Introduced the hard rule: additive-only changes, removals in a later release (expand/contract). AI analysed rollback edge cases (dual-write needs 3 releases, triggers 2; events and messages also covered) |
| Rollback strategy | Pointed out that expand/contract alone makes rollback possible but not cheap; proposed feature flags with OpenFeature, multi-state migration flags and flag-driven migration phases | Agreed flags were missing, but judged the full design too complex for an application with no code. Chose simple flags on Microsoft.FeatureManagement for v1, with OpenFeature documented as the target and explicit triggers to move (ADR-0019) |
| Contract timing | Proposed separating code removal from schema drop, and a stabilisation period | Accepted both; default stabilisation of one production release cycle, adjustable per change |
| Order lifecycle | Drafted a retail-style saga (sync availability check, card authorize/capture); then critiqued it (race on "accepted", duplicated saga state, cancellation races, unknown outcomes) | Identified the root cause: no defined domain. Fixed the domain as **B2B** and specified the lifecycle (reserve → invoice due in 3 days → processing → shipping, customer contact states, API vs support cancellation). AI mapped it to states, proposed aggregate-as-process-manager and durable timers, and listed gaps; user accepted the defaults. Assumptions placed centrally in the README at the user's request |
| Closing ADR gaps | Reviewed the Proposed ADRs and found 22 gaps with default recommendations, including: retries at two layers multiplying, fake adapters reaching production, personal data in permanent events, no owner for prices or customer data, unspecified rounding, and no ADRs for API versioning, deployment or testing | Accepted the defaults; chose to add a Customers module and all three new ADRs (0020–0022). One adjustment made while writing: validating unknown SKUs against the price list instead of the external inventory system, to keep external calls out of the request path |
| Phase 0 spikes | Built and ran the seven experiments against real packages and PostgreSQL. Discovered that the current packages (Wolverine 6, Marten 9) are newer than the AI's training knowledge, so APIs were verified via package XML docs, reflection and compiler feedback instead of memory. Debugged its own spike mistakes (handler class naming, status query) before drawing conclusions, and isolated the circuit-breaker stall with a controlled comparison | Required the experiments to be run for real and the plan fixed from their results before any later phase. Design changes from the evidence: register-before-emit and rollback floor (S3), Migrator applying three schema owners with explicit registration (S4), no listener circuit breaker (S7) |
| HTTP layer | Offered Wolverine.Http vs minimal APIs | Chose minimal APIs dispatching messages for separation of concerns |
| Messaging transport | Proposed in-process first | Made in-process conditional on crash durability; otherwise RabbitMQ |
| Documentation | Drafted README, implementation plan and ADRs | Review pending |
