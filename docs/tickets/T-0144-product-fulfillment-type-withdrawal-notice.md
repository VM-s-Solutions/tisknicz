---
id: T-0144
title: Product fulfillment-type flag ("na zakázku" vs. "skladem") + checkout withdrawal-right copy
status: ready
size: M
owner: dotnet-backend
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0041, T-0084a, T-0084b, T-0130]
blocks: []
user_stories: [US-maker-0018, US-customer-0021]
adrs: []
phase: 7
manual_steps: [ef-migration, nswag-regen]
security_touching: false
layers: [dotnet-db, dotnet-backend, frontend, l10n]
---

# T-0144 — Product fulfillment-type flag + checkout withdrawal-right copy

## Context

Per [dopady-rozhodnuti-na-platformu.md §2.4](../meetings/dopady-rozhodnuti-na-platformu.md#24-produktový-příznak-na-zakázku--skladem--m) (dopady §1 Q5), the business has decided the platform must distinguish "na zakázku" (made-to-order) from "skladem" (in-stock) products, because Czech consumer law treats them differently: made-to-order goods produced to the consumer's specification are exempt from the standard 14-day right of withdrawal (§ 1837 písm. d) občanského zákoníku), and the consumer must be informed of that exemption **before** placing the order. In-stock goods carry the normal 14-day withdrawal right.

Today `Product` has no such flag — every product is implicitly treated the same way, and neither the product detail page nor the checkout flow says anything about withdrawal rights. This ticket adds the flag (maker-set, default `MadeToOrder` — the platform's dominant use case per personas.md) and the checkout-time notice branching on it. This is a **cross-stack** ticket: backend column + NSwag regen, frontend badge (product detail) + checkout copy (two variants) + l10n keys. Satisfies US-maker-0018 (maker sets the flag) and US-customer-0021 (customer sees the correct notice).

Per the phase-7 manifest, this ticket is **not blocked** on any open business question — the only caveat is that the *final legal wording* of both notices is gated behind the pre-existing VOP/GDPR launch-blocker (Q-0030, dopady §5.6). This ticket does not wait for that: it ships the flag, the branching logic, and interim placeholder copy using the same **legal-placeholder-lock pattern** T-0130 already established for the VOP/GDPR pages, so the mechanism is fully testable now and only the literal text needs a swap later.

## Scope

### Backend

- New `Product.FulfillmentType` enum property (`MadeToOrder = 0` [default], `InStock = 1`).
- `CreateProduct.Command` / `UpdateProduct.Command` (and their Validators) gain the new field; maker chooses it explicitly at creation (defaulting the form control to "Na zakázku"), editable afterward via `UpdateProduct`.
- Migration adds the column with a `DEFAULT 0` (`MadeToOrder`) so every pre-existing product defaults to the safer legal posture (most catalog items today are custom production).
- `GetProductById` / `GetMyProductById` / catalog projection DTOs surface the new field so the frontend can render the badge + drive checkout copy.
- NSwag regen (public + maker hosts).

### Frontend

- Product detail page (`/produkt/[productId]`) — badge "Na zakázku" or "Skladem" next to the price, matching `product.fulfillmentType`.
- Maker product form (create/edit, `/dashboard/maker/produkty`) — new two-option control for `FulfillmentType`, defaulting to "Na zakázku".
- Checkout order form (`/objednavka`) — a notice block rendered before the submit action, branching on the ordered product's `FulfillmentType`:
  - `MadeToOrder` → mandatory pre-order notice waiving the 14-day withdrawal right (§ 1837 písm. d) OZ).
  - `InStock` → standard 14-day withdrawal-right notice.
- Both notice variants ship as **legal-placeholder-locked** interim copy (T-0130 pattern: visible, clearly labeled as interim, keyed i18n strings ready for a drop-in replacement once the VOP text is approved).
- New `checkout.withdrawalNotice.*` and `product.fulfillmentType.*` i18n keys in `cs-CZ.ts`.

## Alternatives Considered

- **Option A — Derive fulfillment type from the existing `PriceType` enum (`OnRequest` ⇒ made-to-order, `Fixed`/`From` ⇒ in-stock) instead of adding a new field.** *Rejected* — `PriceType` describes pricing certainty (is the price fixed, a starting-from estimate, or quote-based), not production timing. A `Fixed`-priced 3D-printed item is still made-to-order (the price is just known upfront); conflating the two fields would misclassify the platform's dominant use case and produce legally wrong notices. A dedicated, independent field is the only correct model — this matches US-maker-0018's Alternatives Considered section.
- **Option B — Default new products to `InStock` instead of `MadeToOrder`.** *Rejected* — personas.md documents that "products are made-to-order" for most makers on this platform (3D printing, custom textile, laser/CNC). Defaulting to `InStock` would silently promise a withdrawal right the maker usually can't legally honor for custom production; `MadeToOrder` is the safer legal default and matches the dominant real-world case.
- **Option C — Require a blocking checkbox acknowledgement at checkout instead of a visible notice.** *Rejected for MVP, flagged as a question* — the statutory requirement is that the consumer "must be informed" before ordering; a clearly-placed, unmissable notice satisfies that without the heavier UX of a forced checkbox. Logged in `docs/questions/open.md` in case the external legal reviewer (Q15/Q-0030) later requires an explicit acknowledgement gesture, which would be a small follow-up, not a rebuild.

## Out of scope

- Any change to order-placement validation — the notice is informational; it never blocks submission for either fulfillment type.
- Per-category default (e.g. auto-defaulting `cat-handmade` products to `InStock`) — the maker always sets it explicitly per product.
- Inventory/stock-count tracking for `InStock` products — the flag only drives the legal notice; stock-quantity management stays out of scope at MVP (per personas.md).
- Retroactive display of the notice on past orders — no notice text or fulfillment-type snapshot is stored on `Order`; if a future legal need arises to prove what was shown at order time, that's a separate ticket (see the Alternatives Considered note on US-customer-0021).
- Final, lawyer-approved wording of either notice — gated behind Q-0030 same as the rest of the VOP/GDPR text; this ticket ships the mechanism + placeholder copy only.

## Acceptance criteria

- **AC-1** Given a maker fills the product creation form, when they submit without explicitly choosing a fulfillment type, then the product is created with `FulfillmentType = MadeToOrder` (the form's default selection, not a silent server default the maker never saw).
- **AC-2** Given a maker edits an existing product's `FulfillmentType`, when saved, then the change is persisted via `UpdateProduct.Command` and applies to all *future* checkouts of that product; it has no effect on any already-placed order.
- **AC-3** Given a product with `FulfillmentType = MadeToOrder`, when a customer views its detail page, then a "Na zakázku" badge renders next to the price; when `FulfillmentType = InStock`, the badge reads "Skladem".
- **AC-4** Given a product with `FulfillmentType = MadeToOrder`, when the customer reaches the checkout form (`/objednavka?productId=`), then the made-to-order withdrawal-exemption notice renders above the submit action, before any payment redirect can occur.
- **AC-5** Given a product with `FulfillmentType = InStock`, when the customer reaches checkout, then the standard 14-day withdrawal-right notice renders instead.
- **AC-6** Given the existing pre-launch migration runs against products created before this ticket shipped, when it completes, then every existing product row has `FulfillmentType = MadeToOrder` (the column default) with no data loss and no manual backfill required.
- **AC-7** Given the notice copy is still interim (Q-0030 unresolved), when either notice renders, then it is visually marked as interim per the T-0130 pattern (matching the existing `/vop` and `/gdpr` placeholder-banner treatment) — not presented as final legal text.
- **AC-8** Given the public catalog / product-detail / maker-dashboard API responses are regenerated, when the NSwag client is rebuilt, then `fulfillmentType` appears as a typed field on the relevant DTOs in `frontend/src/lib/api-client/`, committed in the same PR.

## Technical notes

- Precedent for the placeholder-lock pattern: `frontend/src/app/(public)/gdpr/page.tsx` (T-0130) — visible `Alert variant="warning"` banner + keyed `static.privacy.*` strings ready for a drop-in swap. Reuse the same idiom for `checkout.withdrawalNotice.madeToOrder` / `checkout.withdrawalNotice.inStock`.
- `Product.Create` / `Product.Update` (`backend/src/Makables.Core.Domain/Products/Product.cs`) currently take `PriceType` as a sibling enum parameter with no fulfillment concept — extend both factory/mutator signatures analogously (same validation shape, no new invariant beyond enum membership).
- The checkout order form (`frontend/src/app/(customer)/objednavka/page.tsx`) already reads `product.priceType` to gate the CTA (per US-customer-0009 AC-4 precedent) — the fulfillment-type branch is a sibling read on the same loaded product, not a new fetch.
- No `Order` schema change — the notice is resolved purely from the product at render time, consistent with how the Zásilkovna widget config is resolved at render time (not snapshotted onto the order either).

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Products/Product.cs` — add `FulfillmentType` enum + property; extend `Create`/`Update`.
- `backend/src/Makables.Core.Domain/Products/FulfillmentType.cs` — new enum.
- `backend/src/Makables.Infra.Database/Configurations/ProductConfiguration.cs` — map column + default.
- `backend/src/Makables.Infra.Database/Migrations/` — new migration.
- `backend/src/Makables.Core.AppServices/Features/Products/CreateProduct.cs`, `UpdateProduct.cs` — extend Command/Validator/Handler.
- `backend/src/Makables.Core.AppServices/Features/Products/GetProductById.cs`, `GetMyProductById.cs`, catalog projection DTOs — surface the field.
- `frontend/src/app/(customer)/produkt/[productId]/page.tsx` — badge.
- `frontend/src/app/(maker)/dashboard/maker/produkty/*` — form control.
- `frontend/src/app/(customer)/objednavka/page.tsx` — checkout notice branching.
- `frontend/src/lib/i18n/cs-CZ.ts` — new `checkout.withdrawalNotice.*`, `product.fulfillmentType.*` keys.
- `frontend/src/lib/api-client/*` — NSwag regen (public + maker hosts).
- `docs/architecture/roles/product.md` — note the new field once shipped.

## Test plan reference

`docs/test-plans/T-0144.md` (to be created by the implementer; cover the migration default, both Command validators, and a frontend snapshot test per fulfillment-type/checkout-copy branch).

## Status log

- 2026-07-07 `draft` by PM — added to the Phase 7 business-model-pivot manifest per dopady §6 work-package table.
- 2026-07-07 `draft → ready` by BA. Wrote US-maker-0018 + US-customer-0021 with Given/When/Then AC + Alternatives Considered. Locked: `MadeToOrder` as the safer default (not `InStock`); no blocking acknowledgement checkbox at MVP (flagged as a question for the eventual legal review); notice is not snapshotted on the order. Interim copy follows the T-0130 legal-placeholder-lock pattern pending the pre-existing Q-0030 VOP/GDPR launch-blocker — this ticket does not wait on Q-0030 to reach `ready`, only the final wording swap does.
