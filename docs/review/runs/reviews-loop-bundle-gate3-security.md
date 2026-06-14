# Gate 3 (Security) — reviews-loop-bundle

**Verdict: GATE3_PASS**
Branch `feat/order-cleanup-bundle` @ reviews-loop range `9e2a74e..HEAD` (8 commits). Scope: customer `SubmitReview`, maker `RespondToReview`, eligibility/anti-abuse gate, GDPR maker-facing review view. No secrets, webhooks, file upload, or cron touched.

## Checklist results

| # | Check | Result |
|---|---|---|
| 1 | SubmitReview IDOR | PASS — `OrderRepository.GetByIdForCustomerAsync` predicate `o.Id == orderId && o.CustomerUserId == customerUserId`; cross-tenant → null → 404 `OrderNotFound`. Test `Customer_A_cannot_review_customer_B_order_returns_notFound` asserts 404 + no row written. |
| 2 | RespondToReview IDOR | PASS — `GetByIdForMakerAsync` predicate `r.Id == reviewId && r.MakerId == makerId`; foreign → 404 `ReviewNotFound`. Maker-w/o-maker-row also 404 (no oracle). Test `Maker_reply_cross_tenant_404_then_owning_maker_overwrites` asserts Maker B → 404. |
| 3 | Eligibility = anti-abuse gate | PASS — see below. |
| 4 | GDPR maker-facing | PASS — see below. |
| 5 | Body/reply content | PASS — Body 1000 / Reply 500 bounded server-side in Validator AND domain `Create`/`AddReply`. FE renders via JSX text children (React-escaped); no `dangerouslySetInnerHTML` in any new review FE file. No outbox/email send in this bundle. |
| 6 | Rating manipulation | PASS — rating server-validated 1–5 (`ReviewRatingOutOfRange` + domain guard). Recompute is server-side `COUNT/AVG` over rows; aggregate surfaced from `maker.RatingAverageBp/RatingCount`, not paged window, never client-supplied. `RespondToReview.AddReply` touches only `MakerReply/MakerReplyAt`; test asserts `Rating == 5` post-reply. |
| 7 | Authz | PASS — `[Authorize]` on both controllers; commands registered per-host (customer `SubmitReview` only on Customer; `RespondToReview` only on Maker) per ADR 0013 — customer JWT cannot dispatch the reply command. |
| 8 | No secrets / webhooks / upload | PASS — `OutboxEvent`/`EmailTemplate` hits are only the EF snapshot re-serialization + test TRUNCATE list; no new send/webhook/blob/cron code. |

## Eligibility-gate assessment (the structural defence)
Airtight. Four independent layers, all server-side: (1) IDOR-scoped order load — only the caller's own order resolves; (2) `ReviewEligibility.IsReviewableState` → `Delivered`/`Completed` only; (3) `ExistsForOrderAsync` pre-check; (4) hard backstop partial unique index `ux_reviews_order_active UNIQUE(order_id) WHERE is_active`. A fabricated/non-owned order → 404; a non-delivered order → 409 `ReviewOrderNotDelivered` (unit test `Pre_delivery_order_returns_orderNotDelivered_without_writes`, `Shipped`). Concurrent double-submit serializes on the unique index. No path lets an unpaid/non-owned/pre-delivery order seat a review.

## GDPR assessment (§C.7 lock)
PASS. `MakerReceivedReviewDto` carries `ReviewId, OrderId, OrderNumber, Rating, Body, MakerReply, MakerReplyAt, CreatedAt` — NO customer name, NO email, NO `CustomerUserId`. `ReviewQueries.GetMakerReceivedReviewsPagedAsync` projects no customer-identity column and does not join `users`. `CustomerUserId` stays the internal audit identity, never exposed maker-side. Consistent with the T-0081/T-0082 no-email maker lock.

## Findings
- **HIGH/CRITICAL:** none.
- **MEDIUM:** none.
- **LOW (non-blocking):** the Delivered/Completed eligibility rejection has unit coverage (`Shipped`) but no Postgres integration test for the pre-delivery 409 path; existing integration coverage seeds only Delivered orders. Recommend one integration test asserting a `Shipped` order yields `ReviewOrderNotDelivered` with no row written, to pin the gate e2e. Not a release blocker — the unit test + the structural index cover the risk.
