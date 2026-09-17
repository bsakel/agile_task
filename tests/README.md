# Tests

Test levels and tooling follow [ADR-0022](../architecture/adr/0022-testing-strategy.md). Tests run on xUnit v3 with
Microsoft.Testing.Platform (`global.json`), assertions use Shouldly.

| Project | Proves | Needs |
|---|---|---|
| `OrderPlatform.Architecture.Tests` | Module boundaries and layering (ArchUnitNET and `ProjectReference` rules), handler coverage and naming, explicit Marten document and event registration, pinned stored message/event names (`contract-names.approved.txt`) — ADR-0015 | Nothing |
| `OrderPlatform.Migrations.Tests` | Expand/contract and online-safety rules for every DbUp script and for the generated Marten patch — ADR-0009 | Nothing |
| `OrderPlatform.Api.IntegrationTests` | Acceptance criteria of PRs 1a and 1b: Migrator, startup guards, authentication, idempotency, problem details, feature flags, OTLP traces | Docker |
| `OrderPlatform.AppHost.Tests` | The Aspire AppHost runs the Migrator to completion before the Api is ready | Docker, free port 8080 |
| `Ordering.Domain.Tests` | Order state machine: every transition and every disallowed command — ADR-0017 | Nothing |
| `OrderPlatform.Testing` | Shared infrastructure (not a test project) | — |

```bash
dotnet test --project tests/OrderPlatform.Architecture.Tests
```

```bash
dotnet test --project tests/OrderPlatform.Api.IntegrationTests
```

## Shared infrastructure (`OrderPlatform.Testing`)

- `PlatformContainers` — one PostgreSQL and one Keycloak container per run, same images as compose, realm imported from
  `deploy/keycloak`. Tests isolate data with unique keys/accounts, not by recreating databases; `CreateDatabaseAsync` exists
  for tests that need an empty database.
- `PlatformProcess` — runs the built `OrderPlatform.Migrator.dll` / `OrderPlatform.Api.dll` as processes with environment
  variables, like a container platform. Used where the exit code or process-wide state matters (startup guards, tracing).
- `KeycloakTokens` — tokens for the realm's clients and users (client credentials; password grant for users, local realm only).
- `OtlpReceiver` — stand-in OTLP/HTTP collector to assert exported spans.
- `FeatureFlagStates` — theory data for flag on/off and the matching configuration (ADR-0019 rule 3).
- `WireMockTestBase` — base class for HTTP adapter tests against WireMock.Net (ADR-0014).
- `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) replaces `TimeProvider` in time-dependent tests.

The in-process Api host (`OrderPlatformApiFactory` in the integration tests) runs the real `Program` with
`AutoCreate.None` against the Migrator-built schema, as Development because the diagnostics endpoint only exists there.

## Conventions

- **Test data builders:** one builder per aggregate or request in the module's test project (`Builders/OrderBuilder.cs`),
  valid defaults, `With<Property>(...)` methods, `Build()`. No shared static fixtures between modules.
- **Names** describe behaviour: `Same_key_with_a_different_body_returns_422`.
- **Flags:** flagged behaviour is tested with the flag on and off.
- **Time:** no wall-clock assertions; use `FakeTimeProvider` or poll with a timeout.
- **Rules must be proven:** a new architecture or migration rule gets a sample that must fail (see `MigrationRuleTests`).
