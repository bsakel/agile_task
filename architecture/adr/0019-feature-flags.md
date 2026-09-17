# ADR-0019: Feature flags

- **Status:** Accepted
- **Date:** 2026-09-17
- **Related:** ADR-0009 (expand/contract), ADR-0010 (event and message versioning), ADR-0012 (observability)

## Context

Expand/contract (ADR-0009) makes redeploying the previous version safe, but a redeploy is still the only way to revert
a behaviour change. Feature flags separate **deployment** (code is on the servers) from **release** (users get the
behaviour), so most problems can be reverted by switching a flag off, and new behaviour can be rolled out gradually.

A complete flag platform (runtime changes in seconds, multi-state migration flags, audit, central targeting) is the
long-term target, but it is too much machinery for an application with no code yet.

## Decision

### v1 — Microsoft.FeatureManagement behind our own interface

- Flags are evaluated with **Microsoft.FeatureManagement** (`Microsoft.FeatureManagement.AspNetCore`), read from
  `IConfiguration` (appsettings per environment, environment variables, mounted config file with reload on change).
- Application code **never** depends on `IFeatureManager` directly. It uses a thin interface in `BuildingBlocks`:

  ```csharp
  public interface IFeatureFlags
  {
      Task<bool> IsEnabledAsync(string flag, CancellationToken ct = default);
  }
  ```

  v1 implements it with Microsoft.FeatureManagement. Moving to OpenFeature replaces this one adapter.
- **Boolean flags only** in v1. Gradual rollout uses the built-in targeting filter (sticky per user) rather than the
  percentage filter (random per evaluation).
- Endpoints can be gated with the ASP.NET Core integration; handlers use `IFeatureFlags`.

### Rules that apply from day one

1. **Registry.** Every flag is a constant in a single `FeatureFlags` class per module, documented with **type**
   (release, kill switch), **owner** and **expiry date**. Unregistered flag names are not allowed.
2. **Short-lived release flags.** A release flag is removed (code and configuration) once it has been fully enabled for
   a stabilisation period. Kill switches may stay.
3. **Both states tested.** Tests covering flagged behaviour run with the flag on and off.
4. **Consistent decisions for long-running flows.** When a flag changes the outcome of a multi-step process (e.g. an
   order's pricing or fulfilment), the decision is taken once and recorded in the event (e.g. on `OrderSubmitted`); later
   steps use the recorded decision, not a fresh flag evaluation.
5. **Safe defaults.** A missing flag configuration evaluates to **off**, and "off" is always the existing behaviour.
6. **Observable.** The adapter records flag evaluations on the current span, following the OpenTelemetry feature-flag
   semantic conventions (ADR-0012).

### Implementation (PR 1b)

- Registry: flag names are `const string` fields annotated with `[FeatureFlag(type) { Owner, Expires }]` in a module's
  `FeatureFlags` class, exposed through `IModule.FeatureFlags`. The host builds a `FeatureFlagRegistry` at startup and
  fails if a field has no attribute, a release flag has no expiry date, or a name is registered twice. Evaluating an
  unregistered name throws. Expired flags are logged as warnings at startup.
- Names are `<Module>.<Flag>` (e.g. `Pricing.ShippingCharge`); configuration lives in the `FeatureManagement` section
  (`"FeatureManagement": { "Pricing.ShippingCharge": true }`). JSON configuration files reload on change, so editing the
  mounted/deployed file switches a flag without a restart; environment variables require a restart.
- The adapter adds a `feature_flag.evaluation` event with `feature_flag.key`, `feature_flag.provider.name` and
  `feature_flag.result.value` to the current span (verified on the Wolverine handler span in the dashboard).
- `Microsoft.FeatureManagement.AspNetCore` is referenced by the Api host only; PR 1c adds the architecture test.

### Known limitations of v1 (accepted)

| Capability | v1 |
|---|---|
| Change a flag without deployment | Only via configuration reload (mounted file) — otherwise restart; **minutes, not seconds** |
| Consistent gradual rollout across instances | Targeting filter per instance, same configuration everywhere |
| Multi-state migration flags | Not used; expand/contract transitions are sequenced by deployments (ADR-0009) |
| Audit trail and permissions for flag changes | Through git history and the deployment pipeline only |

### Target — OpenFeature

- **OpenFeature** .NET SDK as the evaluation API, behind the same `IFeatureFlags` interface (extended with typed / variant evaluation).
- Provider chosen per environment: `flagd` for local development (Aspire and compose), a managed flag service in production.
- Enables: runtime changes in seconds, multi-state migration flags driving ADR-0009's target pattern, one-way locking of
  migration flags, central targeting, audit and permissions, OpenTelemetry hooks.

### Triggers to move to the target

Any one of these starts the migration:
- A flag must be changed in production without a deployment or restart (e.g. an incident kill switch).
- Gradual rollouts need consistent behaviour across many instances.
- Flag changes require audit or permission control, or are made by operations rather than developers.
- A second deployable needs the same flags.
- Active flag count makes configuration files impractical to manage.
- The first schema transition where deployment-sequenced dual-write (ADR-0009) is judged too slow or risky.

## Alternatives considered

| Option | Why not (now) |
|---|---|
| OpenFeature + flagd from day one | Extra runtime component and concepts before any feature exists; kept as target |
| Hosted flag service (LaunchDarkly, Unleash, Azure App Configuration) now | Cost and vendor decision premature; reachable later via an OpenFeature provider |
| No flags, rely on redeploy | Every behaviour revert becomes a deployment; no gradual rollout |
| Call `IFeatureManager` directly | Couples every call site to the v1 library; makes the planned move expensive |

## Consequences

- Positive: deploy and release separated from the first feature; cheap to adopt; the migration path is one adapter plus provider setup.
- Negative: flag reverts in v1 take minutes; flag debt must be actively managed; flagged code doubles test cases for that area.
