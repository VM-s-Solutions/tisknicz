# Reviews-loop bundle — Final review (T-0100 + T-0115 + T-0117)

Branch `feat/reviews-loop-bundle`, 8 commits (`4652757` grooming + `4e45767..d893c75`). Reviewed against the preliminary draft (`reviews-loop-bundle-draft.md`), `CLAUDE.md` Self-Check, `docs/review/checklist.md`, the three tickets + ADRs.

## Verdict

**REQUEST CHANGES** — one BLOCKER (HIGH-2: the partial unique index `ux_reviews_order_active` is NOT registered in `UniqueConstraintTranslator`, and no concurrent-double-submit test pins the typed conflict — the documented payout-core precedent is breached). One MEDIUM (§B.12 inline `/10_000` literal in the maker aggregate header instead of the shared `RATING_BP_PER_STAR` constant). Everything else passes — the design is correct and well-precedented; the recompute correctness, both IDOR paths, eligibility, GDPR, i18n parity, NSwag double-host regen, role docs, and Gate 5 red-first all verify clean.

## Recompute correctness disposition — CORRECT

- `Maker.RecomputeRating(count, bp)` forwards to `SetCatalogStats` keeping `TotalOrders` untouched; the `[0,50000]` guard applies (`Maker.cs:262`).
- Row-lock present: `MakerRepository.GetByIdForUpdateAsync` is a real `SELECT * FROM makers WHERE id={id} AND is_active FOR UPDATE` via `FromSqlInterpolated` (restating `is_active` because raw SQL bypasses the global filter); returns a tracked instance (`MakerRepository.cs:59-77`). HIGH-1 from the draft is RESOLVED — the read→recompute→write window serializes on the same maker.
- Recompute-from-rows: `GetMakerRatingAggregateAsync` is `COUNT(*) + AVG(rating)` over ACTIVE rows (global filter), one round-trip (`ReviewRepository.cs:44-68`). The just-added (Added-but-unflushed) review is folded in memory: `((existingAvg*existingCount)+newRating)/(existingCount+1)` (`SubmitReview.cs:138-146`). bp = `Math.Clamp((int)Math.Round(avg*10_000, AwayFromZero), 0, 50_000)` — half-up, correct.
- Integration tests pin it: one 4-star → `40000`/`1`; AVG(5,4,3)=4.0 → `40000`/`3`; soft-delete the 3-star + add a 5-star → active set {5,4,5} → `46667`/`3` (self-healing proven, NOT a running average) (`ReviewsIntegrationTests.cs:224-289`). 37 Reviews/recompute unit tests pass.
- Minor note (non-blocking): the fold reconstructs the existing sum as a `double` (`existingAverage*existingCount`) rather than a true integer `SUM(rating)`. At MVP row counts this is exact (tests pass on exact bp), but a `SUM`-based aggregate would be float-drift-proof at scale. Optional hardening; not a defect today.

## Uniqueness-race disposition — BLOCKER

- App-gate present: `ExistsForOrderAsync` → `ReviewAlreadyExists` (`SubmitReview.cs:97`), serial-resubmit pinned (`ReviewsIntegrationTests.cs:293-309`).
- Partial unique index present in the migration: `ux_reviews_order_active UNIQUE (order_id) WHERE is_active` (`20260614090939_AddReviewTable.cs`; `ReviewConfiguration.cs:65-68`, `HasFilter("is_active")`).
- **GAP:** `ux_reviews_order_active` is absent from `UniqueConstraintTranslator.Mappings` (`UniqueConstraintTranslator.cs:35-118`). `UnitOfWorkPipelineBehavior` catches `UniqueConstraintViolationException`, calls `Translate(ex.ConstraintName)`, and on a `null` mapping **rethrows** → raw 500. AC-4 says "the loser surfaces a conflict." The concurrent double-submit loser currently 500s. This is exactly the payout-core BLOCKER-1 lesson (`ux_payout_batches_*` were added for the same reason). There is also no `ReviewPerOrderUniquenessTests` concurrent leg pinning the typed conflict.

## BLOCKERs

1. **HIGH-2 — register the partial unique index in the translator.** Add `["ux_reviews_order_active"] = Error.Conflict("orderId", BusinessErrorMessage.ReviewAlreadyExists)` to `UniqueConstraintTranslator.cs` so the concurrent-double-submit loser surfaces `ReviewAlreadyExists`, not a raw 500. Add a concurrent-submit integration test asserting the loser gets `ReviewAlreadyExists` (AC-4). Quote: checklist / payout-core precedent — "the loser of a TOCTOU race surfaces the same typed Conflict the pre-check would have returned, rather than a raw 500."

## Fold list (non-blocking; bundle into the BLOCKER fix commit)

