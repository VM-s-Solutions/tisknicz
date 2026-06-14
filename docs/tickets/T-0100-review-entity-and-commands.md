---
id: T-0100
title: Review entity + SubmitReview / RespondToReview (per-delivered-order grain)
status: ready
size: M
owner: dotnet-backend
created: 2026-06-14
updated: 2026-06-14
depends_on: [T-0043, T-0060, T-0076]
blocks: [T-0050]
user_stories: [US-customer-0015, US-maker-0014]
adrs: [0013, 0014, 0023]
phase: 4
manual_steps: []
security_touching: true
layers: [domain, appservices, infra-database, web-customer, web-maker]
---

# T-0100 — Review entity + SubmitReview / RespondToReview (per-delivered-order grain)

## Context

T-0100 ships the **write-side of customer reviews**: the `Review` aggregate, the customer `SubmitReview` command, the maker `RespondToReview` command, and the **atomic recompute of the maker's denormalized rating average**. It satisfies **US-customer-0015 — Submit a review after delivery** (AC-1 1–5 stars + optional ≤1000-char comment + atomic `rating_avg`/`rating_count` update; AC-2 second review rejected; AC-3 pre-delivery rejected) and **US-maker-0014 — Respond to a review** (AC-1 reply ≤500 chars; AC-2 cross-tenant → 404; AC-3 reply overwrites — one reply per review).

**The review grain is PER DELIVERED ORDER** (Q1, user-locked 2026-06-14). One review per order, enforced by a **partial unique index `UNIQUE (order_id) WHERE is_active`**. Eligibility = the calling customer owns an order in state `Delivered` or `Completed` with **no active review yet**. There is **no time limit** on the window (Q3): any delivered/completed order is reviewable forever until reviewed. This per-order grain IS the structural anti-abuse defense — a customer cannot spray N reviews at a maker; each review is anchored to a real, paid, delivered order they own. The eligibility gate + the partial unique index are the two halves of that defense and are the security surface of this ticket alongside the IDOR split (see §A.1, §B, Risk).

The maker's **`rating_avg` goes live immediately on the first review** (Q5) — no N-review threshold, no "needs 3 reviews to show" gate. The recompute is **recompute-from-rows, NOT a running average**: each submit runs `AVG(rating)` over the **active** reviews for that maker inside the same UoW, converts to basis points (`avgStars * 10_000`), and writes via the existing `Maker.SetCatalogStats(ratingAverageBp, ratingCount, totalOrders)` hook (T-0043). Recompute-from-rows is **self-healing under soft-delete** — when an admin later deactivates an abusive review (the soft-delete hook exists; no admin UI ships here), the next recompute naturally excludes it. A running average would drift permanently after any deactivation.

The **public review LIST is OUT OF SCOPE** — deferred to **T-0050** (public-host query populating the already-shipped `MakerReviewItem` placeholder in `ICatalogQueries` + T-0047 profile binding). What ships here is the write path + the three **dashboard** read queries (customer "my reviewable orders", customer "my submitted reviews", maker "my received reviews"). The **star NUMBERS are already public** via T-0043 (`Maker.RatingAverageBp` / `RatingCount` on the catalog + profile DTOs); this ticket makes those numbers *non-zero* by wiring the producer. The review *text* stays private to the dashboards until T-0050.

Per ADR 0013 + T-0079/T-0082 precedent, the feature surface is **split per-audience at compile time**: `SubmitReview` is registered only on `Web.Customer`; `RespondToReview` only on `Web.Maker`. A customer JWT cannot dispatch the maker command — the type isn't registered on the customer host. The IDOR shield is the WHERE-predicate baked into the scoped repository reads: `SubmitReview` loads the order via `IOrderRepository.GetByIdForCustomerAsync` (customer-scoped); `RespondToReview` loads the review via `IReviewRepository.GetByIdForMakerAsync` (maker-owns-the-reviewed-order's-maker_id predicate). Cross-tenant misses surface as **404**, never "access denied" — no existence leak.

**No outbox / email ships at MVP.** Both commands are synchronous; the "you received a review" maker notification is an explicit fast-follow (it needs an outbox event + template, out of scope here). **No admin moderation UI ships** — the `Auditable` soft-delete is the hook a later admin command (post-MVP) will pull; this ticket does not surface deactivate/flag.

## Locked design decisions

Captured per `docs/process/deliberation.md`. The user locked 5 dimensions at the 2026-06-14 deliberation (review grain; required-rating/optional-body shape; eligibility window; immutability asymmetry; live-rating-from-first-review). Story-locked dimensions (Q2/Q3/Q4) were fixed at the user-story step; the deferral of the public list to T-0050 is the bundle scope cut. PM-absorbed decisions follow from T-0043 / T-0060 / T-0079 precedents + bundle conventions.

### A. User-locked at the 2026-06-14 deliberation (non-negotiable)

1. **Review grain = PER DELIVERED ORDER** (Q1). One review per order. Partial unique index `UNIQUE (order_id) WHERE is_active`. Eligibility predicate: the caller owns an order in state ∈ `{Delivered, Completed}` AND `IReviewRepository.ExistsForOrderAsync(orderId) == false`. The per-order anchor IS the anti-abuse structural defense — a review must trace to one real delivered order the customer paid for. **Rejected:** per-maker grain (one review per customer-maker pair regardless of order count — weaker abuse anchor; a customer with one order could be perceived as reviewing "the maker in general"; harder to surface "review THIS order" CTA in the order detail); per-product grain (a maker sells many products; the order — not the product — is the delivered unit the customer experienced; product-grain fragments the rating signal and complicates the catalog `rating_avg` rollup); free-standing reviews not tied to an order (no abuse anchor at all — the whole point of the gate is the delivered order).

