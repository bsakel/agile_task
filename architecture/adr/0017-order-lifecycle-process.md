# ADR-0017: Order lifecycle as an event-sourced process

- **Status:** Accepted — delivered in PRs 2d, 2d-2 and 2e. **One open issue: leaving `RequiresAttention` (§2a).**
- **Date:** 2026-09-17
- **Related:** [README §2 — B2B business assumptions](../README.md#2-domain-scope-a-b2b-ordering-platform), ADR-0005 (durable messaging), ADR-0006 (Marten), ADR-0014 (external integrations), ADR-0016 (security), ADR-0018 (pricing), ADR-0019 (feature flags), ADR-0020 (API conventions)

## Context

An order moves from submission through inventory reservation, invoicing, payment, fulfilment and delivery. The flow
involves three external systems (inventory, billing, fulfilment/shipping), waits of days (payment due date, customer
responses), human decisions (customer edits, support cancellations) and compensation (release inventory, void invoice,
refund). No distributed transaction is possible.

The flow follows the **B2B assumptions in the README**. An earlier draft of this ADR assumed a retail flow (synchronous
availability check, card authorization and capture); it was replaced once the domain was fixed as B2B, which removed
the synchronous "accept" race and the card payment concerns.

## Decision

### 1. The Order aggregate is the process manager

There is **no separate saga document**. The Order event stream is the single source of truth for both business state
and process state.

- Handlers react to commands and to events from other modules (e.g. `InventoryReserved`, `InvoicePaid`,
  `FulfilmentFailed`), load the Order stream, let the aggregate decide, append the resulting events and send the next
  command — all in **one Marten transaction with the Wolverine outbox**.
- The aggregate enforces every transition. An event or timer that does not apply to the current state is **ignored and
  logged**, never an error. This makes stale timers, duplicate deliveries and late external events harmless.
- The process lives in the **Ordering module**. Other modules perform single steps (reserve, issue invoice, refund,
  request shipment) and report back with integration events; they hold no knowledge of the overall flow.

### 2. States

| State | Meaning | Exits |
|---|---|---|
| `ValidatingInventory` | Availability check and all-or-nothing reservation in the external inventory system | `Invoicing`, `AwaitingCustomer`, `Cancelled` |
| `AwaitingCustomer` | Inventory problem; customer must reduce the order or cancel | `ValidatingInventory`, `Cancelled` |
| `Invoicing` | Invoice being issued by the external billing system (uses latest price breakdown) | `AwaitingPayment`, `Cancelled` |
| `AwaitingPayment` | Invoice issued, due in 3 calendar days (UTC) | `Processing`, `Cancelled`, `RequiresAttention` |
| `Processing` | Packaging and shipment preparation by the fulfilment system | `Shipped`, `FulfilmentOnHold`, `Refunding` (support only) |
| `FulfilmentOnHold` | Fulfilment error; customer must update information or cancel | `Processing`, `Refunding` |
| `Refunding` | Refund requested from billing, inventory being released, shipment request cancelled | `Cancelled`, `RequiresAttention` |
| `Shipped` | Dispatched by the carrier | `Delivered` |
| `Delivered` | Terminal | — |
| `Cancelled` | Terminal (inventory released, invoice voided or refunded) | `RequiresAttention` on late payment |
| `RequiresAttention` | Automation stopped; a support agent must resolve (late payment, partial payment at due date, compensation failure) | `Cancelled` (support resolution, §2a) |

The state diagram is in [README §8](../README.md#8-order-lifecycle).

### 2a. Leaving `RequiresAttention` — open issue, decide before N3

**This is the one unresolved question in the lifecycle. It needs a business decision before the invoicing and payment
next steps (N3–N6) are built, because those are what start sending orders into `RequiresAttention` in volume.**

**What v1 does.** `ResolveAttention` (back office, mandatory reason) appends `AttentionResolved` and the order ends in
`Cancelled`. Before PR 2e there was no exit at all: this section named none and the README diagram drew no outgoing
edge, so an order that reached `RequiresAttention` could never leave it. Closing it as cancelled is the safe reading —
every reason for attention leaves an order that is not going to be fulfilled as ordered — and it is deliberately the
*only* exit until the question below is answered.

**Why it is not settled.** §6 says support decides "refund or **reinstatement**", and reinstatement has no target state.
It cannot have a single one, because the three reasons need different answers:

| Reason for attention | What reinstatement would have to mean | Why it is not obvious |
|---|---|---|
| `PartialPaymentAtDueDate` | Chase the remainder → back to `AwaitingPayment` | Needs a new due date, and a second overdue timer; refunding the part payment and cancelling is the alternative |
| `LatePaymentAfterCancellation` | Re-reserve and continue → back to `ValidatingInventory` | The inventory was already released, so the stock may be gone; the order may have to be cancelled again, refunding the payment that caused this |
| `CompensationFailed` | Finish the compensation → back to `Refunding` | Not a reinstatement at all: the order stays cancelled, the step has to complete |

**What a decision needs to cover:** which reasons may be reinstated at all, the target state for each, who is allowed to
do it, and what happens when reinstatement itself fails (the `LatePaymentAfterCancellation` path can fail on the very
next step). Until that is decided, support resolves by cancelling and handles the money outside the system — which is a
real gap, not a design choice: the platform records *that* it was resolved and why, but not what was refunded.

**Owner-facing summary:** back-office tooling for these orders is next step N12 and the refund/reinstatement decision is
in the backlog; both should be taken together, and the outcome recorded here as a revision of this ADR.

### 3. Submission and API surface

**Submission** (`SubmitOrder`, synchronous part — no external calls, ADR-0014):
1. Load the customer's account from `Customers.Contracts` (local copy): account must be active; billing and shipping
   address ids and the tax profile come from there.
2. Price the order via `Pricing.Contracts`: an SKU without a price is unknown → `422 unknown-product` (ADR-0018).
3. Start the stream with `OrderSubmitted` (lines, price breakdown, **address and contact ids — no personal data**,
   ADR-0016) and send `ReserveInventory` through the outbox.

Paths below omit the version prefix; all are served under `/v1` (ADR-0020). All state-changing `POST` endpoints require
an `Idempotency-Key`.

Customer API (account-scoped authorization, ADR-0016):

| Endpoint | Command | Allowed in state | Result |
|---|---|---|---|
| `POST /orders` (`Idempotency-Key` header) | `SubmitOrder` | — | `201 Created`, status `ValidatingInventory`, price breakdown |
| `GET /orders/{id}` | query | any | current state, items, price breakdown, invoice info |
| `GET /orders/{id}/history` | query | any | lifecycle events from the stream |
| `POST /orders/{id}/items/reduce` | `ReduceOrderItems` | `AwaitingCustomer` | `202 Accepted` → repriced, `ValidatingInventory` |
| `PUT /orders/{id}/fulfilment-information` | `UpdateFulfilmentInformation` (new address/contact stored in Customers, order references the new ids) | `FulfilmentOnHold` | `202 Accepted` → `Processing` |
| `POST /orders/{id}/cancel` | `CancelOrder` | `ValidatingInventory`, `AwaitingCustomer`, `Invoicing`, `AwaitingPayment`, `FulfilmentOnHold` | `202 Accepted` → compensation → `Cancelled` |
| `POST /orders/{id}/payment-status/refresh` | `CheckInvoiceStatus` | `AwaitingPayment` | `202 Accepted` |

A command in a state that does not allow it returns `409 Conflict` with problem details stating the current state and,
for cancellation from `Processing` onwards, that customer support must be contacted.

Back-office API (support agent policy, mandatory reason recorded on the event):

| Endpoint | Command | Allowed in state |
|---|---|---|
| `POST /backoffice/orders/{id}/cancel` | `CancelOrderBySupport` | all customer-cancellable states and `Processing` |
| `POST /backoffice/orders/{id}/resolve` | `ResolveAttention` (e.g. refund late payment, mark resolved) | `RequiresAttention` |

Later: `POST /webhooks/billing` (signature verified) → `CheckInvoiceStatus` / `InvoicePaid`.

### 4. Timers are durable scheduled messages

| Timer | Scheduled when | On fire |
|---|---|---|
| `CheckInvoiceStatus` (daily) | `InvoiceIssued`, then re-scheduled after each check while `AwaitingPayment` | Billing checks status → `InvoicePaid` / `InvoicePartiallyPaid` / no change |
| `InvoiceOverdue` | `InvoiceIssued`, at issue time + 3 calendar days (UTC) | Unpaid → cancel (void invoice, release inventory); partially paid → `RequiresAttention` |
| `CustomerResponseTimeout` | Entering `AwaitingCustomer` or `FulfilmentOnHold`, + 7 days (configurable) | Cancel with the same compensation as a customer cancellation |

Wolverine persists scheduled messages in PostgreSQL (ADR-0005), so they survive crashes and deployments. Timers are
never cancelled explicitly: when they fire, the aggregate ignores them if the state has moved on (§1).

**One handler, three triggers:** the daily timer, the manual refresh endpoint and the future billing webhook all send the
same `CheckInvoiceStatus` command. Adding the webhook is a new trigger, not new logic.

### 5. Compensation

| Leaving via cancellation from | Compensation steps |
|---|---|
| `ValidatingInventory` | Release reservation if one was made (including a reservation that completes after the cancel) |
| `AwaitingCustomer` | None (nothing reserved — reservation is all-or-nothing) |
| `Invoicing`, `AwaitingPayment` | Void invoice, release inventory |
| `FulfilmentOnHold`, `Processing` (support) | Cancel shipment request, refund invoice, release inventory → `Refunding` until refund confirmed |

Each step is a message handled by the owning module, retried by Wolverine policies. A step that exhausts its retries
moves the order to `RequiresAttention` and raises an alert — automation never retries forever.

### 6. Race conditions handled by the aggregate

| Race | Handling |
|---|---|
| Cancel while reservation is in flight | `InventoryReserved` arrives in `Cancelled` → release it |
| Invoice paid after API cancel or overdue cancellation | `InvoicePaid` arrives in `Cancelled` → `RequiresAttention` (late payment, support decides refund or reinstatement) |
| Payment check and overdue timer fire together | Events are applied in stream order with optimistic concurrency; the losing handler retries against the new state and is ignored if no longer applicable |
| Fulfilment error reported after dispatch | `FulfilmentFailed` in `Shipped` → ignored and logged; carrier issues are out of scope |

### 7. External calls

- Every state-changing external call carries our own **idempotency key** derived from the order and step
  (e.g. `reserve:{orderId}:{attempt}`, `refund:{orderId}`). After a timeout, the adapter queries the external system for
  the key's outcome before retrying (ADR-0014).
- External calls are made from message handlers, never inside the HTTP request.

### 8. Process versioning

Orders live for days, so the process can change while orders are in flight. `OrderSubmitted` records the process
version and any flag decisions that affect the flow (ADR-0019 rule 4). In-flight orders complete on the version they
started with; an old process path is removed only when no active order uses it.

## Alternatives considered

| Option | Why not |
|---|---|
| Separate Wolverine saga document next to the Order stream | Two sources of truth for the same lifecycle that can disagree |
| Choreography (modules react to each other's events without a coordinator) | No single place that shows the flow; compensation logic scattered across modules |
| Synchronous validation and reservation at submission | Puts the external inventory system in the request path; B2B customers do not need an instant answer |
| Daily batch job scanning all unpaid orders | Needs its own scheduler and query; per-order scheduled messages are durable, precise to the due date and share the same handler as manual refresh |
| Retail-style card authorization and capture | Does not match the B2B invoice payment model |

## Consequences

- Positive: the whole lifecycle is visible in one stream and one aggregate; timers, retries and late events are safe by
  construction; every transition is unit-testable without infrastructure.
- Negative: the Order aggregate is the most complex class in the system and must stay free of rules owned by other
  modules (pricing, inventory, billing); `RequiresAttention` needs a support process and tooling beyond the skeleton.
- Business assumptions this ADR depends on are listed in the README. Changing them requires a new ADR superseding this one.
