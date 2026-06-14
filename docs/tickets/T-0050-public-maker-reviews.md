---
id: T-0050
title: GetMakerReviews public query + T-0047 profile review-list binding
status: draft
size: S-M
owner: unassigned
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0100]
blocks: []
user_stories: [US-customer-0008]
adrs: []
phase: 5
manual_steps: []
security_touching: no
layers: [domain, appservices, infra-database, web-public, frontend]
---

# T-0050 — GetMakerReviews public query + T-0047 profile review-list binding

## Context

This is a **tracked deferral**, not part of the reviews-loop bundle. It is intentionally NOT `ready` — it stays `draft` until groomed.

The reviews-loop bundle (**T-0100** — `Review` entity + `IReviewRepository` + `SubmitReview` + `RespondToReview`) produces the Review data and the maker's reply, and atomically maintains the maker's denormalized `RatingAverageBp` / `RatingCount`. But the **public maker-profile review LIST** — the latest N reviews + their maker replies rendered on the T-0047 public profile page (`/katalog/[slug]`) — is a separate read-side slice that the bundle does not cover. It was carved out to keep T-0100 (+ its two frontend submission/reply tickets T-0115 / T-0117) tightly coupled and 3-ticket-tight.

What already exists on master that this slice plugs into:

- **`MakerProfile.Reviews`** (`ICatalogQueries.cs:100`) — an `IReadOnlyList<MakerReviewItem>` field on the public profile DTO. It is a **forward-compat empty placeholder**: T-0044 populates everything *except* this list, returning an empty `Reviews` collection (see `ICatalogQueries.GetMakerBySlugAsync` docstring, lines 27-37).
- **`MakerReviewItem`** (`ICatalogQueries.cs:115`) — the placeholder review DTO (`ReviewId, RatingStars, Comment?, CreatedAt`). Its docstring explicitly names T-0050 as the missing producer. **Note:** it has no `MakerReply` field yet — adding one (or a parallel field) is in-scope here, see Open sub-questions.
- **T-0047 reviews section** — the frontend profile page already renders a reviews section *heading* + an i18n "no reviews yet" empty state (T-0047 AC-7), forward-compatible by design: "when reviews ship, only the body of the section changes." This ticket fills that body.

What is **already public and NOT in scope here**: the star NUMBERS. `RatingAverageBp` + `RatingCount` ship publicly via **T-0043** (denormalized maker stats, rendered as the star row + count on T-0047). Only the per-review LIST is deferred to this ticket — the aggregate rating is done.

This slice directly satisfies **US-customer-0008 AC-3** — "Given a maker has reviews, when the profile loads, then the latest 5 reviews are shown with rating, comment excerpt, and maker reply if present." (The "view all reviews" pagination link in AC-3 is a further follow-up — see Out of scope.)

## Scope (draft-level — to be expanded at grooming)

### Domain layer

- **`Core.Domain/Catalog/ICatalogQueries.cs`** — add a public read method, e.g.:
  ```csharp
  Task<IReadOnlyList<MakerReviewItem>> GetMakerReviewsAsync(
      string makerId,
      int take,
      CancellationToken cancellationToken);
  ```
  Returns the top `take` **active** reviews for a maker, newest-first (`CreatedAt DESC`), each carrying the maker reply when present. `take` defaults to 5 at the call site per US-customer-0008 AC-3 (5 latest).
- **`MakerReviewItem`** (`ICatalogQueries.cs:115`) — extend with the maker reply (`string? MakerReply` + reply timestamp, or a nested `MakerReplyItem?`). Shape decided at grooming. This is the one wire-contract change in the DTO.

### AppServices / infra (catalog read-side)

- Decide the integration point: either fold the review-list read **into** the existing `GetMakerBySlugAsync` projection (one round-trip, `MakerProfile.Reviews` populated inline) OR keep it a **separate** `GetMakerReviewsAsync` call composed by the profile handler. Default lean: populate `MakerProfile.Reviews` inline so the existing public endpoint `GET /api/v1/catalog/makers/{slug}` returns reviews with the profile in one call — no new endpoint, no new frontend fetch. (Grooming confirms.)
- **`Infra.Database`** catalog query impl — `AsNoTracking` projection only, top-N active reviews for the maker, newest-first, LEFT JOIN to the reply. No aggregate recompute here (T-0100 owns `RatingAverageBp`/`RatingCount`); this is pure read.
- Reviews gate: only **active** reviews (soft-delete excluded by the global `Auditable` filter); reply included only when present.

