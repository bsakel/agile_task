# Tests

Test levels and tooling follow [ADR-0022](../architecture/adr/0022-testing-strategy.md). Tests run on xUnit v3 with
Microsoft.Testing.Platform (`global.json`), assertions use Shouldly.

| Project | Proves | Needs |
|---|---|---|
| `OrderPlatform.Architecture.Tests` | Module boundaries and layering (ArchUnitNET and `ProjectReference` rules), handler coverage and naming, explicit Marten document and event registration, pinned stored message/event names (`contract-names.approved.txt`) — ADR-0015 | Nothing |
| `OrderPlatform.Migrations.Tests` | Expand/contract and online-safety rules for every DbUp script and for the generated Marten patch — ADR-0009 | Nothing |
| `OrderPlatform.Api.IntegrationTests` | Acceptance criteria of PRs 1a and 1b: Migrator, startup guards, authentication, idempotency, problem details, feature flags, OTLP traces; a module's seeded data read through its contracts against the Migrator-built schema (PRs 2b, 2c); submitting an order end to end and reserving it through the outbox, reading it back, its history and cancelling it (PRs 2f, 2g, 2i, 2j) | Docker |
| `OrderPlatform.AppHost.Tests` | The Aspire AppHost runs the Migrator to completion before the Api is ready | Docker, free port 8080 |
| `Billing.Infrastructure.Tests` | Billing adapters: the fake provider's payment states, idempotency keys, outcome query and outage; the HTTP adapter against WireMock.Net — success, every error mapping, unknown status, timeout followed by the outcome query, open circuit — ADR-0014 | Nothing |
| `Inventory.Infrastructure.Tests` | The fake inventory adapter behind `IInventoryGateway`: all-or-nothing reservation, idempotency keys, release, configured failures — ADR-0014 | Nothing |
| `Ordering.Domain.Tests` | Order state machine: every transition and every disallowed command — ADR-0017 | Nothing |
| `Pricing.Domain.Tests` | Pricing rule pipeline: stage order, rounding, tax per rate, the shipping charge flag on and off — ADR-0018 | Nothing |
| `Shipping.Infrastructure.Tests` | The fake shipping adapter behind `IShippingGateway`: controllable outcomes, idempotency keys, cancellation and outcome-query failures — ADR-0014 | Nothing |
| `OrderPlatform.Testing` | Shared infrastructure (not a test project) | — |

```bash
dotnet test --project tests/OrderPlatform.Architecture.Tests
```

```bash
dotnet test --project tests/OrderPlatform.Api.IntegrationTests
```

Running everything, filtering to one class or test, and the local caveats (a solution-wide run starts all nine projects
at once) are in the [repository README](../README.md#building-and-testing).

## What each suite contains

One class per concern, and test names written as behaviour sentences — **the list of names is that level's
specification**. To read it for a project, run its built test executable with xUnit's own switch (`-list classes` and
`-list full` also work):

```bash
./tests/Ordering.Domain.Tests/bin/Debug/net10.0/Ordering.Domain.Tests.exe -list tests
```

Listing starts nothing, not even for the Docker-backed projects — but a test project's output folder also holds the
executables it references (`OrderPlatform.Api.exe`, `OrderPlatform.AppHost.exe`), and running one of those by mistake
starts the real application. Name the test executable itself.

### Ordering.Domain.Tests — the order lifecycle

| Class | What it covers |
|---|---|
| `OrderTransitionTests` | The whole of ADR-0017 in one table-driven class. Two tables drive **every state against every trigger** and **every command in every state**, so a trigger the table does not list must be ignored and logged, and a command it does not list must be refused with its stable error code. The remaining tests pin what each transition *records* — the reservation kept when invoicing starts, the invoice id and due date, the mandatory support reason, the tracking reference — and the races of §6: a reservation arriving after cancellation, an overdue timer firing after payment, a fulfilment failure reported after dispatch, a payment arriving after cancellation, and a compensation step that gave up |

### Pricing.Domain.Tests — the pricing pipeline

| Class | What it covers |
|---|---|
| `PricingPipelineTests` | Rules run stage by stage whatever order they were registered in, same-stage rules keep their registration order, the v1 rule list is pinned, totals are the sum of the lines and the tax, and the breakdown carries the price list version and the flag decisions (a flag with no decision is off) |
| `PricingRuleTests` | The v1 rules: a line is unit price × quantity rounded per line, tax per rate on the sum of the rounded lines, a line per rate, no tax line where there is no tax, the reverse-charge customer invoiced without tax, and the shipping charge added and taxed only while its release flag is on |

### Adapter tests — `<Module>.Infrastructure.Tests`

| Class | What it covers |
|---|---|
| `BillingHttpGatewayTests` | The HTTP adapter against WireMock.Net, one stub per row of the provider contract in `Billing.Infrastructure/Http/README.md`: our idempotency key sent on issue, the provider's status vocabulary mapped and never guessed, every provider failure returned as a `Result` failure, a timed-out issue asking what the key produced rather than issuing twice, a key the provider has no record of reported as unavailable, an open circuit failing without reaching the provider, and void and refund each sending the key of their own step |
| `FakeBillingGatewayTests` | The fake selected by `Integrations:Billing:Mode=Fake`: an invoice due three calendar days later, the configured payment state, void and refund taking precedence over it, the idempotency key, the outcome query (including a key the provider never saw) and a provider outage failing every call until it is over |
| `FakeInventoryGatewayTests` | All-or-nothing reservation — nothing is held when a single line is short — plus configured unavailable SKUs and failures, the idempotency key, release restoring stock, the outcome query, and configuration read from the `Integrations` section |
| `FakeShippingGatewayTests` | The controllable outcome of a requested shipment, the idempotency key preventing a second shipment (and a different key being a second one), cancellation while undispatched, refusal once dispatched, and the outcome query |