2. **Rating REQUIRED 1–5; body OPTIONAL ≤1000 chars** (Q2, story-locked). A review must carry a star rating; the text comment is optional and capped at 1000 chars. **Rejected:** star-only with no text ever (loses the qualitative signal other buyers want — US-customer-0015 out-of-scope line explicitly keeps the comment optional, not forbidden); text-required (raises the friction to leave a review → fewer reviews → weaker catalog signal; most buyers will rate-only and that's fine); unbounded text (DB column + abuse surface — 1000 chars is a generous paragraph and matches the dashboard render budget).

3. **Eligibility window = ANY Delivered OR Completed order, NO time limit** (Q3, story-locked). Both `Delivered` (state 4) and `Completed` (state 5) qualify; a review can be left at any time after delivery, forever, until the order is reviewed. **Rejected:** `Completed`-only (delivery is the moment the customer can judge the maker; gating on the later `Completed` transition — which is a payout-side bookkeeping step — would delay reviews for no buyer benefit); a 30/60/90-day window (an arbitrary cutoff that frustrates a customer who comes back a month later; the per-order unique index already prevents spam, so a time cap adds friction without adding protection).

4. **Customer review IMMUTABLE after submit; maker reply OVERWRITABLE** (Q4, story-locked). No edit/delete is surfaced for the customer's rating or body (the rating + body are set-once at `Create`; the soft-delete hook is admin-only and not surfaced here). The maker's reply is **one reply per review, overwritable** (≤500 chars) — `AddReply` overwrites the prior reply and bumps `MakerReplyAt`. **Rejected:** customer-editable review (an editable rating undermines the trust signal — a maker could pressure a customer to revise; immutability is the integrity stance, and a wrong review is an admin-moderation case, not a self-edit case); append-only multi-reply maker thread (the review is not a conversation — the order-message thread T-0079 is the conversation channel; a single canonical public reply per review is the right shape for a profile render); immutable maker reply (a maker should be able to fix a typo or soften a heated first reply before it's seen widely — overwrite is the pragmatic choice).

5. **`rating_avg` LIVE immediately on first review — recompute-from-rows** (Q5). No N-threshold gate. Each submit recomputes `AVG(rating)` over the maker's **active** reviews in the same UoW → basis points → `Maker.SetCatalogStats`. The Maker row is **row-locked** during the recompute (serialize concurrent submits to the same maker). **Rejected:** N-review threshold before showing a rating (a maker with one 5-star review still earns that signal; hiding it penalizes new makers and confuses buyers who see `RatingCount = 1` but `rating_avg = 0`); running average maintained incrementally (`newAvg = (oldAvg*oldCount + rating) / (oldCount+1)`) (drifts permanently after any soft-delete — recompute-from-rows is self-healing and the AVG over a handful-to-hundreds of rows is cheap with the `maker_id` index); a periodic batch recompute job (stale ratings between runs + a Function for a one-query aggregate is over-engineered per ADR 0020).

### B. ADR-locked (no relitigation)

- **ADR 0013 (per-audience JWT enforcement + scoped repo split).** `SubmitReview` runs only under `Web.Customer`; `RespondToReview` only under `Web.Maker`. The type isn't cross-registered. The IDOR shield is the WHERE-predicate in the scoped reads: `IOrderRepository.GetByIdForCustomerAsync(orderId, customerUserId)` (customer cannot review another customer's order — the SQL never selects the row) and `IReviewRepository.GetByIdForMakerAsync(reviewId, makerId)` (maker cannot reply to a review whose order's `maker_id` ≠ theirs). Cross-tenant → **404**, no existence leak.
- **ADR 0014 (UoW pipeline).** `SubmitReview` + `RespondToReview` are commands → `UnitOfWorkPipelineBehavior` commits per request. Handlers NEVER call `SaveChangesAsync()`. The `Review` insert + the `Maker` recompute happen in **one DbContext / one UoW commit** — they cannot half-apply. `ValidationPipelineBehavior` runs on both (rating range, body length, reply length).
- **ADR 0023 (read-side queries split from write-side repositories + paging NFRs).** New `IReviewQueries` (read-side, AsNoTracking projection-only) co-exists with the new `IReviewRepository` (write-side: AddAsync + GetByIdForMakerAsync + ExistsForOrderAsync). The maker-received-reviews query is paginated `20/page` (cap, default 20). Index `(maker_id, created_on DESC)` on `reviews` backs that list; the partial unique index on `order_id` backs the eligibility `ExistsForOrderAsync`.

### C. PM-absorbed (no user input needed)

1. **New entity `Review : Auditable`** in `Core.Domain/Reviews/Review.cs` (mirrors the `OrderMessage` / `Dispute` child-entity precedent):
   - `Id: string` (PK, ULID per project convention).
   - `OrderId: string` (FK → Order; the per-order anchor; partial-unique `WHERE is_active`).
   - `MakerId: string` (FK → Maker; **denormalized** off the order so the `(maker_id, created_on DESC)` list query + the recompute AVG don't JOIN through Order).
   - `CustomerUserId: string` (FK → User; the reviewing user — audit-trail identity).
   - `Rating: short` (SMALLINT, 1–5; validated at `Create` + in the Validator).
   - `Body: string?` (VARCHAR(1000) NULL; trimmed at `Create`; null/empty stays null).
   - `MakerReply: string?` (VARCHAR(500) NULL; null until the maker replies; overwritten by `AddReply`).
   - `MakerReplyAt: DateTimeOffset?` (nullable; stamped/refreshed by `AddReply`).
   - `CountryCode` / `IsActive` / `CreatedBy/On` / `UpdatedBy/On` / `DeactivatedBy/On` inherited from `Auditable`. Soft-delete via `IsActive` / `DeactivatedAt`; deactivated rows excluded by the global Auditable query filter AND by the partial unique index (so a deactivated review frees the order for a new one — self-healing).
   - `const int MaxBodyLength = 1000;` `const int MaxReplyLength = 500;` `const short MinRating = 1;` `const short MaxRating = 5;`.

2. **`Review.Create(id, orderId, makerId, customerUserId, rating, body, countryCode)`** factory (mirrors `OrderMessage.Create` / `Dispute.Open`): validates `id`/`orderId`/`makerId`/`customerUserId` non-empty, `rating ∈ [1,5]` (else `ArgumentOutOfRangeException`), trims `body` (null/whitespace → null) and rejects `> 1000` chars, validates `countryCode`. `MakerReply`/`MakerReplyAt` start null. Programmer-error tail throws `ArgumentException`; user-input validation ALSO runs in the `SubmitReview` Validator so the range/length paths surface as typed `ReviewRatingOutOfRange` / `ReviewBodyTooLong` before reaching the aggregate.

3. **`Review.AddReply(reply, at)`** — overwrites the maker reply (one reply per review). Trims `reply`, rejects empty/whitespace (`ArgumentException`) and `> 500` chars (`ArgumentException`; the Validator surfaces the typed `ReviewReplyTooLong` first), sets `MakerReply = trimmed`, `MakerReplyAt = at`. Idempotent in shape — a second call simply overwrites. The customer's `Rating`/`Body` are NEVER mutated by this method (review body immutability, Q4).

4. **`Maker.RecomputeRating(int ratingCount, int ratingAverageBp)`** domain method on the existing `Maker` entity — thin wrapper that forwards to the existing `SetCatalogStats(ratingAverageBp, ratingCount, TotalOrders)` keeping `TotalOrders` untouched (the order-completion flow owns that field). The AVG aggregation itself is computed by the repository against the DB (see §C.6) and passed in; the domain method just applies the validated values via the existing 0..50000-bp guard. **Naming note:** if a `RecomputeRating` wrapper reads cleaner inline, the handler MAY call `SetCatalogStats` directly — implementer's call — but the recompute MUST keep `TotalOrders` unchanged. The basis-point conversion is `(int)Math.Round(avgStars * 10_000, MidpointRounding.AwayFromZero)` clamped to `[0, 50_000]`.

5. **`IReviewRepository`** (write-side, ADR 0013-scoped) in `Core.Domain/Reviews/IReviewRepository.cs`:
   - `Task AddAsync(Review review, CancellationToken ct);`
   - `Task<Review?> GetByIdForMakerAsync(string reviewId, string makerId, CancellationToken ct);` — `Where(r => r.Id == reviewId && r.MakerId == makerId)`. The predicate IS the maker IDOR shield; null → 404 at the handler.
   - `Task<bool> ExistsForOrderAsync(string orderId, CancellationToken ct);` — `AnyAsync(r => r.OrderId == orderId)` over active rows (the global filter excludes soft-deleted). Backs the eligibility second-leg + races the partial unique index.
   - `Task<(int Count, double AverageStars)> GetMakerRatingAggregateAsync(string makerId, CancellationToken ct);` — `COUNT(*)` + `AVG(rating)` over the maker's active reviews, computed in SQL. Feeds the recompute. (If `Count == 0`, `AverageStars` returns 0 — defensive; the recompute then stores `0` bp / `0` count.)

6. **`IReviewQueries`** (read-side, ADR 0023, AsNoTracking projection-only) in `Core.Domain/Reviews/IReviewQueries.cs`:
   - `Task<IReadOnlyList<ReviewableOrderDto>> GetCustomerReviewableOrdersAsync(string customerUserId, CancellationToken ct);` — orders owned by the customer in state ∈ `{Delivered, Completed}` with NO active review (left-anti-join `reviews ON order_id`). Powers the "leave a review" CTA list. (Small set per customer — no paging.)
   - `Task<IReadOnlyList<SubmittedReviewDto>> GetCustomerSubmittedReviewsAsync(string customerUserId, CancellationToken ct);` — the customer's own submitted reviews (rating + body + maker reply). (Small set — no paging.)
   - `Task<PagedData<MakerReceivedReviewDto>> GetMakerReceivedReviewsPagedAsync(string makerId, int page, int pageSize, CancellationToken ct);` — the maker-dashboard "reviews about me" list. Paginated `20/page`. Sort `CreatedOn DESC`, tiebreak `Id DESC`. Backed by the `(maker_id, created_on DESC)` index.
   - All three bake the audience predicate (`customer_user_id` / `maker_id`) into the EF `Where` — the IDOR shield at the read layer too.

7. **DTOs** (NEW, in `Core.AppServices/Features/Reviews/DTOs/`):
   - `ReviewableOrderDto(string OrderId, string OrderNumber, string MakerId, string MakerCompanyName, DateTimeOffset DeliveredAt)`.
   - `SubmittedReviewDto(string ReviewId, string OrderId, string MakerId, string MakerCompanyName, short Rating, string? Body, string? MakerReply, DateTimeOffset CreatedAt)`.
   - `MakerReceivedReviewDto(string ReviewId, string OrderId, short Rating, string? Body, string? MakerReply, DateTimeOffset? MakerReplyAt, DateTimeOffset CreatedAt)`. Customer identity is NOT exposed (GDPR data-minimization consistent with T-0079/T-0081 — the maker sees the review, not the customer's email/name).

8. **`SubmitReview.cs`** (NEW one-file feature, `Web.Customer`-only) — `Command(string OrderId, short Rating, string? Body)` + Validator + Handler. Handler steps:
   1. Resolve `customerUserId` from `IUserSessionProvider.RequireUserId()`.
   2. Load Order via `IOrderRepository.GetByIdForCustomerAsync(orderId, customerUserId, ct)`. Null → `BusinessResult.Failure(OrderNotFound)` (reused; no existence leak).
   3. Eligibility gate: `order.State` ∈ `{Delivered, Completed}` else `BusinessResult.Failure(ReviewOrderNotDelivered)`.
   4. `if (await reviews.ExistsForOrderAsync(orderId, ct))` → `BusinessResult.Failure(ReviewAlreadyExists)`.
   5. `Review.Create(Ulid.NewUlid(), orderId, order.MakerId, customerUserId, command.Rating, command.Body, order.CountryCode)`.
   6. `await reviews.AddAsync(review, ct);`
   7. Recompute: `var (count, avg) = await reviews.GetMakerRatingAggregateAsync(order.MakerId, ct);` — NOTE the aggregate must count the just-added (not-yet-saved) review; if the repo reads pre-SaveChanges, the handler computes `count = existing + 1` and folds the new rating into the avg in memory, OR the aggregate is taken AFTER the UoW flush within the same transaction — **implementer's call; the integration test `RatingRecomputeCorrectnessTests` pins the arithmetic** so whichever path is chosen, the stored bp must equal the AVG over all active reviews including the new one. Then load the Maker **row-locked** (`GetByIdForUpdateAsync` or `FOR UPDATE` semantics) and `maker.RecomputeRating(count, bp)`.
   8. Return `BusinessResult.Success(new SubmitReviewResponse(review.Id, review.Rating, review.CreatedOn))`.
   - Validator: `Rating.InclusiveBetween(1, 5).WithErrorCode(ReviewRatingOutOfRange)`; `Body` (when present) `.MaximumLength(1000).WithErrorCode(ReviewBodyTooLong)`; `OrderId.NotEmpty()`.

9. **`RespondToReview.cs`** (NEW one-file feature, `Web.Maker`-only) — `Command(string ReviewId, string Reply)` + Validator + Handler. Handler steps:
   1. Resolve `makerId` from `IMakerRepository.GetByUserIdAsync(sessionUserId)` (existing maker-scope resolution).
   2. Load Review via `IReviewRepository.GetByIdForMakerAsync(reviewId, makerId, ct)`. Null → `BusinessResult.Failure(ReviewNotFound)` (maker IDOR shield; cross-tenant → 404).
   3. `review.AddReply(command.Reply, clock.UtcNow);` — overwrites prior reply.
   4. Return `BusinessResult.Success(new RespondToReviewResponse(review.Id, review.MakerReply!, review.MakerReplyAt!.Value))`.
   - Validator: `Reply.NotEmpty().WithErrorCode(ReviewReplyEmpty).MaximumLength(500).WithErrorCode(ReviewReplyTooLong)`; `ReviewId.NotEmpty()`. (`ReviewReplyEmpty` reuses the empty-string guard; see §C.13 note.)

10. **Globally-unique response naming** (NSwag CI fix per PR #38): `SubmitReviewResponse`, `RespondToReviewResponse`, plus the query responses `GetCustomerReviewableOrdersResponse`, `GetCustomerSubmittedReviewsResponse`, `GetMakerReceivedReviewsResponse`. Each a sealed record wrapper.

11. **Controllers** — judge cleanest routes against existing conventions (T-0079/T-0082 nest order-children under `/orders/{orderId}/...`; reviews are addressed standalone once created):
    - `Web.Customer`: `POST /api/v1/orders/{orderId}/review` → `SubmitReview.Command`. `GET /api/v1/reviews/reviewable-orders` → `GetCustomerReviewableOrders.Query`. `GET /api/v1/reviews/mine` → `GetCustomerSubmittedReviews.Query`.
    - `Web.Maker`: `POST /api/v1/reviews/{reviewId}/reply` → `RespondToReview.Command`. `GET /api/v1/reviews?page=1&pageSize=20` → `GetMakerReceivedReviews.Query`.
    - All `[Authorize]` with the host audience. One-liner dispatch via `mediator.Send`. `[ProducesResponseType]` for NSwag.

12. **EF configuration + migration** `Add_Review_table`:
    - New table `reviews`: PK + FK `order_id → orders.id` + FK `maker_id → makers.id` + FK `customer_user_id → users.id` + `rating SMALLINT NOT NULL` + `body VARCHAR(1000) NULL` + `maker_reply VARCHAR(500) NULL` + `maker_reply_at TIMESTAMPTZ NULL` + the `Auditable` columns.
    - **Partial unique index** `UNIQUE (order_id) WHERE is_active` (the per-order grain enforcement + the soft-delete-frees-the-slot self-healing).
    - Index `(maker_id, created_on DESC)` for the maker-received-reviews list + the recompute aggregate scan.
    - Backfill: none — greenfield.

13. **BusinessErrorMessage codes** to ADD (dotted-key convention, cs-CZ parallel keys):
    - `ReviewAlreadyExists = "review.alreadyExists"` (US-customer-0015 AC-2).
    - `ReviewOrderNotDelivered = "review.orderNotDelivered"` (US-customer-0015 AC-3).
    - `ReviewRatingOutOfRange = "review.ratingOutOfRange"` (Validator).
    - `ReviewBodyTooLong = "review.bodyTooLong"` (Validator).
    - `ReviewReplyTooLong = "review.replyTooLong"` (Validator).
    - `ReviewNotFound = "review.notFound"` — the maker-side cross-tenant / missing-review 404 path. `OrderNotFound` is REUSED for the customer-side cross-tenant order miss (no new code). `ReviewReplyEmpty` reuses the existing generic empty-field validation message if one exists; otherwise add `"review.replyEmpty"` — implementer confirms against `BusinessErrorMessage`.

14. **cs-CZ i18n keys** — one per new error code, parallel to `BusinessErrorMessage` (CLAUDE.md i18n rule). Suggested copy: `review.alreadyExists` → "K této objednávce už recenze existuje."; `review.orderNotDelivered` → "Recenzi lze přidat až po doručení objednávky."; `review.ratingOutOfRange` → "Hodnocení musí být 1 až 5 hvězdiček."; `review.bodyTooLong` → "Text recenze může mít nejvýše 1000 znaků."; `review.replyTooLong` → "Odpověď může mít nejvýše 500 znaků."; `review.notFound` → "Recenze nebyla nalezena.".

15. **NSwag regen scope:** BOTH customer + maker hosts (2 commands + 3 queries across the two hosts). Public + Admin hosts untouched — the public review LIST is T-0050, NOT this ticket; the `MakerReviewItem` placeholder in `ICatalogQueries` stays an empty list here.

## Scope

### Domain layer

- **`Core.Domain/Reviews/Review.cs`** — NEW entity per §C.1. `Auditable` base. `Create` factory §C.2 + `AddReply` §C.3. `MaxBodyLength`/`MaxReplyLength`/`MinRating`/`MaxRating` constants.
- **`Core.Domain/Reviews/IReviewRepository.cs`** — NEW write-side interface per §C.5.
- **`Core.Domain/Reviews/IReviewQueries.cs`** — NEW read-side interface per §C.6.
- **`Core.Domain/Makers/Maker.cs`** — MODIFY: add `RecomputeRating(int ratingCount, int ratingAverageBp)` per §C.4 (thin forward to the existing `SetCatalogStats`, `TotalOrders` untouched). Do NOT alter the existing `RatingAverageBp`/`RatingCount`/`TotalOrders` columns or `SetCatalogStats` (T-0043 surfaces — verified present).
- **`Core.Domain/Common/BusinessErrorMessage.cs`** — MODIFY: add the 5 new codes (+ optional `ReviewReplyEmpty`) per §C.13.

### AppServices layer

- **`Core.AppServices/Features/Reviews/DTOs/ReviewableOrderDto.cs`**, **`SubmittedReviewDto.cs`**, **`MakerReceivedReviewDto.cs`** — NEW per §C.7.
- **`Core.AppServices/Features/Reviews/SubmitReview.cs`** — NEW one-file feature per §C.8 (`Command`/Validator/Handler/`SubmitReviewResponse`).
- **`Core.AppServices/Features/Reviews/RespondToReview.cs`** — NEW one-file feature per §C.9.
- **`Core.AppServices/Features/Reviews/GetCustomerReviewableOrders.cs`** — NEW query feature. Calls `IReviewQueries.GetCustomerReviewableOrdersAsync`. Response wraps `IReadOnlyList<ReviewableOrderDto>`.
- **`Core.AppServices/Features/Reviews/GetCustomerSubmittedReviews.cs`** — NEW query feature.
- **`Core.AppServices/Features/Reviews/GetMakerReceivedReviews.cs`** — NEW query feature. `Query(int Page=1, int PageSize=20)` + Validator (`Page >= 1`, `PageSize ∈ [1,20]`). Response wraps `PagedData<MakerReceivedReviewDto>`.

### Infrastructure / Database layer

- **`Infra.Database/Reviews/ReviewRepository.cs`** — NEW write-side impl of `IReviewRepository`. `GetMakerRatingAggregateAsync` runs `COUNT` + `AVG(rating)` in SQL over active rows; `ExistsForOrderAsync` is `AnyAsync`.
- **`Infra.Database/Reviews/ReviewQueries.cs`** — NEW read-side impl of `IReviewQueries`. AsNoTracking projections; the paged maker list does CountAsync + Skip/Take per the bundle's paged-list shape (T-0080 precedent).
- **`Infra.Database/Configurations/ReviewConfiguration.cs`** — NEW EF config. Partial unique index `(order_id) WHERE is_active`; `(maker_id, created_on DESC)` index; column lengths; FK relationships.
- **`Infra.Database/Migrations/<ts>_AddReviewTable.cs`** — NEW migration per §C.12.
- **`Config/Extensions/AddMakablesInfrastructure.cs`** — register `IReviewRepository → ReviewRepository` + `IReviewQueries → ReviewQueries` (scoped).

### Web.Customer host

- **`Web.Customer/Controllers/ReviewsController.cs`** (NEW) — `POST /api/v1/orders/{orderId}/review`, `GET /api/v1/reviews/reviewable-orders`, `GET /api/v1/reviews/mine`. `[Authorize]` customer audience. (Route granularity is the implementer's call vs nesting on the existing OrdersController — §C.11.)

### Web.Maker host

- **`Web.Maker/Controllers/ReviewsController.cs`** (NEW) — `POST /api/v1/reviews/{reviewId}/reply`, `GET /api/v1/reviews?page=1&pageSize=20`. `[Authorize]` maker audience.

### Tests

#### Pure-logic / domain tests (TDD red→green; commit FIRST per `## Commits hint`)

- **`Tests/Domain/Reviews/ReviewCreateTests.cs`** (NEW, ~4 unit): rating below 1 throws; rating above 5 throws; body > 1000 throws; happy-path trims body + leaves `MakerReply` null + accepts null body.
- **`Tests/Domain/Reviews/ReviewAddReplyTests.cs`** (NEW, ~3 unit): reply > 500 throws; second `AddReply` OVERWRITES the first (one reply per review) + bumps `MakerReplyAt`; `AddReply` does NOT mutate `Rating`/`Body` (immutability of the customer review).
- **`Tests/Domain/Makers/MakerRecomputeRatingTests.cs`** (NEW, ~3 unit): `RecomputeRating` stores the bp value + count via `SetCatalogStats`; `TotalOrders` is left untouched; out-of-range bp (>50000) throws (delegated guard). Plus an **eligibility-predicate** test pinning `State ∈ {Delivered, Completed}` accepts and any earlier state rejects (where the predicate lives — handler-extracted helper or inline; pin it as a pure check).

#### Handler tests (NSubstitute mocks; ~5 unit)

- **`Tests/AppServices/Features/Reviews/SubmitReviewHandlerTests.cs`** (NEW, ~3): happy-path creates Review + calls recompute; pre-delivery order → `ReviewOrderNotDelivered`; existing review → `ReviewAlreadyExists`; cross-tenant order (repo returns null) → `OrderNotFound`. (Validator carve-outs — rating out of range, body too long — covered inline.)
- **`Tests/AppServices/Features/Reviews/RespondToReviewHandlerTests.cs`** (NEW, ~2): happy-path sets `MakerReply`; cross-tenant review (repo returns null) → `ReviewNotFound`; reply overwrite path.

#### Integration tests (Testcontainers Postgres + WebApplicationFactory; ~5)

- **`IntegrationTests/Reviews/SubmitReviewEndToEndTests.cs`** — customer POSTs a review to their `Delivered` order → 200 `{ reviewId, rating, createdAt }`; a `reviews` row exists; the `makers` row's `rating_count == 1` and `rating_average_bp` reflects the single rating (e.g. 4 stars → 40000 bp).
- **`IntegrationTests/Reviews/RatingRecomputeCorrectnessTests.cs`** — seed 3 reviews (5, 4, 3 stars) across 3 delivered orders for one maker → `rating_count == 3`, `rating_average_bp == 40000` (AVG 4.0). Soft-deactivate one review row, submit a 4th → the recompute EXCLUDES the deactivated row (self-healing; pins recompute-from-rows, not running-avg).
- **`IntegrationTests/Reviews/ReviewPerOrderUniquenessTests.cs`** — customer submits a review, then submits a SECOND review to the same order → 2nd returns the `ReviewAlreadyExists` business error; the partial unique index holds at the DB level for a concurrent-double-submit race (the 2nd transaction loses).
- **`IntegrationTests/Reviews/SubmitReviewCrossTenantIsolationTests.cs`** — customer A submits a review to customer B's delivered order via the customer host → 404 `OrderNotFound`. Confirms the WHERE-predicate IDOR shield at the SQL level.
- **`IntegrationTests/Reviews/RespondToReviewCrossTenantAndOverwriteTests.cs`** — maker A replies to a review on maker B's order → 404 `ReviewNotFound`. Then maker B replies twice → the 2nd reply overwrites (one `maker_reply` value; `maker_reply_at` bumped).

### Docs

- **`docs/architecture/roles/review.md`** — NEW or update: the per-order grain + partial unique index; the recompute-from-rows self-healing semantics; the per-audience compile-time IDOR split; the immutable-review / overwritable-reply asymmetry; the explicit T-0050 deferral of the public list.
- **`docs/architecture/roles/maker.md`** — note the new `RecomputeRating` producer wiring the previously-dormant `RatingAverageBp`/`RatingCount` (T-0043) fields.
- **`docs/tickets/INDEX.md`** — PM flips T-0100 to `**done**` post-merge.

### NSwag regen

2 commands + 3 queries across customer + maker hosts → **NSwag regen REQUIRED in the same PR** for BOTH hosts. Per the T-0013 pre-commit hook, `frontend/src/lib/api-client/` cannot be edited manually — `npm run generate:api` produces the diff. Public + Admin hosts untouched.

## Alternatives Considered

- **Option A — Per-maker review grain.** *Rejected per A.1* — weaker abuse anchor; loses the "review THIS delivered order" CTA; harder to reason about when a customer has many orders with one maker.
- **Option B — Per-product review grain.** *Rejected per A.1* — the order, not the product, is the delivered unit the customer experienced; product-grain fragments the catalog rating signal.
- **Option C — Free-standing reviews not tied to an order.** *Rejected per A.1* — no abuse anchor; the delivered-order gate is the whole structural defense.
- **Option D — `Completed`-only eligibility (exclude `Delivered`).** *Rejected per A.3* — delivery is when the customer can judge; `Completed` is a payout-bookkeeping step; gating there delays reviews for no buyer benefit.
- **Option E — Time-limited review window (30/60/90 days).** *Rejected per A.3* — arbitrary cutoff frustrates late reviewers; the per-order unique index already prevents spam, so a time cap adds friction without protection.
- **Option F — Customer-editable review (edit/delete surfaced).** *Rejected per A.4* — an editable rating undermines the trust signal and invites maker pressure; a wrong review is an admin-moderation case, not self-edit.
- **Option G — Immutable maker reply.** *Rejected per A.4* — a maker should be able to fix a typo or soften a heated first reply; overwrite (one reply per review) is the pragmatic, profile-correct shape.
- **Option H — Multi-reply maker thread on the review.** *Rejected per A.4* — the review is not a conversation; the order-message thread (T-0079) is the conversation channel; one canonical public reply renders cleanly on a profile.
- **Option I — N-review threshold before `rating_avg` shows.** *Rejected per A.5* — penalizes new makers; a single 5-star review is a real earned signal; hiding it confuses buyers who see `RatingCount = 1` but a zeroed average.
- **Option J — Running/incremental average (no recompute-from-rows).** *Rejected per A.5* — drifts permanently after any soft-delete; recompute-from-rows is self-healing and the AVG over the `maker_id`-indexed rows is cheap.
- **Option K — Periodic batch recompute Azure Function.** *Rejected per A.5 + ADR 0020* — stale ratings between runs; a Function for a one-query aggregate that fits in the submit UoW is over-engineered.
- **Option L — Ship the public review LIST in this ticket.** *Rejected — bundle scope cut* — the public list is T-0050 (public-host query populating the `MakerReviewItem` placeholder + T-0047 profile binding). This ticket ships the write path + dashboard reads; the star NUMBERS are already public via T-0043.
- **Option M — Conditional `if (audience == X)` branch inside one shared review handler.** *Rejected per ADR 0013 + T-0079/T-0082 precedent* — runtime authorization branching is the wrong shield; the per-audience compile-time split + the WHERE-predicate scoped reads are the standard.
- **Option N — Outbox "you received a review" email at MVP.** *Rejected — fast-follow* — keeps this ticket synchronous + tight; the maker-notification email needs an outbox event + template and is explicitly a follow-up, not a blocker for the write path.
- **Option O — Admin moderation UI (deactivate/flag) in this ticket.** *Rejected — out of scope* — the `Auditable` soft-delete hook exists for a later admin command; surfacing the moderation UI is post-MVP. Recompute-from-rows already makes a future deactivate self-healing.

## Out of scope

- **Public review LIST on the maker profile / catalog.** Deferred to **T-0050** (public-host query + `ICatalogQueries.MakerReviewItem` population + T-0047 profile binding). The placeholder stays an empty list here.
- **"You received a review" maker email.** Fast-follow per Option N. No outbox event / template ships here.
- **Admin moderation UI** (deactivate / flag a review). Per Option O — the soft-delete hook exists; no command/UI surfaced.
- **Customer edit / delete of a submitted review.** Immutable per A.4 / Option F.
- **Multi-reply maker thread on a review.** One overwritable reply per A.4 / Option H.
- **Reviewer identity exposure to the maker.** GDPR data-minimization — the maker-received-review DTO carries no customer email/name (consistent with T-0079/T-0081).
- **Verified-purchase badge / weighting, helpfulness votes, review sorting by rating.** Post-MVP catalog features.
- **Frontend review UI.** The customer "leave a review" form + the maker dashboard reply UI + the public profile render are separate frontend tickets. T-0100 ships the backend only.

## Acceptance criteria

- **AC-1** Given an order in state `Delivered` (4) owned by the calling customer with no active review, when the customer `POST`s `/api/v1/orders/{orderId}/review` with `rating ∈ [1,5]` and an optional `body` (≤1000 chars), then the response is `200 OK` with `{ reviewId, rating, createdAt }` and a `reviews` row exists with that `order_id`, `maker_id` (denormalized off the order), `customer_user_id`, and rating. Same accepted for state `Completed` (5).
- **AC-2** Given the same submit succeeds, when the maker's row is inspected, then `makers.rating_count` and `makers.rating_average_bp` are updated **atomically in the same transaction** — `rating_count` equals the count of active reviews for that maker and `rating_average_bp == round(AVG(rating) * 10000)` (e.g. one 4-star review → `rating_count = 1`, `rating_average_bp = 40000`).
- **AC-3** Given an active review already exists for the order, when the customer submits a second review for the same order, then the response is the `ReviewAlreadyExists` (`review.alreadyExists`) business error; no second `reviews` row is created.
- **AC-4** Given a concurrent double-submit race for the same order, when both transactions commit, then the partial unique index `UNIQUE (order_id) WHERE is_active` lets exactly one win; the loser surfaces a conflict (no duplicate active review).
- **AC-5** Given the calling customer's order is in a state earlier than `Delivered` (e.g. `Paid`/`Accepted`/`Shipped`), when a review is submitted, then the response is the `ReviewOrderNotDelivered` (`review.orderNotDelivered`) business error; no `reviews` row is created.
- **AC-6** Given a submit with `rating = 0` or `rating = 6`, when posted, then the response is `400` with error code `ReviewRatingOutOfRange`. Given a `body` of 1001 chars, then `400` with `ReviewBodyTooLong`.
- **AC-7** Given customer A `POST`s a review to customer B's delivered order (cross-tenant probe), when dispatched, then the response is `404 OrderNotFound` — no existence leak. The IDOR shield is the WHERE predicate in `GetByIdForCustomerAsync`.
- **AC-8** Given the maker who owns the reviewed order's `maker_id` `POST`s `/api/v1/reviews/{reviewId}/reply` with a reply ≤500 chars, then the response is `200 OK`; `reviews.maker_reply` is set and `reviews.maker_reply_at` is stamped.
- **AC-9** Given maker A replies to a review whose order's `maker_id` ≠ A (cross-tenant), when dispatched, then the response is `404 ReviewNotFound` — no existence leak. The IDOR shield is the maker-owns predicate in `GetByIdForMakerAsync`.
- **AC-10** Given a maker has already replied to a review, when they submit a new reply, then the new reply OVERWRITES the prior one (one `maker_reply` value persists; `maker_reply_at` bumped) and the customer's `rating`/`body` are unchanged. A reply of 501 chars → `400 ReviewReplyTooLong`.
- **AC-11** Given an admin soft-deactivates one of a maker's reviews and a new review is then submitted, when the recompute runs, then `rating_average_bp` and `rating_count` EXCLUDE the deactivated row (recompute-from-rows is self-healing — confirms it is NOT a running average). The deactivated order also becomes reviewable again (partial unique index frees the slot).
- **AC-12** Given the EF migration runs, when inspected, then the `reviews` table exists with PK + FKs to `orders.id` / `makers.id` / `users.id`, `rating SMALLINT NOT NULL`, `body VARCHAR(1000) NULL`, `maker_reply VARCHAR(500) NULL`, `maker_reply_at TIMESTAMPTZ NULL`, the `Auditable` columns, a **partial unique index `(order_id) WHERE is_active`**, and an index `(maker_id, created_on DESC)`. Build clean; unit tests baseline + ~10 new (domain) + ~5 new (handlers); integration tests baseline + ~5 new (submit e2e, recompute correctness incl. soft-delete, per-order uniqueness, both cross-tenant IDOR paths, reply overwrite). `node scripts/check-consistency.mjs` exit 0. NSwag regen committed for BOTH customer + maker hosts.

## Risk / mitigation

- **Rating drift between `rating_count`/`rating_average_bp` and the actual review rows** (a missed recompute, or a partial failure leaving a Review inserted but the Maker stat stale). **Mitigation:** the Review insert + the Maker recompute commit in ONE UoW (ADR 0014) — they cannot half-apply. The recompute is recompute-from-rows (AVG over active rows), so even if a historical drift existed, the next submit fully heals it. `RatingRecomputeCorrectnessTests` pins the arithmetic including the soft-delete-exclusion case.
- **Concurrent submits to the same maker racing the recompute** (two customers review maker M at the same instant; both read the aggregate, both write — last-writer-wins could lose one count). **Mitigation:** the Maker row is **row-locked** for the recompute (§A.5) so the two transactions serialize; each recompute reads the committed row set. The partial unique index is on `order_id` (different orders), so the two submits don't collide there — the lock is purely to serialize the stat write.
- **Concurrent double-submit to the SAME order** (a customer double-clicks). **Mitigation:** the `ExistsForOrderAsync` check is the happy-path guard; the partial unique index `(order_id) WHERE is_active` is the hard backstop — the second transaction violates the constraint and fails. AC-4 covers it.
- **Review IDOR on either party** (customer reviews someone else's order; maker replies to someone else's review). **Mitigation:** per-audience compile-time command split + WHERE-predicate scoped reads (`GetByIdForCustomerAsync` / `GetByIdForMakerAsync`). Cross-tenant → 404, no existence leak. AC-7 + AC-9 cover both directions at the SQL level.
- **Aggregate counts the just-added review or not** (off-by-one in the recompute depending on read-before/after-flush). **Mitigation:** the handler MUST ensure the stored bp/count include the new review (§C.8 step 7 leaves the read-position to the implementer but the integration test `RatingRecomputeCorrectnessTests` pins the resulting stored values, so the off-by-one cannot ship silently).
- **basis-point rounding** (AVG 4.5 stars → 45000 bp; AVG 3.333… → 33333 bp). **Mitigation:** `round(avgStars * 10000, AwayFromFromZero)` clamped to `[0, 50000]`; the existing `SetCatalogStats` guard rejects out-of-range; `MakerRecomputeRatingTests` pins representative values.

## Test plan reference

Inline plan covers ~10 unit (domain) + ~5 unit (handlers) + ~5 integration. A separate `docs/test-plans/T-0100.md` is reserved only if post-merge regression fixtures grow it.

## Files touched (expected)

### New
- `backend/src/Makables.Core.Domain/Reviews/Review.cs`
- `backend/src/Makables.Core.Domain/Reviews/IReviewRepository.cs`
- `backend/src/Makables.Core.Domain/Reviews/IReviewQueries.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/DTOs/ReviewableOrderDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/DTOs/SubmittedReviewDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/DTOs/MakerReceivedReviewDto.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/SubmitReview.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/RespondToReview.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/GetCustomerReviewableOrders.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/GetCustomerSubmittedReviews.cs`
- `backend/src/Makables.Core.AppServices/Features/Reviews/GetMakerReceivedReviews.cs`
- `backend/src/Makables.Infra.Database/Reviews/ReviewRepository.cs`
- `backend/src/Makables.Infra.Database/Reviews/ReviewQueries.cs`
- `backend/src/Makables.Infra.Database/Configurations/ReviewConfiguration.cs`
- `backend/src/Makables.Infra.Database/Migrations/<ts>_AddReviewTable.cs`
- `backend/src/Makables.Web.Customer/Controllers/ReviewsController.cs`
- `backend/src/Makables.Web.Maker/Controllers/ReviewsController.cs`
- `backend/src/Makables.Tests/Domain/Reviews/ReviewCreateTests.cs`
- `backend/src/Makables.Tests/Domain/Reviews/ReviewAddReplyTests.cs`
- `backend/src/Makables.Tests/Domain/Makers/MakerRecomputeRatingTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Reviews/SubmitReviewHandlerTests.cs`
- `backend/src/Makables.Tests/AppServices/Features/Reviews/RespondToReviewHandlerTests.cs`
- `backend/src/Makables.IntegrationTests/Reviews/SubmitReviewEndToEndTests.cs`
- `backend/src/Makables.IntegrationTests/Reviews/RatingRecomputeCorrectnessTests.cs`
- `backend/src/Makables.IntegrationTests/Reviews/ReviewPerOrderUniquenessTests.cs`
- `backend/src/Makables.IntegrationTests/Reviews/SubmitReviewCrossTenantIsolationTests.cs`
- `backend/src/Makables.IntegrationTests/Reviews/RespondToReviewCrossTenantAndOverwriteTests.cs`

### Modified
- `backend/src/Makables.Core.Domain/Makers/Maker.cs` — add `RecomputeRating` (forwards to existing `SetCatalogStats`, `TotalOrders` untouched).
- `backend/src/Makables.Core.Domain/Common/BusinessErrorMessage.cs` — add 5 (+ optional `ReviewReplyEmpty`) codes.
- `backend/src/Makables.Config/Extensions/AddMakablesInfrastructure.cs` — register `IReviewRepository` + `IReviewQueries`.
- `frontend/src/lib/i18n/cs-CZ/*` (or equivalent i18n source) — add the parallel error-message keys per §C.14.
- `frontend/src/lib/api-client/*` — NSwag-regenerated (BOTH customer + maker hosts); committed in the same PR.
- `docs/architecture/roles/review.md` — per-order grain + recompute-from-rows + IDOR split + immutability asymmetry + T-0050 deferral.
- `docs/architecture/roles/maker.md` — note the `RecomputeRating` producer.

## Commits hint

Suggested commit shape on the implementer's branch:

1. **`test(T-0100): pin domain predicates (red)`** — commit the ~10 domain tests (`Review.Create` range/length, `Review.AddReply` overwrite + body-immutability, `Maker.RecomputeRating` + eligibility predicate) FIRST while the implementations don't exist; verify red.
2. **`feat(T-0100): Review entity + EF migration + Maker.RecomputeRating`** — entity + factory + `AddReply` + `RecomputeRating` + config + migration (partial unique index + maker_id index). Domain tests go green.
3. **`feat(T-0100): IReviewRepository + IReviewQueries + DI`** — write-side (AddAsync + GetByIdForMakerAsync + ExistsForOrderAsync + GetMakerRatingAggregateAsync) + read-side (3 dashboard queries) + DTOs + DI registration + 5 BusinessErrorMessage codes + cs-CZ keys.
4. **`feat(T-0100): SubmitReview + customer host + handler/integration tests`** — `SubmitReview` feature + the 3 customer query features + `ReviewsController` (customer) + SubmitReview handler tests + submit-e2e / recompute / per-order-uniqueness / customer-cross-tenant integration tests.
5. **`feat(T-0100): RespondToReview + maker host + tests + NSwag regen`** — `RespondToReview` + `GetMakerReceivedReviews` + `ReviewsController` (maker) + RespondToReview handler tests + maker cross-tenant/overwrite integration test + NSwag regen for both customer + maker hosts + frontend client commit.

## Status log

- 2026-06-14 `draft` by PM. Created as the review write-side ticket wiring the dormant T-0043 `RatingAverageBp`/`RatingCount` catalog fields. Reference precedents: T-0043 Maker catalog stats + `SetCatalogStats` hook (verified present); T-0060 Order entity + `IOrderRepository.GetByIdForCustomerAsync` (write-scoped per ADR 0013); T-0079 OrderMessage child-entity + per-audience compile-time IDOR split + `IXxxQueries`/`IXxxRepository` seam split (ADR 0023); T-0106 Dispute child-entity + partial-unique-index precedent. Slice scope: new `Review : Auditable` entity (per-delivered-order grain) + `Review.Create`/`AddReply` + `IReviewRepository` + `IReviewQueries` (3 dashboard reads) + EF migration (partial unique index `(order_id) WHERE is_active` + `(maker_id, created_on DESC)`) + `Maker.RecomputeRating` (recompute-from-rows) + 2 per-audience-split commands (`SubmitReview` customer / `RespondToReview` maker) + 5 BusinessErrorMessage codes + cs-CZ keys + ~15 unit + ~5 integration tests + NSwag regen on both customer + maker hosts.
- 2026-06-14 `draft → ready` by PM. User locked 5 dimensions at the 2026-06-14 deliberation: **Q1** per-delivered-order grain (rejected per-maker / per-product / free-standing); **Q2** rating required 1–5, body optional ≤1000 (story-locked); **Q3** eligibility = any `Delivered`/`Completed`, no time limit (story-locked); **Q4** customer review immutable, maker reply overwritable one-per-review ≤500 (story-locked); **Q5** `rating_avg` live from first review via recompute-from-rows (rejected N-threshold / running-average / batch Function). Public review LIST deferred to T-0050 (NOT this bundle). 15 PM-absorbed decisions captured in `## Locked design decisions §C`. No manual_steps. **Ready for dotnet-backend.** Implementer commits the 5-step TDD-red-first sequence above; PR includes backend + frontend client regen.

## Definition of Ready checklist

- [x] Linked user stories present (US-customer-0015 + US-maker-0014).
- [x] Acceptance criteria observable + numbered (AC-1 through AC-12).
- [x] Locked design decisions captured (§A user-locked, §B ADR-locked, §C PM-absorbed).
- [x] Alternatives Considered section with ≥1 rebutted alternative per locked dimension (Options A through O).
- [x] Out of scope explicit (public list → T-0050; email fast-follow; admin moderation post-MVP).
- [x] Risk / mitigation called out for the leading risks (drift, concurrency, double-submit, IDOR, off-by-one, rounding).
- [x] Test plan inline (domain + handler + integration).
- [x] Files touched listed (new + modified).
- [x] Layers / ADRs / dependencies in the frontmatter.
- [x] Security-touching: YES (IDOR on both party endpoints + the eligibility gate + partial unique index are the anti-abuse structural defense).
- [x] Size: M.
- [x] Commits hint with TDD red-first surface called out.
- [x] NSwag regen scope identified (BOTH customer + maker hosts).
- [x] Existing T-0043 surfaces (RatingAverageBp / RatingCount / SetCatalogStats) reused, NOT recreated; public list deferred to T-0050.
