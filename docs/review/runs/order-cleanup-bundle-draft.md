# Order-cleanup bundle — Reviewer preliminary verdict (draft)

> Bundle-scope draft per `docs/process/routing.md` "Bundling related tickets into one PR" §parallel-reviewer. Final verdict happens after implementer reports done; this is the early-warning pass before any diff exists.

## Bundle scope (T-0079 + T-0083)

Two backend-only order-pipeline cleanup tickets sharing the same Order aggregate and outbox + email rails. **T-0079** (M, security-touching=true) ships the customer↔maker two-party async message thread — closes T-0081 §C.7 forward-compat `MakerOrderListItemDto.UnreadMessageCount: int?` (currently null), adds the equivalent field to `CustomerOrderListItemDto` as a NEW contract addition (T-0080 did not reserve one), and introduces a 5-min digest debounce via per-counterparty pending-pointer columns on Order. **T-0083** (S, security-touching=false) ships the daily 02:00 UTC `CancelExpiredPendingPaymentOrdersFunction` mirroring T-0077's AutoDeliver shape verbatim — closes the PendingPayment + 24h gap and lays the `OrderCancellationSource` extension point for T-0105/T-0107. Bundle layout: **1 EF migration** (T-0079 adds order_messages table + 4 Order columns; T-0083 likely needs `CancellationSource` + already-existing `CancelledAt`), **7 new one-file features** (6 OrderMessage features + CancelExpiredOrder), **1 new Function**, **4 new outbox event types**, **NSwag regen on customer + maker hosts (T-0079 only — T-0083 is internal plumbing)**. No frontend tickets in scope.

## Patterns / ADRs the diff must honour

- **patterns.md §A.4 one-file feature**: each of the 7 new features = `Core.AppServices/Features/<Entity>/<UseCase>.cs` with nested `Command`/`Query` + `Response` + `Validator` + `Handler`. Static-class wrapper expected per project precedent (T-0072 / T-0077 shape). Globally-unique Response naming per PR #38 NSwag CI fix: `PostCustomerOrderMessageResponse`, `PostMakerOrderMessageResponse`, `GetCustomerOrderMessagesResponse`, `GetMakerOrderMessagesResponse`, `MarkCustomerOrderMessagesAsReadResponse`, `MarkMakerOrderMessagesAsReadResponse`, `CancelExpiredOrderResponse`. NEVER bare `record Response`.
- **patterns.md §A.7 validator-first**: per-feature Validator with `BusinessErrorMessage` codes; no inline error strings. T-0079 §C.13 adds `OrderMessageBodyEmpty` + `OrderMessageBodyTooLong`. T-0083 adds zero codes per §C "No new BusinessErrorMessage codes" — reviewer hard-fail if implementer invents one.
- **patterns.md §A.12 specifications**: read-side `IOrderMessageQueries` (T-0079 §C.5) is projection-only, AsNoTracking, with WHERE-predicate IDOR shield baked in.
- **ADR 0013 scoped repos + compile-time audience split** ([backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs:106](../../backend/src/Makables.Core.Domain/Orders/IOrderRepository.cs) for `GetByIdForMakerReadOnlyAsync` precedent): T-0079 MUST ship 6 separate features (`PostCustomerOrderMessage` + `PostMakerOrderMessage` + 4 sym). Per T-0082 precedent, a customer JWT cannot dispatch the maker command because the type is not registered on the customer host. WHERE-predicate IDOR shield on `GetByOrderForCustomerAsync` / `GetByOrderForMakerAsync` + `MarkAsReadForCustomerAsync` / `MarkAsReadForMakerAsync`.
- **ADR 0014 UoW pipeline**: no handler calls `SaveChangesAsync`. Pipeline commits. T-0079's `PostMessage` handler does (1) Order load via scoped repo, (2) OrderMessage AddAsync, (3) Order.IncrementUnread*, (4) outbox-row insert (debounce predicate), (5) Order.MarkNotificationEmittedFor* — all in one transaction. T-0083's `CancelExpiredOrder` handler does load → state-guard (Silent Success) → `Order.Cancel(...)` → outbox-row insert.
- **ADR 0017 outbox**: 5-min digest debounce enforced at PostMessage handler time, NOT a Function. Pointer write part of UoW. Per-event-type routing (`OrderMessagePostedCustomerEmail` + `OrderMessagePostedMakerEmail` + `OrderCancelledCustomerEmail`); extend `OutboxEventTypes.IsEmailSend(...)` allowlist at [backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs:110](../../backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs) — reviewer hard-fail if new types are added without joining the allowlist (silent unrouted-event log spam in production).
- **ADR 0019 email**: `EmailSendService` (existing) at `backend/src/Makables.Core.AppServices/Features/Email/IEmailSendService.cs:40` gets new payload-typed branches; new email templates for both languages → cs-CZ at MVP. T-0079 adds 2 templates (customer-recipient + maker-recipient variants of "new message"); T-0083 adds 1 template (auto-expiry cancellation customer-side).
- **ADR 0020 background jobs / Q-0008 MARS workaround**: T-0083's Function is a thin scheduler-wrapper mirroring [backend/src/Makables.Functions/Delivery/AutoDeliverOrdersFunction.cs:68](../../backend/src/Makables.Functions/Delivery/AutoDeliverOrdersFunction.cs) — `.ToListAsync(cancellationToken)` BEFORE per-row `mediator.Send` loop. Per-row `try/catch when (ex is not OperationCanceledException)` + structured end-of-sweep `LogInformation` with `Claimed`/`Dispatched`/`Failed`. T-0079 ships NO Function.
- **ADR 0023 NFRs**: paged messages list AsNoTracking + IgnoreAutoIncludes + PageSize clamp 1–50; index `(order_id, created_at DESC)` per T-0079 AC-12; T-0083 deferred `(state, created_at)` index per T-0077 precedent.

