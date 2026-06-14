# Gate 8 - Performance - reviews-loop-bundle

**Scope:** git diff 9e2a74e..HEAD (8 commits, T-0100 / T-0115 / T-0117). Dual-stack.
**Reviewer:** Performance Optimizer
**Date:** 2026-06-14
**Verdict:** GATE8_FOLD - one Medium frontend serialization finding; everything else passes. No BLOCKER, no budget breach, no index gap.

---

## diff --stat (bundle-scoped)

57 files, +7,878 / -16. Backend: Review entity + config + migration (20260614090939_AddReviewTable) + IReviewQueries/IReviewRepository impls + 5 features (SubmitReview, RespondToReview, GetCustomerReviewableOrders, GetCustomerSubmittedReviews, GetMakerReceivedReviews) + Maker.RecomputeRating + FOR-UPDATE load. Frontend: StarRating, reviews-client.ts, customer order-page review block (3 files), maker /recenze route (7 files), i18n (+57), NSwag regen (customer +562, maker +474).

---

## Backend

### B1 - N+1 - PASS
SubmitReview.Handle is a flat sequence: scoped order load, ExistsForOrderAsync, AddAsync, FOR-UPDATE maker load, GetMakerRatingAggregateAsync. 5 round-trips, no loop, no per-row navigation read. Maker company name / order number in the list queries are scalar subqueries projected in the same SQL (ReviewQueries.cs:42, 63, 97), not lazy navigations - EF folds them into the single SELECT. No N+1.

### Rating recompute cost model - PASS (note)
Per submit: 1 aggregate COUNT/AVG scan over the maker active reviews (ReviewRepository.cs:54-62) + 1 SELECT FOR UPDATE Maker load. Cost model (not measured - no perf harness in diff): at ADR-0023 MVP scale (<=200 orders/day across all makers, low tens of reviews per maker by year-end), the AVG scan reads at most low-hundreds of rows against ix_reviews_maker_created (maker_id, created_at DESC), which covers the WHERE maker_id predicate. Sub-millisecond. The submit path is not an ADR-0023 budgeted surface; closest neighbour Order creation API is 600 ms p95 and this is far lighter. Recompute-from-rows is correct over an incremental counter at this scale (self-healing on soft-delete). No action.

### B2 - AsNoTracking - PASS
All 3 ReviewQueries methods chain AsNoTracking + IgnoreAutoIncludes (lines 31-32, 54-55, 81-82). The aggregate in ReviewRepository is a scalar projection (no entity materialization). GetByIdForMakerAsync / GetByIdForUpdateAsync are tracked by design - handlers mutate the returned entity (reply / RecomputeRating) via the UoW behavior. Correct.

### B3 - Indexes - PASS (no gap)
- ux_reviews_order_active UNIQUE (order_id) WHERE is_active - backs ExistsForOrderAsync + the reviewable-orders anti-join probe.
- ix_reviews_maker_created (maker_id, created_at DESC) - backs the maker list ORDER BY and the recompute AVG scan (single index, two consumers).
- ix_reviews_customer_user (customer_user_id) - backs GetCustomerSubmittedReviews; the reviewable anti-join is driven from the orders side by the pre-existing ix_orders_customer_created (customer_user_id, created_at) (OrderConfiguration.cs:189).
- Reviewable-orders no-review predicate translates to NOT EXISTS (SELECT 1 FROM reviews WHERE order_id = o.id), probe satisfied by ux_reviews_order_active. Indexed both sides. Confirmed - no gap.
- All three indexes present in migration 20260614090939_AddReviewTable.cs:58-74. Metadata-only adds (new table + indexes), no rewrite of existing tables.

### B4 - CancellationToken - PASS
Every handler signature accepts CancellationToken; every await forwards it. ReviewQueries/ReviewRepository propagate ct to every ToListAsync/CountAsync/FirstOrDefaultAsync/AnyAsync.

### B5 - no sync-over-async - PASS
No .Result / .Wait() / .GetAwaiter().GetResult() anywhere in the bundle.

### B6 - pagination - PASS
GetMakerReceivedReviewsPagedAsync is the only list endpoint and is two-pass (CountAsync then Skip/Take), capped MaxPageSize = 20 in the Validator. The two customer reads return unbounded ToListAsync but are owner-scoped, non-public, naturally small (a customer own delivered-unreviewed orders / own submitted reviews). Acceptable at MVP scale; watch-item below, not a gate fail.

