# Order-cleanup bundle (T-0079 + T-0083) — Reviewer FINAL verdict

> Final PR-open pass. Supersedes `order-cleanup-bundle-draft.md`. Incorporates Gate 3 (SecOps, `order-cleanup-bundle-gate3-security.md`) and the Gate 9 + test-catchup audit (`order-cleanup-bundle-gate9-and-tests.md`).

**Commits reviewed:** `18f8401..ea3271f` (5 commits on `feat/order-cleanup-bundle`, diff vs `master`: 51 files, +7137/−11).

## Bundle summary

T-0079 ships the two-party order-message thread: `OrderMessage` aggregate + 6 per-audience one-file features + scoped repo/queries seams + 4 Order columns (2 unread counters, 2 debounce pointers) + 2 outbox event types + email routing + NSwag regen on customer + maker hosts. T-0083 ships `CancelExpiredPendingPaymentOrdersFunction` (daily 02:00 UTC) + `CancelExpiredOrder` one-file feature + `OrderCancellationSource` enum + `Order.Cancel` source stamping + `OrderCancelledCustomerEmail` outbox event. Single migration `20260609174208_OrderCleanupBundle` covers both + seeds 3 email templates with cs-CZ/en-US translations.

## Verdict

**BLOCK** — the implementation core is solid (architecture, IDOR shield, debounce semantics, migration shape, Function shape, contract parity all pass), but the branch ships with a **red integration suite (171/172)** and a **Gate 5 must-cover hard fail** (zero validator tests for the two new `BusinessErrorMessage` codes; two missing maker-twin handler test files; the ~6 T-0079 integration tests required by AC-13 are absent), plus **AC-12's FK requirement is unmet** and **Gate 7 role-file updates required by the ticket are missing**. None of these require redesign; all are foldable. Re-request review after the fold.

## Pre-flight HIGHs — confirmed / refuted against the diff

| Draft finding | Disposition | Evidence |
|---|---|---|
| HIGH-1 `Order.Cancel` default-param "cheat" hard-block | **REFUTED (superseded by final check spec)** | Shipped as `Cancel(IClock clock, OrderCancellationSource source = OrderCancellationSource.Customer)` (`Order.cs:776-778`), which the final review spec (check 9) explicitly accepts as backward-compat. Only production caller is `CancelExpiredOrder.cs:102` and it passes `AutoExpiry` explicitly; all 4 pre-existing callers are tests and compile unchanged. No production mis-tag path exists today; T-0105/T-0107 must pass their source explicitly. |
| HIGH-2 compile-time IDOR shield | **CONFIRMED implemented** | 6 separate features; maker features resolve makerId via `IMakerRepository.GetByUserIdAsync` (`PostMakerOrderMessage.cs:78`, `GetMakerOrderMessages.cs:60`, `MarkMakerOrderMessagesAsRead.cs:49`); WHERE/EXISTS ownership predicates in `OrderMessageQueries.cs:54-56,108-110` and `OrderMessageRepository.cs:48-56,71-79`. SecOps Gate 3 check 2 PASS. |
| HIGH-3 debounce races | **CONFIRMED handled (with notes)** | Pointer read + conditional update + outbox enqueue in one UoW (`PostCustomerOrderMessage.cs:115-157`); Order loaded via tracked `GetByIdForCustomerAsync` (not ReadOnly). MarkAsRead clears the pointer AFTER the bulk sweep in the same request (`MarkCustomerOrderMessagesAsRead.cs:75-82`) — unconditional per ticket locked decision A.2 / §C.7 step 5; at clear time the zero-unread invariant holds by construction (the sweep precedes it). Note check-4's "(NOT unconditional)" phrasing conflicts with the ticket spec; ticket wins. Boundary pinned strictly-less-than: `OrderNotificationDebounceTests.cs:101-111` (exactly 5 min → suppressed). See LOW-3 on the concurrent-post duplicate-email window. |
| HIGH-4 PII / enumeration | **CONFIRMED clean** | Payloads carry only the recipient's OWN email (`OrderMessagePostedMakerEmailPayload` → maker's email; `...CustomerEmailPayload` → `order.ContactEmail`); message Body appears in NO payload and NO log line (SecOps Gate 3 checks 4/5/11 PASS); cross-tenant POST/mark-read return generic `OrderNotFound`; GETs return empty page per T-0080 contract — leak-free in both directions. |
| HIGH-5 Response naming | **CONFIRMED clean** | Zero bare `record Response` in the diff; generated TS has globally-unique `PostCustomerOrderMessageResponse` / `GetMakerOrderMessagesResponse` etc. (customer-api.v1.ts:2216,2370,2797; maker-api.v1.ts:2353,2989,3447). |

