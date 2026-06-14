---
role: Review
kind: aggregate
status: accepted
---

# Review

## Responsibility

Capture a customer's rating + comment for a delivered order, and the maker's optional reply.

## Collaborators

- **Order** (one-to-one; review can only exist for a delivered/completed order)
- **Customer** (reads: identity of the reviewer)
- **Maker** (reads: identity; denormalized rating updated on review create)

## Knows

- `OrderId` (unique — one review per order)
- `Rating` (1–5)
- `Comment` (optional, max ~1000 chars)
- `MakerReply` (optional, set later by maker)

## Does NOT know

- How the rating aggregates onto the maker (the `Maker.UpdateRatingStats` method handles that; called by the create-review handler)
- Moderation policy (post-MVP)

## Lifecycle

- **Created by:** `SubmitReview.Command` (customer action; only valid in `Delivered` or `Completed` order states)
- **Modified by:** `RespondToReview.Command` (maker action; appends or updates maker reply)
- **Persisted by:** `IReviewRepository`
- **Destroyed by:** soft delete only

## Invariants

- One ACTIVE review per order — enforced by the partial unique index `ux_reviews_order_active UNIQUE (order_id) WHERE is_active`. A soft-deleted review frees the order for a new one (self-healing).
- `Rating ∈ [1, 5]`.
- A review exists only if the corresponding order is in `Delivered`/`Completed` (`ReviewEligibility.IsReviewableState`, validated at the handler gate; no time limit, Q3).

## Per-order grain + anti-abuse (Q1, user-locked 2026-06-14)

The per-order grain IS the structural anti-abuse defence: a review must trace to one real, paid, delivered order the customer owns, so a customer cannot spray N reviews at a maker. The eligibility gate (`Delivered`/`Completed` + caller owns the order + no active review) and the partial unique index are the two halves of that defence. `ExistsForOrderAsync` is the happy-path guard; the index is the hard backstop for the concurrent-double-submit race (the loser hits 23505).

## Immutability asymmetry (Q4)

The customer's `Rating` + `Body` are set-once at `Review.Create` and are NEVER mutated — no edit/delete is surfaced (the `Auditable` soft-delete is admin-only, not surfaced at MVP). The maker's `MakerReply` is one OVERWRITABLE reply per review (≤500 chars): `AddReply` overwrites the prior reply and bumps `MakerReplyAt`, and never touches the customer's rating/body.

## Rating recompute — recompute-from-rows, self-healing (Q5)

`rating_avg` goes live on the FIRST review (no N-threshold). Each submit recomputes `AVG(rating)` over the maker's ACTIVE reviews inside the same UoW, converts to basis points (`round(avgStars * 10000)`, clamped `[0, 50000]`), and writes via `Maker.RecomputeRating` against a row-locked Maker (`GetByIdForUpdateAsync`, `SELECT ... FOR UPDATE`) so concurrent submits to the same maker serialize. Recompute-from-rows is self-healing under soft-delete; a running average would drift permanently. The just-added (not-yet-flushed) review is folded in memory so the stored bp/count include it.

## Per-audience IDOR split (ADR 0013)

`SubmitReview` is registered on `Web.Customer` only; `RespondToReview` on `Web.Maker` only — split at compile time. The IDOR shield is the WHERE-predicate in the scoped reads (`GetByIdForCustomerAsync` / `GetByIdForMakerAsync`). Cross-tenant → 404 (`OrderNotFound` / `ReviewNotFound`), never an existence leak. The maker-received-review DTO carries no customer identity (GDPR, T-0079/T-0081).

## Out of scope (deferred)

The **public review LIST** on the maker profile / catalog is deferred to **T-0050** (populates the `ICatalogQueries.MakerReviewItem` placeholder + T-0047 binding). The star NUMBERS are already public via T-0043; the review TEXT stays private to the dashboards until T-0050.

## Implementation pointer

`backend/src/Makables.Core.Domain/Reviews/Review.cs`, `ReviewEligibility.cs`, `IReviewRepository.cs`, `IReviewQueries.cs`.

## Related

- ADRs: 0013, 0014, 0023
- Roles: `order`, `customer`, `maker`