### B8 - money math - N/A
No money in the review surface. Rating is SMALLINT; the aggregate folds in double then converts to integer basis points via Math.Round AwayFromZero clamped 0-50000 (SubmitReview.cs:141-145). Integer-stored, correct.

---

## Frontend

### F1 / F2 - Server Components + no useEffect fetch - PASS
Customer order page + maker /recenze page are Server Components; both SSR-fetch on render. ReviewFormClient / reply-form are client components only for the interactive star/textarea + event-handler submit (submitReview called in handleSubmit, not useEffect). SubmittedReview is server-safe. StarRating is a client component only because the interactive variant uses useState(hovered).

### F3 - next/image - N/A
No product/maker photos in this bundle; stars are inline SVG Icon.

### F4 / F6 - heavy imports / new deps - PASS
No new runtime dependency. StarRating is hand-built (star-rating.tsx:7 confirms no third-party library) over the existing Icon set. ~1.5 KB component code + glyphs already in the icon set. Negligible delta to /recenze and /objednavka/[id] client bundles vs baseline (no charting/markdown/PDF lib). F6 Alternatives-Considered satisfied implicitly - no dep to justify.

### F5 - no client re-fetch of SSR data - PASS
The submitted review is passed as a prop into SubmittedReview; the form re-syncs via router.refresh (server re-render), no client refetch.

### F-serial - [MEDIUM] objednavka/[id]/page.tsx:178-200 - serial SSR waterfall
What: TrackingDetail awaits getOrderMessages (line 195) THEN resolveReviewState (line 200), which itself awaits getSubmittedReviews (line 178) and only then getReviewableOrders (line 186). Three backend GETs chained strictly sequentially; getSubmittedReviews and getReviewableOrders are independent, and messages is independent of both.
Cost: cost model (no RUM in diff). Each SSR GET forwards the audience cookie = ~1 round-trip. Serial worst case ~= t(messages) + t(submitted) + t(reviewable). The submitted/reviewable pair short-circuits when a review is found (reviewable skipped), so the common already-reviewed path is 2 serial calls; the can-review and no-review paths pay all 3 in series. At an assumed ~40-60 ms/intra-region call, parallelizing the two independent review legs saves ~one RTT (~40-80 ms) off TTFB on the post-delivery render. Not a named ADR-0023 surface, but shares the spirit of the Customer dashboard 400 ms p95 budget; one avoidable serial RTT is worth folding.
Fix: wrap the independent fetches in Promise.all of [getOrderMessages, getSubmittedReviews, getReviewableOrders] (or at minimum Promise.all the two review legs inside resolveReviewState) so the round-trips overlap; keep the same branch logic on the resolved results.
Refs: CLAUDE.md Performance (Server Components fetch on render); ADR 0023 section 1 (Customer dashboard 400 ms p95, by analogy); T-0115 AC (review block render).

### F7 - find/filter over server lists - PASS-with-note
resolveReviewState uses find / some over the customer full submitted + reviewable arrays to match one orderId (page.tsx:180, 187). This is the T-0115 section C documented fallback (the order-detail DTO does not fold the review signal). At one customer list size this is fine. Watch-item: if a per-order review-signal field is later added to CustomerOrderDetail, drop both sibling fetches + the array scans.

---

## Watch-items (next-pass, not gating)
1. Customer reviewable/submitted reads are unbounded ToListAsync - fine at MVP (owner-scoped, small); revisit if a power-customer accumulates hundreds of orders, add DataRangeRequest paging per patterns A.8.
2. F7 / F-serial both dissolve if CustomerOrderDetail later carries a reviewState field - preferred long-term shape (one fetch, zero array scan). Raise on the T-0050 public-reviews follow-up.

## Index gap
None. All WHERE / ORDER BY / anti-join columns on reviews are indexed; the orders-side anti-join driver reuses ix_orders_customer_created.

## Self-check
- Every finding has file:line + severity + cost (model, flagged as such; nothing fabricated) + one-sentence fix.
- No BLOCKER raised. No ADR-0023 budget breached.
- Recompute cost stated as a model with the row-count reasoning.
- No finding contradicts an accepted ADR.
