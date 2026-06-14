# Reviews-loop bundle — Reviewer preliminary verdict (draft)

> Bundle-scope draft per `docs/process/routing.md` §parallel-reviewer. Written in parallel with the implementer; final verdict happens after the implementer reports done. This is the early-warning pass before the diff exists. Tree facts verified against the working tree on 2026-06-14 (branch `feat/order-cleanup-bundle`, HEAD `4652757`).

## Bundle scope (T-0100 + T-0115 + T-0117)

Three tickets shipping the customer-review capability end to end on one branch / one PR.

- **T-0100** (M, `security_touching: true`, backend) — the write side. New `Review : Auditable` child entity (per-delivered-order grain), `Review.Create` + `AddReply`, `IReviewRepository` (write) + `IReviewQueries` (3 dashboard reads), EF migration (**partial unique index `(order_id) WHERE is_active`** + `(maker_id, created_on DESC)`), `Maker.RecomputeRating` (recompute-from-rows), 2 per-audience-split commands (`SubmitReview` customer / `RespondToReview` maker), 3 query features, 5 new `BusinessErrorMessage` codes + cs-CZ keys, ~15 unit + ~5 integration tests, **NSwag regen on BOTH customer + maker hosts**. 12 ACs.
- **T-0115** (S, `security_touching: false`, frontend) — the customer surface. Inline review block on the existing `/objednavka/[id]` order-detail page (state Delivered/Completed + no review), NEW `StarRating` primitive (interactive + display), `reviews-client.ts` submit helper, `customer.review.*` i18n keys + 5 error parity keys, three render states. No backend, no regen. 9 ACs.
- **T-0117** (S, `security_touching: false`, frontend) — the maker surface. New `/dashboard/maker/recenze` route (Server Component list + aggregate header + URL-state pagination), `review-card.tsx`, `reply-form.tsx` client island, extends `reviews-client.ts` with `getMakerReviews` + `respondToReview`, `dashboard.maker.reviews.*` i18n keys, reuses the existing `Stars` display component. No backend, no regen. 9 ACs.

~30 ACs total (12 + 9 + 9). Bundle layout: **1 EF migration**, **5 new backend one-file features** (2 commands + 3 queries), **0 new outbox/email** (synchronous-only at MVP — locked), **0 new Functions**, **NSwag regen ×2 hosts (customer + maker; public + admin untouched)**, ~6 new frontend files + 2 i18n catalog extensions.

Seams verified present on the tree:
- `Maker.SetCatalogStats(int ratingAverageBp, int ratingCount, int totalOrders)` (`Maker.cs:236`) + `RatingAverageBp`/`RatingCount` (`private set`, `Maker.cs:129/132`) — T-0043 surface present, to be REUSED not recreated.
- `IOrderRepository.GetByIdForCustomerAsync(orderId, customerUserId, ct)` tracked (`IOrderRepository.cs:79`) + `GetByIdForMakerAsync` (`:87`) + the `*ReadOnlyAsync` variants (`:124`) — the customer-side IDOR shield.
- `Order.MakerId` (`Order.cs:72`), `Order.State` (`:139`), `Order.CountryCode` (Auditable) — the denormalization + eligibility sources.
- `OrderState.Delivered = 4`, `Completed = 5`, earlier states `PendingPayment/Paid=1/Accepted=2/Shipped=3` — the eligibility gate values are correct.
- `IMakerRepository.GetByUserIdAsync` (`:45`) + `GetByIdAsync` (`:54`) — maker-scope resolution present. **No row-lock method exists** (see HIGH-1).
- Partial-unique-index precedent: `DisputeConfiguration` `.IsUnique().HasFilter("resolved_at IS NULL")` (T-0106) — the review filter follows this shape with the DB column name (`is_active`).
- Frontend: `Stars` component (`(public)/katalog/[slug]/stars.tsx`) takes a 0–5 `value` and floors per star — reusable read-only. `star`/`starOutline` icons present in `components/ui/icon.tsx`. `vyplaty` maker dashboard present (the T-0117 structural precedent). `RATING_BP_PER_STAR` lives as `ln = 10_000` in `lib/api-client-helpers/catalog.ts` (§B.12 anchor). `reviews-client.ts`, `star-rating.tsx`, `recenze/` route all ABSENT (greenfield, as expected).
- `BusinessErrorMessage` uses dotted-key constants (`order.notFound` at `:42`); `OrderNotFound` present and REUSED for the customer-side cross-tenant miss (no new code). 139 constants today.
- `docs/architecture/roles/review.md` ALREADY EXISTS (a stub) — must be updated in this PR (RDD parity, see Gate 9).

## Patterns / ADRs the diff must honour

