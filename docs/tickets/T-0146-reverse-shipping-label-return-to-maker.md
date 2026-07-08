---
id: T-0146
title: Reverse Zásilkovna shipping label (return-to-maker) integration
status: ready
size: L
owner: dotnet-backend
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0070, T-0145]
blocks: []
user_stories: [US-customer-0023]
adrs: [0016, 0017]
phase: 7
manual_steps: [ef-migration]
security_touching: false
layers: [dotnet-db, dotnet-backend, frontend, l10n]
---

# T-0146 — Reverse Zásilkovna shipping label (return-to-maker) integration

## Context

Per [dopady-rozhodnuti-na-platformu.md §2.5](../meetings/dopady-rozhodnuti-na-platformu.md#25-reklamační-proces-q6q9--l) (dopady §1 Q8–Q9), once a customer's complaint under T-0145 is confirmed to warrant a physical return, the customer ships the item back to the maker via Zásilkovna, and the cost of that return is charged against the maker (never the customer). This is the "reverse-logistics half" of the reklamace work package — T-0145 (the window + timer) is a hard dependency: there is no confirmed-eligible complaint to attach a return label to until that ticket's `Dispute` machinery is in place with its window rule.

Today `IShippingCarrier`/`PacketaShippingCarrier` (see [shipping-carrier.md](../architecture/roles/shipping-carrier.md)) only creates **forward** shipments (maker → customer, T-0072). This ticket adds the mirror-image capability: a reverse shipment (customer → maker), reusing the existing label-caching pattern from T-0074/T-0075 (fetch, cache to blob, stream on download) rather than inventing a new one.

Satisfies US-customer-0023.

## Scope

### Backend

- `IShippingCarrier` gains a `CreateReturnShipmentAsync(Dispute dispute)` (or equivalent) method alongside the existing `CreateShipmentAsync`, returning the same `Shipment(CarrierRef, TrackingUrl)` shape. `PacketaShippingCarrier` implements it — Packeta's reverse-shipment creation is the same v6 REST surface with sender/recipient swapped (customer's address as sender, maker's registered or pickup address as recipient).
- New admin-triggered command, e.g. `GenerateReturnLabel.Command` (`IAdminAuditableCommand` — this is a money-and-logistics-affecting admin judgment call, mirroring `RefundOrder`'s admin-gated posture per the Alternatives Considered below): given an open `Dispute` in a return-warranting category (`DamagedItem`, `NotAsDescribed`), calls `IShippingCarrierFactory.ResolveAsync` → `carrier.CreateReturnShipmentAsync` → stores the resulting `CarrierRef`/`TrackingUrl` on the `Dispute` (new nullable columns, mirroring `Order`'s existing shipping fields) → enqueues label-fetch (mirrors T-0074's `shipping.generate.label` → `FetchAndStoreShippingLabel` pattern, but writing to a distinct blob path for the return leg, e.g. `invoices/{cc}/disputes/{disputeId}/return-label.pdf`).
- New customer-facing download endpoint (mirrors T-0075's `FilesController.GetShippingLabel`): cache-hit streams from blob; cache-miss falls back to a live Packeta call with fire-and-forget cache-fill.
- Return shipping cost: recorded against the maker. The cost is a small fixed/estimated amount known at label-creation time from the Packeta response (or the platform's existing `DefaultShippingPriceMinor` as a stand-in cost basis, if Packeta doesn't return an itemized reverse-leg price) — deducted from the maker's **next payout batch** as a new negative line item, or reflected on their next fee invoice. **BA flags this cost-application mechanism as a locked default requiring an accounting sign-off before implementation** (see Alternatives Considered + the open question raised below) — the mechanism (payout deduction vs. fee-invoice line) is not specified in the meeting notes beyond "účtovat proti výplatě nebo fee faktuře".
- No automated carrier-status sync for the reverse leg — the maker (or admin on the maker's behalf) manually marks the return as received, closing the loop for the associated dispute's eventual `ResolveDispute.Command`.
- New `BusinessErrorMessage` codes reusing the existing `ShippingCarrier*` family from T-0070 where the error shape matches (e.g. `ShippingCarrierUnavailable`), plus any reverse-specific additions.

### Frontend

- On the customer's order/dispute page: once a return label exists, a "Stáhnout vratkový štítek" download link/button (mirrors the existing forward-label download UX, US-maker-0009, but on the customer side).
- On the admin dispute-review UI: a "Vygenerovat vratkový štítek" action alongside the existing resolve action.

## Alternatives Considered

- **Option A — Auto-generate the return label the instant a customer opens a dispute in a return-warranting category, with no admin gate.** *Rejected as the MVP default* — every other money/logistics-affecting outcome in the dispute model is admin-triggered (dispute.md: `RefundOrder`, `Order.Cancel` are both nested inside `ResolveDispute.Command`, never automatic). Whether a claim is credible enough to justify shipping the maker a return-label bill is a judgment call the existing model reserves for admin. Auto-generating for every complaint (including illegitimate ones) would create needless carrier-cost exposure for makers. **Recorded as a BA default, not a final ruling — logged in `docs/questions/open.md`** since the meeting notes don't explicitly assign the trigger to admin vs. an earlier point in the thread.
- **Option B — Charge the return cost to the customer up-front (refundable if the complaint is upheld), instead of always charging the maker.** *Rejected* — dopady §2.5/Q9 is explicit: "Náklady na vrácení zboží při oprávněné reklamaci nese maker" (the maker bears the cost of a *valid* return). This ticket only generates the label once a return has been judged warranted (i.e. eligible), so by the time a label exists the cost is already the maker's per the business rule — there's no "refundable customer charge" step to build.
- **Option C — Wire live carrier-status sync for the reverse leg (auto-detect the maker received the item), mirroring T-0078's forward sync.** *Rejected for MVP* — the forward auto-deliver/carrier-sync path exists because it drives a customer-facing "did I get my package" experience with clear money consequences (payout release). The reverse leg's "did the maker get the return" doesn't gate an equivalent automatic transition in this ticket's scope (there's no "auto-close the dispute" behavior being built) — manual admin/maker acknowledgment is sufficient and far cheaper to build. Revisit if reklamace volume makes manual tracking a bottleneck.

## Out of scope

- Automatic carrier-status sync confirming the reverse shipment was delivered to the maker (manual acknowledgment only, per Option C above).
- A self-service "start a return" button for the customer with no admin review (Option A — flagged as an open question, not locked here).
- Partial-item / partial-quantity returns (Order is single-product at MVP; a return is all-or-nothing).
- Any change to `Dispute.Resolve` / `ResolveDispute.Command`'s existing outcome dispatch (`Refunded`/`Resumed`/`Cancelled`) — label generation is a separate action from resolution, not a new resolution outcome.
- The exact accounting mechanism (payout-batch negative line item vs. fee-invoice line item) for charging the maker — flagged as needing a locked decision before implementation (see open question).

## Acceptance criteria

- **AC-1** Given an open `Dispute` in category `DamagedItem` or `NotAsDescribed`, when an admin triggers "Vygenerovat vratkový štítek", then a reverse Zásilkovna shipment is created (customer's address → maker's address), `CarrierRef`/`TrackingUrl` are stored on the dispute, and a "Stáhnout vratkový štítek" link becomes visible to the customer on the order/dispute page.
- **AC-2** Given the reverse label is generated, when its cost is computed, then it is recorded against the maker via the agreed mechanism (payout deduction or fee-invoice line — locked once the open question below is answered); it is never added to any customer-facing invoice or charge.
- **AC-3** Given the customer downloads the label (cache-miss on first request), when the download endpoint is called, then Packeta is called live, the PDF is cached to blob storage, and subsequent requests hit the cache (mirrors T-0075 AC-2/AC-3 exactly).
- **AC-4** Given Packeta returns a transient error when creating the reverse shipment, when the admin action is attempted, then it's rejected with the existing `Transient(ShippingCarrierUnavailable)` classification and the admin can retry (T-0070's error-classification table, unchanged).
- **AC-5** Given the maker (or admin on their behalf) confirms the returned item was received, when they mark it received, then that acknowledgment is recorded (no automated carrier-delivery detection) and is visible on the dispute's admin review page ahead of the eventual `ResolveDispute.Command`.
- **AC-6** Given a dispute in a category that does NOT plausibly warrant a physical return (e.g. `NotDelivered`, the two carrier-reserved categories), then the "generate return label" admin action is not offered for that dispute.
- **AC-7** Given a different maker's or customer's dispute id is used to request the return label download, then 404 (ownership-scoped, mirrors every other file-download endpoint's IDOR shield).

## Technical notes

- Precedent for the whole cache→carrier→cache-fill shape: T-0074 (`FetchAndStoreShippingLabel`) + T-0075 (`FilesController.GetShippingLabel`) — reuse verbatim, just pointed at the dispute-scoped blob path and reverse-direction carrier call.
- `PacketaShippingCarrier`'s existing `CreateShipmentAsync(Order)` takes the whole `Order` to read customer contact + address; the reverse variant needs an equivalent read from `Dispute` → `Order` (customer address is already on the order; maker address comes from `Maker.RegisteredAddressId` or the pickup address if `PersonalPickupEnabled`).
- The blob path convention (`invoices/{cc}/orders/{orderId}/label.pdf` for the forward label) needs a parallel convention for the return leg — suggested `invoices/{cc}/disputes/{disputeId}/return-label.pdf`, keeping the existing `invoices/` container (per T-0070's precedent of colocating shipping artifacts there) rather than introducing a new blob container.

## Open question raised

Two points in this ticket are locked as BA defaults, not final rulings, and are logged in `docs/questions/open.md`:

1. **Who/when triggers return-label generation** — admin-gated (this ticket's default, mirroring `RefundOrder`) vs. some earlier automatic trigger once the customer/maker thread agrees a return is warranted.
2. **Accounting mechanism for the maker-borne return cost** — deducted from the next payout batch as a negative line item, vs. reflected as a line on the maker's next fee invoice. This needs a decision before the Scope's "charge the maker" step can be implemented precisely.

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Shipping/IShippingCarrier.cs` — add `CreateReturnShipmentAsync`.
- `backend/src/Makables.Infra.Clients/Packeta/PacketaShippingCarrier.cs` — implement it.
- `backend/src/Makables.Core.Domain/Orders/Dispute.cs` — new nullable `ReturnCarrierRef`/`ReturnTrackingUrl` columns + a mutator.
- `backend/src/Makables.Core.AppServices/Features/Orders/GenerateReturnLabel.cs` — new one-file admin feature.
- `backend/src/Makables.Functions/FetchAndStoreReturnLabelFunction.cs` (or reuse/extend the existing T-0074 Function with a discriminator) — queue-triggered fetch+cache.
- `backend/src/Makables.Web.Customer/Controllers/FilesController.cs` (or equivalent) — new download endpoint.
- `backend/src/Makables.Infra.Database/Migrations/` — new migration for the dispute columns.
- `frontend/` — customer download link + admin "generate return label" action.
- `docs/architecture/roles/dispute.md`, `docs/architecture/roles/shipping-carrier.md` — note the reverse-shipment capability once shipped.

## Test plan reference

`docs/test-plans/T-0146.md` (to be created by the implementer; cover the cache-hit/cache-miss label download paths and the category-gate on which disputes offer the return-label action).

## Status log

- 2026-07-07 `draft` by PM — added to the Phase 7 business-model-pivot manifest per dopady §6 work-package table, split (b) of the §2.5 reklamace package (depends on T-0145).
- 2026-07-07 `draft → ready` by BA. Wrote US-customer-0023 with Given/When/Then AC + Alternatives Considered. Locked as BA defaults (not final rulings): return-label generation is admin-gated (mirrors `RefundOrder`); reverse-leg carrier-status sync is out of scope (manual ack only). Raised an open question in `docs/questions/open.md` on (1) the exact trigger point for label generation and (2) the accounting mechanism for the maker-borne cost — both need a decision before implementation can finalize the cost-charging step precisely, but neither blocks writing AC or reaching `ready`, since the ticket's shape (reverse shipment + cache + admin gate) is well-defined regardless of the answer.
