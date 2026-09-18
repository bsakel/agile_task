# ADR-0018: Pricing as a rule pipeline

- **Status:** Accepted — see the implementation notes below
- **Date:** 2026-09-17
- **Related:** README §2 (B2B assumptions), ADR-0017 (order lifecycle), ADR-0019 (feature flags)

## Context

Pricing must cover unit prices, taxes and "any additional charges". In B2B, prices are frequently customer-specific,
tax depends on the customer's tax profile (e.g. VAT reverse charge for cross-border business customers), and the set of
charges changes more often than any other part of the system. The amounts we calculate are what the customer is invoiced.

## Decision

### Contract

`Pricing.Contracts` exposes `IPricingService.PriceAsync(PricingRequest) → Result<PriceBreakdown>`, where the request
contains the customer account id and the order lines (SKU, quantity).

### Price data

- The Pricing module owns **price lists** in the `pricing` schema: a **base price list** plus **customer-specific
  overrides** per SKU.
- v1 seeds price lists via migrations; an import API or ERP synchronisation is backlog.
- **Product ids are SKUs of the external inventory system.** A SKU without a price in the applicable price list is an
  unknown product: pricing fails and submission returns `422` (ADR-0020). This validates products **without calling the
  external inventory system in the request path**; inventory confirms existence again during reservation (ADR-0017).
- The customer's **tax profile** (country, VAT number status, reverse-charge eligibility) is read from
  `Customers.Contracts`, which serves it from its local copy (no external call in the request path).

### Pipeline

Pricing is an ordered pipeline of `IPricingRule` implementations grouped in **fixed stages**:

| Stage | Purpose | v1 rules |
|---|---|---|
| 1. Subtotal | Unit price × quantity per line | `LineSubtotalRule` |
| 2. Discounts | Reductions on lines or order | none in v1 |
| 3. Charges | Additional charges | `ShippingChargeRule` (behind flag `Pricing.ShippingCharge`) |
| 4. Tax | Tax per tax rate | `TaxRule` (incl. reverse charge) |

- Stages always run in this order. Within a stage, rules run in registration order, and a test asserts the registered order.
- Each rule adds lines to the `PriceBreakdown`; rules never modify lines added by earlier rules.

### Money and rounding

- **Single currency: EUR.** Money is a value object (`decimal` amount + currency); no floating point.
- **Rounding:** 2 decimals, `MidpointRounding.AwayFromZero`.
- Line amounts and charges are rounded **per line**.
- **Tax is calculated per tax rate on the sum of the rounded lines** at that rate, then rounded — not per line.
- The invoice sends our breakdown as-is; the billing system is configured not to recalculate. Any mismatch reported
  by billing is an error, not a silent correction.

### Price lock

Pricing runs at submission and after every order edit. The resulting `PriceBreakdown`, including the price list
version and flag decisions used, is stored on the event (`OrderSubmitted`, `OrderItemsReduced`). The invoice uses the
latest recorded breakdown, so historic orders never change when price lists or rules change.

### Implementation (PR 2c)

- `Pricing.Contracts` carries **its own `PriceBreakdown`** (totals, lines, reverse-charge flag, price list version, flag
  decisions), mapped from the domain breakdown by `PricingService`. A module's `Contracts` project may not reference its
  `Domain` project (ADR-0003, enforced by the architecture tests), so the published breakdown is the contract shape of the
  domain one rather than the domain type itself.
- Schema `pricing`: `price_lists` (the base list has no `account_id`, a customer list has one) and `price_list_items`
  (unit price and tax rate per SKU). No foreign key to `customers.accounts` — no module reads another module's schema
  (ADR-0007). The **shipping charge is an attribute of the price list**, so a customer list can carry its own.
- The recorded `PriceListVersion` names **every list that took part**, base list first, e.g. `base-2026-09+acme-2026-09`,
  so the breakdown stays explainable when only one of the lists changes.
- `Pricing.ShippingCharge` is evaluated **once per pricing run** by `PricingService` and recorded in the breakdown's flag
  decisions; the rules read the recorded decision, never the flag (ADR-0019 rule 4).

## Consequences

- Positive: new charges are a new rule class plus registration; each rule unit-testable in isolation; auditable
  breakdown; deterministic rounding agreed with billing; product validation without a synchronous external dependency.
- Negative: price lists must be kept in sync with the product catalogue; a SKU missing from the price list blocks
  ordering it; per-rate tax rounding must be confirmed with finance for each jurisdiction.