## Pre-flight risks (HIGH first)

### HIGH

- **HIGH-1: `Order.Cancel` signature is a BREAKING CHANGE.** Verified at [backend/src/Makables.Core.Domain/Orders/Order.cs:716](../../backend/src/Makables.Core.Domain/Orders/Order.cs): current signature is `public BusinessResult Cancel(IClock clock)` — NO source parameter. T-0083 §C "Domain method: `Order.Cancel(OrderCancellationSource source)`" assumes a new signature. 4 existing callers will not compile if the signature is changed without overload or default:
  - `backend/src/Makables.IntegrationTests/Webhooks/ComgateWebhookTests.cs:299` — `order.Cancel(new FixedClock(...))`
  - `backend/src/Makables.Tests/Domain/Orders/OrderReservePaymentSessionTests.cs:113` — `order.Cancel(FixedClock())`
  - `backend/src/Makables.Tests/AppServices/Features/Orders/MarkOrderPaidHandlerTests.cs:145, 233` — `o.Cancel(clock)`

  **Reviewer expectation:** signature MUST be widened to `Cancel(IClock clock, OrderCancellationSource source)` and ALL 4 callers updated in the SAME commit. Default-parameter cheat (`OrderCancellationSource source = OrderCancellationSource.Customer`) is REJECTED — semantically `Customer` is wrong for a webhook-cancel test setup and would silently mis-tag the source column on every cancel. Hard-block if implementer ships a default-parameter cheat. The Order XML docs at [Order.cs:34](../../backend/src/Makables.Core.Domain/Orders/Order.cs) already foreshadow this in the authorisation note ("T-0083 customer auto-cancel, T-0105 admin refund").

- **HIGH-2: T-0079 compile-time IDOR shield enforcement.** 6 SEPARATE features mandated by §C.7 + ADR 0013 (mirror T-0082 precedent). Reviewer hard-blocks if implementer short-circuits via:
  - A single shared `PostOrderMessage` with a runtime `audience` field (rejected per Option J).
  - A shared `IOrderMessageRepository.GetByOrderAsync(orderId, audience, ...)` with runtime branch (rejected per Option K).
  - A shared handler that does `if (authorRole == X)` runtime branching (rejected per Option J — the per-audience split IS the branch).

  The WHERE-predicate baked into separate scoped methods IS the IDOR shield AND each command type's per-host MediatR registration is the second layer (customer JWT cannot dispatch maker command because the type is not on the customer host).

- **HIGH-3: 5-min digest debounce correctness — two race conditions.** §C.8 names domain method `ShouldEmitNotificationForCustomer(now)` + `MarkNotificationEmittedForCustomer(now)`. Two scenarios to verify in integration tests:
  1. **Concurrent PostMessage A + B within 5 min** — both transactions read pointer simultaneously, both see "null OR > 5 min", both emit. Mitigation in §Risk: Postgres MVCC + row-level lock on the Order row (via the FK load in the same transaction) serializes the two — the SECOND read sees the now-updated pointer. Reviewer verifies: handler loads Order via the tracked `GetByIdForCustomerAsync` / `GetByIdForMakerAsync` (NOT the read-only variant) so EF takes the row-level lock at commit time. Loading via the `*ReadOnly*` variant would defeat the serialization. Single-email duplicate under clustered topology is acknowledged in §Risk as acceptable.
  2. **MarkAsRead pointer reset then re-emit** — §C.7 step 5 of `MarkCustomerOrderMessagesAsRead` says `ClearPendingNotificationForCustomer()`. Reviewer verifies this is UNCONDITIONAL on MarkAsRead (NOT gated on "all unread messages now read" count == 0). The semantic per the ticket: "so the maker's next post fires immediately, not silenced by a stale debounce window". A conditional reset would re-introduce the silence problem.