- **patterns.md §A.2/§A.3/§A.7 one-file features**: 5 new features in `Core.AppServices/Features/Reviews/`, each `Command`/`Query` + globally-unique `Response` + `Validator` + `Handler`. Globally-unique Response naming per the PR #38 NSwag CI fix: `SubmitReviewResponse`, `RespondToReviewResponse`, `GetCustomerReviewableOrdersResponse`, `GetCustomerSubmittedReviewsResponse`, `GetMakerReceivedReviewsResponse`. **Hard-fail on any bare `record Response`.**
- **patterns.md §A.4 BusinessResult + centralized codes**: every `Error.Code` from `BusinessErrorMessage`; no inline strings. 5 new codes (`review.alreadyExists`, `review.orderNotDelivered`, `review.ratingOutOfRange`, `review.bodyTooLong`, `review.replyTooLong`, + maybe `review.notFound` / `review.replyEmpty`). Negative-path Validator tests required per must-cover §9.
- **patterns.md §A.5 pipeline**: `SubmitReview` + `RespondToReview` are commands → `ValidationPipelineBehavior` then `UnitOfWorkPipelineBehavior` commits. Handlers NEVER call `SaveChangesAsync()`. The Review insert + Maker recompute commit in ONE UoW (ADR 0014 = admin-audit-log file; the UoW-pipeline rule is the live behavior — note the ticket cites "ADR 0014 (UoW pipeline)" but file 0014 is `admin-audit-log.md`; the actual UoW rule lives in patterns §A.5/§A.20 — tree-over-prose, do not ding).
- **patterns.md §A.8 paged query + ADR 0023 NFRs**: `GetMakerReceivedReviewsPagedAsync` AsNoTracking, projection-only, `20/page` cap (ticket says default 20, cap 20), sort `CreatedOn DESC` tiebreak `Id DESC`, backed by `(maker_id, created_on DESC)`. Two-pass Count + Skip/Take per the standard paged shape.
- **patterns.md §A.9/§A.12 + ADR 0013 (data-scoping)**: `IReviewQueries` reads bake the audience predicate (`customer_user_id` / `maker_id`) into the EF `Where` — the read-layer IDOR shield. `IReviewRepository.GetByIdForMakerAsync` predicate IS the maker write-side shield. ADR 0013's soft-delete global query filter on `IsActive` is what makes recompute-from-rows self-healing and `ExistsForOrderAsync` exclude deactivated rows automatically.
- **patterns.md §A.11 Auditable**: `Review : Auditable` — soft delete via `IsActive`/`DeactivatedAt`; the partial unique index on `(order_id) WHERE is_active` frees the slot on deactivation (self-healing).
- **patterns.md §A.16 per-audience hosts + ADR 0013 compile-time split**: `SubmitReview` registered only on `Web.Customer`; `RespondToReview` only on `Web.Maker`. A customer JWT cannot dispatch the maker command — the type isn't on the host. This IS the first IDOR layer (the WHERE-predicate is the second). Mirrors the T-0079 OrderMessage 6-feature split precedent exactly.
- **patterns.md §A.18 money / rate convention**: bp conversion `(int)Math.Round(avgStars * 10_000, MidpointRounding.AwayFromZero)` clamped `[0, 50_000]` — half-up. `SetCatalogStats` already guards 0..50000.
- **patterns.md §B.1 / §B.14 (ADR 0024) / §B.16 (ADR 0022)**: Server Components default; the only client islands are `ReviewFormClient` (T-0115) + `reply-form.tsx` (T-0117). SSR auth via the `apiFetch` cookie-forwarding chokepoint (no per-page session plumbing). All data via `apiFetch` + hand-written `reviews-client.ts` helper returning `Result<T, ApiError>`; route code never imports `lib/api-client/`; the pre-commit hook blocks manual edits.
- **patterns.md §B.8 URL-state pagination** (T-0117): `page` in the URL; `page=1` dropped; junk clamps to 1 (backend Validator authoritative). Local `<Pagination>` pointed at `/dashboard/maker/recenze`.
- **patterns.md §B.12 `RATING_BP_PER_STAR` (load-bearing)**: T-0117's aggregate header (bp→0–5) MUST divide by the shared `ln`/`RATING_BP_PER_STAR = 10_000` constant from `catalog.ts`, NOT an inline literal. The §B.12 history is a real 10× bug (three sites divided by 1000; the `Math.min(5,…)` clamp hid it). **Hard-fail on any inline `/10000` or `/2000` in the header.** T-0115's display variant uses whole-star `Rating` (1–5) so no bp division — but if it ever renders the maker average it uses the same constant.
- **patterns.md §B.18 plural-neutral Czech**: the review-count line in T-0117's header uses the "Label: N" shape (`Recenzí: {count}`), never `{count} recenzí`.
- **patterns.md §B.5 i18n tone**: T-0115 customer surface = **vykání** (V-form); T-0117 maker surface = **tykání** (T-form, pending the open tone question — a flip is catalog-only).

## Pre-flight risks (HIGH first)

### HIGH

- **HIGH-1: Maker row-lock for the recompute has no existing method — it must be ADDED, and the read-position/off-by-one is the load-bearing correctness surface.** §A.5 mandates the Maker row is **row-locked** during recompute so concurrent submits to the same maker serialize. Verified on the tree: `IMakerRepository` has only `GetByUserIdAsync` + `GetByIdAsync` — **no `GetByIdForUpdateAsync` / `FOR UPDATE` variant exists.** The implementer must add one (raw SQL `... FOR UPDATE` or an EF tracked-load with an explicit pessimistic lock). Reviewer expectations:
  - The Review insert + the aggregate read + the Maker recompute all happen in ONE UoW (ADR 0014 atomicity — they cannot half-apply). The plain `GetByIdAsync` (tracked) takes a row-level lock at commit time, but that does NOT serialize two concurrent transactions reading the aggregate BEFORE they write — only an explicit `FOR UPDATE` (or `SELECT … FOR UPDATE` semantics) serializes the read→recompute→write window. A bare tracked load that two transactions execute concurrently can both read the old aggregate and both write — last-writer-wins loses a count. **Request changes if the Maker is loaded via the non-locking `GetByIdAsync` and there is no row-lock.**
  - **Off-by-one**: the stored bp/count MUST include the just-added review. §C.8 step 7 leaves the read-position (pre-flush in-memory fold vs post-flush re-aggregate) to the implementer, BUT `RatingRecomputeCorrectnessTests` (integration) pins the resulting stored values — so the off-by-one cannot ship silently. Verify the test asserts the stored `rating_average_bp`/`rating_count` equal AVG/COUNT over ALL active reviews including the new one (e.g. one 4-star → `40000`/`1`; three 5/4/3 → `40000`/`3`).
  - **bp rounding**: `Math.Round(avgStars * 10_000, MidpointRounding.AwayFromZero)` clamped `[0,50000]`. AVG 3.333… → `33333`; 4.5 → `45000`. `MakerRecomputeRatingTests` pins representative values; `SetCatalogStats`'s existing guard rejects out-of-range.

