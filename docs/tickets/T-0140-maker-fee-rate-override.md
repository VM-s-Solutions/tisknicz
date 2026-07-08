---
id: T-0140
title: Maker fee-rate override (loyalty commission 7%→3,5%, admin-set)
status: in_progress
size: M
owner: dotnet-backend
created: 2026-07-07
updated: 2026-07-07
depends_on: [T-0010, T-0034]
blocks: []
user_stories: [US-admin-0018]
adrs: [0004, 0014]
phase: 7
manual_steps: [ef-migration]
security_touching: false
layers: [dotnet-db, dotnet-backend]
---

# T-0140 — Maker fee-rate override (loyalty commission 7%→3,5%, admin-set)

## Context

Per [dopady-rozhodnuti-na-platformu.md §2.2](../meetings/dopady-rozhodnuti-na-platformu.md#22-provize-7--35-věrnostní--m--okamžitá-oprava-nesouladu), the platform's commission is now 7% base with a 3,5% loyalty rate for makers who've cooperated with the platform longer. The base-rate mismatch (backend seeded 15% while the marketing site promised 7%/3,5%) has already been fixed outside this ticket, via a dotnet-db migration seeding `CountryConfiguration.PlatformFeeRateBp = 700`. What has never existed is the *per-maker* discount: today the commission is one number per country, and there is no way to grant an individual maker a lower rate.

This ticket builds that missing half: a nullable override on `Maker` that, when set, takes priority over the country default in the pricing calculation. Per dopady §5.2, the *criteria* for who qualifies for the loyalty rate ("after longer cooperation" — how many months, orders, or how much revenue) are explicitly undecided and out of scope for this ticket. The locked MVP fallback is **admin-manual**: an admin looks at a maker's history and decides to grant the override, the same way `VerifyMaker` is an admin judgment call today. This ticket does not need to wait for that open question — it only needs the override *mechanism* plus the audit trail, which is a self-contained, well-defined scope. Satisfies US-admin-0018.

## Scope

- New nullable column `Maker.FeeRateOverrideBp` (`int?`), defaulting to `null` (no override — country default applies).
- New admin command `SetMakerFeeOverride.Command` (`IAdminAuditableCommand`, following the `VerifyMaker`/`DeactivateMaker` precedent in T-0034): sets or clears the override with a required `Reason`, audited before/after.
- `OrderPricing.Compute` (or its caller, `PricingService.ComputeForProductAsync`) reads `maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp` instead of `config.PlatformFeeRateBp` unconditionally. `PricingService` must load the `Maker` (via `Product.MakerId`) alongside the existing `Product` + `CountryConfiguration` loads to resolve this.
- The resolved rate continues to be **snapshotted onto the order** at order-creation time exactly as today — changing (or clearing) an override never touches historical orders' pricing.
- No change to the fee-invoice generation flow (T-0068a/T-0068b/T-0101): the weekly payout batch's fee invoice already sums the `PlatformFeeMinor` snapshot per order, so an overridden maker's fee invoice automatically reflects the lower total with zero invoice-side code changes.
- New `Web.Admin` endpoint (mirrors `VerifyMaker`/`DeactivateMaker` controller shape) to set/clear the override.
- Admin UI: a field on the maker detail page (`/dashboard/admin/makers/{id}`) showing the current effective rate (override or country default) and a form to set/clear it.
- NSwag regen (admin host).

## Alternatives Considered

- **Option A — Store the override as a percentage float instead of basis points.** *Rejected* — every other rate in the system (`PlatformFeeRateBp`, VAT rates) is stored as basis points (`int`); a float would be the only non-integer rate field, reintroducing the floating-point rounding risk ADR 0003 deliberately eliminated.
- **Option B — Allow the override to exceed the country default (general per-maker rate, not strictly a discount).** *Rejected for MVP* — the business decision is specifically a loyalty *discount*. Letting an admin silently set a maker's fee *above* the advertised 7% risks a maker-trust issue with no corresponding business need identified in the meeting notes. This ticket's Validator enforces `FeeRateOverrideBp <= CountryConfiguration.PlatformFeeRateBp` (when set). If a future need for a negotiated higher rate emerges, the nullable-int schema doesn't block a later ticket from relaxing that constraint.
- **Option C — Compute the override inside `Maker` as a method (`Maker.EffectiveFeeRateBp(CountryConfiguration config)`).** *Considered, deferred to implementer* — this is a clean way to keep the `?? ` fallback logic in one place rather than duplicating it at every pricing call site. Left as a technical-note suggestion rather than locked scope, since it doesn't change the AC or the wire contract.

## Out of scope

- Automatic/criteria-based award of the loyalty rate (dopady §5.2 — unresolved; blocks only a *future* automation ticket, not this one).
- More than two tiers (base rate vs. one override value) — no tiered ladder (e.g. bronze/silver/gold).
- Maker-facing visibility into *why* a rate was granted (the admin `Reason` is audit-log-only).
- Bulk/CSV override import — one maker at a time via the admin detail page.
- Any change to the fee-invoice template or the payout-batch aggregation logic (both already sum per-order snapshots correctly).

## Acceptance criteria

- **AC-1** Given a maker with no override set (`FeeRateOverrideBp = null`), when the admin sets an override of 350 bp with a reason, then `Maker.FeeRateOverrideBp = 350` is persisted and an `AdminAuditLogEntry` captures `before_json` (`null`) and `after_json` (`350`) plus the reason.
- **AC-2** Given a maker with `FeeRateOverrideBp = 350` and a `CountryConfiguration.PlatformFeeRateBp = 700`, when a new order is priced for one of that maker's products, then `OrderPricing.Compute` uses 350 bp (not 700) to compute the platform-fee line, and that resolved value is what gets snapshotted onto the created order.
- **AC-3** Given a maker with `FeeRateOverrideBp = null`, when an order is priced, then the platform fee uses `CountryConfiguration.PlatformFeeRateBp` exactly as before this ticket — no behavior change for makers without an override.
- **AC-4** Given an admin clears a previously-set override (submits `null`), when saved, then `FeeRateOverrideBp` returns to `null` and subsequent pricing for that maker reverts to the country default; the audit entry captures the clear (`before_json` = old value, `after_json` = `null`).
- **AC-5** Given an admin submits an override value greater than `CountryConfiguration.PlatformFeeRateBp` or negative, when saved, then the command is rejected with a validation error and nothing is persisted.
- **AC-6** Given a payout batch runs for a maker with an active override, when its fee invoice is generated, then the invoice's commission total equals the sum of the affected orders' already-overridden `PlatformFeeMinor` snapshots — no separate invoice-side logic change is needed to prove this (existing T-0068a/b flow, unmodified).
- **AC-7** Given the set-override endpoint is called with no resolvable admin session, then the response is `401 auth.required` and nothing is persisted (fail-closed, per the `RefundOrder`/`UpdateCountryConfiguration` precedent).
- **AC-8** Given an override changes for a maker with existing (already-priced) orders, when those historical orders are viewed, then their `PlatformFeeMinor` snapshot is unchanged — the override only affects orders priced *after* the change.

## Technical notes

- Precedent for the admin command shape: `VerifyMaker.Command` / `DeactivateMaker.Command` (T-0034) — same `IAdminAuditableCommand` pattern, same "load maker → validate → mutate → let UoW commit" shape, no `SaveChangesAsync()` in the handler.
- `PricingService.ComputeForProductAsync` (`backend/src/Makables.Core.AppServices/Services/PricingService.cs`) is the single call site that resolves `CountryConfiguration.PlatformFeeRateBp` today; it will need to also load the `Maker` (via `IMakerRepository`, keyed off `Product.MakerId`) to resolve the effective rate before calling `OrderPricing.Compute`. Consider whether `OrderPricing.Compute`'s signature should accept an already-resolved `int platformFeeRateBp` parameter instead of the whole `CountryConfiguration`, to keep the pure-math layer decoupled from where the rate came from — this is an implementer decision, not locked here.
- New `BusinessErrorMessage` code(s) needed: something like `maker.feeOverrideExceedsCountryDefault` for AC-5's validation failure. Pick a code consistent with existing naming (`country.*`, `maker.*` prefixes already established).
- No new outbox event, no email — this is a silent admin-only change (mirrors `VerifyMaker`'s "no notification" AC).

## Files touched (expected)

- `backend/src/Makables.Core.Domain/Makers/Maker.cs` — add `FeeRateOverrideBp` property + a mutator (e.g. `SetFeeRateOverride(int? rateBp)`).
- `backend/src/Makables.Infra.Database/Configurations/MakerConfiguration.cs` — map the new nullable column.
- `backend/src/Makables.Infra.Database/Migrations/` — new migration adding `fee_rate_override_bp`.
- `backend/src/Makables.Core.AppServices/Features/Makers/SetMakerFeeOverride.cs` — new one-file feature.
- `backend/src/Makables.Core.AppServices/Services/PricingService.cs` — resolve the effective rate via the maker.
- `backend/src/Makables.Core.Domain/Orders/OrderPricing.cs` — accept/consume the resolved rate.
- `backend/src/Makables.Web.Admin/Controllers/MakersController.cs` — new endpoint (or extend existing maker admin controller).
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — new error code.
- `frontend/src/lib/i18n/cs-CZ.ts` — parallel i18n key.
- `frontend/src/lib/api-client/*` — NSwag regen (admin host).
- `docs/architecture/roles/maker.md` — note the new `FeeRateOverrideBp` field + `SetMakerFeeOverride` modifier once shipped.

## Test plan reference

`docs/test-plans/T-0140.md` (to be created by the implementer alongside unit tests for `SetMakerFeeOverride.Handler` and `PricingService`'s override-resolution branch, plus an integration test proving a priced order snapshots the overridden rate).

## Alternatives Considered (implementation — `OrderPricing.Compute` signature)

Technical notes left the `OrderPricing.Compute` signature as an implementer decision. Two shapes were considered when wiring the per-maker override resolution:

- **Option A — Pass the whole resolved `PlatformFeeRateBp` inside a NEW `CountryConfiguration`-shaped value object** (e.g. clone `config` with the field overwritten). *Rejected* — `CountryConfiguration` is an EF-tracked aggregate with a private setter and a `private CountryConfiguration()` constructor; there is no legitimate way to construct a "shadow" instance with one field patched without either exposing a public mutator on the real aggregate (which would let a caller silently rewrite the country's live config) or building a parallel DTO that duplicates every VAT/invoicing field `OrderPricing.Compute` already reads from `config`. Either path adds a maintenance surface for a single `int`.
- **Option B — Add an `int platformFeeRateBp` parameter alongside the unchanged `CountryConfiguration config` parameter.** *Chosen.* `Compute` still takes `config` (needed for `InvoicingMode` + `StandardVatRateBp` + the currency cross-check) but now takes the ALREADY-RESOLVED platform-fee rate explicitly, rather than reading `config.PlatformFeeRateBp` internally. This is the minimal-surface-area change: one new parameter, no new types, and the pure-math layer stays decoupled from *where* the rate came from (country default vs. per-maker override) — `PricingService` is the only place that knows about `Maker.FeeRateOverrideBp` at all. Every existing call site + test needed a one-line update (`cfg.PlatformFeeRateBp` passed explicitly), which also makes the coupling explicit at every call site instead of hidden inside `Compute`.
- **Option C — Push the `maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp` resolution into `CountryConfiguration` itself** (e.g. an `EffectiveFeeRateBp(Maker maker)` method on the config aggregate). *Rejected* — `CountryConfiguration` has no business relationship with `Maker`; giving it a method that takes a `Maker` parameter would be a layering smell (a per-country control-plane entity reaching into a specific maker's state) and would make `CountryConfiguration` harder to unit-test in isolation from `Maker`. The resolution logic belongs in the orchestrator (`PricingService`), which already owns loading both aggregates.

Net effect: `OrderPricing.Compute(productPrice, shippingPrice, config, platformFeeRateBp)` — `config` for VAT + currency, `platformFeeRateBp` as the caller-resolved fee rate. `PricingService.ComputeForProductAsync` loads `Maker` via `IMakerRepository` (keyed off `Product.MakerId`), computes `maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp`, and passes that value in.

## Status log

- 2026-07-07 `draft` by PM — added to the Phase 7 business-model-pivot manifest per dopady §6 work-package table.
- 2026-07-07 `draft → ready` by BA. Wrote US-admin-0018 with Given/When/Then AC + Alternatives Considered. Locked: override is a discount only (`≤` country default, never above); resolved rate is snapshotted on the order at creation time (no change to historical orders); no automatic award criteria (admin-manual is the MVP fallback per dopady §5.2, which does not block this ticket). No new open question raised — the loyalty-criteria question (§5.2) already exists in the phase-7 manifest and is correctly scoped as blocking a *future* ticket, not this one.
- 2026-07-07 `ready → in_progress → implemented` by dotnet-backend. Shipped `Maker.FeeRateOverrideBp` + `SetFeeRateOverride`, migration `AddMakerFeeRateOverride`, the `SetMakerFeeOverride` admin command (T-0034 audit shape), `MakersController.SetFeeOverride` on Web.Admin, and the `PricingService`/`OrderPricing.Compute` resolution wiring — see the Alternatives Considered addition above for the `Compute` signature call. Unit tests cover AC-1/3/4/5/7/8 (`SetMakerFeeOverrideHandlerTests`, `PricingServiceTests`, `OrderPricingTests`); an end-to-end integration test (`CreateOrderMakerFeeOverrideIntegrationTests`) proves AC-2/AC-8 via real order creation against Postgres. AC-6 needs no new code (existing T-0068a/b payout-invoice flow sums the already-overridden snapshot). NSwag admin client regenerated. Frontend admin-detail-page UI + the parallel i18n key are an explicit follow-up (out of scope for this pass per PM instruction).
