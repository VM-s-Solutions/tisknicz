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
- **Persisted by:** `IMakerRepository`
- **Destroyed by:** never (soft delete only via `Deactivated()`)

## Invariants

- A maker's IČO is set at registration and never changes. Re-registering with the same IČO returns the existing maker.
- A maker's snapshot of `CompanyName` / `RegisteredAddress` / `LegalForm` at registration is the source for invoicing. ARES updates do not auto-propagate; admin can trigger a re-fetch via a dedicated command.
- `BankAccount` must pass Czech format validation (`123456789/0100`).
- A maker is active for customer-facing listings only if `IsActive AND User.IsActive AND User.EmailConfirmedAt IS NOT NULL`.

## Implementation pointer

`backend/src/Makables.Core.Domain/Makers/Maker.cs`.

## Rating recompute producer (T-0100)

`RatingAverageBp` / `RatingCount` were shipped dormant by T-0043 (the catalog sort + profile DTO already read them). T-0100 wires the producer: `Maker.RecomputeRating(ratingCount, ratingAverageBp)` is a thin forward to the existing `SetCatalogStats` that keeps `TotalOrders` untouched. The `SubmitReview` handler computes `AVG(rating)` over the maker's **active** reviews (recompute-from-rows, self-healing under soft-delete — a running average would drift after any deactivation), converts to basis points (`round(avgStars * 10000)`, clamped `[0, 50000]`), and applies it against a **row-locked** Maker (`GetByIdForUpdateAsync`, `SELECT ... FOR UPDATE`) so concurrent submits to the same maker serialize. The insert + recompute commit in one UoW (ADR 0014).

## Related

- ADRs: 0004, 0010, 0012, 0013, 0014, 0018, 0023
- Stories: maker registration, maker profile update, admin verify, respond to a review
- Roles: `user`, `company-registry`, `address`, `product`, `payout-batch`, `review`