- **HIGH-2: Per-order uniqueness — the 23505 unique-violation translation is the race backstop and easy to forget (payout-core lesson).** Two halves of the defense: `ExistsForOrderAsync` (happy-path gate → `ReviewAlreadyExists`) AND the partial unique index `(order_id) WHERE is_active` (hard backstop for the concurrent double-submit race). Reviewer expectations:
  - The migration ships the partial unique index with the DB column name in the filter (`HasFilter("is_active")` — matching the `DisputeConfiguration` `resolved_at IS NULL` precedent, NOT the C# property name).
  - **The Postgres `23505` unique-violation on the second concurrent transaction must surface as a clean business failure, not a raw 500.** AC-4 says "the loser surfaces a conflict." Verify the implementer either (a) catches `DbUpdateException`/`PostgresException 23505` and maps to `ReviewAlreadyExists`, or (b) documents that the integration test `ReviewPerOrderUniquenessTests` asserts the concurrent loser gets a typed conflict, not an unhandled 500. The payout-core bundle's lesson (`csvPathAlreadySet`) was exactly a missing constraint-violation translation — **flag if the index ships without a tested translation path.**

- **HIGH-3: IDOR on both party endpoints — cross-tenant must be 404, identical to "not found", no existence leak.** Two directions:
  - `SubmitReview` loads the order via `GetByIdForCustomerAsync(orderId, customerUserId)` (tracked, customer-scoped — the WHERE predicate never selects another customer's order). Cross-tenant → `OrderNotFound` 404 (REUSED code, no new `OrderAccessDenied`). AC-7 pins it at the SQL level.
  - `RespondToReview` loads the review via `GetByIdForMakerAsync(reviewId, makerId)` (`Where(r => r.Id == reviewId && r.MakerId == makerId)` — the maker IDOR shield). Cross-tenant → `ReviewNotFound` 404. AC-9 pins it.
  - Verify NO distinct "access denied" code on either cross-tenant path (mirrors the order-cleanup HIGH-4 enumeration-oracle rule). The compile-time per-host registration is the second layer (customer JWT cannot reach `RespondToReview`).

- **HIGH-4: Eligibility gate = the anti-abuse defense — only Delivered/Completed; orders that LEFT those states must be excluded.** The gate: `order.State ∈ {Delivered (4), Completed (5)}` AND `ExistsForOrderAsync == false`. Reviewer expectations:
  - Pre-Delivered states (`PendingPayment`/`Paid`/`Accepted`/`Shipped`) → `ReviewOrderNotDelivered`. AC-5 pins it.
  - **Refunded/Disputed orders that transitioned OUT of Delivered/Completed are correctly EXCLUDED** — because the gate checks the CURRENT `State`, a Refunded order (state ∉ {Delivered, Completed}) fails the gate. Verify the eligibility predicate reads the live `State`, not a "was ever delivered" flag. The `IReviewQueries.GetCustomerReviewableOrdersAsync` left-anti-join must filter `State ∈ {Delivered, Completed}` so a later-refunded order drops off the reviewable list. (Note: a customer who already reviewed before a refund keeps their review — that's the immutability lock, correct.) **Flag if the eligibility predicate uses `DeliveredAt IS NOT NULL` instead of the current-state check — that would let a refunded/disputed order be reviewed.**

- **HIGH-5: i18n parity (HARVESTED — zero tolerance).** `recurring-findings.md` row #2 fired its **third strike at payout-core** (`csvPathAlreadySet`): "every new `BusinessErrorMessage` constant MUST ship with a matching cs-CZ key in the same PR." This is now a **standing automated-gate candidate** — for this bundle it is **zero tolerance**, and any miss is a request-changes, not a nit. Verify in the diff:
  - All 5 (–6) new `review.*` error codes have parallel cs-CZ keys (the suggested copy is in T-0100 §C.14). A code without a key OR a key without a code is a hard-fail.
  - All `customer.review.*` UI strings (T-0115: section heading, CTA, star aria-labels 1–5, comment placeholder + counter, submit labels, submitted-review heading, "Odpověď výrobce" heading) present (vykání).
  - All `dashboard.maker.reviews.*` UI strings (T-0117: metadata, page title/subtitle, aggregate header avg + count line, card labels, "bez komentáře", reply block + edit, reply form labels, empty/error states, `dashboard.maker.nav.reviews`) present (tykání).
  - **The 5 error parity keys must be shared/consistent across T-0115 and T-0117** — T-0115 ships all 5 (incl. the maker-side `reviewReplyTooLong` "for catalog completeness"); T-0117 also needs `reviewReplyTooLong` + a generic. Verify no duplicate/divergent key definitions for the same code across the two tickets' i18n additions (one canonical key per code). **If the final diff ships any review code without its key, or a key without its code → this is the 4th hit of finding #2 → append `recurring-findings.md` (bump count + add `reviews-loop-bundle` to Tickets seen) + Architect ping** (the codification — a mechanical `BusinessErrorMessage ↔ cs-CZ.ts` parity check in `check-consistency.mjs` — is overdue).

- **HIGH-6: T-0115 reads `canReview`/`review` off `CustomerOrderDetailDto`, but T-0100 as groomed does NOT fold them in — contract seam gap.** `CustomerOrderDetailDto` exists (`CustomerOrderDetailDto.cs:22`, a sealed record). T-0115 §C's PRIMARY render path inspects `canReview` / `review` signals "folded into the existing detail DTO." But T-0100's scope ships a SEPARATE `IReviewQueries.GetCustomerReviewableOrdersAsync` (returns `ReviewableOrderDto`) + `GetCustomerSubmittedReviewsAsync` — it does NOT modify `CustomerOrderDetailDto`. **The producer for T-0115's primary path does not exist in T-0100's scope.** T-0115 §C anticipates this exact case ("If T-0114 instead exposes the review via a separate query… the page SSR-fetches it once alongside the detail… degrades to 'no review block' on failure") and flags it as "a T-0114 gap to escalate, not a T-0115 backend edit." Reviewer expectations:
  - Either T-0100's scope grows to fold `canReview`/`review` into `CustomerOrderDetailDto` (a contract change → NSwag regen already in scope for customer host), OR T-0115 wires the documented sibling-fetch fallback against the `IReviewQueries` dashboard reads. **Both are acceptable; what is NOT acceptable is T-0115 adding a backend edit to satisfy its own page.** Confirm which path the implementer took and that the chosen producer actually ships in the same PR.
  - This is the single most likely cross-ticket break in the bundle. Trace it first at final review.

- **HIGH-7: All-hosts NSwag regen — BOTH customer + maker, and `check:api` clean.** The grooming commit (`4652757`) codified the "all-hosts regen" routing rule this bundle exercises. T-0100 ships 2 commands + 3 queries across the two hosts. Verify:
  - **BOTH** `frontend/src/lib/api-client/customer-api.v1.ts` AND `maker-api.v1.ts` changed in the PR (the customer host gains `SubmitReview` + 2 customer queries; the maker host gains `RespondToReview` + 1 maker query). A PR that regenerates only one host is a hard-fail.
  - `.spec-hashes.json` updated for both hosts (ADR 0022 parity audit trail).
  - Public + Admin hosts UNTOUCHED (the public review LIST is T-0050, explicitly out of scope — the `MakerReviewItem` placeholder stays an empty list). A spurious public/admin client diff is scope creep → flag.
  - No manual edits to `lib/api-client/` (pre-commit hook); the diff is generator-output only. The currently-uncommitted `customer-api.v1.ts` / `maker-api.v1.ts` / `.spec-hashes.json` drift in the working tree (git status) must resolve to exactly the T-0100 regen — nothing unrelated.

### MEDIUM

- **MEDIUM-1: Frontend StarRating (NEW) accessibility — keyboard + aria are AC-gated (ADR 0023 §5 WCAG 2.1 AA on customer surfaces).** T-0115 AC-2 requires the interactive picker keyboard-operable (`role="radiogroup"`, arrow-key + enter/click selection, selected value mirrored to `aria-label`, focus-visible ring) and the submit disabled until a star ∈ [1,5] is chosen. Verify: no `outline: none` without a replacement; form error associated via `aria-describedby` (ADR 0023 §5); five-button hand-built component over the existing `star`/`starOutline` icons (no third-party library — Option E rejected). The customer surface is WCAG AA; a non-keyboard star picker is an accessibility regression, not a nit.

- **MEDIUM-2: Inline CTA gating — render exactly ONE of three states, no dead controls.** T-0115's three-state branch: `canReview` → `ReviewFormClient`; `review !== null` → read-only `SubmittedReview`; neither → render NOTHING (AC-7: no empty placeholder for a `Shipped` order). After submit, `router.refresh()` re-renders into the read-only state (AC-5). **No edit/delete/re-submit control anywhere** (Q4 customer-side immutability — Option C rejected). Verify the block is fully conditional on backend signals (no client-side eligibility re-derivation beyond reading the DTO) and the immutability is rendered as "no controls," not a disabled button.

- **MEDIUM-3: Reply overwrite UX (T-0117) — one reply per review, not appended.** AC-4: a re-submit OVERWRITES the displayed reply (Q4 — one `MakerReply` field), via `respondToReview` → `router.refresh()`. The form pre-fills with the current reply (edit-in-place). Verify: no reply thread/history; the card renders exactly ONE reply; the customer review content stays read-only DOM (only the reply textarea is interactive — Options A/B/C rejected). AC-6: the aggregate header reads `RatingAverageBp`/`RatingCount` off the response envelope — **NOT averaged from the listed page** (the list is paginated; grep proof = no division over `items` in the header — Option D rejected).

- **MEDIUM-4: Server Components default; the only client islands are the two forms.** T-0115's `ReviewFormClient` + T-0117's `reply-form.tsx` are the sole `'use client'` surfaces. `SubmittedReview`, `review-card.tsx`, both `page.tsx` files, the aggregate header stay server-rendered. NO `useEffect` data fetching anywhere (AC-9 both tickets). The re-entrancy / in-flight guard mirrors the `MarkDeliveredButton` / `order-actions-client.tsx` `useRef` pattern (double-submit defense; the backend unique index is the authoritative backstop).

- **MEDIUM-5: `reviews-client.ts` is shared across T-0115 and T-0117 — extend, don't fork.** T-0115 creates it with `submitReview`; T-0117 EXTENDS it with `getMakerReviews` + `respondToReview` (or creates it if T-0115's commit landed second). Verify ONE file, the `Result<T, ApiError>` + `apiFetch` shape mirroring `payouts-client.ts`/`orders-client.ts`, DTO types re-exported from the regenerated client (route code never imports `lib/api-client/`). The maker helpers re-type `createdAt`/`makerReplyAt` to wire-shape `string | undefined` per the `maker-orders.ts` raw-JSON rationale.

- **MEDIUM-6: `Stars` component cross-route-group reuse (T-0117).** The reused `Stars` lives at `(public)/katalog/[slug]/stars.tsx`. T-0117 imports it cross-group OR relocates it to `components/ui/`. Either is fine; a relocation is a clean refactor noted in the PR. Verify the import path is honest and no copy-paste duplicate is created.

- **MEDIUM-7: Reviewer-identity data-minimization (GDPR).** `MakerReceivedReviewDto` carries NO customer email/name (consistent with T-0079/T-0081). T-0117's card shows "customer first name + order number" — verify the DTO exposes only a first name (or the order number), never the full email/identity. The maker sees the review, not the reviewer's PII.

## AC traceability (~30 ACs: 12 + 9 + 9)

### T-0100 — backend write side (12)

| AC | How I verify in the diff |
|---|---|
| AC-1 | `SubmitReviewEndToEndTests`: customer POST to own Delivered order → 200 `{reviewId, rating, createdAt}`; `reviews` row with `order_id`/`maker_id` (denormalized off the order)/`customer_user_id`/`rating`. Completed (5) also accepted. |
| AC-2 | Same test + `RatingRecomputeCorrectnessTests`: `makers.rating_count`/`rating_average_bp` updated in the SAME transaction; one 4-star → `1`/`40000`. (HIGH-1 off-by-one + atomicity.) |
| AC-3 | `ReviewPerOrderUniquenessTests` + handler test: second submit → `ReviewAlreadyExists`; no second row. |
| AC-4 | `ReviewPerOrderUniquenessTests` concurrent leg: partial unique index lets one win; loser surfaces a conflict (HIGH-2 — verify 23505 translation, not raw 500). |
| AC-5 | Handler test + integration: pre-Delivered → `ReviewOrderNotDelivered`; no row. (HIGH-4.) |
| AC-6 | Validator tests: `rating=0`/`6` → 400 `ReviewRatingOutOfRange`; 1001-char body → 400 `ReviewBodyTooLong`. (Must-cover §9 negative paths.) |
| AC-7 | `SubmitReviewCrossTenantIsolationTests`: customer A → customer B's order → 404 `OrderNotFound`, SQL-level. (HIGH-3.) |
| AC-8 | `RespondToReview` happy path: `maker_reply` set + `maker_reply_at` stamped. |
| AC-9 | `RespondToReviewCrossTenantAndOverwriteTests`: maker A → maker B's review → 404 `ReviewNotFound`. (HIGH-3.) |
| AC-10 | Overwrite leg: second reply overwrites (one value; `maker_reply_at` bumped); `rating`/`body` unchanged; 501-char → 400 `ReviewReplyTooLong`. |
| AC-11 | `RatingRecomputeCorrectnessTests` soft-delete leg: deactivate one review + submit a 4th → recompute EXCLUDES the deactivated row (recompute-from-rows self-healing, NOT running-avg); the order becomes reviewable again (partial index frees the slot). |
| AC-12 | Migration review: `reviews` table + PK + 3 FKs + `rating SMALLINT NOT NULL` + `body VARCHAR(1000) NULL` + `maker_reply VARCHAR(500) NULL` + `maker_reply_at TIMESTAMPTZ NULL` + Auditable cols + **partial unique `(order_id) WHERE is_active`** + `(maker_id, created_on DESC)`. Build clean; ~10 domain + ~5 handler + ~5 integration; `check-consistency` exit 0; **NSwag regen BOTH hosts** (HIGH-7). |

### T-0115 — customer UI (9)

| AC | How I verify in the diff |
|---|---|
| AC-1 | Manual QA: Delivered/Completed + no review → review block below the thread, SSR via the forwarded cookie (no client fetch for first paint). Depends on HIGH-6 resolution (folded DTO vs sibling fetch). |
| AC-2 | Submit disabled until a star ∈ [1,5]; keyboard-operable star picker; selected value via `aria-label`. (MEDIUM-1.) |
| AC-3 | Empty comment + chosen rating → valid star-only submit → read-only state. |
| AC-4 | Live `{n}/1000` counter + `maxLength={1000}`; forced `reviewBodyTooLong` 400 → Czech parity copy inline, no crash. |
| AC-5 | `router.refresh()` → form gone, read-only stars + comment + Czech date; NO edit/delete/re-submit control. (MEDIUM-2.) |
| AC-6 | Already-reviewed order → read-only block directly (never the form); maker reply under "Odpověď výrobce" when present. |
| AC-7 | `Shipped` + no review → NO block/CTA/placeholder. |
| AC-8 | `reviewAlreadyExists`/`reviewOrderNotDelivered` → mapped Czech inline, page usable. |
| AC-9 | Build/lint/typecheck clean; zero `any`/`console.*`/client-store/`useEffect`-fetch; all strings via `customer.review.*` (vykání); 5 error parity keys; `lib/api-client/` untouched; 375/768/1280 + keyboard. (HIGH-5, MEDIUM-4.) |

### T-0117 — maker UI (9)

| AC | How I verify in the diff |
|---|---|
| AC-1 | Server Component `page.tsx` (no `'use client'`); `dynamic='force-dynamic'`; data via `getMakerReviews`/`apiFetch` + SSR cookie; no `useEffect` fetch in the route folder. |
| AC-2 | Each card: read-only `Stars` (bp→0–5), customer first name + order number, comment or "bez komentáře", Czech date; most-recent-first. (MEDIUM-6/7.) |
| AC-3 | No reply → reply form; existing reply → reply text + `MakerReplyAt` date + edit affordance pre-filling the form. |
| AC-4 | Reply ≤500 → `respondToReview` success → `router.refresh()` shows it; re-submit OVERWRITES (Q4). (MEDIUM-3.) |
| AC-5 | Reply POST failure (`ReviewReplyTooLong`/`NotFound`/5xx) → inline i18n `Alert`, submit re-enables, list stays. |
| AC-6 | Header avg stars + 1-decimal + count from `RatingAverageBp`/`RatingCount` on the envelope — NOT averaged from `items` (grep proof). Uses `RATING_BP_PER_STAR` (§B.12, HIGH note). |
| AC-7 | Zero reviews → informational empty state (`dashboard.maker.reviews.empty.*`), not an error/blank. |
| AC-8 | `?page=2` → page-2 call + pagination totals; junk clamps to 1; `page=1` absent from canonical URLs; list-API failure → `Alert variant="error"` + retry. |
| AC-9 | 375/768/1280 stack, no horizontal scroll; zero `any`/`console.*`/hardcoded Czech (new `dashboard.maker.reviews.*`, tykání); `lib/api-client/` untouched; lint+build clean; `check-consistency` exit 0. (HIGH-5.) |

## Gate 5 — tests (TDD red-first: HARD requirement; commit order will be checked)

Per `docs/process/quality-gates.md` Gate 5 + must-cover-tests.md, all pure-logic tests MUST be committed RED before/alongside implementation (T-0067+ hard rule; after-the-fact test on pure logic = HARD FAIL). Three red-first surfaces:

1. **Rating recompute / bp conversion** — `MakerRecomputeRatingTests` (`RecomputeRating` stores bp + count via `SetCatalogStats`; `TotalOrders` untouched; out-of-range bp >50000 throws via the delegated guard; representative roundings 3.333→33333, 4.5→45000). Pure domain logic → red-first.
2. **`AddReply`** — `ReviewAddReplyTests` (>500 throws; second `AddReply` OVERWRITES + bumps `MakerReplyAt`; `AddReply` does NOT mutate `Rating`/`Body` — immutability). Pure → red-first.
3. **`Review.Create` + eligibility predicate** — `ReviewCreateTests` (rating <1/>5 throws; body >1000 throws; happy-path trims body, leaves `MakerReply` null, accepts null body) + the eligibility predicate pin (`State ∈ {Delivered, Completed}` accepts, earlier rejects — wherever the predicate lives, pin it as a pure check). Pure → red-first.

Per the `## Commits hint`, commit 1 is `test(T-0100): pin domain predicates (red)` — ~10 domain tests before any implementation. **Verification method:** `git log --reverse <branch> -- <test-files> <impl-files>` per tdd-policy.md; status-log red→green proof acceptable as fallback. Handler tests (~5: SubmitReview happy/pre-delivery/exists/cross-tenant; RespondToReview happy/cross-tenant/overwrite) and integration tests (~5: submit-e2e, recompute correctness incl. soft-delete, per-order uniqueness, both cross-tenant IDOR paths, reply overwrite). **Negative-path Validator tests for all new codes (must-cover §9)** — each of `ReviewRatingOutOfRange`/`ReviewBodyTooLong`/`ReviewReplyTooLong` (+ `ReviewReplyEmpty` if added) needs a Validator test asserting the code.

**The recompute-correctness integration test is the load-bearing surface** (HIGH-1): it pins the stored bp/count regardless of the implementer's read-position choice, so the off-by-one and the recompute-from-rows-vs-running-avg distinction cannot ship silently. The soft-delete-exclusion leg (AC-11) is what proves it is NOT a running average.

Frontend: no automated harness at MVP — manual QA on the Vercel preview per the inline plans (three-state walk, keyboard star picker, char counter + cap, star-only submit, forced-error inline, reply submit/overwrite, empty/error, 375/768/1280). Per Gate 5 frontend, automated tests only where pure logic exists — the StarRating display/whole-star render and the bp→star conversion are candidates but not mandated.

## Gate 9 — mechanical checks + new T1 count + i18n parity (HARVESTED)

- **Baseline**: ~124 post-payout-settlement (per the refund-dispute draft's projection chain 111→118→124; confirm the current baseline file at PR time). **5 new backend feature files** (static-class-wrapped one-file features) → expect ~5 new T1 static-class-wrapper false-positives → baseline drifts to ~129. **HARD FAIL on any NEW non-T1 violation.**
- **T3/T4**: ZERO `SaveChangesAsync` in handlers (UoW pipeline commits); ZERO `dynamic`/`any`/`object`-where-concrete-works. The recompute aggregate returns a typed `(int Count, double AverageStars)` — verify no `dynamic`.
- **T5 BusinessErrorMessage**: 5 (–6) new constants — every reference via the constant; **cs-CZ parity keys in the same PR (HIGH-5, zero tolerance)**. This is the i18n-parity recurring-finding (#2, harvested-at-3) tripwire — a 4th hit obligates a `recurring-findings.md` bump + Architect ping.
- **T6 money**: N/A — no monetary columns (rating bp is a rate/score, not money; `rating SMALLINT`, `*_bp` is a basis-point int on the existing Maker row, no `_minor`/`currency` needed).
- **T7 useEffect**: ZERO data-fetching `useEffect` in either frontend ticket (AC-9 both).
- **NSwag (Gate 6)**: customer + maker `*.v1.ts` + `.spec-hashes.json` regenerated and committed; CI parity green; `check:api` clean; PR description flags the contract change + names both affected `lib/api-client/<host>-api.v1.ts` files. Public + admin untouched. (HIGH-7.)
- **Gate 7 docs + RDD parity (ADR 0015)**: `docs/architecture/roles/review.md` EXISTS as a stub today — it MUST be updated in this PR to reflect the as-built per-order grain + partial unique index + recompute-from-rows self-healing + the per-audience compile-time IDOR split + the immutable-review/overwritable-reply asymmetry + the T-0050 deferral. `docs/architecture/roles/maker.md` MUST note the new `RecomputeRating` producer wiring the previously-dormant `RatingAverageBp`/`RatingCount` (T-0043) fields (today maker.md says "review aggregation happens elsewhere" — make it concrete). **RDD parity check:** the NEW `IReviewRepository` + `IReviewQueries` interfaces want a role-file mention (the `Review` aggregate role file covers them via "Persisted by"); no NEW standalone aggregate/service beyond `Review` (already has a role file). Every handler depends on ≤5 collaborators (SubmitReview: session, order repo, review repo, maker repo, clock = 5 — at the cap; verify it doesn't grow). **Request changes if `review.md` is not updated in the same PR.**
- **Gate 3 (SecOps)**: T-0100 is `security_touching: true` → SecOps mandatory (IDOR on both endpoints + the eligibility gate + partial unique index ARE the anti-abuse structural defense). T-0115/T-0117 are `security_touching: false` (consume the IDOR-shielded, per-audience-split backend). Ping SecOps for T-0100.
- **Gate 8 (Optimizer)**: `GetMakerReceivedReviewsPagedAsync` is a NEW paged query (two-pass Count + Skip/Take, AsNoTracking, backed by `(maker_id, created_on DESC)`) → Optimizer ping. `GetMakerRatingAggregateAsync` is a per-submit `COUNT + AVG` scan over the `maker_id` index — cheap at MVP volume (handful-to-hundreds of rows per maker) but on the SubmitReview request path → confirm acceptable. The SubmitReview handler is a multi-step pipeline (load order → eligibility → exists-gate → create → add → aggregate → row-lock maker → recompute) — at ~5 collaborators it's at the RDD cap; Optimizer should eyeball the recompute read-position for an extra round-trip.

## Bundle DoR compliance check

- ✅ All three tickets have the DoR section with boxes checked; Q1–Q5 user-locked 2026-06-14.
- ✅ Bundle ordering documented (T-0100 backend first; T-0115 + T-0117 consume the regenerated clients; T-0117 `depends_on: [T-0100]` correct).
- ⚠️ **DoR DEFECT (T-0115): the dependency points at a non-existent ticket.** T-0115 frontmatter says `depends_on: [T-0114, T-0086b, T-0043]` and its entire body references "T-0114" as the backend ticket — but **no `T-0114` ticket file exists**, and the backend is **T-0100** (which T-0117 correctly cites, and which the grooming commit `4652757` names). This is a naming defect, not a design defect, BUT it has a substantive consequence: T-0115's primary render path ("folded into the T-0114 detail DTO") describes a producer that T-0100 does NOT ship (see HIGH-6). **→ PM-lane: correct T-0115's `depends_on` and body references from T-0114 → T-0100, and resolve the folded-DTO-vs-sibling-fetch decision (HIGH-6) before the frontend implementer wires the page.** The reviewer cannot edit the ticket; flag for PM.
- ⚠️ **ADR-citation drift (cosmetic):** T-0100 cites "ADR 0014 (UoW pipeline)" but file `0014` is `admin-audit-log.md`; the UoW-pipeline rule lives in patterns §A.5/§A.20. Tree-over-prose — the BEHAVIOR (UoW commits, no handler `SaveChangesAsync`) is unambiguous; do not ding the implementer, but the ticket's ADR map is loose. Same for "ADR 0013 (per-audience JWT…)" — file 0013 is `data-scoping-and-soft-delete`; the per-audience-JWT rule is ADR 0005 + 0013 combined. Behavior is clear; cosmetic.
- ✅ Size: M + S + S — comfortably under the routing.md L-split cap.
- ✅ Branch `feat/order-cleanup-bundle` is REUSED — the refund-dispute draft and order-cleanup draft both warned NOT to reuse `feat/order-cleanup-bundle` (the order-cleanup name is taken by merged T-0079+T-0083 artifacts). **→ PM-lane: the branch name `feat/order-cleanup-bundle` is wrong for this reviews bundle and collides with prior bundle artifacts; the review artifacts use `reviews-loop-bundle`. Flag for PM to confirm the branch name before PR-open.**
- ✅ One manual step (T-0115 Vercel-preview QA; T-0117 Vercel-preview QA) per frontmatter.
- ✅ Single parallel-reviewer artifact (this file).

## Open items the implementer should confirm before/while coding

1. **Add a Maker row-lock method** (`GetByIdForUpdateAsync` or `FOR UPDATE` semantics) — none exists on `IMakerRepository`; the recompute serialization (§A.5) depends on it. A bare tracked `GetByIdAsync` does NOT serialize the concurrent read→recompute→write window (HIGH-1).
2. **Translate Postgres `23505`** unique-violation on the concurrent-double-submit loser to `ReviewAlreadyExists` (or pin a typed conflict in `ReviewPerOrderUniquenessTests`), not a raw 500 (HIGH-2, payout-core lesson).
3. **Eligibility reads the LIVE `State`** (∈ {Delivered, Completed}), not `DeliveredAt IS NOT NULL` — so a later-refunded/disputed order correctly drops off the reviewable set (HIGH-4).
4. **Cross-tenant → identical 404** on both paths (`OrderNotFound` customer / `ReviewNotFound` maker); no distinct access-denied code (HIGH-3).
5. **All 5–6 review codes ship with cs-CZ keys in the same PR; one canonical key per code shared across T-0115/T-0117** — zero tolerance (HIGH-5).
6. **Resolve HIGH-6 before wiring the customer page**: confirm whether T-0100 folds `canReview`/`review` into `CustomerOrderDetailDto`, or T-0115 uses the documented sibling-fetch against `IReviewQueries`. Do NOT add a backend edit inside T-0115.
7. **Regen BOTH customer + maker hosts** + `.spec-hashes.json`; public/admin untouched; `check:api` clean; the working-tree client drift resolves to exactly the T-0100 regen (HIGH-7).
8. **`RATING_BP_PER_STAR` (`ln`) from `catalog.ts`** in T-0117's header — no inline `/10000` (§B.12 10× bug history).
9. **StarRating keyboard + aria** (`role="radiogroup"`, arrow-key, focus-visible, `aria-label`); submit disabled until a star ∈ [1,5]; hand-built (no library) (MEDIUM-1).
10. **One `reviews-client.ts`** — extend, don't fork; `Result<T, ApiError>` shape; DTO re-exports from the regenerated client (MEDIUM-5).
11. **Update `docs/architecture/roles/review.md` + `maker.md`** in the same PR (RDD parity, ADR 0015); globally-unique Response names; ≤5 collaborators per handler (Gate 9).
12. **Bundle commits**: red-first `test(T-0100): pin domain predicates (red)` before implementation; verify via `git log --reverse`.
13. **PM-lane**: fix T-0115's `depends_on`/body (T-0114 → T-0100); confirm the branch name (not `order-cleanup`).

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF** — with **HIGH-1 (Maker row-lock method does not exist; recompute serialization + off-by-one is the load-bearing correctness surface)**, **HIGH-5 (i18n parity is HARVESTED — zero tolerance; a 4th hit fires the harvest bump + Architect ping)**, and **HIGH-6 (T-0115's primary render path reads `canReview`/`review` off `CustomerOrderDetailDto`, which T-0100 as groomed does NOT produce — a real cross-ticket contract seam gap, compounded by the T-0114/T-0100 ticket-ID defect)** as the three named pre-flight concerns the final review will trace line-by-line.

Rationale: the design is internally consistent and well-precedented — the per-audience compile-time IDOR split mirrors T-0079's OrderMessage 6-feature surface exactly; the partial-unique-index-as-race-backstop mirrors T-0106's Dispute; the recompute-from-rows self-healing follows directly from ADR 0013's soft-delete global filter; the bp/rate conversion and `SetCatalogStats` guard are T-0043 surfaces verified present and correctly reused (not recreated). Every "verified on master" claim I re-checked holds (`SetCatalogStats` signature, `Order.MakerId`, `OrderState` values, the scoped order reads, the Dispute partial-index precedent, the `Stars`/`RATING_BP_PER_STAR` frontend anchors, the absent greenfield review files). The locks (per-order grain, immutable review / overwritable reply, live-on-first-review recompute-from-rows, no-time-limit eligibility) are coherent across all three tickets.

Two items need action OUTSIDE the implementer's lane: **PM** — fix T-0115's `depends_on`/body (T-0114 → T-0100) and confirm the branch name (the `feat/order-cleanup-bundle` reuse collides with merged artifacts), and decide the folded-DTO-vs-sibling-fetch contract for HIGH-6 before the customer page is wired; **SecOps** — Gate 3 on T-0100 (the IDOR split + eligibility gate + partial unique index are the anti-abuse structural defense). Hold the line on: row-lock the Maker for the recompute (never a bare tracked load); recompute-from-rows, never running-avg; cross-tenant → identical 404, no existence leak; eligibility on the live state, not a delivered-flag; every new code ships its cs-CZ key (zero tolerance); both hosts regenerated; `review.md` updated in the same PR.
