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

- One review per order.
- `Rating ∈ [1, 5]`.
- A review exists only if the corresponding order is in `Delivered`/`Completed` (validated at create time).

## Implementation pointer

`backend/src/Makables.Core.Domain/Reviews/Review.cs`.

## Related

- Roles: `order`, `customer`, `maker`