## 17 mandatory checks

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | A.4 one-file shape × 7 | PASS | All 7 features: static wrapper + nested Command/Query, Response, Validator, Handler. 7 T1 static-wrapper false-positives baselined (Gate 9 audit: 111→118, exit 0). |
| 2 | Compile-time IDOR, 6 features, makerId via GetByUserIdAsync | PASS | See HIGH-2 row. |
| 3 | GDPR/PII | PASS | Body never logged (SecOps grep zero hits); payloads carry recipient's own email only; cross-tenant → generic `OrderNotFound` (`PostCustomerOrderMessage.cs:97-101` et al.). |
| 4 | Debounce correctness | PASS w/ note | Same-UoW pointer read+update; clear-after-sweep on MarkAsRead (ticket-spec unconditional); strictly-less-than boundary pinned at `OrderNotificationDebounceTests.cs:101-111`. |
| 5 | Globally-unique Response naming | PASS | Zero bare `Response` types; TS classes unique. |
| 6 | AsNoTracking + IgnoreAutoIncludes; two-pass paging | PASS | `OrderMessageQueries.cs:51-53,105-107`; CountAsync + Skip/Take share `baseQuery` (:58, :64-69). |
| 7 | Validator boundaries | PASS (code) / see BLOCKER-2 for tests | `Body.NotEmpty → OrderMessageBodyEmpty` + `MaximumLength(OrderMessage.MaxBodyLength=2000) → OrderMessageBodyTooLong` (`PostCustomerOrderMessage.cs:60-64`); Page ≥ 1, PageSize ∈ [1,50] (`GetCustomerOrderMessages.cs:41-49`). |
| 8 | Outbox idempotency | PASS | Digest emit gated on `ShouldEmitNotificationFor` (:119); `OrderCancelledCustomerEmail` emit behind the Silent Success state guard (`CancelExpiredOrder.cs:90-98`). |
| 9 | `Order.Cancel(source)` backward-compat | PASS | Optional param defaulting Customer (`Order.cs:776-778`); zero broken callers; `CancelExpiredOrder.cs:102` passes `AutoExpiry` explicitly. |
| 10 | Silent Success contract | PASS | Handler no-ops Success on `State != PendingPayment` (`CancelExpiredOrder.cs:90-98`); `Order.Cancel` rejects non-cancellable states (`Order.cs:780-781`). |
| 11 | Per-counterparty pointers nullable | PASS | Migration `:47-51`/`:62-66` — both `timestamp with time zone, nullable: true`; counters `INT NOT NULL DEFAULT 0` (:54-59, :69-74); `cancellation_source SMALLINT NULL` (:38-42). |
| 12 | Function mirrors AutoDeliver | PASS | `ToListAsync` before dispatch loop w/ Q-0008 MARS comment (`CancelExpiredPendingPaymentOrdersFunction.cs:58-68`); per-row `catch when (ex is not OperationCanceledException)` fail-continue (:94-102); end-of-sweep summary (:105-107); `%CancelExpiredPendingPaymentOrders:Schedule%` (:50). |
| 13 | `CustomerOrderListItemDto.UnreadMessageCount` | PASS | Non-nullable `int` (`CustomerOrderListItemDto.cs:38`); projection reads `o.CustomerUnreadMessageCount` (`OrderQueries.cs:96`). |
| 14 | Maker projection flip | PASS (code) / **see BLOCKER-1** | `OrderQueries.cs:158` now `o.MakerUnreadMessageCount` — but the pre-existing pin test asserting null was not re-pointed and now FAILS. |
| 15 | Build + tests | **FAIL** | Build: 0 warnings / 0 errors. Unit: **1379/1379 pass**. Integration: **171/172 — 1 FAIL** (`GetMakerOrdersIntegrationTests.GET_orders_UnreadMessageCount_is_null_pin_until_T_0079`). |
| 16 | Forbidden patterns | PASS | 0 × `Console.WriteLine` / `dynamic` / handler `SaveChangesAsync` (sole diff hit is integration-test seeding) / inline error strings (Gate 9 T5 clean). |
| 17 | NSwag regen (ea3271f) | PASS | Both clients + `.spec-hashes.json` committed; `npx tsc --noEmit` exit 0; Response classes globally unique. |

## Findings

### BLOCKER / HIGH

