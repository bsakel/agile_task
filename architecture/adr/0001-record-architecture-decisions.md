# ADR-0001: Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-09-17

## Context

The skeleton is handed to a delivery team that did not take part in the design discussions. Decisions made without a
record get re-litigated, or silently reversed, once the original authors are gone.

## Decision

We will record every architecturally significant decision as an ADR in `architecture/adr`, using the template in
`template.md`. ADRs are reviewed in pull requests like code. Accepted ADRs are immutable; a changed decision is a new ADR
that supersedes the old one.

A decision is "significant" if it affects module boundaries, data ownership, deployment, a cross-cutting concern, or
introduces a new dependency with lock-in.

## Consequences

- Positive: rationale survives team changes; onboarding reads the ADR index.
- Negative: small overhead per decision.
