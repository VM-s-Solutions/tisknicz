---
role: Maker
kind: aggregate
status: accepted
---

# Maker

## Responsibility

Represent a registered Czech business that produces goods for sale on the platform, snapshot at registration with the legal data needed for invoicing, and track operational state (verified, active, payout-ready).

## Collaborators

- **User** (1:1 at MVP; identity owner)
- **CompanyRegistry** (asks: look up by IČO at registration)
- **Address** (composes: registered seat + optional pickup address)
- **AddressGeocoder** (asks: geocode registered seat — non-blocking)
- **Category** (many-to-many: which categories the maker offers)
- **Product** (one-to-many: catalog entries)

## Knows

- IČO (registration number) — unique
- DIČ (VAT id) — optional; drives invoicing mode
- Company name, legal form, registered address — snapshot from ARES at registration
- Bio (max 500 chars)
- Bank account (Czech format, validated)
- Personal-pickup flag + pickup address + pickup note
- Categories offered
- Denormalized stats: rating average + count, total orders, total revenue (haléře)
- `IsVerified` (admin badge), `IsActive` (`Auditable`)
- `IsRetainedForLegal` (T-0110) — `boolean NOT NULL DEFAULT false`; `true` once this maker row has been anonymized-but-legally-retained by a GDPR erasure
- `PayoutAccountRef` (nullable) + `PayoutAccountStatus` (`NotStarted | PendingRequirements | Enabled | Disabled`) — ADR 0027, T-0142. Set once at first onboarding-link creation (ref) / only via the gateway's account-status webhook (status), never client-settable. Stripe-active countries only.
- `FeeRateOverrideBp` (nullable `int`, T-0140) — admin-set per-maker loyalty platform-fee override in basis points. `null` means "no override — use `CountryConfiguration.PlatformFeeRateBp`". Discount-only: never exceeds the maker's country's `PlatformFeeRateBp` (enforced by `SetMakerFeeOverride`'s handler, which loads both aggregates). `PricingService.ComputeForProductAsync` resolves `maker.FeeRateOverrideBp ?? config.PlatformFeeRateBp` at order-creation time — the resolved rate is snapshotted onto the order and is never re-read retroactively, so changing/clearing the override never touches historical orders.

## Does NOT know

- How orders are routed to it (the catalog query does that)
- How payouts are computed (that's `PayoutService`)
- The customer-facing rating math beyond the stored aggregate (review aggregation happens elsewhere on review creation)
- Whether ARES data is stale (cache is `CompanyRegistry`'s problem)

## Lifecycle

- **Created by:** `RegisterMaker.Command` — after user authenticates, supplies IČO, and confirms ARES-fetched data
- **Modified by:**
  - `UpdateMakerProfile.Command` — maker action (bio, pickup, bank account, categories)
  - `VerifyMaker.Command` — admin action (audited)
  - `DeactivateMaker.Command` — admin action (audited)
  - `SetMakerFeeOverride.Command` — admin action (audited, T-0140) — sets/clears `FeeRateOverrideBp`, a loyalty discount on the platform commission
  - `AnonymizeForErasure()` — invoked ONLY by the `IUserDataDeletionService` seam during a GDPR erasure of the owning user (T-0110); see below
- **Persisted by:** `IMakerRepository`
- **Destroyed by:** never (soft delete only via `Deactivated()`; under GDPR erasure the row is ANONYMIZED-AND-RETAINED, never hard-deleted — tax records reference it)

## GDPR erasure — `AnonymizeForErasure` + `IsRetainedForLegal` (T-0110)

When the owning user is erased (`DeleteUserPermanently` → `IUserDataDeletionService`, patterns §A.23), the maker is **anonymized in place, not hard-deleted** — its `IČO` + `BankAccount` are referenced by retained tax records (invoices, payout batches), so deleting the row would orphan those and violate the legal-retention duty (GDPR Art. 17(3)(b)).

`Maker.AnonymizeForErasure()` is a pure, idempotent transform: it scrubs the free-text PII (`CompanyName`, `LegalForm`, `Bio`, `PickupNote` → the `"Anonymized"` sentinel; `VatId` → null), **RETAINS** `RegistrationNumber` (IČO) + `BankAccount`, and sets `IsRetainedForLegal = true` so the row is a lawful tombstone. Order-completion / rating fields are NOT touched. A second call leaves IČO/bank intact and the flag true (idempotent). `IsRetainedForLegal` lets active-maker / customer-facing surfaces exclude erased tombstones.

## Invariants

- A maker's IČO is set at registration and never changes. Re-registering with the same IČO returns the existing maker.
- A maker's snapshot of `CompanyName` / `RegisteredAddress` / `LegalForm` at registration is the source for invoicing. ARES updates do not auto-propagate; admin can trigger a re-fetch via a dedicated command.
- `BankAccount` must pass Czech format validation (`123456789/0100`).
- A maker is active for customer-facing listings only if `IsActive AND User.IsActive AND User.EmailConfirmedAt IS NOT NULL`.
- **ADR 0027 (Stripe-active countries, T-0142):** a maker may not publish products or accept new orders unless `PayoutAccountStatus == Enabled`, in addition to the existing `IsVerified` gate — the exact publish-vs-accept boundary is a T-0142 design decision, not fixed here.

## Implementation pointer

`backend/src/Makables.Core.Domain/Makers/Maker.cs`.

## Rating recompute producer (T-0100)

`RatingAverageBp` / `RatingCount` were shipped dormant by T-0043 (the catalog sort + profile DTO already read them). T-0100 wires the producer: `Maker.RecomputeRating(ratingCount, ratingAverageBp)` is a thin forward to the existing `SetCatalogStats` that keeps `TotalOrders` untouched. The `SubmitReview` handler computes `AVG(rating)` over the maker's **active** reviews (recompute-from-rows, self-healing under soft-delete — a running average would drift after any deactivation), converts to basis points (`round(avgStars * 10000)`, clamped `[0, 50000]`), and applies it against a **row-locked** Maker (`GetByIdForUpdateAsync`, `SELECT ... FOR UPDATE`) so concurrent submits to the same maker serialize. The insert + recompute commit in one UoW (ADR 0014).

## Related

- ADRs: 0004, 0010, 0012, 0013, 0014, 0018, 0023, 0027 (amends — payout-account fields + the new publish/accept-orders gate)
- Patterns: §A.23 (GDPR erasure seam — `AnonymizeForErasure` is invoked here)
- Extension points: §14 (erasure matrix — Maker = ANONYMIZE + flag)
- Stories: maker registration, maker profile update, admin verify, respond to a review
- Roles: `user`, `company-registry`, `address`, `product`, `payout-batch`, `review`, `payout-account-provider` (new, ADR 0027)