- **BLOCKER-1 — Integration suite is red.** `backend/src/Makables.IntegrationTests/Orders/GetMakerOrdersIntegrationTests.cs:276-288` still pins `UnreadMessageCount == null`; the projection flip at `OrderQueries.cs:158` returns 0/N. The test's own name says it expires at T-0079 — it must be rewritten in this PR to assert the populated value (which doubles as the missing AC-11 maker-side proof). **Fold: required.**
- **BLOCKER-2 — Gate 5 must-cover hard fail.** Per `docs/process/must-cover-tests.md` §5: `OrderMessageBodyEmpty` / `OrderMessageBodyTooLong` are new codes with **zero test references repo-wide** — no `TestValidate` coverage for any OrderMessages validator. Per §9: new failure paths `MakerNotFound`/`MakerUserMissing` (`PostCustomerOrderMessage.cs:130,140`) and `OrderCustomerUserMissing` (`PostMakerOrderMessage.cs:119`) untested. Missing twin files: `GetMakerOrderMessagesHandlerTests.cs`, `MarkMakerOrderMessagesAsReadHandlerTests.cs`. **Fold: required** (items 1-3 of the Gate 9 audit's fold list). Note: TDD ordering itself is CLEAN — commit `18f8401` is tests-only (4 pure-logic files, red) before implementation; no after-the-fact pure-logic tests.
- **BLOCKER-3 — AC-13 integration tests absent.** T-0079 AC-13 requires ~6 new integration tests (cross-tenant isolation, debounce e2e, unread denormalization, list exposure, mark-as-read idempotency, paged ordering). The diff contains ONE integration test file, and it is T-0083's. The bundle's headline locked decision (5-min debounce) has no DB-level proof; AC-6/AC-7/AC-10 have no SQL-level evidence. **Fold: required** — at minimum the debounce-e2e and unread-denormalization/list-exposure tests (Gate 9 audit items 4-5), with cross-tenant, idempotency, and paging assertions folded into them.
- **HIGH-1 — AC-12 FK requirement unmet.** Migration `20260609174208_OrderCleanupBundle.cs:81-103` creates `order_messages` with PK only — no FK to `orders.id` nor `users.id`. AC-12 and §C.3 explicitly require the FK; the precedent table (`20260605152212_OrderAttachments.cs`, `FK_order_attachments_orders_order_id`, cascade) has it; and `OrderMessageConfiguration.cs:12-14` XML doc FALSELY claims "PK + FK to orders.id + FK to users.id". A shadow-FK needs no navigation (`HasOne<Order>().WithMany().HasForeignKey(...)`). **Fold: add the FKs (regenerate the migration — it has not shipped) or obtain Architect sign-off for the deviation + fix the config doc + log the deviation on the PR.**

### MEDIUM

- **MEDIUM-1 — Gate 7 / RDD parity fail: role files not updated.** `docs/architecture/roles/order-message.md` (last touched T-0067 era) describes `SendMessage.Command`, `SenderUserId`/`Content`, admin-view-all, and implementation pointer `Core.Domain/Orders/OrderMessage.cs` — all wrong post-T-0079. `docs/architecture/roles/order.md` lacks the new domain-method surface. Ticket §Docs requires both updates in this PR; ADR 0015 §"PR that changes a handler's collaborators must update the role file". **Fold: required.**
- **MEDIUM-2 — State-guard ambiguity.** Post handlers accept messages on orders in ANY state (incl. `PendingPayment`, `Cancelled`); the role file's invariant says "messages only on Paid and later states"; ticket AC-1 frames the channel as post-payment but its §C.7 handler steps omit the guard. Implementation follows the ticket. **PM/Architect to rule; reconcile the role file either way (folds into MEDIUM-1).**
- **MEDIUM-3 — ADR 0015 collaborator budget exceeded.** `PostCustomerOrderMessage.Handler` and `PostMakerOrderMessage.Handler` take **11** constructor dependencies; `CancelExpiredOrder.Handler` 7 (budget ~5). The email-payload enrichment block (users + makers + languageResolver + publicAppUrls) repeats across three handlers and could collapse behind a single recipient-resolution collaborator. **Pinged Architect — does not block alone, but is the 3rd+ occurrence of the email-enrichment sprawl pattern (T-0067/T-0071/T-0076 precedents); harvest-duty candidate for `docs/review/recurring-findings.md` once approved.**
- **MEDIUM-4 — SecOps Gate 3 HIGH fold outstanding.** Comgate pay-after-auto-cancel race surfaces only an Info-level "lost the race" log (`ComgateWebhookController.cs:182-188`) while money is captured against a Cancelled order with no refund flow until T-0105. SecOps recommends folding a Warning/Error log for `State is Cancelled or Refunded` in this PR (T-0083 creates the race). **Fold: apply SecOps' recommended change.**

### LOW

- **LOW-1** — Outbox payloads deviate from ticket §C.7's minimal `{orderId, messageId}` shape (recipient email + name + counts snapshotted at enqueue time). Matches the T-0067/T-0076/T-0083 payload precedent, so accepted; note the stale-address edge if a user changes email between enqueue and dispatch. No action.
- **LOW-2** — `MarkAsRead` `ExecuteUpdateAsync` commits outside the UoW SaveChanges (no explicit transaction in `UnitOfWorkPipelineBehavior`); a commit failure after the sweep leaves transient counter drift. Self-heals on retry because `ResetUnreadFor` is unconditional. Document in the role file (folds into MEDIUM-1).
- **LOW-3** — Concurrent posts within milliseconds can double-emit (snapshot read of the pointer; no concurrency token). Ticket §Risk pre-accepted a one-email duplicate. No action.
- **LOW-4** — `PageSize > 50` surfaces error code `MinValue` (`InclusiveBetween(...).WithErrorCode(BusinessErrorMessage.MinValue)`); semantically a max violation. Matches the existing paged-query validator shape; tidy when the shared validator is next touched.
- **LOW-5** — `CancelExpiredPendingPaymentOrders:Schedule` exists only in gitignored `local.settings.json`; no committed appsettings template / deployment env-list entry (same gap as T-0077's key — pre-existing pattern). Add both keys to the deployment env-var list as a docs follow-up.
- **LOW-6** — Cross-tenant GET returns an empty page (not 404) — consistent with the T-0080 list-empty contract and leak-free; deviates from a strict reading of §C.13. Accepted; documented in the controller XML doc.

## Bundle DoR compliance

- Both tickets satisfy their Definition of Ready (verified in draft; unchanged).
- Branch correctly named `feat/order-cleanup-bundle` (draft's reused-branch concern did not materialize).
- Bundle ordering + shared-aggregate rationale documented; single migration; size under the L-split cap (~3.4k production LOC incl. generated artifacts; tests ~1.3k).
- Single parallel-reviewer artifact chain: draft → gate 3 → gate 9 → this file.

## Gates 1-7 summary

| Gate | Result | Notes |
|---|---|---|
| 1 CLAUDE.md self-check | PASS | Build 0W/0E; no forbidden patterns; typed throughout; errors via `BusinessErrorMessage`; money N/A. |
| 2 AC traceability | **FAIL** | AC-12 FK missing (HIGH-1); AC-13 integration tests missing (BLOCKER-3); AC-11 maker-side proof is the failing pin test (BLOCKER-1). All other AC items traced. |
| 3 Security | FOLD_RECOMMENDED | SecOps: 12 PASS, 1 HIGH fold (MEDIUM-4 here), 1 out-of-bundle Q-item (rate limiting). |
| 4 Architecture | PASS w/ escalation | Extension points preserved (outbox naming, email routing branch, `OrderCancellationSource` extension point, no provider leakage). ADR 0015 collaborator budget escalated (MEDIUM-3). |
| 5 Tests | **FAIL** | TDD red-first PASS (`18f8401` tests-only). Must-cover §5/§9 hard fail + missing twin files + missing integration coverage (BLOCKER-2/-3). |
| 6 Contract parity | PASS | NSwag regen committed (`ea3271f`), both hosts; tsc exit 0; spec-hashes updated. |
| 7 Docs | **FAIL** | Role files `order-message.md` + `order.md` not updated (MEDIUM-1); schedule key not in committed config/env docs (LOW-5). |

## Required fold before re-review

1. Re-point `GET_orders_UnreadMessageCount_is_null_pin_until_T_0079` to assert populated values (BLOCKER-1 / AC-11).
2. Add `GetMakerOrderMessagesHandlerTests.cs` + `MarkMakerOrderMessagesAsReadHandlerTests.cs` + `TestValidate` validator tests asserting `OrderMessageBodyEmpty`/`OrderMessageBodyTooLong` + §9 negative-path assertions (BLOCKER-2).
3. Add debounce-e2e and unread-denormalization/list-exposure integration tests; fold cross-tenant, idempotency, and paging assertions in (BLOCKER-3).
4. Add FKs on `order_messages` (orders.id, users.id) and regenerate the migration, or Architect-signed deviation + config-doc fix (HIGH-1).
5. Update `docs/architecture/roles/order-message.md` + `order.md`; resolve the Paid+ state-guard question with PM (MEDIUM-1/-2).
6. Apply SecOps' Comgate webhook Warning-log fold (MEDIUM-4).

Re-run: `dotnet build` (0/0), `dotnet test Makables.Tests`, `dotnet test Makables.IntegrationTests` (must be fully green), `node scripts/check-consistency.mjs` (exit 0), `npx tsc --noEmit`.