1. **§B.12 — `RATING_BP_PER_STAR`.** `recenze/page.tsx:130` divides by an inline `10_000` literal; the shared constant `RATING_BP_PER_STAR = 10_000` exists in `catalog.ts:175` precisely to kill this duplicated literal (the §B.12 10× bug history). Import and use it. Value is correct today, so MEDIUM not BLOCKER, but it re-introduces the exact smell the constant exists to prevent.
2. **T-0114 stale ref (PM-lane, doc-only).** T-0115's ticket body still references T-0114; the backend is T-0100. Confirmed not in code (the page uses the documented sibling-fetch against `IReviewQueries`). 1-line ticket fix.
3. **Optional hardening.** `GetMakerRatingAggregateAsync` could return `SUM(rating)` to make the fold integer-exact (avoids `existingAvg*existingCount` float reconstruction). Not required.

## Checks

| Gate / check | Result |
|---|---|
| Gate 1 build | `dotnet build` src → 0 warn / 0 err. |
| Gate 5 TDD red-first | PASS. `4e45767` (3 pure-logic test files: `MakerRecomputeRatingTests`, `ReviewAddReplyTests`, `ReviewCreateTests`) precedes impl `8bd496c`. 37 Reviews/recompute unit tests pass. Validator negative paths pinned: `ReviewRatingOutOfRange`, `ReviewBodyTooLong`, `ReviewReplyEmpty`, `ReviewReplyTooLong`. |
| Recompute atomicity + correctness | PASS (see disposition). Row-lock real; recompute-from-rows; half-up bp; `46667` self-healing pinned. |
| Per-order uniqueness | App-gate + index PASS; **23505 translation FAIL (BLOCKER-1)**. |
| IDOR both endpoints | PASS. `SubmitReview` → `GetByIdForCustomerAsync`, cross-tenant → `OrderNotFound` 404 (`ReviewsIntegrationTests.cs:313-329`). `RespondToReview` → `GetByIdForMakerAsync` (`r.Id==id && r.MakerId==makerId`), cross-tenant → `ReviewNotFound` 404 (`:333-360`). No distinct access-denied code. Compile-time per-host split intact; both controllers `[Authorize]`. |
| Eligibility anti-abuse | PASS. `ReviewEligibility.IsReviewableState` checks LIVE `order.State ∈ {Delivered, Completed}` (not `DeliveredAt`), so Refunded/Disputed/Cancelled correctly excluded. `Shipped → ReviewOrderNotDelivered` pinned (`SubmitReviewHandlerTests.cs:151-159`). The reviewable-orders read filters live state + left-anti-join (`ReviewQueries.cs:30-44`). |
| i18n parity (HARVESTED) | PASS — ZERO gaps. All 7 `review.*` codes have cs-CZ keys (`cs-CZ.ts:479-485`); all `customer.review.*` (vykání, `:725-736`) + `dashboard.maker.reviews.*` + `dashboard.maker.nav.reviews` (tykání, `:870-897`) present. Dotted keys match `resolveErrorMessage` 1:1. No 4th-hit harvest needed. |
| GDPR §C.7 | PASS. `MakerReceivedReviewDto` carries OrderNumber + Rating + Body + MakerReply + dates — NO customer email/name (stricter than the draft's "first name"). |
| RDD parity (ADR 0015) | PASS. `review.md` + `maker.md` updated in this PR (grain, partial index, recompute-from-rows, immutability asymmetry, per-audience IDOR split, GDPR, T-0050 deferral). `SubmitReview` handler = 5 collaborators (orders, reviews, makers, session, ids+logger) — logger/ids are infra, at cap, not over. |
| Deviations (4) | All judged acceptable: DTO location (`Reviews/Queries`, precedent); T-0115 sibling-fetch (correct per draft HIGH-6 — no backend fold); aggregate fields on maker response envelope (Q5, header reads authoritative value not paged avg); dotted i18n keys (matches resolver). |
| Frontend | PASS. `StarRating` (`star-rating.tsx`) — `role="radiogroup"`, roving-tabindex, arrow-keys, `aria-checked`, per-star `aria-label`, focus-visible ring. Inline CTA renders exactly one of form/read-only/nothing (`page.tsx:308-312`). Reply overwrite + edit pre-fill. Server Components default; `'use client'` only on the two forms. No `useEffect` data fetch. |
| Gate 6 NSwag all-hosts | PASS. Both `customer-api.v1.ts` + `maker-api.v1.ts` regenerated; `.spec-hashes.json` customer `b8442a98…` / maker `6bbbba43…` (matches report). Public/admin diffs are merged-payout-bundle artifacts vs this `master`, not Reviews scope. `reviews-client.ts` re-exports generated DTO types; route code never imports `lib/api-client/`. |
| Gate 9 consistency | PASS. 138 tracked (baseline). Reviews delta = exactly +5 T1 static-class-wrapper false-positives (files DO declare `public static class`). Zero new T3/T4/T5 on the Reviews surface. |
| Money | N/A (rating bp is a score, not money). |
| SecOps (Gate 3) | T-0100 `security_touching: true` — IDOR split + eligibility + partial index are the anti-abuse defense; ping SecOps to co-sign (the missing 23505 translation is the one structural-defense gap). |

Re-review needed only on the BLOCKER fix (translator entry + concurrent test) + the §B.12 constant import. Everything else is approved.