### OrderPlatform.Architecture.Tests — the rules that fail the build

| Class | What it covers |
|---|---|
| `ModuleBoundaryTests` | Type-level rules: a module reaches another only through its `Contracts`; `Contracts` depend only on technology-free building blocks; `Domain` has no application, infrastructure or framework dependency; `Application` does not depend on infrastructure or HTTP; only the feature flag adapter uses Microsoft.FeatureManagement; fake adapters live in an infrastructure `Fakes` namespace |
| `ProjectReferenceTests` | The same boundaries at project level, because the compiler drops a reference no code uses and a type-level rule would miss it: every module has its four layer projects, each references only what its layer allows, and only the Api host references Microsoft.FeatureManagement |
| `MessagingRegistrationTests` | Checked against the compiled handler graph of the real Api host, since Wolverine drops a message without a handler silently (spike S1): exactly one handler per command, at least one per integration event, every handled platform message marked as one or the other, and handlers named after their message inside an `Application` project |
| `PersistenceRegistrationTests` | Every type used as a Marten document and every domain event is registered explicitly — with `AutoCreate.None` an unregistered type fails in production only (spike S4) |
| `ProjectionCoverageTests` | `OrderDetails` applies every stored order event, so a new lifecycle event cannot silently stop the read model following the order |
| `ContractNamesTests` | Stored Marten event aliases and Wolverine message type names match `contract-names.approved.txt`; adding a name is a reviewed change to that file, and a class rename can never change one silently |

### OrderPlatform.Migrations.Tests — expand/contract safety

| Class | What it covers |
|---|---|
| `MigrationRuleTests` | Each ADR-0009 rule proved twice — a script that must be rejected and one that must be accepted: unsafe expand scripts, `SET NOT NULL` after a validated check constraint, a contract script that references its expand script and one that does not, and the generated-patch rules accepting Marten table creation while rejecting destructive changes |
| `RepositoryScriptTests` | The same rules applied to every DbUp script in the repository and to the generated Marten patch, plus a guard that there are scripts to check at all and that a contract script completes an expand script from an earlier release. `Generated_Marten_patch_is_expand_only` is skipped unless `MARTEN_PATCH_FILE` is set, which the release-compatibility job provides |

### OrderPlatform.Api.IntegrationTests — endpoints through to the database

Against the schema the Migrator builds, with the Api on `AutoCreate.None`. Asynchronous steps are polled for, never
timed.

| Class | What it covers |
|---|---|
| `MigratorTests` | The Migrator creates every schema, records its run, is safe to run again, and exports a trace of its steps |
| `StartupGuardTests` | The Api against an unmigrated database fails at startup with a clear message, and in Production with a fake integration refuses to start — both as a separate process, so the exit code is checked the way a container platform would |
| `AuthenticationTests` | A real Keycloak token calls the endpoint; no token and an invalid token get 401; a token without the scope gets 403; the portal user is allowed; a caller with no customer account gets 403 on customer endpoints |
| `IdempotencyTests` | A repeated key returns the stored response, the same key with a different body returns 422, a repeat while the first request is in flight returns 409, keys are scoped per account, a missing key returns 400, and a failed request releases its key so a retry runs again |
| `ProblemDetailsTests` | Errors are problem details with a stable `errorCode` and the `traceId`, framework errors included, and exception details never reach the response |
| `FeatureFlagTests` | A flag with no configuration evaluates to off, and flagged behaviour is exercised with the flag on and off |
| `ApiTracingTests` | Request, handler, database and flag-evaluation spans are exported over OTLP and health probes are not — run as its own process, because OpenTelemetry listeners are process-wide |
| `CustomerDirectoryTests` | The seeded accounts of the local realm are readable through `ICustomerDirectory` with tax profile, address and contact ids; an unknown account is a returned failure, not an exception |
| `PricingServiceTests` | A customer-specific price overrides the base price, a SKU with no price fails with `unknown product`, and the breakdown records the price list version and the shipping-charge flag decision |
| `SubmitOrderTests` | `POST /v1/orders` answers 201 with the state and the locked breakdown; an unknown SKU is 422; an inactive account cannot submit; a repeat with the same key returns the original order and starts no second stream; a stored order event carries no personal data |
| `ReadOrderTests` | An order is read back from the inline projection with its state, items and breakdown; the history comes from the raw stream under the stored event names; another account's order and a missing one are both not found |
| `InventoryReservationTests` | Submitting sends `ReserveInventory` through the outbox and available stock moves the order to invoicing; a line the provider cannot supply sends it back to the customer; one submission is traced from the request through the outbox to the order update |
| `CancelOrderTests` | Cancelling a reserved order releases its stock; an order in processing is refused with its state and a support hint; a reservation that completes after the cancel is released; another account's order is not found |

### OrderPlatform.AppHost.Tests

| Class | What it covers |
|---|---|
| `AppHostTests` | The Aspire AppHost runs the Migrator to completion before the Api becomes ready, as deployment does |

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