### Web.Public host

- No new endpoint if the inline approach wins — `GET /api/v1/catalog/makers/{slug}` simply starts returning a non-empty `Reviews` list. Contract shape changes (the `MakerReviewItem` reply field), so **NSwag regen is still required** (public host client).

### Frontend (T-0047 page binding)

- Bind the existing reviews section in `frontend/src/app/(public)/katalog/[slug]/` to render `MakerProfile.Reviews`: per-review rating stars, comment excerpt, `CreatedAt` (cs-CZ date), and the maker reply block when present. Replaces the T-0047 empty-state body; the heading + "no reviews yet" fallback stay for the zero-review case.
- All copy via i18n keys under `catalog.maker.reviews.*` (additive to `cs-CZ.ts`). No hardcoded Czech. Server Component — no client interactivity, no `useEffect` fetch.

### NSwag regen

The `MakerReviewItem` reply-field addition is a contract change → **NSwag regen REQUIRED in the same PR** (public host client). Per the pre-commit hook, `frontend/src/lib/api-client/` is not edited by hand.

## Open sub-questions (resolve at grooming)

- **Single-review skew display tweak (Q5 fast-follow, option b).** When a maker has exactly one review, the star aggregate is fully determined by that single rating, which can look misleadingly precise/extreme on the public profile. Q5's option (b) proposed a display-side tweak for the low-`RatingCount` case (e.g. de-emphasize or annotate the aggregate when `RatingCount == 1`). This is a **display-only** decision (no business-logic change) but it touches both the T-0043 aggregate render and this review list — flag it for the groomer to lock before implementation. Captured here so it is not lost.
- **Inline vs separate query** — confirm the `GetMakerBySlugAsync`-inline approach (default) vs a standalone `GetMakerReviewsAsync` endpoint (needed only if "view all reviews" pagination lands in the same slice).
- **`MakerReviewItem` reply shape** — flat `string? MakerReply` + timestamp vs nested `MakerReplyItem?`.
- **`take` value** — 5 per US-customer-0008 AC-3; confirm no "view all" pagination in this slice (see Out of scope).

## Out of scope (this slice)

- **Review writes** — `SubmitReview` / `RespondToReview` are T-0100. This is read-only.
- **Aggregate rating** — `RatingAverageBp` / `RatingCount` already public via T-0043. Not re-touched except for the optional Q5 single-review display tweak.
- **"View all reviews" pagination link** (US-customer-0008 AC-3 tail) — a paged review endpoint + dedicated reviews view is a further follow-up; this slice ships the latest-N inline list only. Re-evaluate at grooming.
- **Customer-facing review submission UI** — T-0115. **Maker reply UI** — T-0117.

## Definition of Ready — NOT met (why this stays draft)

- `depends_on: [T-0100]` is still `draft` — the Review entity + reply data this slice reads do not exist yet. Cannot be `ready` until T-0100 is `done`.
- Open sub-questions above (DTO reply shape, inline-vs-separate, Q5 display tweak) need grooming before AC can be written G/W/T.
- Acceptance criteria not yet authored — to be written against US-customer-0008 AC-3 at grooming.

## Status log

- 2026-06-14 created as a tracked deferral from the reviews-loop bundle grooming (public review-list slice carved out to keep that bundle 3-ticket tight-coupled). Stays `draft` / NOT ready: blocked on T-0100 (review data producer) and open sub-questions (DTO reply shape, inline-vs-separate query, Q5 single-review display tweak). Pre-existing seams this fills: `MakerProfile.Reviews` empty placeholder (`ICatalogQueries.cs:100`) + `MakerReviewItem` DTO (`:115`, names T-0050 as producer) + T-0047 reviews-section forward-compat body (AC-7).