- **HIGH-4: PII leak in message bodies + email recipient enumeration.** OrderMessage.Body may contain personal data; the outbox payload must NOT leak the other party's email. Reviewer verifies:
  - `OrderNotFound` is the SAME error code for "order doesn't exist" AND "order exists but belongs to another tenant" (per §C.13 + Risk §Recipient email enumeration). Implementer MUST NOT introduce a distinct "OrderAccessDenied" code on the cross-tenant path — that leaks existence. The WHERE-predicate IDOR shield is the natural enforcement (SQL doesn't select the row → handler genuinely cannot distinguish).
  - Outbox payload for `OrderMessagePostedCustomerEmail` / `OrderMessagePostedMakerEmail` MUST NOT include the OTHER party's email. EmailSendService resolves the recipient address at send-time via `IUserRepository`/`IMakerRepository`. Payload should carry `{ orderId, messageId }` only per §C.7 step 6; verify no `senderEmail` / `recipientEmail` slip in.
  - Message Body MUST NOT be logged at Information level (PII). Only at Debug if at all.

- **HIGH-5: Globally-unique Response naming per PR #38 NSwag CI fix.** All 7 new features must use the full-prefix Response name. Reviewer hard-fail on any `public sealed record Response(...)` inside a feature — NSwag flattens nested records to top-level types and a bare `Response` collides across features.

### MEDIUM

- **MEDIUM-1: Per-counterparty pending notification pointer (TWO new TIMESTAMPTZ NULL columns on Order).** Migration must add `customer_pending_notification_email_at TIMESTAMPTZ NULL` + `maker_pending_notification_email_at TIMESTAMPTZ NULL`. Reviewer hard-fail if implementer uses NOT NULL DEFAULT 'epoch' — the absence is the "no pending" semantic. Two separate columns per the two-counterparty model (NOT a single column with a discriminator).
- **MEDIUM-2: Auto-cancel + Comgate webhook in-flight race.** T-0083 Risk section names this; verify the Order.Cancel-then-MarkAsPaid path returns OrderInvalidTransition (Cancel succeeds, subsequent webhook MarkAsPaid sees State != PendingPayment → InvalidTransition). The Comgate webhook (T-0067) handles the inverse direction itself. T-0083 only needs the Silent Success contract on its own side per AC-8.
- **MEDIUM-3: Two-pass count + skip/take on `GetByOrderForCustomerAsync` / `GetByOrderForMakerAsync`.** T-0079 §infra `OrderMessageQueries` follows the standard paged-list shape. Reviewer optimizer-ping: same predicate chain runs twice (CountAsync + Skip/Take). EF Core 10 may not batch. Confirm acceptable at MVP volume (~50 messages/page); flag as a follow-up if production volume tips.
- **MEDIUM-4: AuthorName projection requires JOIN to users/makers.** Customer view: own messages = `Order.ContactName` (snapshot); other = `Maker.CompanyName`. Maker view: own = `Maker.CompanyName`; other = `Order.ContactName`. Two SQL JOINs per page. Verify single-round-trip execution (no per-row N+1) per ADR 0023.
- **MEDIUM-5: `OrderMessageDto.IsMine` computed at projection time.** §C.6 says it's computed by comparing `AuthorUserId == sessionUserId`. For maker view, the comparison is `AuthorRole == Maker && Maker.UserId == sessionUserId` (or similar). Verify the maker-view projection doesn't accidentally compare against the customer's userId (since `Order.CustomerUserId` is on the Order row).
- **MEDIUM-6: Email allowlist extension.** [OutboxEventTypes.cs:110](../../backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs) `IsEmailSend(...)` is the routing chokepoint. All 3 new event types (`OrderMessagePostedCustomerEmail`, `OrderMessagePostedMakerEmail`, `OrderCancelledCustomerEmail`) MUST be appended to the `is`-pattern, OR the OutboxDispatcher will silently log them via the "unrouted" branch (per the existing `OrderDisputedCarrierSourced` precedent at line 95) and no email will ever send. Reviewer greps and counts.
- **MEDIUM-7: T-0083 `CancellationSource` column nullability.** §C says "Persisted on the Order entity as `CancellationSource` (nullable until the Order actually cancels)". `CancelledAt` already exists at [Order.cs:145](../../backend/src/Makables.Core.Domain/Orders/Order.cs). Migration adds only `cancellation_source INT NULL`. Reviewer flags if implementer makes it NOT NULL DEFAULT 0 (would map to `Customer` for every never-cancelled order = wrong semantics).

## Test coverage expectations (Gate 5)

Per [docs/process/must-cover-tests.md](../../docs/process/must-cover-tests.md):

- **Pure-logic TDD red→green (commit 1)** for:
  - `OrderMessageAuthorRole` enum values + wire codes (T-0079 §C.2).
  - `Order.IncrementUnreadFor*` + `Order.ResetUnreadFor*` clamp behaviour (T-0079 §C.8) — section 4 (state machine) + section 11 (set-once-ish invariants).
  - `Order.ShouldEmitNotificationFor*(now)` + `Order.MarkNotificationEmittedFor*(now)` + `Order.ClearPendingNotificationFor*()` (T-0079 §C.8) — pure predicate.
  - `Order.Cancel(IClock, OrderCancellationSource)` legal + illegal transitions per §4 must-cover (one test per state-graph edge: PendingPayment→Cancelled, Paid→Cancelled, Accepted→Cancelled all WITH `AutoExpiry` source; illegal Shipped/Delivered/Completed/Cancelled/Refunded/Disputed → InvalidTransition).
  - `OrderCancellationSource` enum wire codes (T-0083 §C).
  - `IsOrderMessagePosted` + `IsOrderCancelled` extension methods on `OutboxEventTypes` (mirror precedent at [OutboxEventTypes.cs:110](../../backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs)).
  - 5-min debounce predicate edge cases (pointer null / pointer 4:59 ago / pointer 5:01 ago).
- **Handler tests (~14 + ~4 = ~18 unit)** per T-0079 §Tests + T-0083 §Test plan stub.
- **Integration tests (~6 + ~1 = ~7)** per the two tickets.
- **Bundle target:** ~30 new unit + ~7 new integration.
- **`BusinessErrorMessage` negative-path tests (must-cover §9):** 2 new codes from T-0079 (`OrderMessageBodyEmpty` + `OrderMessageBodyTooLong`) → each MUST have a Validator test asserting the code. `OrderNotFound` reused from existing surface.

**TDD red-first commit ordering (Gate 5 HARD FAIL if violated):**
- T-0079 commit 1 per `## Commits hint`: `test(T-0079): pin pure-logic predicates (red)` — 8 Order domain tests before implementation. ✅ ticket spec compliant.
- T-0083 commit 1: `test(T-0083): pin OrderCancellationSource + Order.Cancel(AutoExpiry) + IsOrderCancelled (red)`. ✅ ticket spec compliant.

Reviewer will walk `git log --reverse <branch> -- <test-files> <impl-files>` per [tdd-policy.md §The rule](../../docs/process/tdd-policy.md). Status-log proof acceptable as fallback per the carve-out.

## Mechanical-check expectations (Gate 9)

What new T1–T7 violations might this PR introduce?
- **T1 one-file feature shape**: 7 new features. Static-class wrapper pattern → false-positives expected (consistency baseline drifts upward).
- **T3 SaveChangesAsync in handlers**: ZERO hits expected — pipeline commits.
- **T4 `dynamic` / `any`**: ZERO hits expected.
- **T5 BusinessErrorMessage codes**: 2 new codes (T-0079: `OrderMessageBodyEmpty` + `OrderMessageBodyTooLong`). cs-CZ i18n keys must ship in same PR per the BA + l10n routing rule.
- **T6 money columns**: N/A (no monetary fields added).
- **T7 useEffect**: N/A (no frontend).
- **Consistency baseline**: 111 (post order-queries-bundle) → ~118 expected (~7 new T1 false-positives from static-class-wrapped features).

## Bundle DoR compliance check

Per [docs/process/routing.md §Bundle workflow](../../docs/process/routing.md):
- ✅ Both tickets individually satisfy DoR (T-0079 §Definition of Ready + T-0083 §Definition of Ready).
- ✅ Bundle scope named in branch name (`feat/T-0049ab-maker-backend-prep` — wait, NO: current branch is the prior bundle's branch; implementer is expected to switch to `feat/order-cleanup-bundle` or similar before starting work. **Flag for PM if the implementer reuses the existing branch.**)
- ✅ Bundle ordering documented (T-0079 references T-0081's UnreadMessageCount forward-compat; T-0083 mirrors T-0077 + introduces `OrderCancellationSource` as extension point for T-0105/T-0107).
- ✅ No external blockers between tickets — both are pure backend.
- ✅ Single parallel-reviewer artifact (this file).
- ✅ L-split rule not triggered (M + S; total ≤ ~3000 LOC production + ~1500 LOC tests). Bundle size estimate: T-0079 ~1200 LOC + ~800 LOC tests; T-0083 ~400 LOC + ~300 LOC tests. Comfortably under cap.

## Open items the implementer should confirm

1. **`Order.Cancel` extension shape.** Per HIGH-1: prefer widening to `Cancel(IClock clock, OrderCancellationSource source)` over default parameter. Update all 4 existing callers in the same commit. Update the inline `<see cref="Cancel"/>` XML doc.
2. **Per-counterparty pending notification pointer columns.** Verify migration uses `TIMESTAMPTZ NULL` (not NOT NULL DEFAULT). Two separate columns, not a single discriminator-keyed column.
3. **Email source for outbox payload.** Confirm payload carries `{ orderId, messageId }` ONLY — recipient address resolved at EmailSendService-render time via existing `IUserRepository` / `IMakerRepository` per ADR 0019.
4. **ULID generation helper for `OrderMessage.Id`.** Use the existing project ULID helper (used by Order itself per [T-0060](../../docs/tickets/T-0060-order-entity-state-machine.md)); no inline `Guid.NewGuid().ToString()`.
5. **Integration test fixtures.** Follow `MarkCreated` + `MarkUpdated` pattern from delivery-close + order-queries bundles. Seed timestamps via `IClock` injection (FixedClock); never `DateTimeOffset.UtcNow` in test setup.
6. **T-0080 `CustomerOrderListItemDto` field addition.** Verified at [backend/src/Makables.Core.Domain/Orders/Queries/CustomerOrderListItemDto.cs](../../backend/src/Makables.Core.Domain/Orders/Queries/CustomerOrderListItemDto.cs): record currently has 8 positional fields ending in `ProductTitle`. T-0079 §C.14 adds `int? UnreadMessageCount` as a NEW record param. Implementer must update `OrderQueries.GetCustomerOrdersPagedAsync` projection AND the 1+ existing constructor-shape integration tests in the same commit. Reviewer hard-fail if the field is added but the maker projection at [MakerOrderListItemDto.cs:36](../../backend/src/Makables.Core.Domain/Orders/Queries/MakerOrderListItemDto.cs) still returns null (per §C.14 the maker side flips from null to `o.MakerUnreadMessageCount` in the same PR).
7. **`OutboxEventTypes.IsEmailSend(...)` allowlist extension.** Append all 3 new event types to the `is`-pattern at [OutboxEventTypes.cs:110](../../backend/src/Makables.Core.Domain/Outbox/OutboxEventTypes.cs) AND ship corresponding `OutboxEventTypesTests` per the existing test precedent at `backend/src/Makables.Tests/Domain/Outbox/OutboxEventTypesTests.cs`.
8. **T-0083 schedule key naming + appsettings.** §C says `CancelExpiredPendingPaymentOrders:Schedule` default `0 0 2 * * *`. Verify implementer follows the T-0077 `AutoDeliverOrders:Schedule` convention shape exactly — same `Options` binding pattern, same `host.json` registration discipline.

## Preliminary verdict

**STRUCTURALLY_SOUND_PENDING_DIFF** — with **HIGH-1 (`Order.Cancel` signature widening) as the dominant pre-flight concern**.

Rationale: both tickets satisfy DoR; both follow established T-0072 / T-0077 / T-0082 precedents; bundle size is well under the routing.md cap; the user has locked the 4 critical design dimensions on each ticket (8 total); ADR alignment is clean across 0013 / 0014 / 0017 / 0019 / 0020 / 0023. The HIGH risks are all enforcement matters that the parallel diff review will catch — they are not design-level concerns and do not require ticket revision.

The single specific pre-flight callout the implementer needs before they start: **HIGH-1 — `Order.Cancel(IClock clock)` is a real existing signature with 4 callers; do not ship the new `OrderCancellationSource` parameter as a default-valued cheat.** Widen the signature, update all 4 callers, in the same commit as the new `AutoExpiry` source semantics. Everything else is verification of established patterns at PR-open.
