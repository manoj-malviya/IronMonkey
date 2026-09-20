# Products, Quotes and Deal Economics

## Context

`Opportunity` carries a single `decimal Amount` and nothing else. There is no product catalog, no line items, no pricing, no discounting, no quote document. That single number is where every industry's commercial detail currently goes to be lost: a dealership's vehicle plus finance plus add-ons, a university's tuition plus fees per term, a broker's premium plus commission, an agency's retainer plus one-off work.

The consequence reaches past data entry. Forecasting cannot distinguish recurring from one-off revenue. Reports cannot answer "what do we sell most of". Nothing can produce the document the customer actually signs. And because `Amount` is a bare decimal with no currency attached, a tenant operating in more than one currency is silently adding numbers that are not commensurable — which the invariant-culture `¤` problem has been masking rather than causing.

## Prompt

Add a configurable product catalog, quote generation and richer deal economics — flexible enough that a dealership, a university and a broker each recognize their own commercial model.

### Catalog

- Add tenant-scoped products/services with code, name, description, category, active flag, and support for custom fields through the existing definition and binder machinery. A vertical's catalog needs vertical attributes; do not invent a second field system for it.
- Support pricing that is not a single number: list price, cost, currency, and at least one recurring model alongside one-off — per unit, per term, per month — because subscription, tuition-per-semester and premium-per-year are the same shape and none of them is a one-time amount.
- Support price lists or tiers so a tenant can price differently by segment, region or agreement, with explicit precedence when more than one applies.
- Version prices with effective dates, and make quotes reference the price as it was when quoted. A price change must never silently rewrite the value of a quote already sent to a customer.

### Line items and deal value

- Give opportunities line items referencing catalog entries, with quantity, unit price, discount, tax and computed totals. Allow a free-text line for something not in the catalog, so the model does not block a deal the catalog has not caught up with.
- Compute deal value from line items rather than storing a free decimal, and migrate existing `Amount` values onto a single line item so no opportunity loses its value. Reconcile explicitly what happens to opportunities whose `Amount` was zero or unset.
- Attach a currency to monetary values and refuse to aggregate across currencies without an explicit conversion. Store the rate used and its date on the record — a report that silently sums pounds and euros is worse than one that declines to.
- Round and total monetary values in one place with a documented rule, applied identically in the API, the UI and reports. Three call sites rounding independently will disagree on a total by pennies, and a customer-facing quote that disagrees with the CRM is a support incident.
- Separate one-off from recurring value in the model, so forecasting can report both without inferring it.

### Quotes

- Generate a quote from an opportunity: a numbered, versioned document with validity dates, terms, line items and totals, rendered with tenant branding.
- Keep quote versions immutable once sent, and make superseding explicit. Editing a sent quote in place destroys the record of what the customer was actually offered.
- Support a status lifecycle — draft, sent, accepted, rejected, expired — and record who changed it and when.
- Produce a shareable document. If a quote is exposed by link for customer acceptance, the token must be unguessable, scoped to that quote, expiring, revocable, and must not expose any other tenant data; treat that page as unauthenticated attacker-reachable surface.
- Add optional approval rules — for instance, a discount above a threshold requires a manager — expressed through the permission and hierarchy model rather than hardcoded.
- Wire quote events into the workflow engine as triggers and the communications layer for delivery, rather than building a private notification path.

### Reporting and recipes

- Extend forecasting and reporting to use line-item data: revenue by product, by category, recurring versus one-off, discount analysis, and win rates by product.
- Use `MoneyFormat`/tenant currency formatting throughout; never `"C0"`, which renders `¤308,000` under the invariant culture.
- Let `RecipeContentModel` seed a starter catalog and quote template per vertical, so the Automobile recipe arrives with a plausible product shape. Recipes stored before this change must still provision.

## Acceptance criteria

- A tenant defines products with recurring and one-off pricing, builds an opportunity from line items, and sees a total that matches the generated quote exactly.
- Existing opportunities migrate with their `Amount` preserved as a line item, including the zero and unset cases.
- A price change does not alter the value of a quote already sent.
- Monetary values carry a currency; cross-currency aggregation either converts with a recorded rate or is refused, and never silently sums.
- Rounding is identical across API, UI and quote document.
- A sent quote cannot be edited in place; superseding creates a new version and the history shows both.
- A shared quote link is unguessable, expiring, revocable, and exposes nothing but that quote.
- Discount approval thresholds are enforced server-side.
- Reports break revenue down by product and separate recurring from one-off.
- Tests cover the `Amount` migration, price versioning, rounding consistency, multi-currency refusal, quote immutability, approval enforcement, shared-link scoping and expiry, and recipe compatibility.

## Likely implementation surfaces

- `IronMonkey.Data/Entities/Opportunity.cs` plus new product, price, line-item and quote entities and a tenant migration with backfill
- `IronMonkey.ApiService/Features/Opportunities/` and a new catalog/quotes feature
- `IronMonkey.ApiService/Features/Reports/` and the dashboard endpoints
- The `MoneyFormat` helper and tenant currency settings
- The workflow engine triggers and the communications layer
- `IronMonkey.Data/RecipeContent/RecipeContentModel.cs`
- `IronMonkey.Web/Components/Pages/Admin/Opportunities/`
- Tests in `IronMonkey.Tests`

Money is the part users check by hand. Get rounding, currency and versioning right before adding features on top.
